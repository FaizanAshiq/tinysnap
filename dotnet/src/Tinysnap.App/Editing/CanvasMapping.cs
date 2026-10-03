using Tinysnap.Core;

namespace Tinysnap.App.Editing;

/// <summary>Between the canvas control's DIPs and the document's capture pixels. At 100% one
/// DIP is one point, a pixel over the capture's scale, so a capture shows at the size it had on
/// screen. <c>Shown</c> is what the canvas shows, whose corner moves left of or above the
/// capture once a shape is drawn past its edge.</summary>
internal readonly record struct CanvasMapping(Rect Shown, double CaptureScale, double Zoom)
{
    public Size Size => new(Shown.Width / CaptureScale * Zoom, Shown.Height / CaptureScale * Zoom);

    public Point ToPixels(Point dip) =>
        new(dip.X / Zoom * CaptureScale + Shown.MinX, dip.Y / Zoom * CaptureScale + Shown.MinY);

    public Point ToDips(Point pixel) =>
        new((pixel.X - Shown.MinX) / CaptureScale * Zoom, (pixel.Y - Shown.MinY) / CaptureScale * Zoom);

    public Rect ToDips(Rect pixels)
    {
        var corner = ToDips(new Point(pixels.MinX, pixels.MinY));
        return new Rect(corner.X, corner.Y, pixels.Width / CaptureScale * Zoom, pixels.Height / CaptureScale * Zoom);
    }

    /// <summary>Six DIPs of reach for a handle, whatever the zoom, in capture pixels.</summary>
    public double Reach => 6 / Zoom * CaptureScale;
}

/// <summary>How big an editor opens and how far it zooms.</summary>
internal static class EditorFit
{
    /// <summary>Room round the canvas when it opens and on Zoom to Fit, so it never meets the
    /// window's edges.</summary>
    public const double Margin = 24;

    public const double MinZoom = 0.1;
    public const double MaxZoom = 16;

    /// <summary>At 100% when it fits, shrunk to fit 90% of the monitor's work area when it does
    /// not, never zoomed in. <paramref name="canvas"/> is the canvas at 100%, in DIPs; the
    /// client size returned includes the toolbar. Never larger than the work area, even when the
    /// minimum is: on a small screen the window fits and its toolbar scrolls.</summary>
    public static (Size Client, double Zoom) Initial(Size canvas, Size workArea, double toolbar, Size minimum, double rail = 0)
    {
        var margins = Margin * 2;
        var available = new Size(workArea.Width * 0.9 - margins - rail, workArea.Height * 0.9 - toolbar - margins);
        var zoom = Math.Min(1, Math.Min(available.Width / canvas.Width, available.Height / canvas.Height));
        var client = new Size(Math.Min(Math.Max(canvas.Width * zoom + margins + rail, minimum.Width), workArea.Width),
                              Math.Min(Math.Max(canvas.Height * zoom + margins + toolbar, minimum.Height), workArea.Height));
        return (client, zoom);
    }

    /// <summary>The largest zoom that shows all of <paramref name="canvas"/> in
    /// <paramref name="view"/> with its margin, for Ctrl+0.</summary>
    public static double Fit(Size canvas, Size view) =>
        Clamp(Math.Min((view.Width - Margin * 2) / canvas.Width, (view.Height - Margin * 2) / canvas.Height));

    public static double ZoomIn(double zoom) => Clamp(zoom * 1.25);

    public static double ZoomOut(double zoom) => Clamp(zoom / 1.25);

    public static double Clamp(double zoom) => Math.Min(Math.Max(zoom, MinZoom), MaxZoom);
}
