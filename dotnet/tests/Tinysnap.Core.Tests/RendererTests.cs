using SkiaSharp;

namespace Tinysnap.Core.Tests;

public class RendererTests
{
    private static readonly (int, int, int) White = (255, 255, 255);

    private static SKImage Render(Capture capture, IEnumerable<Annotation> annotations, IReadOnlySet<Guid>? hidden = null) =>
        Renderer.Render(new Document(capture, annotations: [.. annotations]), hidden: hidden)!;

    [Fact]
    public void AHalfSeeThroughOverlayLandsHalfwayBetweenItAndTheCapture()
    {
        var capture = Fixture.Capture(100, 100);
        var black = new PastedImage(Fixture.CaptureImage(10, 10, Fixture.Black));
        var overlay = Annotation.New(new AnnotationKind.Image(new Rect(10, 10, 40, 40), black),
                                     new Style(Palette.Red, corners: CornerSize.Square, opacity: 0.5));
        var image = Render(capture, [overlay]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 30, 30), (128, 128, 128), 4));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 80, 80), White));
    }

    [Fact]
    public void ADifferenceOverlayMatchingTheCaptureRendersBlack()
    {
        var capture = Fixture.Capture(100, 100, fill: Fixture.Blue);
        var same = new PastedImage(Fixture.CaptureImage(10, 10, Fixture.Blue));
        var overlay = Annotation.New(new AnnotationKind.Image(new Rect(10, 10, 40, 40), same),
                                     new Style(Palette.Red, corners: CornerSize.Square, difference: true));
        var image = Render(capture, [overlay]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 30, 30), (0, 0, 0)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 80, 80), (0, 0, 255)));
    }

    [Fact]
    public void AGrownCanvasTakesTheColourMostOfTheCaptureBorderHas()
    {
        var slate = Fixture.Rgb(0.2, 0.3, 0.4);
        // A white patch on the top edge, which the fill must not pick up.
        var capture = Fixture.Capture(100, 60, fill: slate, paint: c => Fixture.Fill(c, new Rect(0, 0, 20, 10), Fixture.White));
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(80, 20, 60, 20)));
        var document = new Document(capture, annotations: [box]);
        var image = Renderer.Render(document);
        Assert.NotNull(image);

        Assert.True(image.Width == (int)document.Extent.Width && image.Height == (int)document.Extent.Height);
        // Past the capture's right edge and clear of the rectangle.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, image.Width - 3, 3), (51, 77, 102)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, image.Width - 3, image.Height - 3), (51, 77, 102)));
        // The capture itself is untouched, white patch and all.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 5, 5), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 50, 50), (51, 77, 102)));
    }

    [Fact]
    public void KeepsTheCaptureTheRightWayUp()
    {
        var capture = Fixture.Capture(40, 40, paint: c => Fixture.Fill(c, new Rect(0, 0, 40, 20), Fixture.Blue));
        var image = Render(capture, []);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 5, 5), (0, 0, 255)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 5, 35), White));
    }

    [Fact]
    public void DrawsARectangleWhereItIsStored()
    {
        var capture = Fixture.Capture(100, 100);
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(10, 10, 50, 30)))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 10, 25), (255, 59, 48)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 35, 25), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 80, 80), White));
    }

    [Fact]
    public void ARectanglesCornersAreRoundedUnlessSetSquare()
    {
        var rect = new Rect(20, 20, 60, 60);
        var rounded = Render(Fixture.Capture(100, 100), [Fixture.Annotation(new AnnotationKind.Rectangle(rect))]);
        var sharp = Render(Fixture.Capture(100, 100),
                           [Fixture.Annotation(new AnnotationKind.Rectangle(rect), new Style(Palette.Red, corners: CornerSize.Square))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(rounded, 19, 19), White, 40));
        Assert.True(Fixture.IsClose(Fixture.Pixel(sharp, 19, 19), (255, 59, 48)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(rounded, 19, 50), (255, 59, 48)));
    }

    [Fact]
    public void AFullyRoundSpotlightOnASquareIsACircle()
    {
        var rect = new Rect(20, 20, 60, 60);
        var square = Render(Fixture.Capture(100, 100),
                            [Fixture.Annotation(new AnnotationKind.Spotlight(rect), new Style(Palette.Red, corners: CornerSize.Square))]);
        var circle = Render(Fixture.Capture(100, 100),
                            [Fixture.Annotation(new AnnotationKind.Spotlight(rect), new Style(Palette.Red, corners: CornerSize.Full))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(square, 23, 23), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(circle, 23, 23), (128, 128, 128), 3));
        Assert.True(Fixture.IsClose(Fixture.Pixel(circle, 50, 50), White));
    }

    [Fact]
    public void EraseMatchesAFlatBackground()
    {
        var grey = Fixture.Rgb(0.5, 0.5, 0.5);
        var capture = Fixture.Capture(100, 100, fill: grey, paint: c => Fixture.Fill(c, new Rect(40, 40, 20, 20), Fixture.Black));
        var background = Fixture.Pixel(capture.Image, 5, 5);
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Erase(new Rect(35, 35, 30, 30)))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 50, 50), background, 1));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 36, 36), background, 1));
    }

    [Fact]
    public void EraseDoesNotSmearALetterThatTouchesItsEdge()
    {
        var grey = Fixture.Rgb(0.5, 0.5, 0.5);
        // A stroke crossing the box's left edge, as when the word beside the erased one is not
        // quite clear of it. It used to streak across the box, row by row.
        var capture = Fixture.Capture(100, 100, fill: grey, paint: c => Fixture.Fill(c, new Rect(30, 46, 7, 8), Fixture.Black));
        var background = Fixture.Pixel(capture.Image, 5, 5);
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Erase(new Rect(35, 35, 30, 30)))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 37, 50), background, 2));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 45, 50), background, 2));
    }

    [Fact]
    public void EraseAtTheEdgeBlendsTheSidesItHas()
    {
        var capture = Fixture.Capture(100, 100, fill: Fixture.Green, paint: c => Fixture.Fill(c, new Rect(0, 40, 20, 20), Fixture.Black));
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Erase(new Rect(0, 35, 30, 30)))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 2, 50), (0, 255, 0), 1));
    }

    [Fact]
    public void EraseCarriesAGradientThrough()
    {
        var capture = Fixture.Capture(256, 60, paint: c =>
        {
            for (var x = 0; x < 256; x++) Fixture.Fill(c, new Rect(x, 0, 1, 60), new SKColor((byte)x, (byte)x, (byte)x));
        });
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Erase(new Rect(108, 10, 40, 40)))]);
        Assert.True(Math.Abs(Fixture.Pixel(image, 128, 30).R - 128) <= 3);
    }

    [Fact]
    public void ARedactionHidesWhatIsBelowAndLeavesWhatIsAbove()
    {
        var capture = Fixture.Capture(200, 100);
        var square = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(20, 20, 40, 40)), new Style("#000000", filled: true));
        var erase = Fixture.Annotation(new AnnotationKind.Erase(new Rect(10, 10, 80, 80)));
        var line = Fixture.Annotation(new AnnotationKind.Line(new Point(0, 40), new Point(200, 40)),
                                      new Style("#00FF00", StyleSize.Large));
        var image = Render(capture, [square, erase, line]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 30, 30), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 30, 40), (0, 255, 0)));
    }

    [Fact]
    public void BlurChangesThePixelsInItsBox()
    {
        var capture = Fixture.Capture(100, 100, paint: c => Fixture.Fill(c, new Rect(0, 0, 50, 100), Fixture.Black));
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Blur(new Rect(30, 30, 40, 40)))]);
        var edge = Fixture.Pixel(image, 50, 50);
        Assert.True(edge.R > 40 && edge.R < 215, $"edge reads {edge.R}");
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 90, 90), White));
    }

    [Fact]
    public void PixelateTurnsFineDetailIntoFlatBlocks()
    {
        var capture = Fixture.Capture(64, 64, paint: c =>
        {
            for (var x = 0; x < 64; x += 2) Fixture.Fill(c, new Rect(x, 0, 1, 64), Fixture.Black);
        });
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Pixelate(new Rect(0, 0, 64, 64)),
                                                        new Style(Palette.Red, StyleSize.Small))]);
        // Inside one block, away from the box's rounded corner.
        var a = Fixture.Pixel(image, 9, 9);
        var b = Fixture.Pixel(image, 14, 14);
        Assert.True(Math.Abs(a.R - 128) <= 40);
        Assert.True(Math.Abs(a.R - b.R) <= 25);
    }

    [Fact]
    public void RedactionsPastTheEdgeOfTheCaptureDoNotCrash()
    {
        var capture = Fixture.Capture(50, 50);
        Annotation[] annotations =
        [
            Fixture.Annotation(new AnnotationKind.Blur(new Rect(-20, -20, 40, 40))),
            Fixture.Annotation(new AnnotationKind.Pixelate(new Rect(30, 30, 100, 100))),
            Fixture.Annotation(new AnnotationKind.Erase(new Rect(-10, 45, 100, 100))),
            Fixture.Annotation(new AnnotationKind.Erase(new Rect(60, 60, 10, 10))),
            Fixture.Annotation(new AnnotationKind.Magnifier(Point.Zero, 30, 2)),
        ];
        var image = Render(capture, annotations);
        // The canvas grows to take them all in.
        var extent = new Document(capture, annotations: [.. annotations]).Extent;
        Assert.True(image.Width == (int)extent.Width && image.Height == (int)extent.Height);
    }

    [Fact]
    public void TheMagnifierEnlargesWhatIsUnderIt()
    {
        var capture = Fixture.Capture(200, 200, paint: c => Fixture.Fill(c, new Rect(98, 0, 4, 200), Fixture.Black));
        Assert.True(Fixture.IsClose(Fixture.Pixel(capture.Image, 103, 100), White));
        var image = Render(capture, [Fixture.Annotation(new AnnotationKind.Magnifier(new Point(100, 100), 50, 2))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 103, 100), (0, 0, 0)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 107, 100), White));
    }

    [Fact]
    public void ASpotlightDimsOnlyOutsideItsBox()
    {
        var image = Render(Fixture.Capture(100, 100), [Fixture.Annotation(new AnnotationKind.Spotlight(new Rect(20, 20, 40, 40)))]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 40, 40), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 80, 80), (128, 128, 128), 3));
    }

    [Fact]
    public void OverlappingSpotlightsLightOneSharedArea()
    {
        var image = Render(Fixture.Capture(100, 100),
        [
            Fixture.Annotation(new AnnotationKind.Spotlight(new Rect(10, 10, 40, 40))),
            Fixture.Annotation(new AnnotationKind.Spotlight(new Rect(30, 30, 40, 40))),
        ]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 35, 35), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 65, 65), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 90, 10), (128, 128, 128), 3));
    }

    [Fact]
    public void TextIsDrawnUprightInsideItsBounds()
    {
        var text = Fixture.Annotation(new AnnotationKind.Text(new Point(10, 10), "Hello"));
        var image = Render(Fixture.Capture(300, 100), [text]);
        var bounds = text.Bounds(1);
        var buffer = PixelBuffer.From(image)!;
        var inside = 0;
        var outside = 0;
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                if (buffer.Pixel(x, y).G >= 200) continue;
                if (bounds.Inset(-1, -1).Contains(new Point(x, y))) inside++;
                else outside++;
            }
        }
        Assert.True(inside > 20);
        Assert.Equal(0, outside);
    }

    [Fact]
    public void AStepIsAFilledCircleInItsColour()
    {
        var step = Fixture.Annotation(new AnnotationKind.Step(new Point(50, 50)));
        var image = Render(Fixture.Capture(100, 100), [step]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 50 - 13, 50), (255, 59, 48)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 50 - 20, 50), White));
    }

    [Fact]
    public void HiddenAnnotationsAreLeftOut()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(0, 0, 50, 50)), new Style("#000000", filled: true));
        var image = Render(Fixture.Capture(100, 100), [box], new HashSet<Guid> { box.Id });
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 25, 25), White));
    }

    [Fact]
    public void AFullScreenFiveKCaptureRendersWithRedactions()
    {
        var capture = Fixture.Capture(6016, 3384, 2);
        var image = Render(capture,
        [
            Fixture.Annotation(new AnnotationKind.Blur(new Rect(1000, 1000, 2000, 1000))),
            Fixture.Annotation(new AnnotationKind.Pixelate(new Rect(3000, 500, 1000, 1000))),
            Fixture.Annotation(new AnnotationKind.Erase(new Rect(100, 100, 500, 500))),
        ]);
        Assert.True(image.Width == 6016 && image.Height == 3384);
    }

    [Fact]
    public void AnArrowDrawnDownRightHasItsInkDownRight()
    {
        // A flip left over from Core Graphics would draw the head at the top right instead.
        var arrow = Fixture.Annotation(new AnnotationKind.Arrow(new Point(20, 20), new Point(180, 180)));
        var image = Render(Fixture.Capture(200, 200), [arrow]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 175, 175), (255, 59, 48), 40));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 175, 25), White));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 25, 175), White));
    }
}
