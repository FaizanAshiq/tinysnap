using SkiaSharp;
using Tinysnap.Platform;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.Linux;

/// <summary>GNOME's screenshot is one image of the whole desktop. It is cut into one frozen
/// screen per monitor, at the monitors' places in X11's layout scaled to the image's own size,
/// since the image can be larger than that layout on a scaled desktop. Each screen keeps its X11
/// place, which is where XWayland puts the overlay's window.</summary>
internal static class DesktopCut
{
    public static IReadOnlyList<FrozenScreen> Cut(SKImage desktop, IReadOnlyList<(Rect Bounds, bool Primary)> monitors, double scale)
    {
        if (monitors.Count == 0) return [new FrozenScreen(new Rect(0, 0, desktop.Width, desktop.Height), scale, desktop)];
        double left = monitors.Min(m => m.Bounds.MinX), top = monitors.Min(m => m.Bounds.MinY);
        var factor = desktop.Width / (monitors.Max(m => m.Bounds.MaxX) - left);
        return [.. monitors.Select(monitor =>
        {
            // Edges rounded on their own, so neighbours meet without a gap or an overlap.
            int x = Edge(monitor.Bounds.MinX - left, desktop.Width), y = Edge(monitor.Bounds.MinY - top, desktop.Height);
            int right = Edge(monitor.Bounds.MaxX - left, desktop.Width), bottom = Edge(monitor.Bounds.MaxY - top, desktop.Height);
            var part = desktop.Subset(SKRectI.Create(x, y, right - x, bottom - y));
            return new FrozenScreen(new Rect(x, y, right - x, bottom - y), scale * factor, part, monitor.Bounds);
        })];

        int Edge(double layout, int limit) => Math.Clamp((int)Math.Round(layout * factor), 0, limit);
    }
}
