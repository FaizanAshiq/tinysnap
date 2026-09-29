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

public interface IPlatform
{
    IScreenCapture Screen { get; }
    IHotkeys Hotkeys { get; }
}
