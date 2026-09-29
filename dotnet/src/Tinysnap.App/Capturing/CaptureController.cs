using Avalonia;
using Avalonia.Threading;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.App.Pinning;
using Tinysnap.Core;
using Tinysnap.Platform;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.App.Capturing;

/// <summary>Every way into a capture: an area or a window picked on the frozen screen, or the whole
/// monitor under the pointer, each opened in an editor or shown as a thumbnail as After Capture
/// says, and kept in the library first while Keep the library is on. It owns what a capture
/// turns into: editors, the thumbnail, pins and the library's entries.</summary>
public sealed class CaptureController
{
    private readonly IPlatform platform;
    private readonly PreferencesStore preferences;
    private readonly LibraryStore library;
    private readonly TimeProvider? time;
    private readonly EditorServices services;
    private readonly List<EditorWindow> editors = [];
    private readonly List<PinWindow> pins = [];
    /// <summary>Entries whose image is being drawn after their editor closed, and the work.</summary>
    private readonly Dictionary<string, Task> rendering = [];
    private ITimer? sweeper;

    /// <param name="dialogs">Null for the app's own; tests answer them.</param>
    /// <param name="time">Null for the system clock; tests fire the timers themselves.</param>
    internal CaptureController(IPlatform platform, PreferencesStore preferences, LibraryStore library, IDialogs? dialogs = null,
                               TimeProvider? time = null)
    {
        this.platform = platform;
        this.preferences = preferences;
        this.library = library;
        this.time = time;
        services = new EditorServices(platform.Clipboard, () => preferences.Current, dialogs ?? new AvaloniaDialogs(), Pin,
                                      preferences.RememberStyles, library, () => LibraryChanged?.Invoke(), time);
    }

    internal LibraryStore Library => library;

    /// <summary>Why the last capture was not kept, for Settings; null once one is.</summary>
    internal string? LibraryError { get; private set; }

    /// <summary>An entry was added, drawn again, swept or cleared.</summary>
    internal event Action? LibraryChanged;

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

    /// <summary>Where every capture goes once it is taken: into the library, then to an editor
    /// or the thumbnail, as Settings says. <paramref name="around"/> is where it was taken, in
    /// physical pixels. A thumbnail still showing goes first, copied as a time out would.</summary>
    private void Open(Capture capture, Rect around)
    {
        Thumbnail?.Dismiss(copying: true);
        var entry = Keep(capture);
        var at = new PixelRect((int)around.X, (int)around.Y, (int)around.Width, (int)around.Height);
        if (preferences.Current.AfterCapture == AfterCapture.Thumbnail)
            ShowThumbnail(new Document(capture), at, entry);
        else
            OpenEditor(new Document(capture), at, entry, DateTimeOffset.Now);
    }

    /// <summary>The capture added to the library while Keep the library is on. A failure still
    /// lets it open, and Settings says why it was not kept.</summary>
    private LibraryEntry? Keep(Capture capture)
    {
        if (!preferences.Current.KeepLibrary) return null;
        try
        {
            var entry = library.Add(capture, DateTimeOffset.Now);
            LibraryError = null;
            LibraryChanged?.Invoke();
            return entry;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LibraryError = error.Message;
            return null;
        }
    }

    /// <summary>Opens a library entry, or brings its editor forward when it is already open. A
    /// damaged entry opens flat and on its own, so nothing is written over it.</summary>
    internal void Open(LibraryEntry entry)
    {
        if (editors.FirstOrDefault(e => e.Entry == entry) is { } open)
        {
            open.Activate();
            return;
        }
        if (library.Open(entry) is not { } opened) return;
        OpenEditor(opened.Document, null, opened.IsEditable ? entry : null, entry.Captured);
    }

    private void OpenEditor(Document document, PixelRect? around, LibraryEntry? entry, DateTimeOffset captured)
    {
        var remembered = preferences.Current;
        var session = new EditorSession(document, styles: remembered.Styles, colorHex: remembered.ColorHex);
        var editor = new EditorWindow(session, captured, services, around, entry);
        editors.Add(editor);
        editor.Closed += (_, _) =>
        {
            editors.Remove(editor);
            if (editor.PendingRender() is var (edited, kept)) Render(kept, edited);
        };
        editor.Show();
        editor.Activate();
    }

