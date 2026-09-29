using Avalonia;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.App.Pinning;
using Tinysnap.Core;
using Tinysnap.Platform;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.App.Capturing;

/// <summary>Every way into a capture: an area or a window picked on the frozen screen, or the whole
/// monitor under the pointer, each opened in an editor or shown as a thumbnail as After Capture
/// says. It owns what a capture turns into: editors, the thumbnail and pins. Nothing is kept yet;
/// the library comes with milestone 4.</summary>
public sealed class CaptureController
{
    private readonly IPlatform platform;
    private readonly PreferencesStore preferences;
    private readonly TimeProvider? time;
    private readonly EditorServices services;
    private readonly List<EditorWindow> editors = [];
    private readonly List<PinWindow> pins = [];

    /// <param name="dialogs">Null for the app's own; tests answer them.</param>
    /// <param name="time">Null for the system clock; tests fire the thumbnail's timer themselves.</param>
    internal CaptureController(IPlatform platform, PreferencesStore preferences, IDialogs? dialogs = null, TimeProvider? time = null)
    {
        this.platform = platform;
        this.preferences = preferences;
        this.time = time;
        services = new EditorServices(platform.Clipboard, () => preferences.Current, dialogs ?? new AvaloniaDialogs(), Pin,
                                      preferences.RememberStyles);
    }

    internal AreaOverlay? Overlay { get; private set; }

    internal IReadOnlyList<EditorWindow> Editors => editors;

    internal IReadOnlyList<PinWindow> Pins => pins;

    private CaptureThumbnail? thumbnail;

    /// <summary>The thumbnail showing now, if any, not one sliding away. One at a time.</summary>
    internal CaptureThumbnail? Thumbnail => thumbnail is { IsGone: false } ? thumbnail : null;

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

    /// <summary>An editor or a thumbnail on <paramref name="capture"/>, on the monitor it came
    /// from. <paramref name="around"/> is where it was taken, in physical pixels. A thumbnail
    /// still showing goes first, copied as a time out would.</summary>
    private void Open(Capture capture, Rect around)
    {
        Thumbnail?.Dismiss(copying: true);
        var at = new PixelRect((int)around.X, (int)around.Y, (int)around.Width, (int)around.Height);
        if (preferences.Current.AfterCapture == AfterCapture.Thumbnail)
            ShowThumbnail(new Document(capture), at);
        else
            OpenEditor(new Document(capture), at);
    }

    private void OpenEditor(Document document, PixelRect? around)
    {
        var remembered = preferences.Current;
        var session = new EditorSession(document, styles: remembered.Styles, colorHex: remembered.ColorHex);
        var editor = new EditorWindow(session, DateTimeOffset.Now, services, around);
        editors.Add(editor);
        editor.Closed += (_, _) => editors.Remove(editor);
        editor.Show();
        editor.Activate();
    }

    private void ShowThumbnail(Document document, PixelRect around)
    {
        var shown = new CaptureThumbnail(document, services, around.Center, !platform.ReduceMotion, time);
        shown.OpenRequested += opened => OpenEditor(opened, around);
        // Let go of its capture once it has slid away.
        shown.Closed += (_, _) =>
        {
            if (thumbnail == shown) thumbnail = null;
        };
        thumbnail = shown;
        shown.Show();
    }

    /// <summary>A pin of a finished image, from an editor or the thumbnail. It owns the image.</summary>
    private void Pin(ExportedImage exported, bool keepsSize)
    {
        var pointer = platform.Screen.PointerPosition();
        var pin = new PinWindow(exported.Image, exported.Dpi / 72, keepsSize, services,
                                new PixelPoint((int)pointer.X, (int)pointer.Y));
        pin.OpenRequested += (image, scale) => OpenEditor(new Document(new Capture(image, scale)), null);
        pins.Add(pin);
        pin.Closed += (_, _) => pins.Remove(pin);
        pin.Show();
    }

    /// <summary>Everything closed for Quit. Each editor with edits asks first, and Cancel on any
    /// of them keeps the app running with that editor and those after it open. A thumbnail still
    /// showing copies itself on the way out, as a time out would.</summary>
    internal async Task<bool> CloseAll()
    {
        foreach (var editor in editors.ToList())
            if (!await editor.CloseAsking()) return false;
        Thumbnail?.Dismiss(copying: true);
        foreach (var pin in pins.ToList()) pin.Close();
        return true;
    }
}
