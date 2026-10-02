#!/usr/bin/env bash
# gnome-windows.sh <AppImage>: runs Tinysnap on Wayland in a headless GNOME Shell, through
# XWayland, and asks GNOME where each of its windows lands: at start, with the area overlay up,
# and with an editor open. A stand-in answers the screenshot portal, which GNOME cannot run here.
# Run under dbus-run-session. Writes gnome/report.txt and screenshots beside it.
set -u
app=$(readlink -f "$1")
mkdir -p gnome
report=$PWD/gnome/report.txt
say() { echo "$*" | tee -a "$report"; }
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/tmp/runtime-$UID}; mkdir -p "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
export XDG_CURRENT_DESKTOP=GNOME XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY=wayland-0
dbus-update-activation-environment XDG_CURRENT_DESKTOP XDG_SESSION_TYPE WAYLAND_DISPLAY
# The portal's picture: a red band where GNOME's top bar is, so a screenshot shows whether the
# overlay covers it. The stand-in owns the portal's name before anything could start the real one.
convert -size 1280x800 xc:'#3366cc' -fill '#cc0000' -draw 'rectangle 0,0 1279,31' gnome/desktop.png
python3 "$(dirname "$0")/fake-portal.py" gnome/desktop.png > gnome/portal.log 2>&1 &
for i in $(seq 1 50); do gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop >/dev/null 2>&1 && break; sleep 0.2; done
eval_js() { gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval "$1" 2>&1; }
gnome-shell --headless --wayland --virtual-monitor 1280x800 --unsafe-mode > gnome/shell.log 2>&1 &
# Up once it answers: its name is taken a moment before it can.
for i in $(seq 1 60); do eval_js 'Main.layoutManager !== undefined' | grep -q "'true'" && break; sleep 1; done
shot() { gdbus call --session --dest org.gnome.Shell.Screenshot --object-path /org/gnome/Shell/Screenshot \
  --method org.gnome.Shell.Screenshot.Screenshot false false "$PWD/gnome/$1.png" >/dev/null 2>&1; }
key() { eval_js "let b; try { b = global.stage.context.get_backend(); } catch (e) { b = imports.gi.Clutter.get_default_backend(); }
const C = imports.gi.Clutter; const k = b.get_default_seat().create_virtual_device(C.InputDeviceType.KEYBOARD_DEVICE);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_$1, C.KeyState.PRESSED);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_$1, C.KeyState.RELEASED); 'sent'" >/dev/null; }
windows() {
  eval_js 'global.get_window_actors().map(a => { const w = a.meta_window; const r = w.get_frame_rect();
    return `${w.get_title()} [${w.get_wm_class()}] at ${r.x},${r.y} ${r.width}x${r.height}${w.is_above() ? " above" : ""}${w.is_fullscreen() ? " fullscreen" : ""}${a.visible ? "" : " hidden"}`; }).join(" | ")'
}
call() { gdbus call --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap --method com.faizanashiq.Tinysnap.Perform "$1" >/dev/null 2>&1; }

say "shell up after ${i}s: $(eval_js 'Main.overview.hide(); "top bar " + Main.panel.height + " high"')"
sleep 1
auth=$(ls "$XDG_RUNTIME_DIR"/.mutter-Xwaylandauth.* 2>/dev/null | head -1)
DISPLAY=:0 XAUTHORITY=$auth "$app" > gnome/app.log 2>&1 &
for i in $(seq 1 100); do gdbus introspect --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap >/dev/null 2>&1 && break; sleep 0.2; done
say "tinysnap up after $((i / 5))s"
# The first seconds, when the overlay warms up out of sight.
for i in $(seq 1 15); do say "start +$((i * 200)) ms: $(windows)"; [ $i = 3 ] && shot start; sleep 0.2; done

call area
sleep 1.5
say "overlay: $(windows)"
shot overlay
key Escape
sleep 1
say "after Esc: $(windows)"

call fullscreen
sleep 2
say "editor: $(windows)"
shot editor
key Escape
key Escape

call window
sleep 2
say "capture window: $(windows)"
shot window
say "portal: $(tr '\n' ' ' < gnome/portal.log)"
say "done"
