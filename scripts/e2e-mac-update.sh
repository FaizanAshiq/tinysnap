#!/usr/bin/env bash
# e2e-mac-update.sh <Tinysnap-mac.zip> <next>: the downloaded Mac app updating itself, as a person's
# would. Unpacked into an Applications folder it can write to and started with a newer build waiting
# in <next> (its Tinysnap-mac.zip and version), it downloads it, checks it is signed exactly as
# itself, quits, and starts again as the new version, saying so. Run on CI's Mac.
set -euo pipefail

apps="$HOME/Applications"
app="$apps/Tinysnap.app"
next="$(cd "$2" && pwd)"
version="$(cat "$next/version")"
failed=0
check() {
    if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; failed=1; fi
}
shown() { plutil -extract CFBundleShortVersionString raw "$app/Contents/Info.plist"; }

mkdir -p "$apps"
ditto -x -k "$1" "$apps"
before="$(shown)"
cat > "$next/latest.json" <<EOF
{"tag_name": "v$version", "assets": [{"name": "Tinysnap-mac.zip", "browser_download_url": "file://$next/Tinysnap-mac.zip"}]}
EOF
echo "     $before installed, $version waiting"

TINYSNAP_UPDATE_FEED="$next" "$app/Contents/MacOS/Tinysnap" > app.log 2>&1 &
first=$!
start=$(date +%s)
for _ in $(seq 1 120); do
    if [ "$(shown 2>/dev/null)" = "$version" ] && ! kill -0 "$first" 2>/dev/null \
        && pgrep -f "$app/Contents/MacOS/Tinysnap" >/dev/null; then break; fi
    sleep 1
done
echo "     after $(($(date +%s) - start)) s"
sleep 3

check "the copy that downloaded the update quit for it" '! kill -0 "$first" 2>/dev/null'
check "the app in Applications is $version" '[ "$(shown)" = "$version" ]'
check "the new version is running" 'pgrep -f "$app/Contents/MacOS/Tinysnap" >/dev/null'
# Set just before quitting and taken by the new copy when it says what it was updated to.
check "the new version said it was updated" '! defaults read com.faizanashiq.tinysnap TinysnapUpdatedTo >/dev/null 2>&1'
check "nothing is left beside it" '[ ! -e "$app.replaced" ]'

pkill -f "$app/Contents/MacOS/Tinysnap" || true
exit $failed
