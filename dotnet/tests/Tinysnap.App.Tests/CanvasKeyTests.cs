using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Point = Avalonia.Point;
using CorePoint = Tinysnap.Core.Point;

namespace Tinysnap.App.Tests;

public class CanvasKeyTests
{
    private static void Press(Window window, Key key, string? symbol = null, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, symbol);
        window.KeyRelease(key, modifiers, PhysicalKey.None, symbol);
    }

    /// <summary>An arrow from pixel (20, 20) to (120, 80), selected, with the canvas focused.</summary>
    private static (Window Window, CanvasControl Canvas) WithArrow()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300));
        window.MouseDown(new Point(10, 10), MouseButton.Left);
        window.MouseMove(new Point(60, 40), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(60, 40), MouseButton.Left);
        return (window, canvas);
    }

    [AvaloniaFact]
    public void ToolLettersPickTools()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300));
        canvas.Focus();
        Press(window, Key.R, "r");
        Assert.Equal(Tool.Rectangle, canvas.Session.Tool);
        Press(window, Key.E, "E", RawInputModifiers.Shift);
        Assert.Equal(Tool.Erase, canvas.Session.Tool);
    }

    [AvaloniaFact]
    public void BracketsStepTheSizeAsOneUndo()
    {
        var (window, canvas) = WithArrow();
        Press(window, Key.OemCloseBrackets, "]");
        Press(window, Key.OemCloseBrackets, "]");
        Assert.Equal(StyleSize.ExtraLarge, canvas.Session.Display.Annotations[0].Style.Size);
        Press(window, Key.Z, "z", RawInputModifiers.Control);
        Assert.Equal(StyleSize.Medium, canvas.Session.Display.Annotations[0].Style.Size);
    }

    [AvaloniaFact]
    public void DeleteRemovesTheSelection()
    {
        var (window, canvas) = WithArrow();
        Press(window, Key.Delete);
        Assert.Empty(canvas.Session.Display.Annotations);
    }

    [AvaloniaFact]
    public void ArrowsNudgeByOneOrTenWithShift()
    {
        var (window, canvas) = WithArrow();
        Press(window, Key.Right);
        Press(window, Key.Down, modifiers: RawInputModifiers.Shift);
        Assert.Equal(new AnnotationKind.Arrow(new CorePoint(21, 30), new CorePoint(121, 90)),
                     canvas.Session.Display.Annotations[0].Kind);
    }

    [AvaloniaFact]
    public void EscapeFinishesTypingThenDeselectsThenCloses()
    {
        var (window, canvas) = WithArrow();
        var closed = 0;
        canvas.CloseRequested += () => closed++;
        Press(window, Key.Escape);
        Assert.Null(canvas.Session.Selection);
        Assert.Equal(0, closed);
        Press(window, Key.Escape);
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void CtrlZAndCtrlYUndoAndRedo()
    {
        var (window, canvas) = WithArrow();
        Press(window, Key.Z, "z", RawInputModifiers.Control);
        Assert.Empty(canvas.Session.Display.Annotations);
        Press(window, Key.Y, "y", RawInputModifiers.Control);
        Assert.Single(canvas.Session.Display.Annotations);
        Press(window, Key.Z, "z", RawInputModifiers.Control);
        Press(window, Key.Z, "Z", RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Single(canvas.Session.Display.Annotations);
    }

    [AvaloniaFact]
    public void TypedTextBecomesATextAnnotation()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300), tool: Tool.Text);
        window.MouseDown(new Point(20, 20), MouseButton.Left);
        window.MouseUp(new Point(20, 20), MouseButton.Left);
        window.KeyTextInput("Hi");
        Press(window, Key.Escape);
        Assert.Equal(new AnnotationKind.Text(new CorePoint(40, 40), "Hi"),
                     Assert.Single(canvas.Session.Display.Annotations).Kind);
    }

    [AvaloniaFact]
    public void TypingLettersIntoTextNeverPicksATool()
    {
        var (window, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300), tool: Tool.Text);
        window.MouseDown(new Point(20, 20), MouseButton.Left);
        window.MouseUp(new Point(20, 20), MouseButton.Left);
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        window.KeyTextInput("r");
        window.KeyRelease(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        window.KeyTextInput("a");
        window.KeyRelease(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        Assert.Equal(Tool.Text, canvas.Session.Tool);
        Press(window, Key.Escape);
        Assert.Equal("ra", Assert.IsType<AnnotationKind.Text>(Assert.Single(canvas.Session.Display.Annotations).Kind).String);
    }

    [AvaloniaFact]
    public void APastedImageIsInsertedAndTheToolStays()
    {
        // Pixels land one to one on a capture of the same scale, so an older capture pasted in
        // lines up with this one.
        var (_, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300));
        using var surface = SKSurface.Create(new SKImageInfo(40, 20));
        surface.Canvas.Clear(SKColors.Blue);
        canvas.InsertImage(surface.Snapshot(), scale: 2);
        var pasted = Assert.Single(canvas.Session.Display.Annotations);
        var kind = Assert.IsType<AnnotationKind.Image>(pasted.Kind);
        Assert.Equal(new Tinysnap.Core.Size(40, 20), kind.Rect.Size);
        Assert.Equal(Tool.Arrow, canvas.Session.Tool);
        Assert.Equal(pasted.Id, canvas.Session.Selection);
    }

    [AvaloniaFact]
    public void AnImageFileInsertsAtItsOwnScale()
    {
        // A 2x PNG, 40 by 20 pixels, is 20 by 10 points, so 40 by 20 pixels on a 2x capture.
        var (_, canvas) = CanvasHost.Open(CanvasHost.Blank(400, 300));
        using var surface = SKSurface.Create(new SKImageInfo(40, 20));
        surface.Canvas.Clear(SKColors.Blue);
        var path = Path.Combine(Path.GetTempPath(), $"tinysnap-drop-{Guid.NewGuid()}.png");
        File.WriteAllBytes(path, Png.Encode(surface.Snapshot(), dpi: 144)!);
        Assert.True(canvas.InsertFile(path));
        var kind = Assert.IsType<AnnotationKind.Image>(Assert.Single(canvas.Session.Display.Annotations).Kind);
        Assert.Equal(new Tinysnap.Core.Size(40, 20), kind.Rect.Size);
        Assert.False(canvas.InsertFile(Path.Combine(Path.GetTempPath(), "no-such-picture.png")));
    }
}
