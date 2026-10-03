using SkiaSharp;

namespace Tinysnap.Core.Tests;

/// <summary>Past 100% the canvas draws what is in view at the screen's own resolution: shapes
/// smooth, the capture's pixels sharp squares, and every redaction as it is in the whole render.</summary>
public class CloseUpTests
{
    /// <summary>One pixel wide black and white stripes, the finest detail a capture has.</summary>
    private static Capture Stripes() => Fixture.Capture(80, 80, paint: canvas =>
    {
        for (var x = 0; x < 80; x += 2) Fixture.Fill(canvas, new Rect(x, 0, 1, 80), Fixture.Black);
    });

    /// <summary>The largest difference in any channel of any pixel, the two the same size.</summary>
    private static int Difference(SKImage a, SKImage b)
    {
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        var first = PixelBuffer.From(a)!;
        var second = PixelBuffer.From(b)!;
        var most = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                var (p, q) = (first.Pixel(x, y), second.Pixel(x, y));
                most = Math.Max(most, Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B))));
            }
        return most;
    }

    /// <summary>The whole document drawn at <paramref name="scale"/>, its pixels sharp, cut to <paramref name="region"/>.</summary>
    private static SKImage Whole(Document document, double scale, Rect region)
    {
        using var full = Renderer.Render(document, outputScale: scale, sharpPixels: true)!;
        var cut = new SKRectI((int)((region.MinX - document.Extent.MinX) * scale), (int)((region.MinY - document.Extent.MinY) * scale),
                              (int)((region.MaxX - document.Extent.MinX) * scale), (int)((region.MaxY - document.Extent.MinY) * scale));
        return full.Subset(cut);
    }

    [Fact]
    public void UpCloseEachCapturePixelIsASharpSquare()
    {
        var close = Renderer.RenderCloseUp(new Document(Stripes()), new Rect(0, 0, 20, 20), 4, framed: false)!.Value;
        Assert.Equal(new Rect(0, 0, 20, 20), close.Region);
        // Black from 0 to 3, white from 4 to 7: no grey run between them.
        Assert.True(Fixture.IsClose(Fixture.Pixel(close.Image, 3, 10), (0, 0, 0)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(close.Image, 4, 10), (255, 255, 255)));
    }

    [Fact]
    public void UpCloseAShapeIsDrawnAtTheScreensResolution()
    {
        // A diagonal edge drawn at 4x falls across each capture pixel, not along its sides.
        var line = Fixture.Annotation(new AnnotationKind.Line(new Point(0, 0), new Point(40, 30)));
        var document = new Document(Fixture.Capture(60, 60), annotations: [line]);
        var visible = new Rect(0, 0, 60, 60);
        var close = Renderer.RenderCloseUp(document, visible, 4, framed: false)!.Value;
        // Antialiasing shifts up to a tenth of a pixel with where a render starts; the 1x render
        // enlarged is off by nearly 200 along the edge.
        Assert.InRange(Difference(close.Image, Whole(document, 4, visible)), 0, 32);
    }

    [Fact]
    public void APixelateBoxHalfInViewIsDrawnAsItIsWhole()
    {
        // Counted from the box's own corner with the same grain, not from the edge of the view.
        var box = Fixture.Annotation(new AnnotationKind.Pixelate(new Rect(10, 10, 50, 50)),
                                     new Style(Palette.Red, StyleSize.Small, corners: CornerSize.Square));
        var document = new Document(Stripes(), annotations: [box]);
        var close = Renderer.RenderCloseUp(document, new Rect(33, 33, 40, 40), 2, framed: false)!.Value;
        Assert.True(close.Region.Contains(new Rect(10, 10, 50, 50)));
        Assert.InRange(Difference(close.Image, Whole(document, 2, close.Region)), 0, 1);
    }

    [Fact]
    public void ABlurBoxHalfInViewIsDrawnAsItIsWhole()
    {
        var box = Fixture.Annotation(new AnnotationKind.Blur(new Rect(20, 20, 30, 30)), new Style(Palette.Red, corners: CornerSize.Square));
        var document = new Document(Stripes(), annotations: [box]);
        var close = Renderer.RenderCloseUp(document, new Rect(40, 40, 30, 30), 2, framed: false)!.Value;
        Assert.True(close.Region.Contains(new Rect(20, 20, 30, 30)));
        Assert.InRange(Difference(close.Image, Whole(document, 2, close.Region)), 0, 3);
    }

    [Fact]
    public void TheViewGrowsOnlyForWhatReadsBackAndReachesIntoIt()
    {
        var document = new Document(Fixture.Capture(400, 300), annotations:
        [
            Fixture.Annotation(new AnnotationKind.Magnifier(new Point(100, 100), 30, 2)),
            Fixture.Annotation(new AnnotationKind.Pixelate(new Rect(300, 200, 50, 50))),
            Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(0, 0, 400, 300))),
        ]);
        // The lens whole, the far pixelate box and the outline left alone.
        Assert.Equal(new Rect(70, 70, 91, 91), Renderer.CloseUpRegion(document, new Rect(110.4, 110.4, 50, 50), framed: false));
    }

    [Fact]
    public void FramedTheViewStaysOnTheOutput()
    {
        var backdrop = new Backdrop(BackdropFill.Solid, Palette.Red, BackdropPadding.Medium, CornerSize.Square, BackdropShadow.None);
        var document = new Document(Fixture.Capture(200, 100), crop: new Rect(50, 0, 100, 100), backdrop: backdrop);
        Assert.Equal(new Rect(50, 0, 70, 60), Renderer.CloseUpRegion(document, new Rect(0, 0, 120, 60), framed: true));
    }

    [Fact]
    public void ACloseUpTooBigToDrawIsLeftOut()
    {
        // A huge blur box half in view at the deepest zoom: the canvas shows its pixels instead.
        var document = new Document(Fixture.Capture(3000, 3000),
                                    annotations: [Fixture.Annotation(new AnnotationKind.Blur(new Rect(0, 0, 3000, 3000)))]);
        Assert.Null(Renderer.RenderCloseUp(document, new Rect(10, 10, 40, 40), 32, framed: false));
    }
}
