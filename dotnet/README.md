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

CI runs every test on Windows and the Core and app tests on macOS. Its `e2e` job then installs
the x64 `Setup.exe` it just built on a Windows desktop and drives it with real keys and a real
mouse (`tests/e2e/drive.ps1`): the hotkeys, the area overlay, saving, copying, the library,
reading text, opening a file with a second launch, pinning, editors fitting a 1024 by 768
screen, updating itself to a newer build, and uninstalling. It keeps a screenshot of each step as the `e2e-screenshots` artifact.

On Linux, CI runs the Linux tests on Ubuntu 24.04 under a session bus and an X server, builds an
AppImage for x64 and ARM64 with Tesseract and fifteen languages inside, runs its self-check with
no Tesseract on the system, and drives each AppImage end to end on an X11 desktop
(`tests/e2e/drive-linux.sh`): GNOME's shortcut calls, an area saved, the screen copied, text and
a QR code read, Open With, a pin, the AppImage moved, the AppImage updating itself to a newer
build, and Remove from This Computer. Its
screenshots are the `linux-e2e-linux-x64` and `linux-e2e-linux-arm64` artifacts. A last job runs
the x64 AppImage on Wayland in a headless GNOME Shell (`tests/e2e/gnome-windows.sh`), with a
small stand-in for the screenshot portal, which GNOME's own cannot run there: the self-check in
the session, nothing showing at start, the overlay full screen over the top bar with the frozen
screen in it, an editor for a fullscreen capture, and Capture Window through GNOME's picker.

Print Screen on Windows is caught by a keyboard hook before Windows' own screen snip sees it, so
it opens Tinysnap whatever Windows Settings says, and it goes back to the snip when Tinysnap
quits. A shortcut that is taken all the same, by another app or the system, is told once at
launch with how to free it, on every platform.

What only a person on a Windows machine can check: capture on real monitors at mixed scales,
Print Screen with the screen snip turned on, pasting into other apps, dragging out, how a pin resizes on a real wheel and touchpad, the
thumbnail's slide and swipe, the tray menu, start at login, the Recycle Bin, a second launch
with no file bringing the first forward, text in the person's own languages, the desktop
picture as a backdrop, the ARM64 build, and how it all feels.

What only a person on a real GNOME desktop can check: that the overlay takes keys and drags at
once after Print Screen (GNOME's virtual input does not reach XWayland windows in CI, and GNOME
shows its top bar over a full screen window that is not focused), GNOME's screenshot permission
and the flash on each capture, GNOME's own window picker, the tray where GNOME has one, the
shortcuts in GNOME Settings, a 200% monitor, and how it feels.

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

## Packaging and releasing

Every push builds, for x64 and ARM64, a per-user `Setup.exe` (no admin prompt), a portable zip
and the full package with Velopack, kept for two weeks as the `package` job's artifacts. Nothing
is published from CI. The Windows app carries the Mac app's version, and both ship in the same
GitHub release.

1. Run the build on a real Windows machine first: install `Tinysnap-win-x64-Setup.exe` from the
   artifacts and go through the checks above.
2. Tag and release as the Mac app does, then attach the Windows files from the tag's run: for
   each of `win-x64` and `win-arm64`, `Tinysnap-<runtime>-Setup.exe`, `Tinysnap-<runtime>-Portable.zip`,
   and the update feed and package, `releases.<runtime>.json` and `Tinysnap-X.Y.Z-<runtime>-full.nupkg`.
3. Copy `packaging/winget/FaizanAshiq.Tinysnap/<version>` for the new version, fill in each
   installer's `InstallerSha256` (`Get-FileHash .\Tinysnap-win-x64-Setup.exe`), run
   `winget validate` on the folder, and send it to `microsoft/winget-pkgs` with `wingetcreate submit`.
4. Once winget has it, add the Windows install line (`winget install FaizanAshiq.Tinysnap`) to
   the site's Tinysnap page, `config/tinysnap.ts` in the site repo, by PR.

For Linux, attach from the same run, for each of `linux-x64` and `linux-arm64`,
`Tinysnap-<runtime>.AppImage`, `releases.<runtime>.json` and `Tinysnap-X.Y.Z-<runtime>-full.nupkg`,
after the Wayland path has been checked on a real GNOME machine.

Installed copies keep themselves up to date. Once a week, ten seconds after the first start and
then a week after the last look, which a restart does not hurry, Tinysnap reads the feed for its
platform and processor from the latest release, downloads the
full package, and installs it the first minute nothing of Tinysnap's is open: it quits, Velopack
puts the new version in place, and it starts again saying "Updated to Tinysnap X". Quit before
that, and it is installed at the next start. A release without the feed and package files leaves
every copy where it is. CI proves the whole round on Windows and on Linux: each e2e run updates the
installed copy to the same build packed one version on, served from a folder named in
`TINYSNAP_UPDATE_FEED`.

Third-party notices are in `THIRD-PARTY-NOTICES.md`.
