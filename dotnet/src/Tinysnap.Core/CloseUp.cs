using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>The canvas past 100%: what is in view drawn at the screen's own resolution, so
/// shapes stay smooth however far in, while the capture's pixels stay sharp squares.</summary>
public static partial class Renderer
{
    /// <summary>More output pixels than this and the canvas shows the plain render enlarged instead.</summary>
    internal const double CloseUpPixelLimit = 40_000_000;

    /// <summary><paramref name="visible"/>, in capture pixels, drawn at <paramref name="outputScale"/>
    /// with the region it covers. Null when nothing is in view, or when a box that reads back makes
    /// it too big to draw.</summary>
    public static (SKImage Image, Rect Region)? RenderCloseUp(Document document, Rect visible, double outputScale, bool framed,
                                                             IReadOnlySet<Guid>? hidden = null)
    {
        if (CloseUpRegion(document, visible, framed) is not { } region) return null;
        var size = PixelSize(region, outputScale);
        // ponytail: a huge blur half in view at a deep zoom shows as squares; render only its
        // visible part with the reach it needs if that ever matters.
        if (size.Width * size.Height > CloseUpPixelLimit) return null;
        return Render(document, region, outputScale, hidden, sharpPixels: true) is { } image ? (image, region) : null;
    }

    /// <summary><paramref name="visible"/> on whole capture pixels, inside the output when framed,
    /// then grown to take in whole each box that reads back what is beneath it and reaches into
    /// view: a blur, pixelate or magnifier half in view then looks as it does whole, rather than
    /// counting from the edge of the view. Never past the extent.</summary>
    internal static Rect? CloseUpRegion(Document document, Rect visible, bool framed)
    {
        var shown = visible.Intersection(framed ? document.OutputPixelRect : document.Extent);
        if (shown.IsEmpty) return null;
        var reaches = document.Annotations.Where(a => !a.IsHidden).Select(a => ReadBack(a, document.Scale))
            .OfType<Rect>().ToList();
        var region = shown.Integral;
        // A box taken in can reach another, as a magnifier over a blur does.
        var grown = true;
        while (grown)
        {
            grown = false;
            foreach (var reach in reaches.Where(reach => reach.Intersects(region) && !region.Contains(reach)))
            {
                region = region.Union(reach).Integral;
                grown = true;
            }
        }
        return region.Intersection(document.Extent);
    }

    /// <summary>What an annotation reads back to draw itself, in capture pixels; null when it
    /// draws without looking at what is beneath it.</summary>
    private static Rect? ReadBack(Annotation annotation, double scale) => annotation.Kind switch
    {
        // A Gaussian reaches three of its sizes.
        AnnotationKind.Blur blur => blur.Rect.Inset(-annotation.PixelSize(scale) * 3, -annotation.PixelSize(scale) * 3),
        AnnotationKind.Pixelate pixelate => pixelate.Rect,
        AnnotationKind.Erase erase => erase.Rect.Inset(-1, -1),
        AnnotationKind.Magnifier lens => new Rect(lens.Center.X - lens.Radius, lens.Center.Y - lens.Radius, lens.Radius * 2, lens.Radius * 2),
        _ => null,
    };
}
