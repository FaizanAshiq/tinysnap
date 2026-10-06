#!/usr/bin/env bash
# Create the Mac release signing identity, once, and hand it to CI.
#
# Why this exists: every Mac build CI makes for download is signed with this one
# certificate. macOS keeps Screen Recording for an app by its signature, so with the
# same certificate on every version the permission survives each update, and an
# update installs only when it is signed exactly as the running copy is.
#
# It is not a Developer ID: the first open of a download still needs Open Anyway in
# System Settings, Privacy & Security. Run it once, from the repository:
#
#   ./scripts/release-identity.sh
#
# It keeps the identity in ~/.config/tinysnap/release-signing and stores it in the
# repository's secrets as MAC_SIGNING_P12 and MAC_SIGNING_PASSWORD. Back the folder
# up: lose it, and the next build is a new app to macOS, asking for Screen Recording
# again on every Mac.
set -euo pipefail

NAME="Tinysnap Release"
REPO="FaizanAshiq/tinysnap"
DIR="$HOME/.config/tinysnap/release-signing"

if [ -e "$DIR/identity.p12" ]; then
    echo "$DIR already holds an identity; it is not replaced." >&2
    exit 1
fi
mkdir -p "$DIR"
chmod 700 "$DIR"

# macOS ships LibreSSL at this path, whose PKCS12 the keychain reads; OpenSSL 3 writes
# one it rejects. See signing-identity.sh.
SSL=/usr/bin/openssl
PASSWORD="$(head -c 24 /dev/urandom | base64 | tr -d '/+=')"
(
    umask 077
    "$SSL" req -x509 -newkey rsa:2048 -nodes -days 7300 \
        -keyout "$DIR/key.pem" -out "$DIR/cert.pem" -subj "/CN=$NAME" \
        -addext "basicConstraints=critical,CA:false" \
        -addext "keyUsage=critical,digitalSignature" \
        -addext "extendedKeyUsage=critical,codeSigning" \
        2>/dev/null
    "$SSL" pkcs12 -export -inkey "$DIR/key.pem" -in "$DIR/cert.pem" \
        -out "$DIR/identity.p12" -passout "pass:$PASSWORD" -name "$NAME"
    printf '%s' "$PASSWORD" > "$DIR/password"
)

base64 -i "$DIR/identity.p12" | gh secret set MAC_SIGNING_P12 -R "$REPO"
printf '%s' "$PASSWORD" | gh secret set MAC_SIGNING_PASSWORD -R "$REPO"

echo "Created \"$NAME\", kept in $DIR and stored in $REPO's secrets."
