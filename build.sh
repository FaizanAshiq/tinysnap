#!/usr/bin/env bash
set -euo pipefail

CONFIG="${1:-release}"
APP="dist/Tinysnap.app"

# TINYSNAP_UNIVERSAL=1 builds for Apple silicon and Intel at once, as the download is.
ARCHS=()
[ "${TINYSNAP_UNIVERSAL:-}" = 1 ] && ARCHS=(--arch arm64 --arch x86_64)

# --disable-sandbox because Swift Package Manager sandboxes the manifest compile, and
# that cannot nest inside another sandbox: under Homebrew it fails with
# "sandbox_apply: Operation not permitted". Nothing is lost here, since the package has
# no dependencies and so no third party manifest to isolate.
swift build -c "$CONFIG" --disable-sandbox ${ARCHS[@]+"${ARCHS[@]}"}
BIN="$(swift build -c "$CONFIG" --disable-sandbox ${ARCHS[@]+"${ARCHS[@]}"} --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# Kept out of Spotlight, so macOS never takes a test build here for the installed app: it
# opens an app by its bundle id, the login item included, and picks the newest copy it knows.
# The marker sits beside the app, so a copy of the app itself carries nothing.
touch dist/.metadata_never_index

cp "$BIN/TinysnapApp" "$APP/Contents/MacOS/Tinysnap"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

# Sign with the local identity when there is one, because an ad hoc signature is a
# hash of the binary: every rebuild becomes a new app as far as macOS is concerned,
# and Screen Recording has to be granted all over again. Run scripts/signing-identity.sh
# once to create it. CI signs the download with "Tinysnap Release" instead, from the
# repository's secrets (scripts/release-identity.sh), and sets TINYSNAP_REQUIRE_IDENTITY=1:
# an ad hoc download could never update, so the build fails rather than ship one.
IDENTITY="${TINYSNAP_SIGN_IDENTITY:-Tinysnap Local Signing}"

if ! security find-identity -p codesigning | grep -qF "$IDENTITY" \
    || ! codesign --force --sign "$IDENTITY" "$APP" 2>/dev/null; then
    if [ "${TINYSNAP_REQUIRE_IDENTITY:-}" = 1 ]; then
        echo "Could not sign with \"$IDENTITY\"." >&2
        exit 1
    fi
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
