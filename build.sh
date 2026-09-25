#!/usr/bin/env bash
set -euo pipefail

CONFIG="${1:-release}"
APP="dist/Tinysnap.app"

# --disable-sandbox because Swift Package Manager sandboxes the manifest compile, and
# that cannot nest inside another sandbox: under Homebrew it fails with
# "sandbox_apply: Operation not permitted". Nothing is lost here, since the package has
# no dependencies and so no third party manifest to isolate.
swift build -c "$CONFIG" --disable-sandbox

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp ".build/$CONFIG/TinysnapApp" "$APP/Contents/MacOS/Tinysnap"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

# Sign with the local identity when there is one, because an ad hoc signature is a
# hash of the binary: every rebuild becomes a new app as far as macOS is concerned,
# and Screen Recording has to be granted all over again. Run scripts/signing-identity.sh
# once to create it. Neither is a Developer ID, so a downloaded copy is still blocked
# by Gatekeeper, which is why Tinysnap is installed from source.
IDENTITY="${TINYSNAP_SIGN_IDENTITY:-Tinysnap Local Signing}"

if ! security find-identity -p codesigning | grep -qF "$IDENTITY" \
    || ! codesign --force --sign "$IDENTITY" "$APP" 2>/dev/null; then
    # No identity, or the keychain is out of reach, which is the case inside a build
    # sandbox. Ad hoc still produces a working app, it just loses the stable identity.
    codesign --force --sign - "$APP"
fi

echo "Built $APP"

if [ "${2:-}" = "--install" ]; then
    DEST="${PREFIX:-/Applications}"
    rm -rf "$DEST/Tinysnap.app"
    cp -R "$APP" "$DEST/Tinysnap.app"
    echo "Installed to $DEST/Tinysnap.app"
fi
