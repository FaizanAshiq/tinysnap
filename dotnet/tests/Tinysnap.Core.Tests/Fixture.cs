using SkiaSharp;

namespace Tinysnap.Core.Tests;

/// <summary>Synthetic captures for tests, painted in the same y down space as the rest of Core.</summary>
internal static class Fixture
{
    public static readonly SKColor White = new(255, 255, 255);
    public static readonly SKColor Black = new(0, 0, 0);
    public static readonly SKColor Blue = new(0, 0, 255);
    public static readonly SKColor Green = new(0, 255, 0);
    public static readonly Style Red = new(Palette.Red);

    /// <summary>A colour from sRGB components in 0 to 1, as the Swift tests write them.</summary>
    public static SKColor Rgb(double red, double green, double blue) =>
        new((byte)Geometry.Round(red * 255), (byte)Geometry.Round(green * 255), (byte)Geometry.Round(blue * 255));

    public static SKImage CaptureImage(int width, int height, SKColor? fill = null, Action<SKCanvas>? paint = null)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(fill ?? White);
        // Skia is already y down, so the painter draws in capture pixels with no flip.
        paint?.Invoke(surface.Canvas);
        return surface.Snapshot();
    }

    public static Capture Capture(int width, int height, double scale = 1, SKColor? fill = null,
                                  Action<SKCanvas>? paint = null) =>
        new(CaptureImage(width, height, fill, paint), scale);

    public static Annotation Annotation(AnnotationKind kind, Style? style = null) => Core.Annotation.New(kind, style ?? Red);

    public static void Fill(SKCanvas canvas, Rect rect, SKColor color)
    {
        using var paint = new SKPaint { Color = color };
        canvas.DrawRect(rect.ToSK(), paint);
    }

    public static (int R, int G, int B) Pixel(SKImage image, int x, int y)
    {
        var p = PixelBuffer.From(image)!.Pixel(x, y);
        return (p.R, p.G, p.B);
    }

    public static bool IsClose((int R, int G, int B) pixel, (int R, int G, int B) expected, int tolerance = 3) =>
        Math.Abs(pixel.R - expected.R) <= tolerance && Math.Abs(pixel.G - expected.G) <= tolerance
        && Math.Abs(pixel.B - expected.B) <= tolerance;

    public static (int R, int G, int B) Rgb(SKColor color) => (color.Red, color.Green, color.Blue);
}
