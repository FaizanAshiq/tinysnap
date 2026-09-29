namespace Tinysnap.Core.Tests;

public class ResizeTests
{
    /// <summary>A 2x capture as it is, grown past its left edge, cropped on odd pixels, and
    /// cropped inside a backdrop: every sum the renderer rounds.</summary>
    private static Document[] Documents()
    {
        var capture = Fixture.Capture(301, 201, 2);
        var past = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(-40, 10, 60, 20)));
        var crop = new Rect(33.5, 17.25, 200.5, 101);
        return
        [
            new Document(capture),
            new Document(capture, annotations: [past]),
            new Document(capture, crop),
            new Document(capture, crop, backdrop: Backdrop.Defaults),
        ];
    }

    [Fact]
    public void TheSizeShownIsExactlyTheSizeAnExportMakes()
    {
        foreach (var document in Documents())
        {
            foreach (var resize in new[] { 0.01, 0.25, 0.37, 0.5, 0.583, 1, 1.5, 2, 3.3 })
            {
                var image = Exporter.Export(document with { Resize = resize }, ExportScale.Native)!.Image;
                Assert.Equal(new Size(image.Width, image.Height), document.ExportPixelSize(resize));
            }
        }
    }

    [Fact]
    public void ATypedWidthOrHeightGivesExactlyThatSize()
    {
        foreach (var document in Documents().SkipLast(1))
        {
            // All within 1% to 400% of the smallest of them, a crop 101 pixels high.
            for (var pixels = 5; pixels <= 380; pixels += 7)
            {
                Assert.Equal(pixels, document.ExportPixelSize(document.ResizeForWidth(pixels)).Width);
                Assert.Equal(pixels, document.ExportPixelSize(document.ResizeForHeight(pixels)).Height);
            }
        }
    }

    [Fact]
    public void ATypedWidthKeepsTheAspectRatio()
    {
        var document = new Document(Fixture.Capture(1200, 800, 2));
        Assert.Equal(new Size(700, 466), document.ExportPixelSize(document.ResizeForWidth(700)));
    }

    [Fact]
    public void DividingThePixelsWantedByTheFullSizeCanLandAPixelShort()
    {
        // Why the fields search rather than divide: on the odd crop, some widths come out one
        // short of what was typed.
        var cropped = Documents()[2];
        var full = cropped.ExportPixelSize(1).Width;
        var shortOnes = Enumerable.Range(1, 400).Where(p => cropped.ExportPixelSize(p / full).Width != p);
        Assert.NotEmpty(shortOnes);
    }

    [Fact]
    public void WithABackdropATypedWidthLandsWithinAPixel()
    {
        // The padding grows on both sides at once, so the width moves two pixels at a time there.
        var framed = Documents()[3];
        for (var pixels = 250; pixels <= 320; pixels++)
        {
            var width = framed.ExportPixelSize(framed.ResizeForWidth(pixels)).Width;
            Assert.True(width >= pixels && width <= pixels + 1);
        }
    }

    [Fact]
    public void TheSizeIsHeldBetweenOnePercentAndFourHundred()
    {
        var document = new Document(Fixture.Capture(300, 200));
        Assert.Equal(0.01, document.ClampedResize(0.001));
        Assert.Equal(4, document.ClampedResize(9));
        Assert.Equal(4, document.ResizeForWidth(5000));
        Assert.Equal(0.01, document.ResizeForWidth(0));
    }

    [Fact]
    public void NoExportIsLongerThanSixteenThousandPixels()
    {
        // 5000 pixels across at 400% would be 20,000.
        var wide = new Document(Fixture.Capture(5000, 10));
        Assert.True(wide.LargestResize() < 4);
        Assert.True(wide.ExportPixelSize(wide.LargestResize()).Width <= 16_384);
        Assert.True(wide.ExportPixelSize(wide.LargestResize() + 0.001).Width > 16_384);
        Assert.Equal(wide.LargestResize(), wide.ClampedResize(4));
    }

    [Fact]
    public void WithNoSizeOfItsOwnTheSettingIsTheStartingSize()
    {
        var document = new Document(Fixture.Capture(800, 600, 2));
        Assert.Equal(1, Exporter.OutputScale(document, ExportScale.Native));
        Assert.Equal(0.5, Exporter.OutputScale(document, ExportScale.OneX));
        Assert.Equal(2, Exporter.OutputScale(document with { Resize = 2 }, ExportScale.OneX));
    }

    [Fact]
    public void ANewSizeIsOneUndoableStepHeldToTheLimits()
    {
        var editor = new EditorSession(new Document(Fixture.Capture(300, 200)));
        editor.SetResize(2);
        editor.SetResize(9);
        Assert.Equal(4.0, editor.Display.Resize);
        editor.Undo();
        Assert.Equal(2.0, editor.Display.Resize);
        editor.Undo();
        Assert.Null(editor.Display.Resize);
    }
}
