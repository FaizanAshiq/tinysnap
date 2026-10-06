namespace Tinysnap.Core.Tests;

/// <summary>A shape drawn, moved or resized past the crop takes the crop out with it, the way one
/// drawn past the capture's edge grows the canvas, so nothing drawn is cut from what is copied or
/// saved.</summary>
public class CropFollowsShapesTests
{
    private static readonly Rect Crop = new(100, 100, 200, 150);

    private static EditorSession Cropped(Tool tool = Tool.Rectangle, CropRatio ratio = CropRatio.Free)
    {
        var session = new EditorSession(new Document(Fixture.Capture(400, 300)), Tool.Crop);
        session.Restyle(s => s with { CropRatio = ratio });
        Drag(session, new Point(100, 100), new Point(300, 250));
        session.Choose(tool);
        return session;
    }

    private static void Drag(EditorSession session, Point from, Point to)
    {
        session.PointerDown(from, reach: 4);
        session.PointerDragged(to);
        session.PointerUp();
    }

    [Fact]
    public void AShapeDrawnPastTheCropTakesItOut()
    {
        var session = Cropped();
        Assert.Equal(Crop, session.Display.Crop);
        Drag(session, new Point(150, 150), new Point(340, 200));
        // Out to the box's right side and the margin a shape past the capture's edge gets.
        Assert.Equal(new Rect(100, 100, 340 + 16 - 100, 150), session.Display.Crop);
    }

    /// <summary>As reported: an arrow from outside the crop, pointing in, had its tail cut off.</summary>
    [Fact]
    public void AnArrowFromOutsideTheCropPointingInTakesItOut()
    {
        var session = Cropped(Tool.Arrow);
        Drag(session, new Point(60, 60), new Point(200, 180));
        var grown = session.Display.Crop!.Value;
        Assert.True(grown.Contains(session.Display.Annotations[0].Bounds(1)), $"{grown}");
        Assert.Equal(Crop.MaxX, grown.MaxX);
        Assert.Equal(Crop.MaxY, grown.MaxY);
    }

    /// <summary>Measured from the last step: dragged out and back, the shape leaves the crop as it was.</summary>
    [Fact]
    public void AShapeDraggedOutAndBackLeavesTheCrop()
    {
        var session = Cropped();
        Drag(session, new Point(150, 150), new Point(200, 200));
        // Picked up by its left side: an outlined box has nothing to hold in its middle.
        session.Choose(Tool.Select);
        session.PointerDown(new Point(150, 175), reach: 4);
        session.PointerDragged(new Point(290, 175));
        Assert.True(session.Display.Crop!.Value.MaxX > Crop.MaxX);
        session.PointerDragged(new Point(155, 175));
        session.PointerUp();
        Assert.Equal(Crop, session.Display.Crop);
    }

    [Fact]
    public void UndoPutsTheCropBackWithTheShape()
    {
        var session = Cropped();
        Drag(session, new Point(150, 150), new Point(340, 200));
        session.Undo();
        Assert.Empty(session.Display.Annotations);
        Assert.Equal(Crop, session.Display.Crop);
    }

    /// <summary>Wholly outside, a shape is somewhere the crop leaves out, as one cropped away is.</summary>
    [Fact]
    public void AShapeWhollyOutsideTheCropLeavesIt()
    {
        var session = Cropped();
        Drag(session, new Point(10, 10), new Point(60, 60));
        Assert.Equal(Crop, session.Display.Crop);
    }

    /// <summary>They hide what is under them rather than add anything, and Redact's boxes reach a
    /// little past the text they cover.</summary>
    [Fact]
    public void BlurPixelateEraseAndRedactLeaveTheCrop()
    {
        foreach (var tool in new[] { Tool.Blur, Tool.Pixelate, Tool.Erase })
        {
            var each = Cropped(tool);
            Drag(each, new Point(250, 150), new Point(350, 200));
            Assert.Equal(Crop, each.Display.Crop);
        }
        var session = Cropped();
        session.Redact([new Rect(280, 150, 40, 10)]);
        Assert.Equal(Crop, session.Display.Crop);
    }

    [Fact]
    public void ACropWithARatioKeepsItAsItGrows()
    {
        var session = Cropped(ratio: CropRatio.Square);
        var square = session.Display.Crop!.Value;
        Assert.Equal(square.Width, square.Height);
        Drag(session, new Point(150, 150), new Point(330, 200));
        var grown = session.Display.Crop!.Value;
        Assert.Equal(grown.Width, grown.Height);
        Assert.True(grown.Width > square.Width);
        Assert.True(grown.Contains(session.Display.Annotations[0].Bounds(1)), $"{grown}");
    }
}
