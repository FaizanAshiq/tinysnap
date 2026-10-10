using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.App.Capturing;

/// <summary>What the overlay ended with.</summary>
internal abstract record AreaResult
{
    private AreaResult() { }

    /// <summary>A box in points from its monitor's top left corner.</summary>
    public sealed record Area(FrozenScreen Screen, Rect Points) : AreaResult;

    public sealed record PickedWindow(PickableWindow Picked) : AreaResult;

    public sealed record Cancelled : AreaResult;
}

/// <summary>One overlay window per monitor, each showing that monitor frozen. The person drags
/// a box, presses Ctrl+A for the whole monitor, or presses Space and clicks a window; Esc cancels. Window mode is shared, so Space
/// on one monitor lights windows on all of them.</summary>
internal sealed class AreaOverlay
{
    private readonly FrozenDesktop desktop;
    private readonly Action<AreaResult> onFinish;
    private readonly Point? pointer;
    private readonly Action? systemPicker;
    private readonly Action<Avalonia.Controls.Window> raise;
    private bool isFinished;

    public IReadOnlyList<OverlayWindow> Windows { get; }
    public bool WindowMode { get; private set; }

    /// <param name="pointer">Where the pointer is, in pixels, so the overlay under it takes the keys.</param>
    /// <param name="systemPicker">Where windows cannot be listed, what Space hands over to instead.</param>
    /// <param name="fullScreen">Each window asks to be full screen, where the desktop would
    /// otherwise keep it below its panel.</param>
    /// <param name="raise">How the window under the pointer is given the keyboard; its own
    /// activation when not given.</param>
    public AreaOverlay(FrozenDesktop desktop, Action<AreaResult> onFinish, Point? pointer = null, Action? systemPicker = null,
                       bool fullScreen = false, Action<Avalonia.Controls.Window>? raise = null)
    {
        this.desktop = desktop;
        this.onFinish = onFinish;
        this.pointer = pointer;
        this.systemPicker = systemPicker;
        this.raise = raise ?? (window => window.Activate());
        Windows = [.. desktop.Screens.Select(screen => new OverlayWindow(screen, this, fullScreen))];
    }

    public void Show()
    {
        foreach (var window in Windows) window.Show();
        var underPointer = pointer is { } at ? Windows.FirstOrDefault(w => w.Screen.Bounds.Contains(at)) : null;
        if (underPointer is not null && pointer is { } start) underPointer.PointerAt(start);
        if ((underPointer ?? Windows.FirstOrDefault()) is { } keyed) raise(keyed);
    }

    /// <summary>The frontmost window under <paramref name="pixel"/>.</summary>
    internal PickableWindow? WindowAt(Point pixel) => desktop.Windows.FirstOrDefault(w => w.Bounds.Contains(pixel));

    internal void ToggleWindowMode()
    {
        if (systemPicker is not null)
        {
            Finish(new AreaResult.Cancelled());
            systemPicker();
            return;
        }
        WindowMode = !WindowMode;
        foreach (var window in Windows) window.WindowModeChanged();
    }

    /// <summary>Ctrl+A: the whole monitor under the pointer, as a box dragged from corner to corner
    /// would be. The keys stay with the overlay that first had them, so the pointer says which.</summary>
    internal void SelectAll(OverlayWindow heard)
    {
        var window = Windows.FirstOrDefault(w => w.HasPointer) ?? heard;
        Finish(new AreaResult.Area(window.Screen, new Rect(0, 0, window.Width, window.Height)));
    }

    internal void Finish(AreaResult result)
    {
        if (isFinished) return;
        isFinished = true;
        foreach (var window in Windows) window.Close();
        onFinish(result);
    }
}
