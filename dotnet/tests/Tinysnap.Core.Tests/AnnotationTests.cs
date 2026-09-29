namespace Tinysnap.Core.Tests;

public class AnnotationTests
{
    [Fact]
    public void AThinLineCanStillBeGrabbed()
    {
        // Small is 2 points, 4 pixels at 2x, but the reach is at least 4 points.
        var line = Fixture.Annotation(new AnnotationKind.Line(Point.Zero, new Point(100, 0)),
                                      new Style(Palette.Red, StyleSize.Small));
        Assert.True(line.Contains(new Point(50, 7), 2));
        Assert.False(line.Contains(new Point(50, 9), 2));
    }

    [Fact]
    public void AnOutlinedBoxIsHitOnItsEdgeNotItsMiddle()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(0, 0, 100, 100)));
        Assert.True(box.Contains(new Point(1, 50), 2));
        Assert.False(box.Contains(new Point(50, 50), 2));
    }

    [Fact]
    public void AFilledBoxIsHitAnywhereInside()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(0, 0, 100, 100)),
                                     new Style(Palette.Red, filled: true));
        Assert.True(box.Contains(new Point(50, 50), 2));
    }

    [Fact]
    public void AnOvalOutlineIsHitOnTheCurveNotTheCorner()
    {
        var oval = Fixture.Annotation(new AnnotationKind.Oval(new Rect(0, 0, 100, 100)));
        Assert.True(oval.Contains(new Point(50, 1), 2));
        Assert.False(oval.Contains(new Point(3, 3), 2));
    }

    [Fact]
    public void MovingShiftsEveryPoint()
    {
        var arrow = Fixture.Annotation(new AnnotationKind.Arrow(Point.Zero, new Point(10, 10)));
        Assert.Equal(new AnnotationKind.Arrow(new Point(5, -5), new Point(15, 5)), arrow.Moved(new Vector(5, -5)).Kind);
    }

    [Fact]
    public void DraggingALineEndWithShiftSnaps()
    {
        var line = Fixture.Annotation(new AnnotationKind.Line(Point.Zero, new Point(10, 0)));
        var moved = line.Resized(Handle.End, new Point(100, 4), constrained: true);
        Assert.Equal(new AnnotationKind.Line(Point.Zero, new Point(100, 0)), moved.Kind);
    }

    [Fact]
    public void DraggingAMagnifierCornerChangesItsRadius()
    {
        var lens = Fixture.Annotation(new AnnotationKind.Magnifier(new Point(100, 100), 80, 2));
        var resized = lens.Resized(Handle.TopLeft, new Point(40, 70), constrained: false);
        Assert.Equal(new AnnotationKind.Magnifier(new Point(100, 100), 60, 2), resized.Kind);
    }

    [Fact]
    public void TextBoundsGrowWithEachLine()
    {
        var one = Fixture.Annotation(new AnnotationKind.Text(Point.Zero, "Hi")).Bounds(2);
        var two = Fixture.Annotation(new AnnotationKind.Text(Point.Zero, "Hi\nthere")).Bounds(2);
        Assert.True(two.Height > one.Height * 1.9);
        Assert.True(two.Width > one.Width);
    }

    [Fact]
    public void AClickWithoutADragIsTooSmallToKeep()
    {
        Assert.True(Fixture.Annotation(new AnnotationKind.Arrow(Point.Zero, new Point(1, 1))).IsDegenerate(2));
        Assert.True(Fixture.Annotation(new AnnotationKind.Text(Point.Zero, " \n ")).IsDegenerate(2));
        Assert.False(Fixture.Annotation(new AnnotationKind.Step(Point.Zero)).IsDegenerate(2));
    }

    [Fact]
    public void TextStepsAndStrokesHaveNoResizeHandles()
    {
        Assert.Empty(Fixture.Annotation(new AnnotationKind.Step(Point.Zero)).Handles(2));
        Assert.Equal(2, Fixture.Annotation(new AnnotationKind.Line(Point.Zero, new Point(9, 9))).Handles(2).Count);
        Assert.Equal(8, Fixture.Annotation(new AnnotationKind.Blur(new Rect(0, 0, 9, 9))).Handles(2).Count);
    }
}
