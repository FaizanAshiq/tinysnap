# Tinysnap for Windows

A port of Tinysnap to Windows, in progress, built on .NET, Avalonia and SkiaSharp. So far it
captures an area, a window or the whole monitor under the pointer with Ctrl+Shift+2 and
Ctrl+Shift+1 or from the tray, and opens it in an editor with every tool, undo, zoom and
the style bar. Copying, saving, the library and settings come next.

The Mac app in the rest of this repository is unchanged, and both read the same library
files. `tests/fixtures` holds one library entry written by each app, and each app's tests
open the other's.

| Project | Holds |
| --- | --- |
| `src/Tinysnap.Core` | Editing, rendering, export and the library, ported from `Sources/TinysnapCore` |
| `src/Tinysnap.Platform` | What the app needs from the operating system, as interfaces |
| `src/Tinysnap.App` | Every window: the area overlay, the editor, the tray |
| `src/Tinysnap.Windows` | GDI capture, window listing and hotkeys, and the Windows exe |
| `src/Tinysnap.Dev` | The same app on a Mac, on a painted desktop, for development |

```bash
dotnet test dotnet/tests/Tinysnap.Core.Tests     # anywhere
dotnet test dotnet/tests/Tinysnap.App.Tests      # anywhere, headless
dotnet test dotnet/Tinysnap.slnx                 # on Windows, the platform tests too
dotnet run --project dotnet/src/Tinysnap.Dev     # the app on a Mac
```

CI runs every test on Windows and the Core and app tests on macOS. What only a person on a
Windows machine can check: that the hotkeys reach the app while other apps are in front,
capture on real monitors at mixed scales, and how capture and editing feel.

Third-party notices are in `THIRD-PARTY-NOTICES.md`.
