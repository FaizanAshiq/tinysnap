using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>The output, and the output inside its backdrop, which is what exports and the
/// canvas show once a backdrop is on.</summary>
public static partial class Renderer
{
    /// <summary>The output flattened: the whole canvas drawn, then the crop cut out on whole
    /// output pixels. Drawing only the crop left a redaction or magnifier at its edge less to
    /// read back than the canvas had, so the export came out different from the screen.</summary>
    public static SKImage? RenderOutput(Document document, double outputScale = 1, IReadOnlySet<Guid>? hidden = null)
    {
        if (OutputCut(document, outputScale) is not { } cut) return null;
        using var full = Render(document, document.Extent, outputScale, hidden);
        return full?.Subset(cut.ToSKRectI());
    }

    /// <summary>Where the output is cut from a render of the whole extent, on whole output
    /// pixels rounded inwards. <c>ExportPixelSize</c> sizes exports with it too.</summary>
    internal static Rect? OutputCut(Document document, double outputScale)
    {
        var region = document.Extent;
        // The render starts at the extent's corner, left of or above the capture once the
        // canvas has grown.
        var rect = document.OutputRect.Offset(-region.MinX, -region.MinY);
        var left = Math.Ceiling(rect.MinX * outputScale);
        var top = Math.Ceiling(rect.MinY * outputScale);
        var cut = new Rect(left, top, Math.Max(1, Math.Floor(rect.MaxX * outputScale) - left),
                           Math.Max(1, Math.Floor(rect.MaxY * outputScale) - top))
            .Intersection(new Rect(Point.Zero, PixelSize(region, outputScale)));
        return cut.IsNull ? null : cut;
    }

    /// <summary>The output inside its backdrop, or the plain output when there is none.</summary>
    public static SKImage? RenderFramed(Document document, double outputScale = 1, IReadOnlySet<Guid>? hidden = null)
    {
        FrameGround? ground = null;
        try { return RenderFramed(document, outputScale, hidden, ref ground); }
        finally { ground?.Dispose(); }
    }

    /// <summary>The same, drawn over <paramref name="ground"/> while it still fits, and over a
    /// new one kept there when it does not. The shadow was most of what a frame cost, and a
    /// stroke, an undo or a restyle never moves it, so the canvas keeps one ground and pays
    /// for the output. The ground belongs to the caller, and one this replaces is disposed.</summary>
    public static SKImage? RenderFramed(Document document, double outputScale, IReadOnlySet<Guid>? hidden,
                                        ref FrameGround? ground)
    {
        var content = RenderOutput(document, outputScale, hidden);
        if (content is null) return null;
        if (document.Backdrop is not { } backdrop) return content;
        var perPoint = document.Scale * outputScale;
        var place = Placement(content, backdrop, perPoint);
        var key = new FrameGround.Key(backdrop, document.Capture, document.OutputPixelRect, document.Extent, outputScale);
        if (ground is null || ground.GroundKey != key)
        {
            // The shadow follows the output with nothing drawn on it: a window's see-through
            // corners shape it, the marks drawn on top do not.
            using var bare = RenderOutput(document, outputScale, document.Annotations.Select(a => a.Id).ToHashSet());
            var image = MakeGround(bare ?? content, place, backdrop, document.Capture, perPoint);
            // A full screen ground is tens of megabytes the collector cannot see.
            ground?.Dispose();
            ground = image is null ? null : new FrameGround(image, key);
        }
        if (ground is null) return null;
        using (content) return Frame(content, ground.Image, place);
    }

    /// <summary>The frame's size, where the output sits in it, and how round its corners are.</summary>
    private readonly record struct FramePlacement(int Width, int Height, Rect Box, double Corner);

    private static FramePlacement Placement(SKImage content, Backdrop backdrop, double perPoint)
    {
        var padding = FramePadding(backdrop, perPoint);
        var box = new Rect(padding, padding, content.Width, content.Height);
        var corner = Math.Min((backdrop.Corners.Points() ?? double.MaxValue) * perPoint, Math.Min(box.Width, box.Height) / 2);
        return new FramePlacement(content.Width + padding * 2, content.Height + padding * 2, box, corner);
    }

    /// <summary>The backdrop's padding on each side, in whole output pixels.</summary>
    internal static int FramePadding(Backdrop backdrop, double perPoint) =>
        (int)Geometry.Round(backdrop.Padding.Points() * perPoint);

    private static SKPath RoundedBox(FramePlacement place)
    {
        using var builder = new SKPathBuilder();
        builder.AddRoundRect(place.Box.ToSK(), (float)place.Corner, (float)place.Corner, SKPathDirection.Clockwise);
        return builder.Detach();
    }