    private void ShowThumbnail(Document document, PixelRect around, LibraryEntry? entry)
    {
        var shown = new CaptureThumbnail(document, services, around.Center, !platform.ReduceMotion, time, entry);
        shown.OpenRequested += opened => OpenEditor(opened, around, entry, entry?.Captured ?? DateTimeOffset.Now);
        // Let go of its capture once it has slid away.
        shown.Closed += (_, _) =>
        {
            if (thumbnail == shown) thumbnail = null;
        };
        thumbnail = shown;
        shown.Show();
    }

    /// <summary>A pin of a finished image, from an editor or the thumbnail. It owns the image.
    /// Double-clicked, it reopens its library entry editable when it came from one.</summary>
    private void Pin(ExportedImage exported, bool keepsSize, LibraryEntry? entry)
    {
        var pointer = platform.Screen.PointerPosition();
        var pin = new PinWindow(exported.Image, exported.Dpi / 72, keepsSize, services,
                                new PixelPoint((int)pointer.X, (int)pointer.Y));
        pin.OpenRequested += (image, scale) =>
        {
            if (entry is null)
            {
                OpenEditor(new Document(new Capture(image, scale)), null, null, DateTimeOffset.Now);
                return;
            }
            image.Dispose();
            Open(entry);
        };
        pins.Add(pin);
        pin.Closed += (_, _) => pins.Remove(pin);
        pin.Show();
    }

    // Library

    /// <summary>Entries open in an editor or being drawn, which are never swept, cleared or
    /// drawn a second time.</summary>
    internal IReadOnlySet<string> OpenEntryNames =>
        editors.Select(e => e.Entry?.Name).OfType<string>().Concat(rendering.Keys).ToHashSet();

    /// <summary>An entry's image, drawn on a worker thread: at 400% a large capture is many
    /// thousands of pixels across, and closing the editor stalled on it. Dated by the edits it
    /// was drawn from, so newer edits still read as stale. Until it lands the entry counts as
    /// open. Quitting first loses nothing: the edits are written, and a stale image is drawn
    /// again the next time the library looks.</summary>
    internal void Render(LibraryEntry entry, Document document)
    {
        if (rendering.ContainsKey(entry.Name)) return;
        var asOf = library.EditsDate(entry);
        rendering[entry.Name] = Task.Run(() =>
        {
            try
            {
                library.SaveImage(document, entry, asOf);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Drawn again the next time the library finds it stale.
            }
        }).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            rendering.Remove(entry.Name);
            LibraryChanged?.Invoke();
        }), TaskScheduler.Default);
    }

    /// <summary>Every image being drawn, landed.</summary>
    internal Task WhenRendered() => Task.WhenAll(rendering.Values.ToList());

    /// <summary>Removes entries past 30 days, never one that is open.</summary>
    internal void Sweep(DateTimeOffset? now = null)
    {
        if (library.Sweep(now, OpenEntryNames).Count > 0) LibraryChanged?.Invoke();
    }

    /// <summary>Sweeps now and once a day while the app runs.</summary>
    internal void StartSweeping()
    {
        Sweep();
        var day = TimeSpan.FromDays(1);
        sweeper = (time ?? TimeProvider.System).CreateTimer(_ => Dispatcher.UIThread.Post(() => Sweep()), null, day, day);
    }

    /// <summary>Everything closed for Quit. Each editor with edits asks first, and Cancel on any
    /// of them keeps the app running with that editor and those after it open. A thumbnail still
    /// showing goes as a time out would.</summary>
    internal async Task<bool> CloseAll()
    {
        foreach (var editor in editors.ToList())
            if (!await editor.CloseAsking()) return false;
        Thumbnail?.Dismiss(copying: true);
        foreach (var pin in pins.ToList()) pin.Close();
        return true;
    }
}
