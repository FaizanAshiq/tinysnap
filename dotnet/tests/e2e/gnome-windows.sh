#!/usr/bin/env bash
# gnome-windows.sh <AppImage>: runs Tinysnap on Wayland in a headless GNOME Shell, through
# XWayland, and checks what GNOME makes of its windows: nothing showing at start, the area
# overlay full screen over the top bar showing the frozen screen, an editor for a fullscreen
# capture, and Capture Window through GNOME's own picker. A stand-in answers the screenshot
# portal, which GNOME's backend cannot run here. GNOME's virtual keyboard and pointer do not reach
# XWayland windows, so typing and dragging stay a check on a real desktop. Run under
# dbus-run-session. Writes gnome/results.txt and screenshots beside it.
set -u
app=$(readlink -f "$1")
mkdir -p gnome
results=$PWD/gnome/results.txt
failed=0
note() { echo "     $*" | tee -a "$results"; }
check() {
  local name=$1
  shift
  if "$@"; then echo "ok   $name" | tee -a "$results"; else echo "FAIL $name" | tee -a "$results"; failed=1; fi
}
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/tmp/runtime-$UID}; mkdir -p "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
export XDG_CURRENT_DESKTOP=GNOME XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY=wayland-0
dbus-update-activation-environment XDG_CURRENT_DESKTOP XDG_SESSION_TYPE WAYLAND_DISPLAY
# The portal's picture: a red band where GNOME's top bar is, so a screenshot shows whether the
# overlay covers it. The stand-in owns the portal's name before anything could start the real one.
convert -size 1280x800 xc:'#3366cc' -fill '#cc0000' -draw 'rectangle 0,0 1279,31' gnome/desktop.png
python3 "$(dirname "$0")/fake-portal.py" gnome/desktop.png > gnome/portal.log 2>&1 &
for _ in $(seq 1 50); do gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop >/dev/null 2>&1 && break; sleep 0.2; done

eval_js() { gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval "$1" 2>&1; }
shot() { gdbus call --session --dest org.gnome.Shell.Screenshot --object-path /org/gnome/Shell/Screenshot \
  --method org.gnome.Shell.Screenshot.Screenshot false false "$PWD/gnome/$1.png" >/dev/null 2>&1; }
pixel() { convert "gnome/$1.png" -crop "1x1+$2+$3" +repage -format '%[hex:u.p{0,0}]' info: 2>/dev/null; }
# Tinysnap's windows as GNOME sees them: title, place, size and state, one per line.
windows() {
  eval_js 'global.get_window_actors().map(a => a.meta_window).filter(w => w.get_wm_class() === "Tinysnap").map(w => { const r = w.get_frame_rect();
    return `${w.get_title()} at ${r.x},${r.y} ${r.width}x${r.height}${w.is_fullscreen() ? " fullscreen" : ""}${w.is_above() ? " above" : ""}${w.has_focus() ? " focused" : ""}`; }).join(";")' |
    sed -e "s/^(true, '\"//" -e "s/\"')$//" | tr ';' '\n'
}
call() { gdbus call --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap --method com.faizanashiq.Tinysnap.Perform "$1" >/dev/null 2>&1; }
within() { local tries=$(($1 * 10)); shift; for _ in $(seq 1 $tries); do "$@" && return 0; sleep 0.1; done; return 1; }
shown() { windows | grep -q "$1"; }
none_shown() { [ -z "$(windows)" ]; }
self_check_passes() { ! grep -q FAILED gnome/self-check.txt && grep -q "^ok" gnome/self-check.txt; }
portal_asked() { grep -q "interactive $1" gnome/portal.log; }
started() { for _ in $(seq 1 100); do gdbus introspect --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap >/dev/null 2>&1 && return 0; sleep 0.2; done; return 1; }

gnome-shell --headless --wayland --virtual-monitor 1280x800 --unsafe-mode > gnome/shell.log 2>&1 &
# Up once it answers: its name is taken a moment before it can.
for _ in $(seq 1 60); do eval_js 'Main.layoutManager !== undefined' | grep -q "'true'" && break; sleep 1; done
note "top bar: $(eval_js 'Main.overview.hide(); Main.panel.height + " high"')"
auth=$(ls "$XDG_RUNTIME_DIR"/.mutter-Xwaylandauth.* 2>/dev/null | head -1)
note "screen as XWayland reports it: $(DISPLAY=:0 XAUTHORITY=$auth xrandr --listmonitors 2>&1 | tail -n +2 | tr -s ' ')"

DISPLAY=:0 XAUTHORITY=$auth timeout 120 "$app" --self-check "$PWD/gnome/self-check.txt" > gnome/self-check.log 2>&1
check "the self-check passes in a GNOME session, fonts included" self_check_passes
sed 's/^/     /' gnome/self-check.txt >> "$results"

DISPLAY=:0 XAUTHORITY=$auth "$app" > gnome/app.log 2>&1 &
started
# The overlay warms up in the first seconds; GNOME would pull a window placed off screen into view.
start_clear=true
for _ in $(seq 1 30); do none_shown || start_clear=false; sleep 0.1; done
check "nothing shows while the overlay warms up" $start_clear

call area
check "the overlay covers the whole screen, full screen" within 10 shown "at 0,0 1280x800 fullscreen"
check "the overlay has the keyboard" within 5 shown "fullscreen.*focused"
sleep 1
shot overlay
note "overlay pixels: middle $(pixel overlay 300 600), top $(pixel overlay 300 10)"
# Which of red, green and blue leads in a pixel, dimmed or not.
leads() { local hex=$(pixel overlay "$1" "$2"); local r=$((16#${hex:0:2})) g=$((16#${hex:2:2})) b=$((16#${hex:4:2}));
  case $3 in blue) [ $b -gt $((r + 40)) ] && [ $b -gt $((g + 40)) ];; red) [ $r -gt $((g + 40)) ] && [ $r -gt $((b + 40)) ];; esac; }
# The picture's blue drawn, not left black, and its red band where the top bar would show.
check "the overlay shows the frozen screen" leads 300 600 blue
check "the overlay covers GNOME's top bar" leads 300 10 red
note "windows: $(windows | tr '\n' ';')"
# Closed from outside, since no key reaches it here; the next copy starts once this one has let
# go of its name, or it would hand over to it and quit.
pkill -x Tinysnap
for _ in $(seq 1 50); do gdbus introspect --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap >/dev/null 2>&1 || break; sleep 0.2; done
DISPLAY=:0 XAUTHORITY=$auth "$app" >> gnome/app.log 2>&1 &
started

call fullscreen
check "a fullscreen capture opens an editor" within 10 shown "^Capture at"
check "the editor has the keyboard" within 5 shown "^Capture at.*focused"
note "windows: $(windows | tr '\n' ';')"
shot editor

call window
check "Capture Window opens GNOME's own picker" within 10 portal_asked True
note "windows: $(windows | tr '\n' ';')"
shot window

grep -v "No such schema" gnome/app.log | sed 's/^/     /' >> "$results"
echo "$(grep -c '^ok' "$results") checks passed"
exit $failed
