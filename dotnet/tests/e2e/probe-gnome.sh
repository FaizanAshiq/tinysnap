#!/usr/bin/env bash
# Can a GitHub runner run GNOME Shell headless, take a portal screenshot without a person, and
# drive keys? Writes what it found to probe/report.txt, with screenshots beside it.
set -u
mkdir -p probe
report=probe/report.txt
say() { echo "$*" | tee -a "$report"; }
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/tmp/runtime-$UID}; mkdir -p "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
say "gnome-shell $(gnome-shell --version 2>&1)"
# A real GNOME session sets these before anything starts, and the portals D-Bus starts later
# read them: the frontend picks GNOME's backend by desktop name, and the backend refuses to
# start without a session type.
export XDG_CURRENT_DESKTOP=GNOME XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY=wayland-0
dbus-update-activation-environment XDG_CURRENT_DESKTOP XDG_SESSION_TYPE WAYLAND_DISPLAY
gnome-shell --headless --wayland --virtual-monitor 1280x800 --unsafe-mode > probe/shell.log 2>&1 &
for i in $(seq 1 60); do gdbus introspect --session --dest org.gnome.Shell --object-path /org/gnome/Shell >/dev/null 2>&1 && break; sleep 1; done
gdbus introspect --session --dest org.gnome.Shell --object-path /org/gnome/Shell >/dev/null 2>&1 && say "shell: up after ${i}s" || say "shell: DOWN"
say "show-screenshot-ui: $(gsettings get org.gnome.shell.keybindings show-screenshot-ui 2>&1)"
eval_js() { gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval "$1" 2>&1; }
# GNOME starts in the Overview; a desktop session leaves it at once.
say "overview hidden: $(eval_js 'Main.overview.hide(); Main.overview.visible')"
say "portals: $(ls /usr/share/xdg-desktop-portal/portals/ 2>&1 | tr '\n' ' ')"
say "runtime dir: $(ls -a "$XDG_RUNTIME_DIR" | tr '\n' ' ')"
G_MESSAGES_DEBUG=all /usr/libexec/xdg-desktop-portal-gnome --verbose > probe/portal-gnome.log 2>&1 &
sleep 3
/usr/libexec/xdg-desktop-portal --replace --verbose > probe/portal.log 2>&1 &
sleep 5
say "portal owner pid: $(gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus --method org.freedesktop.DBus.GetConnectionUnixProcessID org.freedesktop.portal.Desktop 2>&1) explicit: $(pgrep -f libexec/xdg-desktop-portal$ | tr '\n' ' ')"
say "screenshot interface: $(gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop 2>&1 | grep -c 'interface org.freedesktop.portal.Screenshot')"
# Allow screenshots for unsandboxed apps, as the person's Allow would.
say "permission: $(gdbus call --session --dest org.freedesktop.impl.portal.PermissionStore --object-path /org/freedesktop/impl/portal/PermissionStore \
  --method org.freedesktop.impl.portal.PermissionStore.SetPermission screenshot true screenshot '' "['yes']" 2>&1)"
( timeout 60 gdbus monitor --session --dest org.freedesktop.portal.Desktop > probe/monitor.log 2>&1 & )
sleep 2
start=$(date +%s%N)
say "call: $(gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop \
  --method org.freedesktop.portal.Screenshot.Screenshot '' "{'handle_token': <'probe1'>, 'interactive': <false>}" 2>&1)"
shot() { gdbus call --session --dest org.gnome.Shell.Screenshot --object-path /org/gnome/Shell/Screenshot \
  --method org.gnome.Shell.Screenshot.Screenshot false false "$PWD/probe/$1.png" >/dev/null 2>&1; }
key() { gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval "let b; try { b = global.stage.context.get_backend(); } catch (e) { b = imports.gi.Clutter.get_default_backend(); }
const C = imports.gi.Clutter; const k = b.get_default_seat().create_virtual_device(C.InputDeviceType.KEYBOARD_DEVICE);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_$1, C.KeyState.PRESSED);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_$1, C.KeyState.RELEASED); 'sent'" >/dev/null 2>&1; }
sleep 3
shot dialog
say "windows: $(eval_js 'global.display.list_all_windows().map(w => w.get_title() + "/" + w.get_wm_class() + "/" + w.get_window_type()).join(", ")')"
say "modal dialogs: $(eval_js 'Main.modalCount + " " + (global.stage.key_focus ? global.stage.key_focus.toString() : "none")')"
# Whatever asks, focused, then answered by keyboard: Return takes the default button.
say "focus: $(eval_js 'const ws = global.display.list_all_windows(); if (ws.length) ws[ws.length - 1].activate(global.get_current_time()); ws.length')"
sleep 1
shot focused
key Return
for i in $(seq 1 40); do grep -q "Response" probe/monitor.log && break; sleep 0.25; done
grep -q "Response" probe/monitor.log || { key Tab; key Return; for i in $(seq 1 40); do grep -q "Response" probe/monitor.log && break; sleep 0.25; done; }
say "portal answer after $(( ($(date +%s%N) - start) / 1000000 )) ms"
say "permission now: $(gdbus call --session --dest org.freedesktop.impl.portal.PermissionStore --object-path /org/freedesktop/impl/portal/PermissionStore \
  --method org.freedesktop.impl.portal.PermissionStore.Lookup screenshot screenshot 2>&1)"
# A second capture: does the stored answer let it through without a dialog?
start=$(date +%s%N)
gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop \
  --method org.freedesktop.portal.Screenshot.Screenshot '' "{'handle_token': <'probe2'>, 'interactive': <false>}" >/dev/null 2>&1
for i in $(seq 1 40); do [ "$(grep -c Response probe/monitor.log)" -ge 2 ] && break; sleep 0.25; done
say "second answer after $(( ($(date +%s%N) - start) / 1000000 )) ms, responses: $(grep -c Response probe/monitor.log)"
grep -A4 "Response" probe/monitor.log >> "$report"
uri=$(grep -o "file://[^'\"]*" probe/monitor.log | head -1)
[ -n "$uri" ] && cp "${uri#file://}" probe/portal.png && say "screenshot: $(file -b probe/portal.png)"
auth=$(ls "$XDG_RUNTIME_DIR"/.mutter-Xwaylandauth.* 2>/dev/null | head -1)
say "xwayland auth: ${auth:-none}"
for d in :0 :1; do say "xwayland $d: $(DISPLAY=$d XAUTHORITY=$auth timeout 10 xdpyinfo 2>&1 | grep -E 'name of display|dimensions|unable' | tr '\n' ' ')"; done
js='let b; try { b = global.stage.context.get_backend(); } catch (e) { b = imports.gi.Clutter.get_default_backend(); }
const C = imports.gi.Clutter; const k = b.get_default_seat().create_virtual_device(C.InputDeviceType.KEYBOARD_DEVICE);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_Print, C.KeyState.PRESSED);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_Print, C.KeyState.RELEASED); "sent"'
say "eval keys: $(gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval "$js" 2>&1)"
sleep 2
say "screenshot ui after Print: $(gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval 'Main.screenshotUI.visible' 2>&1)"
say "done"
