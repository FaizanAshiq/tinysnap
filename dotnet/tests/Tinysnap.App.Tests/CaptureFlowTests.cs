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
        new(new FakePlatform(new FakeScreenCapture(() => desktop, () => pointer, cornerRadius)), TestServices.Store());

    private static readonly Tinysnap.Core.Preferences Thumbnails = Tinysnap.Core.Preferences.Defaults with
    {
        AfterCapture = Tinysnap.Core.AfterCapture.Thumbnail,
    };

    /// <summary>A controller on one Retina monitor, with its clipboard and dialogs to look at.</summary>
    private static (CaptureController Controller, FakeClipboard Clipboard) WithOutput(Tinysnap.Core.Preferences preferences,
                                                                                     FakeDialogs? dialogs = null)
    {
        var platform = new FakePlatform(new FakeScreenCapture(() => Screens.Desktop(Retina), () => new CorePoint(100, 100)));
        return (new CaptureController(platform, TestServices.Store(preferences), dialogs ?? new FakeDialogs(), new FakeTime()),
                (FakeClipboard)platform.Clipboard);
    }

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

    [AvaloniaFact]
    public void TheThumbnailSettingShowsAThumbnailInsteadOfAnEditor()
    {
        var (controller, _) = WithOutput(Thumbnails);
        controller.CaptureFullscreen();
        Assert.Empty(controller.Editors);
        Assert.NotNull(controller.Thumbnail);
    }

    [AvaloniaFact]
    public void ANewCaptureSendsTheLastThumbnailAway()
    {
        var (controller, clipboard) = WithOutput(Thumbnails);
        controller.CaptureFullscreen();
        var first = controller.Thumbnail!;
        controller.CaptureFullscreen();
        Assert.True(first.IsGone);
        Assert.NotSame(first, controller.Thumbnail);
        // Copied on the way out, as a time out would.
        Assert.Equal(1600, clipboard.Image!.Width);
    }

    [AvaloniaFact]
    public void ClickingTheThumbnailOpensTheEditor()
    {
        var (controller, _) = WithOutput(Thumbnails);
        controller.CaptureFullscreen();
        var thumbnail = controller.Thumbnail!;
        thumbnail.MouseDown(new Point(60, 60), MouseButton.Left);
        thumbnail.MouseUp(new Point(60, 60), MouseButton.Left);
        Assert.Equal(1600, Opened(controller).Image.Width);
        Assert.Null(controller.Thumbnail);
    }

    [AvaloniaFact]
    public void PinAndCloseTurnsTheEditorIntoAPin()
    {
        var (controller, _) = WithOutput(Tinysnap.Core.Preferences.Defaults);
        controller.CaptureFullscreen();
        var editor = Assert.Single(controller.Editors);
        editor.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
        Assert.Empty(controller.Editors);
        Assert.Equal(new Tinysnap.Core.Size(800, 600), Assert.Single(controller.Pins).ImageSize);
    }

    [AvaloniaFact]
    public void DoubleClickingAPinOpensAnEditor()
    {
        var (controller, _) = WithOutput(Tinysnap.Core.Preferences.Defaults);
        controller.CaptureFullscreen();
        Assert.Single(controller.Editors).KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
        var pin = Assert.Single(controller.Pins);
        for (var click = 0; click < 2; click++)
        {
            pin.MouseDown(new Point(40, 40), MouseButton.Left);
            pin.MouseUp(new Point(40, 40), MouseButton.Left);
        }
        Assert.Equal(1600, Opened(controller).Image.Width);
    }

    [AvaloniaFact]
    public async Task QuitAsksAboutEveryEditorWithEdits()
    {
        var dialogs = new FakeDialogs(Tinysnap.App.CloseChoice.Cancel);
        var (controller, _) = WithOutput(Tinysnap.Core.Preferences.Defaults, dialogs);
        controller.CaptureFullscreen();
        var edited = Assert.Single(controller.Editors);
        TestServices.Draw(edited);
        controller.CaptureFullscreen();
        Assert.False(await controller.CloseAll());
        Assert.Equal(1, dialogs.Asked);
        Assert.Equal(2, controller.Editors.Count);

        dialogs.Answer = Tinysnap.App.CloseChoice.Discard;
        Assert.True(await controller.CloseAll());
        Assert.Equal(2, dialogs.Asked);
        Assert.Empty(controller.Editors);
    }
}
