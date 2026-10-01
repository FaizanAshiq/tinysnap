#!/usr/bin/env bash
# Can a GitHub runner run GNOME Shell headless, take a portal screenshot without a person, and
# drive keys? Writes what it found to probe/report.txt, with screenshots beside it.
set -u
mkdir -p probe
report=probe/report.txt
say() { echo "$*" | tee -a "$report"; }
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/tmp/runtime-$UID}; mkdir -p "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
say "gnome-shell $(gnome-shell --version 2>&1)"
gnome-shell --headless --wayland --virtual-monitor 1280x800 --unsafe-mode > probe/shell.log 2>&1 &
for i in $(seq 1 60); do gdbus introspect --session --dest org.gnome.Shell --object-path /org/gnome/Shell >/dev/null 2>&1 && break; sleep 1; done
gdbus introspect --session --dest org.gnome.Shell --object-path /org/gnome/Shell >/dev/null 2>&1 && say "shell: up after ${i}s" || say "shell: DOWN"
say "show-screenshot-ui: $(gsettings get org.gnome.shell.keybindings show-screenshot-ui 2>&1)"
# The portal picks its GNOME backend by desktop name, and the backend needs the Wayland display.
export XDG_CURRENT_DESKTOP=GNOME WAYLAND_DISPLAY=wayland-0 GDK_BACKEND=wayland
say "portals: $(ls /usr/share/xdg-desktop-portal/portals/ 2>&1 | tr '\n' ' ')"
/usr/libexec/xdg-desktop-portal-gnome --verbose > probe/portal-gnome.log 2>&1 &
sleep 2
/usr/libexec/xdg-desktop-portal --verbose > probe/portal.log 2>&1 &
for i in $(seq 1 20); do gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop 2>/dev/null | grep -q portal.Screenshot && break; sleep 0.5; done
say "screenshot interface: $(gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop 2>&1 | grep -c 'interface org.freedesktop.portal.Screenshot')"
# Allow screenshots for unsandboxed apps, as the person's Allow would.
say "permission: $(gdbus call --session --dest org.freedesktop.impl.portal.PermissionStore --object-path /org/freedesktop/impl/portal/PermissionStore \
  --method org.freedesktop.impl.portal.PermissionStore.SetPermission screenshot true screenshot '' "['yes']" 2>&1)"
( timeout 25 gdbus monitor --session --dest org.freedesktop.portal.Desktop > probe/monitor.log 2>&1 & )
sleep 2
start=$(date +%s%N)
say "call: $(gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop \
  --method org.freedesktop.portal.Screenshot.Screenshot '' "{'handle_token': <'probe1'>, 'interactive': <false>}" 2>&1)"
for i in $(seq 1 80); do grep -q "Response" probe/monitor.log && break; sleep 0.25; done
say "portal answer after $(( ($(date +%s%N) - start) / 1000000 )) ms"
grep -A4 "Response" probe/monitor.log >> "$report"
uri=$(grep -o "file://[^'\"]*" probe/monitor.log | head -1)
[ -n "$uri" ] && cp "${uri#file://}" probe/portal.png && say "screenshot: $(file -b probe/portal.png)"
for d in :0 :1; do DISPLAY=$d timeout 10 xdpyinfo >/dev/null 2>&1 && say "xwayland: $d"; done
js='let b; try { b = global.stage.context.get_backend(); } catch (e) { b = imports.gi.Clutter.get_default_backend(); }
const C = imports.gi.Clutter; const k = b.get_default_seat().create_virtual_device(C.InputDeviceType.KEYBOARD_DEVICE);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_Print, C.KeyState.PRESSED);
k.notify_keyval(global.get_current_time() * 1000, C.KEY_Print, C.KeyState.RELEASED); "sent"'
say "eval keys: $(gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval "$js" 2>&1)"
sleep 2
say "screenshot ui after Print: $(gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval 'Main.screenshotUI.visible' 2>&1)"
say "done"
