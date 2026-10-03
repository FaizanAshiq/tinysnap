using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>The one renderer. The canvas draws what this returns and export writes it, so
/// what you see is exactly what you copy. It always draws into a CPU surface: redactions and
/// the magnifier read back what is drawn so far, which a GPU canvas cannot do mid-frame.</summary>
public static partial class Renderer
{
    /// <summary>A region's size in output pixels. Rounded down: rounding 400.5 up gave a last
    /// column only half covered by the capture, which came out half transparent.</summary>
    internal static Size PixelSize(Rect region, double outputScale) =>
        new(Math.Max(1, Math.Floor(region.Size.Width * outputScale)), Math.Max(1, Math.Floor(region.Size.Height * outputScale)));

    internal static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

    /// <summary>Draws <paramref name="region"/> of the document, in capture pixels, at
    /// <paramref name="outputScale"/> output pixels per capture pixel, the whole extent unless
    /// told otherwise. <paramref name="hidden"/> leaves out annotations, used for the text
    /// being typed. <paramref name="sharpPixels"/> enlarges the capture as squares, as the canvas
    /// shows it past 100%, rather than smoothing it as an export does.</summary>
    public static SKImage? Render(Document document, Rect? region = null, double outputScale = 1,
                                  IReadOnlySet<Guid>? hidden = null, bool sharpPixels = false)
    {
        var area = region ?? document.Extent;
        var size = PixelSize(area, outputScale);
        using var surface = SKSurface.Create(Info((int)size.Width, (int)size.Height));
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        // Capture pixels, y down, which is Skia's own space: scale and shift, nothing flips.
        canvas.Scale((float)outputScale);
        canvas.Translate((float)-area.MinX, (float)-area.MinY);

        var painter = new Canvas(surface, area, outputScale, size, document.Scale, document.Extent.Origin);
        // Past the capture, the canvas carries on in the capture's edge colour.
        if (!document.Capture.Bounds.Contains(area))
        {
            using var edge = new SKPaint { Color = document.Capture.EdgeColor };
            canvas.DrawRect(area.ToSK(), edge);
        }
        painter.Draw(document.Capture.Image, document.Capture.Bounds, crisp: sharpPixels);

        var visible = document.Annotations.Where(a => !a.IsHidden && (hidden is null || !hidden.Contains(a.Id))).ToList();
        var spotlightDrawn = false;
        foreach (var annotation in visible)
        {
            if (annotation.Kind is AnnotationKind.Spotlight)
            {
                // Every spotlight lights one shared area, drawn at the first one's place.
                if (spotlightDrawn) continue;
                spotlightDrawn = true;
                painter.Spotlight(visible
                    .Where(a => a.Kind is AnnotationKind.Spotlight)
                    .Select(a => (((AnnotationKind.Spotlight)a.Kind).Rect, a.Style.Corners))
                    .ToList());
                continue;
            }
            painter.Draw(annotation, document.StepNumber(annotation.Id));
        }
        return surface.Snapshot();
    }
}

/// <summary>One render in progress. Redactions and the magnifier read back what has been
/// drawn so far, which is why they affect everything beneath them and nothing above.</summary>
/// <param name="anchor">Where the whole render starts, so a redaction's grain lies the same in a
/// render of part of the document as in the whole.</param>
internal sealed class Canvas(SKSurface surface, Rect region, double outputScale, Size deviceSize, double scale, Point anchor)
{
    private SKCanvas Context => surface.Canvas;
    public Rect Region { get; } = region;
    public double OutputScale { get; } = outputScale;
    public Size DeviceSize { get; } = deviceSize;
    public double Scale { get; } = scale;

    // Catmull-Rom interpolates, so a capture drawn at its own size comes back exactly, as
    // Core Graphics' high quality does; Mitchell softened every edge by a pixel. Shrinking
    // goes through mipmaps instead, which averages rather than skipping pixels.
    private static readonly SKSamplingOptions Enlarging = new(SKCubicResampler.CatmullRom);
    private static readonly SKSamplingOptions Shrinking = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private static readonly SKSamplingOptions Crisp = new(SKFilterMode.Nearest, SKMipmapMode.None);

