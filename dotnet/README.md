# Tinysnap for Windows

A port of Tinysnap to Windows, in progress, built on .NET, Avalonia and SkiaSharp. So far it
captures an area with Print Screen, a window, or the whole monitor under the pointer with
Ctrl+Shift+1, or from the tray, and opens it in an editor with every tool, undo, zoom and
the style bar, or as a thumbnail in the corner. From there a capture is copied, saved,
dragged into another app or pinned above every window. Every capture is kept 30 days in a
library window and reopens editable; Settings changes the hotkeys and the rest, and the tray
menu has every action, Repeat Last Area and Delayed Capture included. Copy Text reads text with
Windows' own recogniser and Scan QR Code reads codes with ZXing.Net, both on the device; the
editor also measures, frames a capture with a backdrop, and sets its export size. What remains
is the installer and the release.

The Mac app in the rest of this repository is unchanged, and both read the same library
files. `tests/fixtures` holds one library entry written by each app, and each app's tests
open the other's.

| Project | Holds |
| --- | --- |
| `src/Tinysnap.Core` | Editing, rendering, export and the library, ported from `Sources/TinysnapCore` |
| `src/Tinysnap.Platform` | What the app needs from the operating system, as interfaces |
| `src/Tinysnap.App` | Every window: the area overlay, the editor, pins, the thumbnail, the library, Settings, the tray |
| `src/Tinysnap.Windows` | GDI capture, window listing, hotkeys, the clipboard, the Recycle Bin, start at login, one copy at a time, text and QR reading, and the Windows exe |
| `src/Tinysnap.Dev` | The same app on a Mac, on a painted desktop, for development |

```bash
dotnet test dotnet/tests/Tinysnap.Core.Tests     # anywhere
dotnet test dotnet/tests/Tinysnap.App.Tests      # anywhere, headless
dotnet test dotnet/Tinysnap.slnx                 # on Windows, the platform tests too
dotnet run --project dotnet/src/Tinysnap.Dev     # the app on a Mac
```

CI runs every test on Windows and the Core and app tests on macOS. Its `e2e` job then installs
the x64 `Setup.exe` it just built on a Windows desktop and drives it with real keys and a real
mouse (`tests/e2e/drive.ps1`): the hotkeys, the area overlay, saving, copying, the library,
reading text, opening a file with a second launch, pinning, editors fitting a 1024 by 768
screen, and uninstalling. It keeps a screenshot of each step as the `e2e-screenshots` artifact.

What only a person on a Windows machine can check: capture on real monitors at mixed scales,
pasting into other apps, dragging out, how a pin resizes on a real wheel and touchpad, the
thumbnail's slide and swipe, the tray menu, start at login, the Recycle Bin, a second launch
with no file bringing the first forward, text in the person's own languages, the desktop
picture as a backdrop, the ARM64 build, and how it all feels.

## Packaging and releasing

Every push builds, for x64 and ARM64, a per-user `Setup.exe` (no admin prompt), a portable zip
and the full package with Velopack, kept for two weeks as the `package` job's artifacts. Nothing
is published from CI. The Windows app carries the Mac app's version, and both ship in the same
GitHub release.

1. Run the build on a real Windows machine first: install `Tinysnap-win-x64-Setup.exe` from the
   artifacts and go through the checks above.
2. Tag and release as the Mac app does, then attach the Windows files from the tag's run:
   `gh release upload vX.Y.Z Tinysnap-win-x64-Setup.exe Tinysnap-win-x64-Portable.zip Tinysnap-win-arm64-Setup.exe Tinysnap-win-arm64-Portable.zip`.
3. Copy `packaging/winget/FaizanAshiq.Tinysnap/<version>` for the new version, fill in each
   installer's `InstallerSha256` (`Get-FileHash .\Tinysnap-win-x64-Setup.exe`), run
   `winget validate` on the folder, and send it to `microsoft/winget-pkgs` with `wingetcreate submit`.
4. Once winget has it, add the Windows install line (`winget install FaizanAshiq.Tinysnap`) to
   the site's Tinysnap page, `config/tinysnap.ts` in the site repo, by PR.

The app never checks for updates; winget brings new versions.

Third-party notices are in `THIRD-PARTY-NOTICES.md`.
