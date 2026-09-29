using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Tinysnap.App;

/// <summary>Draws straight onto Avalonia's own Skia canvas, in the control's coordinates, so a
/// capture or a render from Core reaches the screen without being copied into an Avalonia
/// bitmap. It runs on the render thread: <paramref name="draw"/> must only read what it was
/// given, never the control's live state, and <paramref name="done"/> hands back anything it held.</summary>
internal sealed class SkiaDraw(Rect bounds, Action<SKCanvas> draw, Action? done = null) : ICustomDrawOperation
{
    private int isDone;

    public Rect Bounds => bounds;

    public bool HitTest(Point point) => bounds.Contains(point);

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        draw(lease.SkCanvas);
    }

    /// <summary>Called once, when Avalonia lets go of the operation, so what it drew from can go.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDone, 1) == 0) done?.Invoke();
    }
}
