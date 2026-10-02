using SkiaSharp;
using Tinysnap.Platform;
using ZXing;
using ZXing.Common;

namespace Tinysnap.Linux.Tests;

public class QrCodesTests
{
    private static SKImage Code(string message)
    {
        var data = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 240, Height = 240, Margin = 2 },
        }.Write(message);
        return SKImage.FromPixelCopy(new SKImageInfo(data.Width, data.Height, SKColorType.Bgra8888, SKAlphaType.Premul), data.Pixels);
    }

    [Fact]
    public void EveryCodeIsReadOnce()
    {
        using var first = Code("https://example.com/one");
        using var second = Code("WIFI:S:Home;T:WPA;P:secret;;");
        using var surface = SKSurface.Create(new SKImageInfo(800, 300));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(first, 20, 30);
        surface.Canvas.DrawImage(second, 520, 30);
        using var both = surface.Snapshot();
        var reading = QrCodes.Read(both);
        Assert.Equal(["https://example.com/one", "WIFI:S:Home;T:WPA;P:secret;;"], reading.Codes.Order());
        Assert.Empty(reading.Lines);
    }

    [Fact]
    public void APictureWithNoCodeReadsAsNone()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 100));
        surface.Canvas.Clear(SKColors.SkyBlue);
        using var blank = surface.Snapshot();
        Assert.Empty(QrCodes.Read(blank).Codes);
    }
}
