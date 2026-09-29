namespace Tinysnap.Core.Tests;

// The Mac's other six TextReaderTests need an OCR engine and move to milestone 5 with
// Windows OCR: readsLinesTopToBottom, sideBySideTextReadsAcrossEachRowBeforeGoingDown,
// copyingTextReadsOnlyTheTextAndScanningReadsOnlyTheCode, aBlankImageReadsAsNothing,
// textUnderAnEraseIsNeverRead and onlyTheTextInADraggedAreaIsRead.
public class TextReaderTests
{
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
}
