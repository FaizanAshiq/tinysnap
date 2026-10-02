#!/usr/bin/env bash
# Runs the published Linux build on an X server with a window manager and a session bus, as
# GNOME's shortcuts reach it: a fullscreen capture must open an editor, Copy must leave a PNG on
# the clipboard after the editor closes, and the menu entry must be written. Run under
# dbus-run-session and xvfb-run. Screenshots and the app's output land in smoke/.
set -u
app=$1
mkdir -p smoke
results=smoke/results.txt
failed=0
check() { if eval "$2"; then echo "ok   $1" | tee -a "$results"; else echo "FAIL $1" | tee -a "$results"; failed=1; fi; }
until_true() { for _ in $(seq 1 "$1"); do eval "$2" && return 0; sleep 0.5; done; return 1; }
export HOME=$PWD/smoke/home XDG_DATA_HOME=$PWD/smoke/home/.local/share XDG_CONFIG_HOME=$PWD/smoke/home/.config
mkdir -p "$HOME"
openbox &
sleep 1
"$app" > smoke/app.log 2>&1 &
pid=$!
call() { gdbus call --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap --method com.faizanashiq.Tinysnap.Perform "$1" >/dev/null 2>&1; }
check "owns its D-Bus name" 'until_true 60 "gdbus introspect --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap >/dev/null 2>&1"'
check "writes its menu entry" 'until_true 20 "test -f $XDG_DATA_HOME/applications/com.faizanashiq.Tinysnap.desktop"'
start=$(date +%s%N)
call fullscreen
check "a fullscreen capture opens an editor" 'until_true 30 "xdotool search --name \"^Capture at\" >/dev/null"'
echo "editor after $(( ($(date +%s%N) - start) / 1000000 )) ms" | tee -a "$results"
import -window root smoke/editor.png
editor=$(xdotool search --name "^Capture at" | head -1)
xdotool windowactivate --sync "$editor" key ctrl+c
check "Copy closes the editor" 'until_true 20 "! xdotool search --name \"^Capture at\" >/dev/null"'
check "Copy leaves a PNG on the clipboard" 'until_true 20 "xclip -selection clipboard -t TARGETS -o 2>/dev/null | grep -q image/png"'
xclip -selection clipboard -t image/png -o > smoke/clipboard.png 2>/dev/null
check "the PNG is the whole screen" '[ "$(identify -format %wx%h smoke/clipboard.png 2>/dev/null)" = 1280x800 ]'
check "a second launch hands over and quits" 'timeout 20 "$app" && kill -0 $pid'
kill $pid 2>/dev/null
exit $failed
