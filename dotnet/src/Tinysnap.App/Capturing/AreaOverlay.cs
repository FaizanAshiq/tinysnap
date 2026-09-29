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
/// a box, or presses Space and clicks a window; Esc cancels. Window mode is shared, so Space
/// on one monitor lights windows on all of them.</summary>
internal sealed class AreaOverlay
{
    private readonly FrozenDesktop desktop;
    private readonly Action<AreaResult> onFinish;
    private readonly Point? pointer;
    private bool isFinished;

    public IReadOnlyList<OverlayWindow> Windows { get; }
    public bool WindowMode { get; private set; }

    /// <param name="pointer">Where the pointer is, in pixels, so the overlay under it takes the keys.</param>
    public AreaOverlay(FrozenDesktop desktop, Action<AreaResult> onFinish, Point? pointer = null)
    {
        this.desktop = desktop;
        this.onFinish = onFinish;
        this.pointer = pointer;
        Windows = [.. desktop.Screens.Select(screen => new OverlayWindow(screen, this))];
    }

    public void Show()
    {
        foreach (var window in Windows) window.Show();
        var underPointer = pointer is { } at ? Windows.FirstOrDefault(w => w.Screen.Bounds.Contains(at)) : null;
        (underPointer ?? Windows.FirstOrDefault())?.Activate();
    }

    /// <summary>The frontmost window under <paramref name="pixel"/>.</summary>
    internal PickableWindow? WindowAt(Point pixel) => desktop.Windows.FirstOrDefault(w => w.Bounds.Contains(pixel));

    internal void ToggleWindowMode()
    {
        WindowMode = !WindowMode;
        foreach (var window in Windows) window.WindowModeChanged();
    }

    internal void Finish(AreaResult result)
    {
        if (isFinished) return;
        isFinished = true;
        foreach (var window in Windows) window.Close();
        onFinish(result);
    }
}
