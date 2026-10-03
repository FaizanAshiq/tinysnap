using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Tinysnap.Core;
using Point = Avalonia.Point;
using Vector = Avalonia.Vector;
using CorePoint = Tinysnap.Core.Point;
using CoreRect = Tinysnap.Core.Rect;

namespace Tinysnap.App.Tests;

public class CanvasTests
{
    [AvaloniaFact]
    public void ADragDrawsAnArrowWhereThePointerWent()
    {
        // 400 by 300 pixels at 2x is 200 by 150 points on screen.
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300));
        window.MouseDown(new Point(10, 10), MouseButton.Left);
        window.MouseMove(new Point(60, 40));
        window.MouseUp(new Point(60, 40), MouseButton.Left);
        Assert.Equal(new AnnotationKind.Arrow(new CorePoint(20, 20), new CorePoint(120, 80)),
                     Assert.Single(canvas.Session.Display.Annotations).Kind);
    }

    [AvaloniaFact]
    public void ADragAtTwoHundredPercentDrawsWhereThePointerWent()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300), zoom: 2);
        window.MouseDown(new Point(20, 20), MouseButton.Left);
        window.MouseMove(new Point(120, 80));
        window.MouseUp(new Point(120, 80), MouseButton.Left);
        Assert.Equal(new AnnotationKind.Arrow(new CorePoint(20, 20), new CorePoint(120, 80)),
                     Assert.Single(canvas.Session.Display.Annotations).Kind);
    }

    [AvaloniaFact]
    public void ShiftWhileDraggingSnapsTheArrow()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300));
        window.MouseDown(new Point(10, 10), MouseButton.Left);
        window.MouseMove(new Point(60, 12), RawInputModifiers.Shift | RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(60, 12), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal(new AnnotationKind.Arrow(new CorePoint(20, 20), new CorePoint(120, 20)),
                     Assert.Single(canvas.Session.Display.Annotations).Kind);
    }

    [AvaloniaFact]
    public void SpaceWhileDrawingMovesTheBox()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300), tool: Tool.Erase);
        window.MouseDown(new Point(5, 5), MouseButton.Left);
        window.MouseMove(new Point(25, 20), RawInputModifiers.LeftMouseButton);
        window.KeyPress(Key.Space, RawInputModifiers.LeftMouseButton, PhysicalKey.Space, " ");
        window.MouseMove(new Point(35, 30), RawInputModifiers.LeftMouseButton);
        window.KeyRelease(Key.Space, RawInputModifiers.LeftMouseButton, PhysicalKey.Space, " ");
        window.MouseMove(new Point(50, 45), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(50, 45), MouseButton.Left);
        Assert.Equal(new AnnotationKind.Erase(new CoreRect(30, 30, 70, 60)),
                     Assert.Single(canvas.Session.Display.Annotations).Kind);
    }

    [AvaloniaFact]
    public void HoveringABlurBordersIt()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300), tool: Tool.Blur);
        window.MouseDown(new Point(50, 50), MouseButton.Left);
        window.MouseMove(new Point(100, 80), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(100, 80), MouseButton.Left);
        canvas.Session.Choose(Tool.Arrow);
        window.MouseMove(new Point(75, 65));
        Assert.Equal(canvas.Session.Display.Annotations[0].Id, canvas.Hovered);
        window.MouseMove(new Point(150, 120));
        Assert.Null(canvas.Hovered);
    }

    [AvaloniaFact]
    public void TheWheelOverAMagnifierZoomsIt()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300), tool: Tool.Magnifier);
        window.MouseDown(new Point(100, 75), MouseButton.Left);
        window.MouseUp(new Point(100, 75), MouseButton.Left);
        window.MouseWheel(new Point(100, 75), new Vector(0, 1));
        var lens = Assert.IsType<AnnotationKind.Magnifier>(Assert.Single(canvas.Session.Display.Annotations).Kind);
        Assert.Equal(2.5, lens.Zoom);
    }

    [AvaloniaFact]
    public void AMeasureClickKeepsTheLiveReading()
    {
        // Between the two cards, 16 points across.
        var (window, canvas) = CanvasHost.Open(CanvasHost.TwoCards(), tool: Tool.Measure);
        window.MouseMove(new Point(78, 50));
        window.MouseDown(new Point(78, 50), MouseButton.Left);
        window.MouseUp(new Point(78, 50), MouseButton.Left);
        var kept = Assert.IsType<AnnotationKind.Measure>(Assert.Single(canvas.Session.Display.Annotations).Kind);
        Assert.Equal(new AnnotationKind.Measure(new CorePoint(140, 100), new CorePoint(172, 100)), kept);
    }

    [AvaloniaFact]
    public void TheRenderedCanvasShowsTheArrowInItsColour()
    {
        var (window, _) = CanvasHost.Open(CanvasHost.Blank(400, 300));
        window.MouseDown(new Point(10, 75), MouseButton.Left);
        window.MouseMove(new Point(190, 75), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(190, 75), MouseButton.Left);
        using var frame = Frames.Capture(window);
        var middle = frame.GetPixel(100, 75);
        Assert.True(middle.Red > 230 && middle.Green < 90 && middle.Blue < 80, middle.ToString());
    }

    [AvaloniaFact]
    public void ZoomedInAnArrowStaysSmoothWhileTheCapturesPixelsStaySquare()
    {
        // At 400% a 2x capture shows each of its pixels as two by two screen pixels. The arrow's
        // edge is drawn at the screen's resolution, so it cuts across those squares. Both stay
        // well inside the capture, so the canvas does not grow and the squares start at 0, 0.
        var capture = CanvasHost.Capture(300, 200, 2, canvas =>
        {
            using var black = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black };
            for (var x = 0; x < 300; x += 2) canvas.DrawRect(new SkiaSharp.SKRect(x, 170, x + 1, 200), black);
        });
        var (window, canvas) = CanvasHost.Open(capture, zoom: 4);
        window.MouseDown(new Point(120, 120), MouseButton.Left);
        window.MouseMove(new Point(400, 300), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(400, 300), MouseButton.Left);
        // Its selection border would cut the squares too.
        canvas.Apply(session => session.Select(null));
        Assert.Equal(new CoreRect(0, 0, 300, 200), canvas.Shown);
        using var frame = Frames.Capture(window);
        var cut = 0;
        for (var y = 100; y < 320; y += 2)
            for (var x = 100; x < 420; x += 2)
            {
                var block = new[] { frame.GetPixel(x, y), frame.GetPixel(x + 1, y), frame.GetPixel(x, y + 1), frame.GetPixel(x + 1, y + 1) };
                if (block.Distinct().Count() > 1) cut++;
            }
        Assert.True(cut > 20, $"{cut} squares cut by the arrow's edge");
        // The stripes, one capture pixel wide, stay sharp: black then white, nothing between.
        Assert.Equal(SkiaSharp.SKColors.Black, frame.GetPixel(1, 360));
        Assert.Equal(SkiaSharp.SKColors.White, frame.GetPixel(2, 360));
    }
}