    /// <summary>The fill over everything, then the shadow <paramref name="caster"/> throws under
    /// the output's rounded shape, with the caster taken back out: its pixels are the output's
    /// to draw. <paramref name="perPoint"/> is output pixels per point.</summary>
    private static SKImage? MakeGround(SKImage caster, FramePlacement place, Backdrop backdrop, Capture capture, double perPoint)
    {
        using var surface = SKSurface.Create(Info(place.Width, place.Height));
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var whole = new SKRect(0, 0, place.Width, place.Height);

        // Top left to bottom right, in the capture's own two colours.
        void PaintGradient()
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(whole.Right, whole.Bottom),
                                                             capture.GradientColors, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(whole, paint);
        }

        switch (backdrop.Fill)
        {
            case BackdropFill.Solid:
                using (var paint = new SKPaint { Color = Palette.Color(backdrop.ColorHex) })
                    canvas.DrawRect(whole, paint);
                break;
            case BackdropFill.Clear:
                break;
            case BackdropFill.Gradient:
                PaintGradient();
                break;
            case BackdropFill.Wallpaper:
                // A wallpaper that could not be read draws the gradient instead.
                if (backdrop.Wallpaper?.Image.Image is not { } picture)
                {
                    PaintGradient();
                    break;
                }
                var fit = Math.Max(whole.Width / picture.Width, whole.Height / picture.Height);
                var size = new Size(picture.Width * fit, picture.Height * fit);
                var sampling = fit < 1 ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                                       : new SKSamplingOptions(SKCubicResampler.CatmullRom);
                canvas.DrawImage(picture, new Rect((whole.Width - size.Width) / 2, (whole.Height - size.Height) / 2,
                                                   size.Width, size.Height).ToSK(), sampling);
                break;
        }

        if (Shadow(backdrop.Shadow) is var (blur, drop, alpha))
        {
            using var layer = SKSurface.Create(Info(place.Width, place.Height));
            if (layer is not null)
            {
                var shade = layer.Canvas;
                shade.Clear(SKColors.Transparent);
                using var shape = RoundedBox(place);
                // Core Graphics' shadow blur is about twice Skia's sigma, and its offset is
                // y up where Skia's is y down, so the shadow still falls below.
                var sigma = (float)(blur * perPoint / 2);
                using (var shadow = SKImageFilter.CreateDropShadowOnly(0, (float)(drop * perPoint), sigma, sigma,
                                                                       new SKColor(0, 0, 0, (byte)Geometry.Round(alpha * 255))))
                using (var paint = new SKPaint { ImageFilter = shadow })
                {
                    // One layer, so the shadow follows the clipped output's shape, see-through
                    // window corners and all.
                    shade.SaveLayer(paint);
                    shade.ClipPath(shape, SKClipOperation.Intersect, antialias: true);
                    shade.DrawImage(caster, place.Box.ToSK(), new SKSamplingOptions(SKFilterMode.Nearest));
                    shade.Restore();
                }
                // Then the caster out again, leaving its shadow where the output does not cover it.
                using (var cut = new SKPaint { BlendMode = SKBlendMode.DstOut })
                {
                    shade.Save();
                    shade.ClipPath(shape, SKClipOperation.Intersect, antialias: true);
                    shade.DrawImage(caster, place.Box.ToSK(), new SKSamplingOptions(SKFilterMode.Nearest), cut);
                    shade.Restore();
                }
                using var shaded = layer.Snapshot();
                canvas.DrawImage(shaded, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            }
        }
        return surface.Snapshot();
    }

    /// <summary>The output over its ground, clipped to its rounded shape.</summary>
    private static SKImage? Frame(SKImage content, SKImage ground, FramePlacement place)
    {
        using var surface = SKSurface.Create(Info(place.Width, place.Height));
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawImage(ground, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        using var shape = RoundedBox(place);
        canvas.ClipPath(shape, SKClipOperation.Intersect, antialias: true);
        canvas.DrawImage(content, place.Box.ToSK(), new SKSamplingOptions(SKFilterMode.Nearest));
        return surface.Snapshot();
    }

    /// <summary>Blur and drop in points, and how dark.</summary>
    private static (double Blur, double Drop, double Alpha)? Shadow(BackdropShadow shadow) => shadow switch
    {
        BackdropShadow.Soft => (24, 10, 0.3),
        BackdropShadow.Strong => (40, 18, 0.45),
        _ => null,
    };
}

/// <summary>A frame without its output: the backdrop's fill and the shadow the output throws
/// on it.</summary>
public sealed class FrameGround : IDisposable
{
    public SKImage Image { get; }
    internal Key GroundKey { get; }

    internal FrameGround(SKImage image, Key key)
    {
        Image = image;
        GroundKey = key;
    }

    public void Dispose() => Image.Dispose();

    /// <summary>Everything the ground depends on. The annotations are not in it, which is the point.</summary>
    internal sealed record Key(Backdrop Backdrop, Capture Capture, Rect Output, Rect Extent, double OutputScale);
}
