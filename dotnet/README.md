# Tinysnap for Windows and Linux

Tinysnap on Windows and on Linux with GNOME, built on .NET, Avalonia and SkiaSharp. It
captures an area with Print Screen, a window, or the whole monitor with Ctrl+Shift+1, or from
the tray, and opens it in an editor with every tool, undo, zoom and the style bar, or as a
thumbnail in the corner. From there a capture is copied, saved, dragged into another app or
pinned above every window. Every capture is kept 30 days in a library window and reopens
editable; Settings changes the hotkeys and the rest, and the tray menu has every action,
Repeat Last Area and Delayed Capture included. Copy Text reads text on the device, with
Windows' own recogniser or, on Linux, the Tesseract bundled in the AppImage, and Scan QR Code
reads codes with ZXing.Net. The editor also measures, frames a capture with a backdrop, and
sets its export size. Both ship in the same GitHub release as the Mac app: the Windows installer
since 1.2.0, the Linux AppImage since 1.3.0.

The Mac app in the rest of this repository is unchanged, and both read the same library
files. `tests/fixtures` holds one library entry written by each app, and each app's tests
open the other's.

| Project | Holds |
| --- | --- |
| `src/Tinysnap.Core` | Editing, rendering, export and the library, ported from `Sources/TinysnapCore` |
| `src/Tinysnap.Platform` | What the app needs from the operating system, as interfaces |
| `src/Tinysnap.App` | Every window: the area overlay, the editor, pins, the thumbnail, the library, Settings, the tray |
| `src/Tinysnap.Windows` | GDI capture, window listing, hotkeys, the clipboard, the Recycle Bin, start at login, one copy at a time, text reading, and the Windows exe |
| `src/Tinysnap.Linux` | Capture through GNOME's portal or X11, hotkeys as GNOME shortcuts over D-Bus, the clipboard, the Trash, the menu entry and start at login, Tesseract, and the Linux program |
| `src/Tinysnap.Dev` | The same app on a Mac, on a painted desktop, for development |

```bash
dotnet test dotnet/tests/Tinysnap.Core.Tests     # anywhere
dotnet test dotnet/tests/Tinysnap.App.Tests      # anywhere, headless
dotnet test dotnet/Tinysnap.slnx                 # on Windows, the platform tests too
dotnet test dotnet/tests/Tinysnap.Linux.Tests    # on Linux, under dbus-run-session and xvfb-run
dotnet run --project dotnet/src/Tinysnap.Dev     # the app on a Mac
```

Print Screen on Windows is caught by a keyboard hook before Windows' own screen snip sees it, so
it opens Tinysnap whatever Windows Settings says, and it goes back to the snip when Tinysnap
quits. A shortcut that is taken all the same, by another app or the system, is told once at
launch with how to free it, on every platform.

## Linux

GNOME 46 or later (Ubuntu 24.04, Fedora 40), on Wayland or X11. Tinysnap runs on X11, which
XWayland provides on Wayland. Everything it writes lives where GNOME expects it:

| What | Where |
| --- | --- |
| Preferences | `~/.config/Tinysnap/preferences.json` |
| Library | `~/.local/share/Tinysnap/Library` |
| Menu entry and Open With | `~/.local/share/applications/com.faizanashiq.Tinysnap.desktop`, rewritten on every start |
| Start at login | `~/.config/autostart/com.faizanashiq.Tinysnap.desktop` |
| Hotkeys | GNOME custom shortcuts named `Tinysnap: ...`, there while Tinysnap runs |

Print Screen is taken from GNOME's screenshot tool while Tinysnap runs and given back when it
quits, is ended, or is started again after a crash. Settings, Remove from This Computer, takes
away the menu entry, start at login and the shortcuts; the person then deletes the AppImage.

## Updates

Installed copies keep themselves up to date. Once a week, ten seconds after the first start and
then a week after the last look by the calendar, which a restart does not hurry and sleep does not
hold back (the date is checked hourly, since a timer stops while the computer sleeps), Tinysnap reads the feed for its
platform and processor from the latest release, downloads the
full package, and installs it the first minute nothing of Tinysnap's is open: it quits, Velopack
puts the new version in place, and it starts again saying "Updated to Tinysnap X". Quit before
that, and it is installed at the next start.

Third-party notices are in `THIRD-PARTY-NOTICES.md`.