    public void Draw(SKImage image, Rect rect, bool crisp = false, SKPaint? paint = null)
    {
        var sampling = crisp ? Crisp
            : Context.TotalMatrix.ScaleX * rect.Size.Width / image.Width < 0.999 ? Shrinking
            : Enlarging;
        Context.DrawImage(image, rect.ToSK(), sampling, paint);
    }

    private static SKPaint Stroke(SKColor color, double width, SKStrokeCap cap = SKStrokeCap.Round) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = (float)width,
        StrokeCap = cap,
        StrokeJoin = SKStrokeJoin.Round,
    };

    private static SKPaint Fill(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    public void Draw(Annotation annotation, int? stepNumber)
    {
        var size = annotation.PixelSize(Scale);
        var color = Palette.Color(annotation.Style.ColorHex);
        Context.Save();
        try
        {
            switch (annotation.Kind)
            {
                case AnnotationKind.Arrow(var from, var to):
                {
                    using var path = ArrowShape.Path(from, to, size);
                    // Half the width again as a round joined outline, so no corner is a peak.
                    using var paint = Stroke(color, size * 0.5);
                    paint.Style = SKPaintStyle.StrokeAndFill;
                    Context.DrawPath(path, paint);
                    break;
                }
                case AnnotationKind.Line(var from, var to):
                {
                    using var paint = Stroke(color, size);
                    Context.DrawLine(from.ToSK(), to.ToSK(), paint);
                    break;
                }
                case AnnotationKind.Measure(var from, var to):
                    MeasureShape.Draw(Context, from, to, size / Scale, color, Scale, annotation.LabelAt);
                    break;
                case AnnotationKind.Rectangle(var rect):
                {
                    // Half the stroke again, so the inside of an outline is as round as the outside.
                    var radius = Radius(annotation.Style.Corners, rect, annotation.Style.Filled ? 0 : size / 2);
                    using var paint = annotation.Style.Filled ? Fill(color) : Stroke(color, size);
                    Context.DrawRoundRect(rect.ToSK(), (float)radius, (float)radius, paint);
                    break;
                }
                case AnnotationKind.Oval(var rect):
                {
                    using var paint = annotation.Style.Filled ? Fill(color) : Stroke(color, size);
                    Context.DrawOval(rect.ToSK(), paint);
                    break;
                }
                case AnnotationKind.Text(var origin, var text):
                    TextLayout.Draw(Context, text, origin, size / Scale, Scale, color, align: annotation.Style.Align);
                    break;
                case AnnotationKind.Highlighter(var from, var to):
                {
                    using var paint = Stroke(Palette.Color(annotation.Style.ColorHex, 0.4), size);
                    paint.BlendMode = SKBlendMode.Multiply;
                    Context.DrawLine(from.ToSK(), to.ToSK(), paint);
                    break;
                }
                case AnnotationKind.Freehand(var points):
                {
                    using var path = Smoothing.Path(points);
                    using var paint = Stroke(color, size);
                    Context.DrawPath(path, paint);
                    break;
                }
                case AnnotationKind.Step(var center):
                    DrawStep(stepNumber ?? 0, center, size, color);
                    break;
                case AnnotationKind.Image(var rect, var pasted):
                {
                    var corner = Radius(annotation.Style.Corners, rect);
                    ClipRounded(rect, corner);
                    using var paint = new SKPaint
                    {
                        Color = SKColors.White.WithAlpha((byte)Geometry.Round(annotation.Style.Opacity * 255)),
                        BlendMode = annotation.Style.Difference ? SKBlendMode.Difference : SKBlendMode.SrcOver,
                    };
                    Draw(pasted.Image, rect, paint: paint);
                    break;
                }
                case AnnotationKind.Magnifier(var center, var radius, var zoom):
                    Magnify(center, radius, zoom);
                    break;
                case AnnotationKind.Blur(var rect):
                    Blur(rect, size * OutputScale, annotation.Style.Corners);
                    break;
                case AnnotationKind.Pixelate(var rect):
                    Pixelate(rect, size * OutputScale, annotation.Style.Corners);
                    break;
                case AnnotationKind.Erase(var rect):
                    Erase(rect);
                    break;
            }
        }
        finally
        {
            Context.Restore();
        }
    }

    private void DrawStep(int number, Point center, double diameter, SKColor color)
    {
        using (var paint = Fill(color))
            Context.DrawOval(new SKRect((float)(center.X - diameter / 2), (float)(center.Y - diameter / 2),
                                        (float)(center.X + diameter / 2), (float)(center.Y + diameter / 2)), paint);

        var label = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var points = diameter / Scale * 0.55;
        var metrics = TextLayout.Metrics(label, points, Scale, bold: true);
        var origin = new Point(center.X - metrics.Width / 2, center.Y - (metrics.Ascent + metrics.Descent) / 2);
        TextLayout.Draw(Context, label, origin, points, Scale, SKColors.White, bold: true);
    }

    /// <summary>A box's corner radius in capture pixels, never more than half its shorter side.</summary>
    public double Radius(CornerSize corners, Rect rect, double extra = 0)
    {
        var half = Math.Max(0, Math.Min(rect.Size.Width, rect.Size.Height) / 2);
        if (corners.Points() is not { } points) return half;
        return points == 0 ? 0 : Math.Min(points * Scale + extra, half);
    }

    private void ClipRounded(Rect rect, double corner)
    {
        using var path = new SKPath();
        path.AddRoundRect(rect.ToSK(), (float)corner, (float)corner, SKPathDirection.Clockwise);
        Context.ClipPath(path, SKClipOperation.Intersect, antialias: true);
    }

    public void Spotlight(IReadOnlyList<(Rect Rect, CornerSize Corners)> rects)
    {
        Context.Save();
        Context.SaveLayer();
        using (var dim = new SKPaint { Color = new SKColor(0, 0, 0, 128) })
            Context.DrawRect(Region.ToSK(), dim);
        using var clear = new SKPaint { BlendMode = SKBlendMode.Clear, IsAntialias = true };
        foreach (var (rect, corners) in rects)
        {
            if (rect.Size.Width <= 0 || rect.Size.Height <= 0) continue;
            var corner = (float)Radius(corners, rect);
            Context.DrawRoundRect(rect.ToSK(), corner, corner, clear);
        }
        Context.Restore();
        Context.Restore();
    }

    // Reading back what is drawn so far

    /// <summary>A box in capture pixels as whole output pixels, top left origin, clipped to
    /// the output. Null when nothing of it is on the output at all.</summary>
    private Rect? DeviceRect(Rect rect)
    {
        var device = new Rect((rect.MinX - Region.MinX) * OutputScale, (rect.MinY - Region.MinY) * OutputScale,
                              rect.Size.Width * OutputScale, rect.Size.Height * OutputScale)
            .Integral
            .Intersection(new Rect(Point.Zero, DeviceSize));
        return device.IsNull || device.Width < 1 || device.Height < 1 ? null : device;
    }

    /// <summary>A box's corner in the output pixels of the whole render, which seeds its noise.</summary>
    private (int X, int Y) Grain(Rect device) =>
        ((int)(device.MinX + Math.Round((Region.MinX - anchor.X) * OutputScale)),
         (int)(device.MinY + Math.Round((Region.MinY - anchor.Y) * OutputScale)));

    private Rect CaptureRect(Rect device) =>
        new(device.MinX / OutputScale + Region.MinX, device.MinY / OutputScale + Region.MinY,
            device.Width / OutputScale, device.Height / OutputScale);

    private void Blur(Rect rect, double blurRadius, CornerSize corners)
    {
        if (DeviceRect(rect) is not { } device) return;
        using var snapshot = surface.Snapshot();
        using var patchSurface = SKSurface.Create(Renderer.Info((int)device.Width, (int)device.Height));
        if (patchSurface is null) return;
        var sigma = (float)Math.Max(1, blurRadius);
        // The whole snapshot is the blur's input, clamped at its edges, so the patch reads
        // its real neighbours rather than a dark border.
        using (var blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { ImageFilter = blur })
            patchSurface.Canvas.DrawImage(snapshot, (float)-device.MinX, (float)-device.MinY, Crisp, paint);
        using var patch = patchSurface.Snapshot();
        var buffer = PixelBuffer.From(patch);
        if (buffer is null) return;
        buffer.AddNoise(12, Grain(device));
        using var noisy = buffer.MakeImage();
        if (noisy is null) return;
        DrawRounded(noisy, CaptureRect(device), crisp: false, corners);
    }

    /// <summary>Blur and pixelate boxes take the corners their style sets, like every other box.</summary>
    private void DrawRounded(SKImage image, Rect rect, bool crisp, CornerSize corners)
    {
        Context.Save();
        ClipRounded(rect, Radius(corners, rect));
        Draw(image, rect, crisp);
        Context.Restore();
    }

    /// <summary>Square blocks, each the average of the pixels it covers, counted from the
    /// box's top left corner.</summary>
    private void Pixelate(Rect rect, double block, CornerSize corners)
    {
        if (DeviceRect(rect) is not { } device) return;
        using var snapshot = surface.Snapshot();
        using var patch = snapshot.Subset(device.ToSKRectI());
        if (patch is null || PixelBuffer.From(patch) is not { } buffer) return;
        buffer.Pixelate(Math.Max(2, (int)Geometry.Round(block)));
        buffer.AddNoise(12, Grain(device));
        using var blocks = buffer.MakeImage();
        if (blocks is null) return;
        DrawRounded(blocks, CaptureRect(device), crisp: true, corners);
    }

    private void Erase(Rect rect)
    {
        if (DeviceRect(rect) is not { } device) return;
        using var snapshot = surface.Snapshot();
        // One pixel of surroundings on each side, where the output has them.
        var around = device.Inset(-1, -1).Intersection(new Rect(Point.Zero, DeviceSize));
        using var patch = snapshot.Subset(around.ToSKRectI());
        if (patch is null || PixelBuffer.From(patch) is not { } buffer) return;
        buffer.EraseFill(((int)(device.MinX - around.MinX), (int)(device.MinY - around.MinY),
                          (int)device.Width, (int)device.Height));
        using var filled = buffer.MakeImage();
        if (filled is null) return;
        Draw(filled, CaptureRect(around), crisp: true);
    }

    /// <summary>Enlarges everything drawn so far inside a circle, pixels kept sharp.</summary>
    private void Magnify(Point center, double radius, double zoom)
    {
        if (radius <= 0) return;
        using var snapshot = surface.Snapshot();
        var lens = new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        Context.Save();
        using (var path = new SKPath())
        {
            path.AddOval(lens.ToSK(), SKPathDirection.Clockwise);
            Context.ClipPath(path, SKClipOperation.Intersect, antialias: true);
        }
        Context.Translate((float)center.X, (float)center.Y);
        Context.Scale((float)zoom);
        Context.Translate((float)-center.X, (float)-center.Y);
        Draw(snapshot, Region, crisp: true);
        Context.Restore();

        using (var ring = Stroke(SKColors.White, 3 * Scale))
            Context.DrawOval(lens.ToSK(), ring);
        using (var edge = Stroke(new SKColor(0, 0, 0, 77), 1 * Scale))
            Context.DrawOval(lens.Inset(-1.5 * Scale, -1.5 * Scale).ToSK(), edge);
    }
}
