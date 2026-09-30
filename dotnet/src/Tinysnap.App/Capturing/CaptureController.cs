using Avalonia;
using Avalonia.Threading;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.App.Library;
using Tinysnap.App.Pinning;
using Tinysnap.App.Settings;
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
                                      preferences.RememberStyles, library, () => LibraryChanged?.Invoke(), time,
                                      () => ShowLibrary(), ReadAndCopy,
                                      measure => preferences.Update(p => p with { Measure = measure }),
                                      backdrop => preferences.Update(p => p with { Backdrop = backdrop }), ReadWallpaper);
        // A Measure setting changed in one editor reaches every other.
        preferences.Changed += changed =>
        {
            foreach (var editor in editors) editor.Canvas.MeasureSettings = changed.Measure;
        };
        Hotkeys = new HotkeyRegistrar(platform.Hotkeys);
        var applied = preferences.Current.HotKeys;
        Hotkeys.Apply(applied);
        // Only a change to the hotkeys lets them go and takes them again, not a remembered style.
        preferences.Changed += changed =>
        {
            if (changed.HotKeys == applied) return;
            applied = changed.HotKeys;
            Hotkeys.Apply(applied);
        };
        platform.Hotkeys.Pressed += action => Dispatcher.UIThread.Post(() => Perform(action));
        platform.Reopened += files => Dispatcher.UIThread.Post(() => Reopen(files));
    }

    /// <summary>A hotkey or a tray item.</summary>
    public void Perform(HotKeyAction action)
    {
        switch (action)
        {
            case HotKeyAction.Area: CaptureArea(); break;
            case HotKeyAction.Fullscreen: CaptureFullscreen(); break;
            case HotKeyAction.Text: OpenOverlay(Purpose.Text); break;
            case HotKeyAction.Qr: OpenOverlay(Purpose.Codes); break;
            case HotKeyAction.RepeatArea: RepeatLastArea(); break;
            case HotKeyAction.Delayed: StartDelayedCapture(); break;
            case HotKeyAction.Library: ShowLibrary(); break;
        }
    }

    /// <summary>Tinysnap opened again while running: with files, from "Open with", each opens in
    /// an editor; without, from the Start menu or its shortcut, Settings when nothing is open,
    /// which is the way back with the tray icon hidden, or the open captures brought forward.</summary>
    private void Reopen(IReadOnlyList<string> files)
    {
        if (files.Count > 0)
        {
            OpenFiles(files);
            return;
        }
        if (editors.Count == 0)
        {
            ShowSettings();
            return;
        }
        foreach (var editor in editors) editor.Activate();
    }

    /// <summary>The countdown, the last area, or anything else the tray menu shows changed.</summary>
    internal event Action? StateChanged;

    /// <summary>The monitor and the box last captured as a picture, in that monitor's points.</summary>
    private (Rect Screen, Rect Points)? lastArea;

    internal bool HasLastArea => lastArea is not null;

    /// <summary>Seconds left before a delayed capture opens the overlay; null when none is counting.</summary>
    internal int? SecondsLeft { get; private set; }

    private ITimer? countdown;

    /// <summary>The last box again, from a fresh freeze of the same monitor. If that monitor is
    /// gone, or there is no last box, the overlay opens instead.</summary>
    internal void RepeatLastArea()
    {
        if (lastArea is not var (bounds, points) || SecondsLeft is not null)
        {
            CaptureArea();
            return;
        }
        var desktop = platform.Screen.Freeze();
        if (desktop.Screens.FirstOrDefault(s => s.Bounds == bounds) is not { } screen
            || Capture.Crop(screen.Image, points, screen.Scale) is not { } capture)
        {
            CaptureArea();
            return;
        }
        Open(capture, InPixels(screen, points));
    }

    /// <summary>Counts down in the tray, then opens the area overlay, so an open menu or a hover
    /// state can be set up first and caught by the freeze.</summary>
    internal void StartDelayedCapture()
    {
        if (SecondsLeft is not null) return;
        SecondsLeft = Math.Clamp(preferences.Current.DelaySeconds, Tinysnap.Core.Preferences.DelayMin, Tinysnap.Core.Preferences.DelayMax);
        StateChanged?.Invoke();
        countdown = (time ?? TimeProvider.System).CreateTimer(_ => Dispatcher.UIThread.Post(Tick), null, TimeSpan.FromSeconds(1),
                                                              Timeout.InfiniteTimeSpan);
    }

    private void Tick()
    {
        if (SecondsLeft is not { } left) return;
        if (left > 1)
        {
            SecondsLeft = left - 1;
            StateChanged?.Invoke();
            countdown?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            return;
        }
        countdown?.Dispose();
        countdown = null;
        SecondsLeft = null;
        StateChanged?.Invoke();
        CaptureArea();
    }

    private static Rect InPixels(FrozenScreen screen, Rect points) =>
        new(screen.Bounds.X + points.X * screen.Scale, screen.Bounds.Y + points.Y * screen.Scale,
            points.Width * screen.Scale, points.Height * screen.Scale);

    internal PreferencesStore Preferences => preferences;

    internal HotkeyRegistrar Hotkeys { get; }

    internal IStartup Startup => platform.Startup;

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

    /// <summary>What a box drawn on the overlay is for.</summary>
    private enum Purpose { Picture, Text, Codes }

    /// <summary>Freezes every monitor, then puts the area overlay over the frozen image.</summary>
    public void CaptureArea() => OpenOverlay(Purpose.Picture);

    private void OpenOverlay(Purpose purpose)
    {
        if (Overlay is not null) return;
        var desktop = platform.Screen.Freeze();
        if (desktop.Screens.Count == 0) return;
        Overlay = new AreaOverlay(desktop, result =>
        {
            Overlay = null;
            Finish(result, desktop, purpose);
        }, platform.Screen.PointerPosition());
        Overlay.Show();
    }

    private Task reading = Task.CompletedTask;

    /// <summary>The text or codes last asked for, landed and copied.</summary>
    internal Task WhenRead() => reading;

    /// <summary>A capture read for text or codes rather than opened, then let go.</summary>
    private void Read(Capture capture, bool codes, Rect around)
    {
        reading = ReadThenRelease();
        async Task ReadThenRelease()
        {
            await ReadAndCopy(capture.Image, codes, new PixelPoint((int)around.Center.X, (int)around.Center.Y));
            capture.Image.Dispose();
        }
    }

    /// <summary>The whole monitor under the pointer.</summary>
    public void CaptureFullscreen()
    {
        var desktop = platform.Screen.Freeze();
        var pointer = platform.Screen.PointerPosition();
        var screen = desktop.Screens.FirstOrDefault(s => s.Bounds.Contains(pointer)) ?? desktop.Screens.FirstOrDefault();
        if (screen is not null) Open(new Capture(screen.Image, screen.Scale), screen.Bounds);
    }

    private void Finish(AreaResult result, FrozenDesktop desktop, Purpose purpose)
    {
        var (capture, around) = result switch
        {
            AreaResult.Area(var screen, var points) => (Capture.Crop(screen.Image, points, screen.Scale), InPixels(screen, points)),
            AreaResult.PickedWindow(var picked) => (CutWindow(picked, desktop), picked.Bounds),
            _ => (null, default),
        };
        if (capture is null) return;
        if (purpose != Purpose.Picture)
        {
            Read(capture, purpose == Purpose.Codes, around);
            return;
        }
        // Repeat Last Area repeats pictures, not text grabs.
        if (result is AreaResult.Area(var frozen, var box))
        {
            lastArea = (frozen.Bounds, box);
            StateChanged?.Invoke();
        }
        Open(capture, around);
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

    /// <summary>Pictures opened with Tinysnap, each in an editor named for its file. A PNG keeps
    /// the scale its DPI gives; any other picture is one pixel a point. A file that is no picture
    /// is passed over. Nothing is kept in the library: the file is already kept.</summary>
    internal void OpenFiles(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (Picture(path) is not var (image, scale)) continue;
            OpenEditor(new Document(new Capture(image, scale)), null, null, DateTimeOffset.Now, Path.GetFileName(path));
        }
    }

    private static (SKImage Image, double Scale)? Picture(string path)
    {
        if (LibraryStore.ReadImage(path) is { } png) return png;
        try
        {
            return File.Exists(path) && SKImage.FromEncodedData(path) is { } image ? (image, 1) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void OpenEditor(Document document, PixelRect? around, LibraryEntry? entry, DateTimeOffset captured, string? title = null)
    {
        var remembered = preferences.Current;
        var session = new EditorSession(document, styles: remembered.Styles, colorHex: remembered.ColorHex);
        var editor = new EditorWindow(session, captured, services, around, entry, title);
        editor.Canvas.MeasureSettings = remembered.Measure;
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

    /// <summary>The desktop picture as a backdrop fill: read at no more than 1600 pixels across,
    /// then softened. Null when there is none, and the gradient is drawn.</summary>
    private BackdropWallpaper? ReadWallpaper()
    {
        using var picture = platform.Files.Wallpaper();
        if (picture is null) return null;
        var fit = Math.Min(1.0, 1600.0 / Math.Max(picture.Width, picture.Height));
        using var shrunk = fit < 1 ? Shrunk(picture, fit) : null;
        return Backdrop.Soften(shrunk ?? picture) is { } softened ? new BackdropWallpaper(Guid.NewGuid(), new PastedImage(softened)) : null;
    }

    private static SKImage Shrunk(SKImage image, double fit)
    {
        var info = new SKImageInfo(Math.Max(1, (int)(image.Width * fit)), Math.Max(1, (int)(image.Height * fit)));
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawImage(image, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return surface.Snapshot();
    }

    // Text and codes

    /// <summary>The toast showing what was last copied, if it is still up.</summary>
    internal TextToast? Toast { get; private set; }

    /// <summary>Reads <paramref name="image"/> for text or, with <paramref name="codes"/>, for QR
    /// codes on a worker thread, copies what it found, and says so in a toast at the top of the
    /// monitor holding <paramref name="on"/>. The image stays the caller's.</summary>
    internal async Task ReadAndCopy(SKImage image, bool codes, PixelPoint? on)
    {
        var reading = await Task.Run(() => platform.Text.Read(image, codes));
        Toast?.Dismiss();
        string title, shown = "";
        if (reading is null)
            title = codes ? "Could not scan for a QR code" : "Could not read text";
        else if (reading.IsEmpty)
            title = codes ? "No QR code found" : "No text found";
        else if (!services.Clipboard.SetText(reading.Text))
            title = "Tinysnap could not copy to the clipboard";
        else
        {
            shown = string.Join("\n", reading.Text.Split('\n').Take(4));
            title = !codes ? "Text copied" : reading.Codes.Length == 1 ? "QR code copied" : $"{reading.Codes.Length} QR codes copied";
        }
        Toast = new TextToast(title, shown, shown.Length > 0 ? reading : null, services.Clipboard, platform.Files, on, time);
        Toast.Show();
    }

    // Library

    private LibraryWindow? libraryWindow;

    internal LibraryWindow? OpenLibraryWindow => libraryWindow;

    internal SettingsWindow? OpenSettings => settingsWindow;

    internal EditorServices Services => services;

    internal IFileActions Files => platform.Files;

    /// <summary>The one library window, made when first asked for, shown and brought forward.</summary>
    internal LibraryWindow ShowLibrary()
    {
        if (libraryWindow is null)
        {
            libraryWindow = new LibraryWindow(this);
            libraryWindow.Closed += (_, _) => libraryWindow = null;
        }
        libraryWindow.ShowInFront();
        return libraryWindow;
    }

    private SettingsWindow? settingsWindow;

    /// <summary>The one Settings window, made when first asked for, shown and brought forward.</summary>
    internal SettingsWindow ShowSettings()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(this);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        if (!settingsWindow.IsVisible) settingsWindow.Show();
        settingsWindow.Activate();
        return settingsWindow;
    }

    /// <summary>For the library window, after it moved an entry to the Recycle Bin.</summary>
    internal void NotifyLibraryChanged() => LibraryChanged?.Invoke();

    /// <summary>Has an entry's open editor write its edits and image now, so nothing handed out
    /// from the library lags behind edits still on screen, a redaction least of all.</summary>
    internal void Flush(LibraryEntry entry) => editors.FirstOrDefault(e => e.Entry == entry)?.Keep(renderingImage: true);

    /// <summary>The entry drawn from its edits, brought up to date with any editor still open on
    /// it, pinned at its size when it has one. False when it could not be drawn.</summary>
    internal bool PinEntry(LibraryEntry entry)
    {
        Flush(entry);
        if (library.Open(entry)?.Document is not { } document || Output.Export(document, ExportScale.Native) is not var (exported, _))
            return false;
        Pin(exported, document.Resize is not null, entry);
        return true;
    }

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
