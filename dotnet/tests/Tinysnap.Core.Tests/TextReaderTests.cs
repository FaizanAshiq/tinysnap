using SkiaSharp;

namespace Tinysnap.Core.Tests;

// The Mac's other six TextReaderTests need an OCR engine, so they run against Windows OCR in
// Tinysnap.Windows.Tests: readsLinesTopToBottom, sideBySideTextReadsAcrossEachRowBeforeGoingDown,
// copyingTextReadsOnlyTheTextAndScanningReadsOnlyTheCode, aBlankImageReadsAsNothing,
// textUnderAnEraseIsNeverRead and onlyTheTextInADraggedAreaIsRead.
public class TextReaderTests
{
    [Fact]
    public void TheWarmUpSampleIsDarkWordsOnWhite()
    {
        // A blank image would never wake the recogniser: there has to be something to read.
        using var sample = TextReader.WarmUpSample();
        Assert.Equal((480, 80), (sample.Width, sample.Height));
        using var bitmap = SKBitmap.FromImage(sample);
        Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
        Assert.Contains(bitmap.Pixels, pixel => pixel.Red < 64);
    }

    [Fact]
    public void JoiningLinesLeavesSingleSpaces()
    {
        Assert.Equal("one two three four", TextReader.Join("one\ntwo  three\n four \n"));
    }

    [Fact]
    public void ALinkIsOfferedOnlyForExactlyOneWebAddress()
    {
        Assert.Equal(new Uri("https://example.com/a"), new TextReading([], ["https://example.com/a"]).Link);
        Assert.Null(new TextReading([], ["see https://example.com"]).Link);
        Assert.Null(new TextReading([], ["https://a.example", "https://b.example"]).Link);
        Assert.Null(new TextReading([], ["file:///etc/hosts"]).Link);
    }

    [Fact]
    public void SideBySideBoxesReadAcrossEachRowBeforeGoingDown()
    {
        // Vision's boxes are y up and normalised, as Windows' OCR boxes will be mapped to.
        var boxes = new[]
        {
            new Rect(0.55, 0.20, 0.30, 0.05), // right low
            new Rect(0.05, 0.70, 0.30, 0.05), // left top
            new Rect(0.05, 0.20, 0.30, 0.05), // left low
            new Rect(0.55, 0.71, 0.30, 0.05), // right top
        };
        Assert.Equal(new[] { 1, 3, 2, 0 }, TextReader.ReadingOrder(boxes));
    }

    [Fact]
    public void LinesComeBackInReadingOrder()
    {
        // Pixel boxes with y growing downward, as Windows' OCR reports them: a dashboard of two
        // columns reads row by row.
        var lines = new (string Text, Rect Box)[]
        {
            ("right low", new Rect(550, 700, 300, 50)),
            ("left top", new Rect(50, 200, 300, 50)),
            ("left low", new Rect(50, 700, 300, 50)),
            ("right top", new Rect(550, 190, 300, 50)),
        };
        Assert.Equal(new[] { "left top", "right top", "left low", "right low" }, TextReader.InReadingOrder(lines).ToArray());
    }
}
