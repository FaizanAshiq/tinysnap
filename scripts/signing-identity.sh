#!/usr/bin/env bash
# Create a self signed code signing identity for building Tinysnap locally.
#
# Why this exists: an ad hoc signature is a hash of the binary, so every rebuild
# produces a new identity and macOS forgets that you granted Screen Recording. A self
# signed certificate is a fixed identity, so the permission is granted once and
# survives every later build.
#
# It is not a Developer ID and does nothing for anyone downloading the app. It only
# makes the local build loop bearable. Run it once:
#
#   ./scripts/signing-identity.sh
#
# After that build.sh picks the identity up on its own.
set -euo pipefail

NAME="${TINYSNAP_SIGN_IDENTITY:-Tinysnap Local Signing}"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

# No -v here: -v lists only trusted identities, and this one is deliberately never
# trusted (see below), so with it every run would delete and remake the identity and
# Screen Recording would have to be granted again.
if security find-identity -p codesigning | grep -qF "$NAME"; then
    echo "Already have a signing identity called \"$NAME\"."
    exit 0
fi

# A certificate without a usable private key, or one that was never trusted, is
# worse than nothing: it looks present but cannot sign. Clear any leftover first.
while security find-certificate -c "$NAME" "$KEYCHAIN" >/dev/null 2>&1; do
    security delete-certificate -c "$NAME" "$KEYCHAIN" >/dev/null 2>&1 || break
done

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# macOS ships LibreSSL at this path and it writes a PKCS12 bundle the keychain can
# read. OpenSSL 3, which Homebrew may have put earlier on PATH, writes one the
# keychain rejects with "MAC verification failed".
SSL=/usr/bin/openssl
KEY="$(head -c 16 /dev/urandom | base64)"

"$SSL" req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout "$WORK/key.pem" -out "$WORK/cert.pem" \
    -subj "/CN=$NAME" \
    -addext "basicConstraints=critical,CA:false" \
    -addext "keyUsage=critical,digitalSignature" \
    -addext "extendedKeyUsage=critical,codeSigning" \
    2>/dev/null

"$SSL" pkcs12 -export -inkey "$WORK/key.pem" -in "$WORK/cert.pem" \
    -out "$WORK/identity.p12" -passout "pass:$KEY" -name "$NAME"

# -T /usr/bin/codesign lets codesign use the key without a prompt on every build.
security import "$WORK/identity.p12" -k "$KEYCHAIN" -P "$KEY" -T /usr/bin/codesign >/dev/null

# Deliberately not marked as trusted. codesign signs with an untrusted self signed
# certificate perfectly well, and what matters here is only that the identity stays
# the same from one build to the next. Skipping the trust step also skips a password
# prompt and avoids adding anything to your trusted roots.

echo "Created \"$NAME\". Builds will now use it instead of an ad hoc signature."
