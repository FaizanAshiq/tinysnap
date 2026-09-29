using SkiaSharp;

namespace Tinysnap.Core.Tests;

public class ExportTests
{
    private static ExportedImage Export(Document document, ExportScale scale)
    {
        var exported = Exporter.Export(document, scale);
        Assert.NotNull(exported);
        return exported;
    }

    [Fact]
    public void AnExportTakesInShapesDrawnPastTheCapture()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(-40, 10, 60, 20)));
        var document = new Document(Fixture.Capture(100, 60, 2), annotations: [box]);
        var exported = Export(document, ExportScale.Native);
        Assert.Equal((int)document.Extent.Width, exported.Image.Width);
        Assert.Equal((int)document.Extent.Height, exported.Image.Height);
        Assert.True(document.Extent.MinX < 0);
    }

    [Fact]
    public void ACropOnAGrownCanvasCutsFromTheRightPlace()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(-40, 10, 60, 20)));
        var document = new Document(Fixture.Capture(100, 60, fill: Fixture.Blue), annotations: [box]) with
        {
            Crop = new Rect(60, 30, 20, 20),
        };
        var exported = Export(document, ExportScale.Native);
        Assert.True(exported.Image.Width == 20 && exported.Image.Height == 20);
        Assert.True(Fixture.IsClose(Fixture.Pixel(exported.Image, 10, 10), (0, 0, 255)));
    }

    [Fact]
    public void NativeExportKeepsEveryPixelAndTagsRetinaDpi()
    {
        var document = new Document(Fixture.Capture(800, 600, 2));
        var exported = Export(document, ExportScale.Native);
        Assert.Equal(800, exported.Image.Width);
        Assert.Equal(144, exported.Dpi);
        Assert.Equal(new Size(400, 300), exported.PointSize);

        var png = Exporter.PngData(exported);
        Assert.NotNull(png);
        var decoded = Png.Decode(png);
        Assert.NotNull(decoded);
        Assert.True(Math.Abs(decoded.Value.Scale * 72 - 144) < 0.5);
    }

    [Fact]
    public void OneXExportHalvesARetinaCapture()
    {
        var exported = Export(new Document(Fixture.Capture(800, 600, 2)), ExportScale.OneX);
        Assert.Equal(400, exported.Image.Width);
        Assert.Equal(300, exported.Image.Height);
        Assert.Equal(72, exported.Dpi);
    }

    [Fact]
    public void AStandardDisplayCaptureIsTheSameAtEitherScale()
    {
        var document = new Document(Fixture.Capture(300, 200, 1));
        Assert.Equal(300, Export(document, ExportScale.OneX).Image.Width);
        Assert.Equal(72, Export(document, ExportScale.Native).Dpi);
    }

    [Fact]
    public void ExportUsesTheCropAndCutsOffWhatIsOutsideIt()
    {
        var document = new Document(Fixture.Capture(400, 300)) with
        {
            Crop = new Rect(100, 100, 200, 100),
            Annotations = [Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(0, 0, 50, 50)), new Style("#000000", filled: true))],
        };
        var exported = Export(document, ExportScale.Native);
        Assert.True(exported.Image.Width == 200 && exported.Image.Height == 100);
        Assert.True(Fixture.IsClose(Fixture.Pixel(exported.Image, 0, 0), (255, 255, 255)));
    }

    [Fact]
    public void ExportMatchesTheCanvasAtTheCropEdge()
    {
        // A gradient, so reading back less of it than the canvas does changes the result.
        var capture = Fixture.Capture(400, 200, paint: c =>
        {
            for (var x = 0; x < 400; x++)
            {
                var value = x / 399.0;
                Fixture.Fill(c, new Rect(x, 0, 1, 200), Fixture.Rgb(value, 1 - value, 0.5));
            }
        });
        var document = new Document(capture, annotations:
        [
            Fixture.Annotation(new AnnotationKind.Erase(new Rect(150, 50, 100, 100))),
            Fixture.Annotation(new AnnotationKind.Magnifier(new Point(215, 100), 30, 2)),
        ]) with { Crop = new Rect(0, 0, 200, 200) };
        var canvas = Renderer.Render(document);
        Assert.NotNull(canvas);
        var exported = Export(document, ExportScale.Native).Image;
        foreach (var x in new[] { 150, 170, 186, 190, 199 })
            Assert.True(Fixture.IsClose(Fixture.Pixel(exported, x, 100), Fixture.Pixel(canvas, x, 100), 0));
    }

    [Fact]
    public void AOneXExportOfAnOddSizedRetinaCaptureHasNoHalfCoveredEdge()
    {
        var exported = Export(new Document(Fixture.Capture(801, 601, 2)), ExportScale.OneX);
        Assert.True(exported.Image.Width == 400 && exported.Image.Height == 300);
        var buffer = PixelBuffer.From(exported.Image)!;
        Assert.Equal(255, buffer.Pixel(399, 150).A);
        Assert.Equal(255, buffer.Pixel(200, 299).A);
        Assert.Equal(new Size(400, 300), exported.PointSize);
    }

    [Fact]
    public void AOneXExportOfAnOddCropAtTheCaptureEdgeHasNoHalfCoveredEdge()
    {
        // Only a crop that reaches the capture's edge has nothing beyond it to fill the last
        // half pixel.
        var document = new Document(Fixture.Capture(800, 600, 2)) with { Crop = new Rect(1, 1, 799, 599) };
        var exported = Export(document, ExportScale.OneX);
        var buffer = PixelBuffer.From(exported.Image)!;
        Assert.Equal(255, buffer.Pixel(exported.Image.Width - 1, 0).A);
        Assert.Equal(255, buffer.Pixel(0, exported.Image.Height - 1).A);
    }

    [Fact]
    public void AnExportIsDrawnAtTheCapturesOwnSize()
    {
        var document = new Document(Fixture.Capture(800, 600, 2), resize: 2);
        // A size of its own wins over the setting.
        var doubled = Export(document, ExportScale.OneX);
        Assert.True(doubled.Image.Width == 1600 && doubled.Image.Height == 1200);
        document = document with { Resize = 0.5 };
        Assert.Equal(400, Export(document, ExportScale.Native).Image.Width);
    }

    [Fact]
    public void TheWholeFrameIsDrawnAtTheSize()
    {
        var document = new Document(Fixture.Capture(800, 600, 2), backdrop: Backdrop.Defaults) with
        {
            Crop = new Rect(100, 100, 200, 100),
        };
        var full = Export(document, ExportScale.Native).Image;
        var half = Export(document with { Resize = 0.5 }, ExportScale.Native).Image;
        Assert.True(Math.Abs(half.Width - full.Width / 2) <= 1 && Math.Abs(half.Height - full.Height / 2) <= 1);
    }

    [Fact]
    public void ARetinaCaptureKeepsItsSizeInPointsDownToHalf()
    {
        var document = new Document(Fixture.Capture(1200, 800, 2));
        foreach (var (resize, dpi) in new[] { (2.0, 288.0), (1, 144), (0.75, 108), (0.5, 72), (0.25, 72) })
            Assert.Equal(dpi, Export(document with { Resize = resize }, ExportScale.Native).Dpi);
        Assert.Equal(new Size(600, 400), Export(document with { Resize = 0.75 }, ExportScale.Native).PointSize);
        // Below half it shows smaller: 300 pixels at 72 dpi.
        Assert.Equal(new Size(300, 200), Export(document with { Resize = 0.25 }, ExportScale.Native).PointSize);
    }

    [Fact]
    public void TextIsReadFromEveryPixelWhateverSizeOrBackdropTheCaptureHas()
    {
        var document = new Document(Fixture.Capture(800, 600, 2), backdrop: Backdrop.Defaults, resize: 0.25);
        var image = Exporter.ReadingImage(document);
        Assert.NotNull(image);
        // Full size, and no padding: a backdrop has nothing to read.
        Assert.True(image.Width == 800 && image.Height == 600);
    }

    [Fact]
    public void AnAreaNarrowsWhatIsReadToThePartOfTheOutputUnderIt()
    {
        var document = new Document(Fixture.Capture(400, 300, fill: Fixture.Blue)) with { Crop = new Rect(100, 50, 200, 200) };
        // Half inside the crop: only the half inside is read.
        var image = Exporter.ReadingImage(document, new Rect(50, 100, 100, 40));
        Assert.NotNull(image);
        Assert.True(image.Width == 50 && image.Height == 40);
        Assert.Null(Exporter.ReadingImage(document, new Rect(0, 0, 40, 40)));
    }

    [Fact]
    public void NamesFilesLikeMacOSAndCountsUpOnClashes()
    {
        var date = new DateTimeOffset(2026, 9, 25, 9, 41, 12, TimeSpan.Zero);
        Assert.Equal("Tinysnap 2026-09-25 at 09.41.12.png", FileNaming.FileName(date, TimeZoneInfo.Utc, _ => false));
        var taken = new HashSet<string> { "Tinysnap 2026-09-25 at 09.41.12.png", "Tinysnap 2026-09-25 at 09.41.12 2.png" };
        Assert.Equal("Tinysnap 2026-09-25 at 09.41.12 3.png", FileNaming.FileName(date, TimeZoneInfo.Utc, taken.Contains));
    }

    [Fact]
    public void ANativeExportAtOneAndAHalfIsTaggedAt108Dpi()
    {
        var exported = Export(new Document(Fixture.Capture(150, 90, 1.5)), ExportScale.Native);
        Assert.Equal(108, exported.Dpi, 6);
        Assert.Equal(new Size(100, 60), exported.PointSize);
        Assert.Equal(1.5, Png.Decode(Exporter.PngData(exported)!)!.Value.Scale);
    }
}

public class PngTests
{
    [Fact]
    public void AFractionalScaleSurvivesAPngRoundTrip()
    {
        // pHYs stores whole pixels per metre, so 108 DPI comes back as 107.99; the scale must
        // still read 1.5, never round to 2 as the Mac once did.
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var png = Png.Encode(Fixture.CaptureImage(60, 40), dpi: 72 * scale)!;
            var decoded = Png.Decode(png);
            Assert.NotNull(decoded);
            Assert.Equal(60, decoded.Value.Image.Width);
            Assert.Equal(scale, decoded.Value.Scale);
        }
    }

    [Fact]
    public void APngWithoutDpiReadsAsOnePixelPerPoint()
    {
        var plain = Fixture.CaptureImage(10, 10).Encode(SKEncodedImageFormat.Png, 100).ToArray();
        Assert.Equal(1.0, Png.Decode(plain)!.Value.Scale);
    }
}
