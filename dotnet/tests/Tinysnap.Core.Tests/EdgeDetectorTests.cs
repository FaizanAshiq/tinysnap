using SkiaSharp;

namespace Tinysnap.Core.Tests;

public class EdgeDetectorTests
{
    private static readonly SKColor Page = Fixture.Rgb(0.97, 0.97, 0.97);

    /// <summary>Two 40 pixel cards, 20 apart, on a 160 by 100 capture.</summary>
    private static LuminanceBuffer Cards(SKColor card, SKColor? page = null)
    {
        var image = Fixture.CaptureImage(160, 100, page ?? Fixture.White, c =>
        {
            Fixture.Fill(c, new Rect(20, 30, 40, 40), card);
            Fixture.Fill(c, new Rect(80, 30, 40, 40), card);
        });
        return LuminanceBuffer.From(image)!;
    }

    [Fact]
    public void BrightnessReadsWhiteAsOneAndBlackAsZero()
    {
        var buffer = LuminanceBuffer.From(Fixture.CaptureImage(4, 4, paint: c => Fixture.Fill(c, new Rect(0, 0, 2, 4), Fixture.Black)));
        Assert.NotNull(buffer);
        Assert.Equal(0, buffer.Luminance(0, 0));
        Assert.Equal(1, buffer.Luminance(3, 3));
    }

    [Fact]
    public void TheGapBetweenTwoCardsIsFoundToThePixel()
    {
        var region = new EdgeDetector(0.08, runLength: 1).Bounds((70, 50), Cards(Fixture.Black));
        // Nothing above or below the gap, so the capture's border closes it there.
        Assert.Equal(new Rect(60, 0, 20, 100), region);
    }

    [Fact]
    public void ACardAShadeOffThePageIsFoundOnlyAtALowContrast()
    {
        var buffer = Cards(Fixture.White, Page);
        Assert.Null(new EdgeDetector(0.08, runLength: 1).Bounds((70, 50), buffer));
        Assert.Equal(new Rect(60, 0, 20, 100), new EdgeDetector(0.02, runLength: 1).Bounds((70, 50), buffer));
    }

    [Fact]
    public void ARegionWithNoEdgeOnOneSideIsClosedByTheBorder()
    {
        var region = new EdgeDetector(0.08, runLength: 1).Bounds((5, 50), Cards(Fixture.Black));
        Assert.Equal(new Rect(0, 0, 20, 100), region);
    }

    [Fact]
    public void AFlatFieldHasNoRegion()
    {
        var buffer = LuminanceBuffer.From(Fixture.CaptureImage(50, 50))!;
        Assert.Null(new EdgeDetector(0.08, runLength: 1).Bounds((25, 25), buffer));
    }

    [Fact]
    public void AStrayPixelIsNotAnEdgeOnceARunIsAsked()
    {
        var buffer = LuminanceBuffer.From(Fixture.CaptureImage(100, 20, paint: c => Fixture.Fill(c, new Rect(70, 0, 1, 20), Fixture.Black)))!;
        // A capture of scale 2 asks for two pixels, a point, before a change counts.
        Assert.Null(new EdgeDetector(0.08, scale: 2).FirstEdge((50, 10), EdgeDetector.Direction.Right, buffer));
        Assert.Equal(69, new EdgeDetector(0.08, scale: 1).FirstEdge((50, 10), EdgeDetector.Direction.Right, buffer));
    }
}
