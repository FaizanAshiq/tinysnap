using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;
using Tinysnap.App.Capturing;
using Tinysnap.Dev;
using Tinysnap.Platform;
using CorePoint = Tinysnap.Core.Point;
using CoreRect = Tinysnap.Core.Rect;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

public class CaptureFlowTests
{
    private static CaptureController Controller(FrozenDesktop desktop, CorePoint pointer, double cornerRadius = 0) =>
        new(new FakePlatform(new FakeScreenCapture(() => desktop, () => pointer, cornerRadius)), () => Tinysnap.Core.Preferences.Defaults);

    private static void Drag(OverlayWindow window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
    }

    private static Tinysnap.Core.Capture Opened(CaptureController controller) =>
        Assert.Single(controller.Editors).Canvas.Session.Display.Capture;

    private static SKColor Pixel(Tinysnap.Core.Capture capture, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(capture.Image);
        return bitmap.GetPixel(x, y);
    }

    private static readonly FrozenScreen Retina = Screens.Frozen(new CoreRect(0, 0, 1600, 1200), 2, SKColors.Blue);

    [AvaloniaFact]
    public void FullscreenOpensTheMonitorUnderThePointer()
    {
        var plain = Screens.Frozen(new CoreRect(1600, 0, 1920, 1080), 1, SKColors.Lime);
        var controller = Controller(Screens.Desktop(Retina, plain), new CorePoint(2000, 500));
        controller.CaptureFullscreen();
        var capture = Opened(controller);
        Assert.Equal(new Tinysnap.Core.Size(1920, 1080), capture.PixelSize);
        Assert.Equal(1, capture.Scale);
        Assert.Equal(SKColors.Lime, Pixel(capture, 10, 10));
    }

    [AvaloniaFact]
    public void AnAreaOpensAtItsPixelSize()
    {
        var controller = Controller(Screens.Desktop(Retina), new CorePoint(100, 100));
        controller.CaptureArea();
        Drag(controller.Overlay!.Windows[0], new Point(100, 100), new Point(300, 250));
        var capture = Opened(controller);
        Assert.Equal(new Tinysnap.Core.Size(400, 300), capture.PixelSize);
        Assert.Equal(2, capture.Scale);
        Assert.Null(controller.Overlay);
    }

    [AvaloniaFact]
    public void ABoxOnAOneAndAHalfMonitorBesideAPlainOneCutsItsOwnPixels()
    {
        var plain = Screens.Frozen(new CoreRect(0, 0, 1920, 1080), 1, SKColors.Red);
        var scaled = Screens.Frozen(new CoreRect(1920, 0, 2880, 1620), 1.5, SKColors.Blue);
        var controller = Controller(Screens.Desktop(plain, scaled), new CorePoint(2500, 500));
        controller.CaptureArea();
        Drag(controller.Overlay!.Windows[1], new Point(100, 100), new Point(300, 200));
        var capture = Opened(controller);
        Assert.Equal(new Tinysnap.Core.Size(300, 150), capture.PixelSize);
        Assert.Equal(1.5, capture.Scale);
        Assert.Equal(SKColors.Blue, Pixel(capture, 150, 75));
    }

    [AvaloniaFact]
    public void AMonitorLeftOfThePrimaryCapturesFromItsOwnImage()
    {
        var left = Screens.Frozen(new CoreRect(-1920, 0, 1920, 1080), 1, SKColors.Lime);
        var controller = Controller(Screens.Desktop(Retina, left), new CorePoint(-500, 300));
        controller.CaptureArea();
        Assert.Equal(new PixelPoint(-1920, 0), controller.Overlay!.Windows[1].Position);
        Drag(controller.Overlay.Windows[1], new Point(10, 10), new Point(110, 60));
        Assert.Equal(SKColors.Lime, Pixel(Opened(controller), 50, 25));

        controller = Controller(Screens.Desktop(Retina, left), new CorePoint(-500, 300));
        controller.CaptureFullscreen();
        Assert.Equal(SKColors.Lime, Pixel(Opened(controller), 10, 10));
    }

    [AvaloniaFact]
    public void AWindowCaptureCutsTheWindowAndClearsItsCorners()
    {
        var window = new PickableWindow(new CoreRect(200, 200, 400, 300), "Notes");
        var controller = Controller(new FrozenDesktop([Retina], [window]), new CorePoint(300, 300), cornerRadius: 8);
        controller.CaptureArea();
        var overlay = controller.Overlay!.Windows[0];
        overlay.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        overlay.MouseDown(new Point(150, 150), MouseButton.Left);
        var capture = Opened(controller);
        Assert.Equal(new Tinysnap.Core.Size(400, 300), capture.PixelSize);
        Assert.Equal(2, capture.Scale);
        Assert.Equal(0, Pixel(capture, 0, 0).Alpha);
        Assert.Equal(SKColors.Blue, Pixel(capture, 200, 150));
    }

    [AvaloniaFact]
    public void CancellingOpensNothing()
    {
        var controller = Controller(Screens.Desktop(Retina), new CorePoint(100, 100));
        controller.CaptureArea();
        controller.Overlay!.Windows[0].KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Empty(controller.Editors);
        Assert.Null(controller.Overlay);
    }
}
