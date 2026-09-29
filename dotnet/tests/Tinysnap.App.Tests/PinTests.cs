using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.App.Pinning;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVector = Avalonia.Vector;

namespace Tinysnap.App.Tests;

public class PinTests
{
    /// <summary>A shown pin of a blank 400 by 300 capture at 2x.</summary>
    private static PinWindow Pin(EditorServices? services = null, bool keepsSize = false)
    {
        var pin = new PinWindow(CanvasHost.Blank(400, 300).Image, 2, keepsSize, services ?? Make());
        pin.Show();
        Dispatcher.UIThread.RunJobs();
        pin.UpdateLayout();
        return pin;
    }

    /// <summary>Where the pin shows its image, in the window's own DIPs.</summary>
    private static AvaloniaPoint InImage(PinWindow pin, double x, double y) =>
        new(PinGeometry.Shadow + x, PinGeometry.Shadow + y);

    [AvaloniaFact]
    public void APinOpensAtItsPointSize()
    {
        var pin = Pin();
        Assert.Equal(new Size(200, 150), pin.ImageSize);
        Assert.Equal(200 + PinGeometry.Shadow * 2, pin.Width);
    }

    [Fact]
    public void APinLargerThanTheScreenFitsFourFifths()
    {
        var fitted = PinGeometry.Initial(new Size(3000, 1000), new Size(1920, 1040));
        Assert.Equal(1536, fitted.Width, 6);
        Assert.Equal(512, fitted.Height, 6);
        Assert.Equal(new Size(200, 150), PinGeometry.Initial(new Size(200, 150), new Size(1920, 1040)));
    }

    [AvaloniaFact]
    public void DigitsSetOpacity()
    {
        var pin = Pin();
        Press(pin, Key.D5, symbol: "5");
        Assert.Equal(0.5, pin.PinOpacity);
        Assert.False(pin.HasShadow);
        Press(pin, Key.NumPad3, symbol: "3");
        Assert.Equal(0.3, pin.PinOpacity, 6);
        Press(pin, Key.D0, symbol: "0");
        Assert.Equal(1, pin.PinOpacity);
        Assert.True(pin.HasShadow);
    }

    [Fact]
    public void TheWheelResizesByATenthAboutThePointer()
    {
        var frame = new Rect(100, 100, 200, 150);
        var pointer = new Point(250, 200);
        var grown = PinGeometry.Resized(frame, pointer, PinGeometry.WheelFactor(1), new Size(1920, 1080));
        Assert.Equal(220, grown.Width, 6);
        Assert.Equal(165, grown.Height, 6);
        // The point under the pointer stays under it: three quarters across, two thirds down.
        Assert.Equal(250 - 0.75 * 220, grown.X, 6);
        Assert.Equal(200 - 2.0 / 3 * 165, grown.Y, 6);

        // A wheel that reports a big delta still moves a tenth; a trackpad follows its delta.
        Assert.Equal(1.1, PinGeometry.WheelFactor(3));
        Assert.Equal(1 / 1.1, PinGeometry.WheelFactor(-2));
        Assert.Equal(Math.Pow(1.1, 0.25), PinGeometry.WheelFactor(0.25));

        // Never past the screen, never under 64 points on the short side.
        Assert.Equal(1080, PinGeometry.Resized(frame, pointer, 100, new Size(1920, 1080)).Height, 6);
        Assert.Equal(64, PinGeometry.Resized(frame, pointer, 0.01, new Size(1920, 1080)).Height, 6);
    }

    [AvaloniaFact]
    public void TheWheelGrowsTheShownPin()
    {
        var pin = Pin();
        pin.MouseWheel(InImage(pin, 150, 100), new AvaloniaVector(0, 1));
        Assert.Equal(220, pin.ImageSize.Width, 6);
        Assert.Equal(165, pin.ImageSize.Height, 6);
    }

    [AvaloniaFact]
    public void EscapeClosesAPin()
    {
        var pin = Pin();
        var closed = false;
        pin.Closed += (_, _) => closed = true;
        pin.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.True(closed);

        var other = Pin();
        closed = false;
        other.Closed += (_, _) => closed = true;
        other.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.None, "w");
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void DoubleClickOpensThePinInAnEditor()
    {
        var pin = Pin();
        (SKImage Image, double Scale)? opened = null;
        pin.OpenRequested += (image, scale) => opened = (image, scale);
        var point = InImage(pin, 50, 50);
        pin.MouseDown(point, MouseButton.Left);
        pin.MouseUp(point, MouseButton.Left);
        Assert.Null(opened);
        pin.MouseDown(point, MouseButton.Left);
        pin.MouseUp(point, MouseButton.Left);
        Assert.Equal(400, opened!.Value.Image.Width);
        Assert.Equal(2, opened.Value.Scale);
    }

    [AvaloniaFact]
    public void CtrlCCopiesAtTheExportSettingUnlessThePinKeepsItsSize()
    {
        var clipboard = new FakeClipboard();
        var services = new EditorServices(clipboard, () => Preferences.Defaults with { ExportScale = ExportScale.OneX }, new FakeDialogs());
        Press(Pin(services), Key.C, RawInputModifiers.Control, "c");
        Assert.Equal(200, clipboard.Image!.Width);

        Press(Pin(services, keepsSize: true), Key.C, RawInputModifiers.Control, "c");
        Assert.Equal(400, clipboard.Image!.Width);
    }

    [AvaloniaFact]
    public void CtrlSSavesIntoTheSaveFolder()
    {
        var folder = TemporaryFolder();
        var pin = Pin(Make(saveFolder: folder));
        Press(pin, Key.S, RawInputModifiers.Control, "s");
        var saved = Assert.Single(Directory.GetFiles(folder));
        Assert.Equal(400, Png.Decode(File.ReadAllBytes(saved))!.Value.Image.Width);
    }
}
