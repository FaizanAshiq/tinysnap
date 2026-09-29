using SkiaSharp;

namespace Tinysnap.Core.Tests;

public class SkiaSmokeTests
{
    // Proves the native Skia library loads on this machine before anything is ported onto it.
    [Fact]
    public void SkiaDrawsAPixelWhereItIsTold()
    {
        var info = new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Red };
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.White, bitmap.GetPixel(3, 3));
    }
}
