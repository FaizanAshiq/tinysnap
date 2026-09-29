using Avalonia;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Tinysnap.Platform;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.App.Capturing;

/// <summary>Every way into a capture: an area or a window picked on the frozen screen, or the whole
/// monitor under the pointer, each opened in an editor. Nothing is kept yet; the library comes
/// with milestone 4.</summary>
public sealed class CaptureController(IPlatform platform, Func<Preferences> preferences)
{
    private readonly List<EditorWindow> editors = [];

    internal AreaOverlay? Overlay { get; private set; }

    internal IReadOnlyList<EditorWindow> Editors => editors;

    /// <summary>Freezes every monitor, then puts the area overlay over the frozen image.</summary>
    public void CaptureArea()
    {
        if (Overlay is not null) return;
        var desktop = platform.Screen.Freeze();
        if (desktop.Screens.Count == 0) return;
        Overlay = new AreaOverlay(desktop, result =>
        {
            Overlay = null;
            Finish(result, desktop);
        }, platform.Screen.PointerPosition());
        Overlay.Show();
    }

    /// <summary>The whole monitor under the pointer.</summary>
    public void CaptureFullscreen()
    {
        var desktop = platform.Screen.Freeze();
        var pointer = platform.Screen.PointerPosition();
        var screen = desktop.Screens.FirstOrDefault(s => s.Bounds.Contains(pointer)) ?? desktop.Screens.FirstOrDefault();
        if (screen is not null) Open(new Capture(screen.Image, screen.Scale), screen.Bounds);
    }

    private void Finish(AreaResult result, FrozenDesktop desktop)
    {
        switch (result)
        {
            case AreaResult.Area(var screen, var points):
                if (Capture.Crop(screen.Image, points, screen.Scale) is { } capture)
                {
                    var pixels = new Rect(screen.Bounds.X + points.X * screen.Scale, screen.Bounds.Y + points.Y * screen.Scale,
                                          points.Width * screen.Scale, points.Height * screen.Scale);
                    Open(capture, pixels);
                }
                break;
            case AreaResult.PickedWindow(var picked):
                if (CutWindow(picked, desktop) is { } window) Open(window, picked.Bounds);
                break;
        }
    }

    /// <summary>The window cut from the frozen monitor it most covers, its rounded corners left
    /// see-through as the window shows them.</summary>
    private Capture? CutWindow(PickableWindow picked, FrozenDesktop desktop)
    {
        static double Area(Rect rect) => rect.IsNull ? 0 : rect.Width * rect.Height;
        var screen = desktop.Screens.MaxBy(s => Area(s.Bounds.Intersection(picked.Bounds)));
        if (screen is null) return null;
        var cut = picked.Bounds.Intersection(screen.Bounds);
        if (cut.IsNull || cut.Width < 1 || cut.Height < 1) return null;
        var local = cut.Offset(-screen.Bounds.X, -screen.Bounds.Y).Integral;
        var info = new SKImageInfo((int)local.Width, (int)local.Height, SKColorType.Rgba8888, SKAlphaType.Premul,
                                   SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        if (surface is null) return null;
        var target = new SKRect(0, 0, info.Width, info.Height);
        surface.Canvas.Clear(SKColors.Transparent);
        var radius = (float)(platform.Screen.WindowCornerRadius * screen.Scale);
        if (radius > 0)
        {
            using var corners = new SKPath();
            corners.AddRoundRect(target, radius, radius);
            surface.Canvas.ClipPath(corners, antialias: true);
        }
        surface.Canvas.DrawImage(screen.Image, local.ToSK(), target, new SKSamplingOptions(SKFilterMode.Nearest));
        return new Capture(surface.Snapshot(), screen.Scale);
    }

    /// <summary>An editor on <paramref name="capture"/>, opened on the monitor it came from.
    /// <paramref name="around"/> is where it was taken, in physical pixels.</summary>
    internal EditorWindow Open(Capture capture, Rect around)
    {
        var remembered = preferences();
        var session = new EditorSession(new Document(capture), styles: remembered.Styles, colorHex: remembered.ColorHex);
        var editor = new EditorWindow(session, DateTimeOffset.Now,
                                      new PixelRect((int)around.X, (int)around.Y, (int)around.Width, (int)around.Height));
        editors.Add(editor);
        editor.Closed += (_, _) => editors.Remove(editor);
        editor.Show();
        editor.Activate();
        return editor;
    }
}
