#!/usr/bin/env bash
# drive-linux.sh <AppImage>: runs the AppImage CI just built on an X server with a window manager
# and a session bus, and drives it as a person would: hotkeys arrive as the gdbus calls GNOME's
# shortcuts make, the pointer and keys through xdotool. Each step is checked on the files, the
# clipboard and GNOME's settings it should change, with a screenshot kept in shots/. Run under
# dbus-run-session and xvfb-run at 1280 by 800.
set -u
appimage=$(readlink -f "$1")
shots=$PWD/shots
mkdir -p "$shots"
results=$shots/results.txt
failed=0
step=0

# check <name> <command...>: runs the command, records ok or FAIL, and keeps a screenshot.
check() {
  local name=$1
  shift
  step=$((step + 1))
  if "$@"; then echo "ok   $name" | tee -a "$results"; else echo "FAIL $name" | tee -a "$results"; failed=1; fi
  import -window root "$shots/$(printf %02d $step) ${name//\//-}.png" 2>/dev/null
}
# within <seconds> <command...>: true once the command is, tried every half second.
within() {
  local tries=$(($1 * 2))
  shift
  for _ in $(seq 1 $tries); do "$@" && return 0; sleep 0.5; done
  return 1
}

call() { gdbus call --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap --method com.faizanashiq.Tinysnap.Perform "$1" >/dev/null 2>&1; }
running() { gdbus introspect --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap >/dev/null 2>&1; }
stopped() { ! running; }
window() { xdotool search --name "$1" 2>/dev/null | head -1; }
shown() { [ -n "$(window "$1")" ]; }
gone() { [ -z "$(window "$1")" ]; }
press() { xdotool windowactivate --sync "$(window "$1")" key "$2"; }
drag() { xdotool mousemove "$1" "$2" sleep 0.2 mousedown 1 mousemove $((($1 + $3) / 2)) $((($2 + $4) / 2)) sleep 0.1 mousemove "$3" "$4" sleep 0.2 mouseup 1; }
size_is() { [ "$(identify -format %wx%h "$1" 2>/dev/null)" = "$2" ]; }
pixel_is() { [[ "$(convert "$1" -crop "1x1+$2+$3" +repage -format "%[hex:u.p{0,0}]" info: 2>/dev/null)" == "$4"* ]]; }
clipboard_has() { xclip -selection clipboard -o 2>/dev/null | grep -qF "$1"; }
clipboard_is() { [ "$(xclip -selection clipboard -o 2>/dev/null)" = "$1" ]; }
menu_runs() { grep -qF "Exec=\"$1\" %F" "$menu" 2>/dev/null; }
shortcuts() { gsettings get org.gnome.settings-daemon.plugins.media-keys custom-keybindings 2>/dev/null; }
has_shortcuts() { shortcuts | grep -q tinysnap-area; }
no_shortcuts() { ! shortcuts | grep -q tinysnap; }
gnome_has_print() { gsettings get org.gnome.shell.keybindings show-screenshot-ui 2>/dev/null | grep -q Print; }
gnome_lost_print() { ! gnome_has_print; }
captures_kept() { [ "$(ls -d "$library"/*/ 2>/dev/null | wc -l)" -ge "$1" ]; }
saved_and_closed() { ls "$saves"/*.png >/dev/null 2>&1 && gone "^Capture at"; }
first_save_is() { size_is "$(ls "$saves"/*.png | head -1)" "$1"; }
pinned_and_closed() { shown "^Pinned capture" && gone "Receipt.png"; }
removed() { [ ! -f "$menu" ] && [ ! -f "$icon" ]; }

export HOME=$PWD/e2e-home XDG_DATA_HOME=$PWD/e2e-home/.local/share XDG_CONFIG_HOME=$PWD/e2e-home/.config XDG_STATE_HOME=$PWD/e2e-home/.local/state
export LANG=en_US.UTF-8
# Services the bus starts later, dconf among them, write where readers look only if they are
# told the test home too: dconf's writer otherwise saves to the runner's own ~/.config.
dbus-update-activation-environment HOME XDG_DATA_HOME XDG_CONFIG_HOME XDG_STATE_HOME LANG
mkdir -p "$HOME/Pictures"
library=$XDG_DATA_HOME/Tinysnap/Library
menu=$XDG_DATA_HOME/applications/com.faizanashiq.Tinysnap.desktop
icon=$XDG_DATA_HOME/icons/hicolor/256x256/apps/com.faizanashiq.Tinysnap.png
saves=$HOME/Pictures/Screenshots

# The desktop: a known colour, a line of text and a QR code to capture.
openbox &
sleep 1
xsetroot -solid '#3366cc'
convert -size 900x200 xc:white -font DejaVu-Sans -pointsize 48 -fill black -annotate +40+120 'Tinysnap reads this line' "$shots/line.png"
qrencode -s 6 -o "$shots/code.png" 'https://example.com/tinysnap'
display -geometry +60+60 -title line "$shots/line.png" &
display -geometry +940+440 -title code "$shots/code.png" &
sleep 2

"$appimage" > "$shots/app.log" 2>&1 &
check "owns its D-Bus name" within 45 running
check "writes its menu entry for the AppImage" within 10 menu_runs "$appimage"
check "registers its shortcuts with GNOME" within 10 has_shortcuts
check "takes Print Screen from GNOME's screenshot tool" gnome_lost_print

# An area, dragged on the overlay, saved.
call area
sleep 1.5
drag 100 300 400 450
check "a dragged area opens an editor" within 15 shown "^Capture at"
press "^Capture at" ctrl+s
check "Save writes the area to the save folder and closes the editor" within 10 saved_and_closed
check "the saved area is 300 by 150" first_save_is 300x150

# The whole screen, copied.
call fullscreen
check "a fullscreen capture opens an editor" within 15 shown "^Capture at"
press "^Capture at" ctrl+c
check "Copy closes the editor" within 10 gone "^Capture at"
xclip -selection clipboard -t image/png -o > "$shots/clipboard.png" 2>/dev/null
check "Copy leaves the whole screen on the clipboard" size_is "$shots/clipboard.png" 1280x800
check "the copy holds the screen's own pixels" pixel_is "$shots/clipboard.png" 5 795 3366CC
check "the library keeps both captures" captures_kept 2

# Text and a QR code, read off the screen.
call text
sleep 1.5
drag 70 100 970 280
check "Capture Text copies the line" within 15 clipboard_has "Tinysnap reads this line"
call qr
sleep 1.5
drag 930 430 1275 795
check "Scan QR Code copies what the code holds" within 15 clipboard_is "https://example.com/tinysnap"

# Open With: a second launch with a file hands it to the running copy.
cp "$shots/line.png" "$HOME/Receipt.png"
"$appimage" "$HOME/Receipt.png" >> "$shots/app.log" 2>&1
check "Open With opens the file in the running copy" within 15 shown "Receipt.png"
press "Receipt.png" ctrl+p
check "Pin closes the editor and pins the capture" within 10 pinned_and_closed
press "^Pinned capture" Escape
check "Esc closes the pin" within 10 gone "^Pinned capture"

# Ended as a logout ends it, then moved: the next start follows the AppImage to its new place.
pkill -TERM -x Tinysnap
within 10 stopped
check "being ended gives Print Screen back to GNOME" within 10 gnome_has_print
mkdir -p "$HOME/Applications"
moved=$HOME/Applications/Tinysnap.AppImage
cp "$appimage" "$moved"
"$moved" >> "$shots/app.log" 2>&1 &
check "the moved AppImage starts" within 45 running
check "the menu entry follows the moved AppImage" within 10 menu_runs "$moved"

# Remove from This Computer: opened again with no editor, Tinysnap shows Settings.
"$moved" >> "$shots/app.log" 2>&1
check "a second launch shows Settings" within 15 shown "^Tinysnap Settings"
# The button is the last control in Settings, and Remove the dialog's first button. The window
# manager leaves the new dialog unfocused, so it is focused first.
press "^Tinysnap Settings" shift+Tab
xdotool key space
within 10 shown "^Tinysnap$"
import -window root "$shots/remove-dialog.png"
press "^Tinysnap$" Tab
xdotool key space
check "Remove from This Computer quits" within 15 stopped
check "it leaves no menu entry or icon" removed
check "it leaves no GNOME shortcuts, Print Screen given back" eval 'no_shortcuts && gnome_has_print'
check "it keeps the library" captures_kept 2

pkill -x Tinysnap 2>/dev/null
echo "$(grep -c '^ok' "$results") of $step passed"
exit $failed
