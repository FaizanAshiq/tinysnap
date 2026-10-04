namespace Tinysnap.Core.Tests;

public class PixelTests
{
    [Fact]
    public void ReadsTheColourOfOnePixelAsHex()
    {
        var image = Fixture.CaptureImage(40, 40, paint: c => Fixture.Fill(c, new Rect(0, 0, 40, 20), Fixture.Blue));
        Assert.Equal("#0000FF", ColorProbe.Hex(image, 5, 5));
        Assert.Equal("#FFFFFF", ColorProbe.Hex(image, 5, 35));
        Assert.Null(ColorProbe.Hex(image, 50, 5));
    }

    [Fact]
    public void PixelateAveragesEachBlockCountedFromTheTopLeft()
    {
        var image = Fixture.CaptureImage(4, 2, paint: c => Fixture.Fill(c, new Rect(0, 0, 1, 2), Fixture.Black));
        var buffer = PixelBuffer.From(image)!;
        buffer.Pixelate(block: 2);
        // Half black, half white in the first 2 by 2 block.
        Assert.Equal((byte)127, buffer.Pixel(0, 0).R);
        Assert.Equal((byte)127, buffer.Pixel(1, 1).R);
        Assert.Equal((byte)255, buffer.Pixel(3, 0).R);
    }

    [Fact]
    public void TheBufferIsRgbaPremultipliedWithRowZeroAtTheTop()
    {
        var image = Fixture.CaptureImage(2, 2, paint: c => Fixture.Fill(c, new Rect(0, 0, 2, 1), Fixture.Blue));
        var buffer = PixelBuffer.From(image)!;
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), buffer.Pixel(1, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), buffer.Pixel(1, 1));
    }

    /// <summary>The erase fill smooths each edge with the median of the pixels within 16 of each one.
    /// It slides one sorted window along the edge rather than sorting a fresh copy at every pixel.</summary>
    [Fact]
    public void EveryMedianMatchesSortingItsWindow()
    {
        var random = new Random(7);
        foreach (var count in new[] { 1, 2, 5, 16, 33, 34, 40, 200 })
        {
            var values = new byte[count];
            random.NextBytes(values);
            var expected = Enumerable.Range(0, count).Select(i =>
            {
                var window = values[Math.Max(0, i - 16)..(Math.Min(count - 1, i + 16) + 1)];
                Array.Sort(window);
                return window[window.Length / 2];
            }).ToArray();
            Assert.Equal(expected, PixelBuffer.SlidingMedians(values, 16));
        }
    }
}
