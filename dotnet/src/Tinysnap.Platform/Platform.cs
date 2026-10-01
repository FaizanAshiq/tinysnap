using SkiaSharp;
using Tinysnap.Core;

namespace Tinysnap.Platform;

// Everything here is in physical pixels on the virtual desktop, y growing downward, with the
// primary monitor's top left corner at (0, 0), so a monitor left of or above it sits at
// negative coordinates. A point is a pixel divided by its monitor's scale.

/// <summary>One monitor as it looked the moment a capture began.</summary>
public sealed record FrozenScreen(Rect Bounds, double Scale, SKImage Image);

/// <summary>A window that window mode can pick, its shadow left out of its bounds.</summary>
public sealed record PickableWindow(Rect Bounds, string Title);

/// <summary>Every monitor frozen, and the windows on them front to back.</summary>
public sealed record FrozenDesktop(IReadOnlyList<FrozenScreen> Screens, IReadOnlyList<PickableWindow> Windows);

public interface IScreenCapture
{
    FrozenDesktop Freeze();

    Point PointerPosition();

    /// <summary>How round a window's corners are, in points, so a window capture can leave them
    /// see-through as the window shows them: 8 on Windows 11, 0 before it.</summary>
    double WindowCornerRadius { get; }
}

public interface IHotkeys : IDisposable
{
    event Action<HotKeyAction>? Pressed;

    /// <summary>False when another app already holds the combination.</summary>
    bool Register(HotKeyAction action, HotKeyBinding binding);

    void UnregisterAll();
}

public interface IClipboard
{
    /// <summary>The image as a PNG, which keeps its DPI, and as a bitmap for apps that read only
    /// that, see-through pixels kept in both. The image is read only during the call and stays the
    /// caller's to dispose. False when the clipboard could not be taken.</summary>
    bool SetImage(SKImage image, byte[] png, double dpi);

    bool SetText(string text);

    /// <summary>Changes whenever anything, in any app, puts something on the clipboard.</summary>
    uint ChangeCount { get; }
}

public interface IFileActions
{
    /// <summary>To the Recycle Bin rather than gone, so a slip of the Delete key can be undone.
    /// False when it could not be moved.</summary>
    bool MoveToRecycleBin(string path);

    /// <summary>The file manager on the folder holding <paramref name="path"/>, with it selected.</summary>
    void Reveal(string path);

    /// <summary>A web address in the person's browser.</summary>
    void Open(Uri link);

    /// <summary>The desktop picture, for the wallpaper backdrop, the caller's to dispose. Null when
    /// there is no picture file to read, as with a solid colour.</summary>
    SKImage? Wallpaper();
}

public interface IStartup
{
    /// <summary>Asked of the system each time, so it never disagrees with the system's own list.</summary>
    bool IsEnabled { get; }

    /// <summary>False when the system refused the change.</summary>
    bool SetEnabled(bool enabled);
}

public interface ITextReader
{
    /// <summary>The lines of text in <paramref name="image"/> in reading order or, with
    /// <paramref name="codes"/>, what every QR code in it holds, each once. Asked for apart, a
    /// caption beside a code is never taken for it. Null when the image could not be read, for
    /// example with no recogniser for the person's languages.</summary>
    Task<TextReading?> Read(SKImage image, bool codes);
}

public interface IPlatform
{
    ITextReader Text { get; }
    IFileActions Files { get; }
    IStartup Startup { get; }
    IScreenCapture Screen { get; }
    IHotkeys Hotkeys { get; }
    IClipboard Clipboard { get; }

    /// <summary>Tinysnap was opened again while it runs, with the files it was opened with, if any,
    /// as "Open with" gives them; the second copy has already quit.</summary>
    event Action<IReadOnlyList<string>>? Reopened;

    /// <summary>True when the person turned animations off, so nothing slides; read each time,
    /// since it can change while the app runs.</summary>
    bool ReduceMotion { get; }

    /// <summary>True when the taskbar or bar the tray icon sits on is light, so the icon is drawn
    /// dark; read each time, since it can change while the app runs.</summary>
    bool LightTaskbar { get; }
}
