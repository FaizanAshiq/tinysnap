using SkiaSharp;
using Tinysnap.Core;

namespace Tinysnap.Linux.Tests;

public class DesktopCutTests
{
    /// <summary>A desktop of two monitors side by side, left red and right blue.</summary>
    private static SKImage Desktop(int width, int height, int split)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Red);
        using var blue = new SKPaint { Color = SKColors.Blue };
        surface.Canvas.DrawRect(split, 0, width - split, height, blue);
        return surface.Snapshot();
    }

    private static SKColor At(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    [Fact]
    public void EachMonitorGetsItsOwnPart()
    {
        using var desktop = Desktop(3000, 1000, 1000);
        var screens = DesktopCut.Cut(desktop, [(new Rect(0, 0, 1000, 1000), true), (new Rect(1000, 0, 2000, 1000), false)], 1);
        Assert.Equal([1000, 2000], screens.Select(s => s.Image.Width));
        Assert.Equal(SKColors.Red, At(screens[0].Image, 999, 500));
        Assert.Equal(SKColors.Blue, At(screens[1].Image, 0, 500));
        Assert.Equal([new Rect(0, 0, 1000, 1000), new Rect(1000, 0, 2000, 1000)], screens.Select(s => s.Place ?? s.Bounds));
    }

    [Fact]
    public void AnImageLargerThanTheLayoutIsCutInProportionAndScaled()
    {
        // GNOME's picture at twice the size of the layout XWayland reports.
        using var desktop = Desktop(4000, 1600, 2000);
        var screens = DesktopCut.Cut(desktop, [(new Rect(0, 0, 1000, 800), true), (new Rect(1000, 0, 1000, 800), false)], 1);
        Assert.Equal([2000, 2000], screens.Select(s => s.Image.Width));
        Assert.Equal([2.0, 2.0], screens.Select(s => s.Scale));
        Assert.Equal(new Rect(2000, 0, 2000, 1600), screens[1].Bounds);
        // Windows still go where XWayland has the monitor.
        Assert.Equal(new Rect(1000, 0, 1000, 800), screens[1].Place);
        Assert.Equal(SKColors.Blue, At(screens[1].Image, 0, 800));
    }

    [Fact]
    public void AFractionalScaleStaysInsideThePicture()
    {
        // 1.25 times a layout of three 1000 point monitors does not divide evenly into pixels.
        using var desktop = Desktop(3750, 1250, 1250);
        var screens = DesktopCut.Cut(desktop,
            [(new Rect(0, 0, 1000, 1000), true), (new Rect(1000, 0, 1000, 1000), false), (new Rect(2000, 0, 1000, 1000), false)], 1);
        Assert.Equal(3750, screens.Sum(s => s.Image.Width));
        Assert.All(screens, s => Assert.Equal(1250, s.Image.Height));
        Assert.True(screens[2].Bounds.MaxX <= 3750);
    }

    [Fact]
    public void ALayoutNotStartingAtTheCornerIsCutFromItsOwnCorner()
    {
        using var desktop = Desktop(2000, 1000, 1000);
        var screens = DesktopCut.Cut(desktop, [(new Rect(100, 50, 1000, 1000), true), (new Rect(1100, 50, 1000, 1000), false)], 1);
        Assert.Equal(SKColors.Red, At(screens[0].Image, 0, 0));
        Assert.Equal(SKColors.Blue, At(screens[1].Image, 0, 0));
    }

    [Fact]
    public void WithNoMonitorsTheWholePictureIsOneScreen()
    {
        using var desktop = Desktop(800, 600, 400);
        var screen = Assert.Single(DesktopCut.Cut(desktop, [], 1.5));
        Assert.Equal(new Rect(0, 0, 800, 600), screen.Bounds);
        Assert.Equal(1.5, screen.Scale);
    }
}
