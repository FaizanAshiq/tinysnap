namespace Tinysnap.Core;

/// <summary>A capture's export size, as output pixels per capture pixel: 0.5 is half of full
/// resolution. The Size panel's chips and its pixel fields both come down to one of these.</summary>
public static class DocumentSizing
{
    /// <summary>1% to 400%.</summary>
    public const double ResizeMin = 0.01;
    public const double ResizeMax = 4;

    /// <summary>The longest side an export may have, so a 5K capture cannot ask for a gigabyte.</summary>
    public const double LongestExportSide = 16_384;

    /// <summary>The export's size in pixels at <paramref name="resize"/>, from the same sums the
    /// renderer cuts and frames with, so the panel never shows a size the export does not make.</summary>
    public static Size ExportPixelSize(this Document document, double resize)
    {
        var cut = Renderer.OutputCut(document, resize)?.Size ?? new Size(1, 1);
        if (document.Backdrop is not { } backdrop) return cut;
        var padding = (double)Renderer.FramePadding(backdrop, document.Scale * resize) * 2;
        return new Size(cut.Width + padding, cut.Height + padding);
    }

    /// <summary>The largest size within the limits whose export stays inside the longest side.</summary>
    public static double LargestResize(this Document document)
    {
        bool TooLong(Size size) => Math.Max(size.Width, size.Height) > LongestExportSide;
        if (!TooLong(document.ExportPixelSize(ResizeMax))) return ResizeMax;
        return document.Bisect(TooLong).Under;
    }

    public static double ClampedResize(this Document document, double resize) =>
        Math.Min(Math.Max(resize, ResizeMin), document.LargestResize());

    /// <summary>The size whose export is <paramref name="pixels"/> wide, held to the limits.</summary>
    public static double ResizeForWidth(this Document document, int pixels) =>
        document.ClampedResize(document.Bisect(size => size.Width >= pixels).Over);

    /// <summary>The size whose export is <paramref name="pixels"/> high, held to the limits.</summary>
    public static double ResizeForHeight(this Document document, int pixels) =>
        document.ClampedResize(document.Bisect(size => size.Height >= pixels).Over);

    /// <summary>Narrows the limits to where <paramref name="reached"/> turns true. The sums
    /// round to whole pixels, so dividing the pixels wanted by the full size can land a pixel short.</summary>
    private static (double Under, double Over) Bisect(this Document document, Func<Size, bool> reached)
    {
        double under = ResizeMin, over = ResizeMax;
        if (reached(document.ExportPixelSize(under))) return (under, under);
        for (var step = 0; step < 40; step++)
        {
            var middle = (under + over) / 2;
            if (reached(document.ExportPixelSize(middle))) over = middle;
            else under = middle;
        }
        return (under, over);
    }
}
