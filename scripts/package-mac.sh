#!/usr/bin/env bash
# The Mac app as it is released, from dist/Tinysnap.app as build.sh left it:
#
#   dist/Tinysnap-mac.dmg  what people download: the app beside a link to Applications, to drag onto
#   dist/Tinysnap-mac.zip  what a copy that keeps itself up to date downloads and unpacks
#
# CI runs it after a universal build signed with the release identity.
set -euo pipefail

APP="dist/Tinysnap.app"
[ -d "$APP" ] || { echo "No $APP: run build.sh first." >&2; exit 1; }

# ditto keeps the signature and extended attributes that zip would drop.
rm -f dist/Tinysnap-mac.zip dist/Tinysnap-mac.dmg
ditto -c -k --keepParent "$APP" dist/Tinysnap-mac.zip

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
ditto "$APP" "$STAGE/Tinysnap.app"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname Tinysnap -srcfolder "$STAGE" -ov -format UDZO dist/Tinysnap-mac.dmg >/dev/null

echo "Packed dist/Tinysnap-mac.dmg and dist/Tinysnap-mac.zip"
