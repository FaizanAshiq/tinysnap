# Tinysnap for Windows

A port of Tinysnap to Windows, in progress, built on .NET and SkiaSharp. This folder holds
its core so far: the editor session, the renderer, export, preferences and the library,
ported from `Sources/TinysnapCore` with its tests.

The Mac app in the rest of this repository is unchanged, and both read the same library
files. `tests/fixtures` holds one library entry written by each app, and each app's tests
open the other's.

```bash
dotnet test dotnet/Tinysnap.slnx
```
