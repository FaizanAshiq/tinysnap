namespace Tinysnap.Core.Tests;

public class GeometryTests
{
    [Fact]
    public void SnapsToTheNearestAxisExactly()
    {
        var origin = new Point(10, 10);
        Assert.Equal(new Point(110, 10), new Point(110, 13).Snapped45(origin));
        Assert.Equal(new Point(10, -90), new Point(12, -90).Snapped45(origin));
    }

    [Fact]
    public void SnapsToADiagonalKeepingItsDirection()
    {
        Assert.Equal(new Point(-50, 50), new Point(-40, 60).Snapped45(Point.Zero));
    }

    [Fact]
    public void SquaresABoxFromItsAnchor()
    {
        Assert.Equal(new Point(30, -30), new Point(30, -10).Squared(Point.Zero));
    }

    [Fact]
    public void MeasuresDistanceToASegment()
    {
        var segmentEnd = new Point(10, 0);
        Assert.Equal(3, new Point(5, 3).DistanceToSegment(Point.Zero, segmentEnd));
        Assert.Equal(5, new Point(13, 4).DistanceToSegment(Point.Zero, segmentEnd));
    }

    [Fact]
    public void BuildsARectFromCornersInEitherOrder()
    {
        Assert.Equal(new Rect(10, 20, 40, 20), Rect.FromCorners(new Point(50, 40), new Point(10, 20)));
    }

    [Fact]
    public void DraggingACornerPastTheOppositeOneFlipsTheBox()
    {
        var box = new Rect(10, 10, 20, 20);
        Assert.Equal(new Rect(30, 30, 10, 20), box.Resized(Handle.TopLeft, new Point(40, 50), square: false));
    }

    [Fact]
    public void DraggingAnEdgeMovesOnlyThatEdge()
    {
        var box = new Rect(10, 10, 20, 20);
        Assert.Equal(new Rect(10, 10, 40, 20), box.Resized(Handle.Right, new Point(50, 99), square: false));
    }

    [Fact]
    public void ASquareCornerDragKeepsTheOppositeCorner()
    {
        var box = new Rect(0, 0, 10, 10);
        Assert.Equal(new Rect(0, 0, 30, 30), box.Resized(Handle.BottomRight, new Point(30, 12), square: true));
    }

    [Fact]
    public void ADragDrawsABoxFromItsCorner()
    {
        var box = Rect.Dragged(new Point(100, 100), new Point(130, 120), square: false, fromCentre: false);
        Assert.Equal(new Rect(100, 100, 30, 20), box);
    }

    [Fact]
    public void OptionDrawsTheBoxOutFromItsCentre()
    {
        var box = Rect.Dragged(new Point(100, 100), new Point(130, 120), square: false, fromCentre: true);
        Assert.Equal(new Rect(70, 80, 60, 40), box);
    }

    [Fact]
    public void ShiftAndOptionDrawASquareFromItsCentre()
    {
        var box = Rect.Dragged(new Point(100, 100), new Point(130, 110), square: true, fromCentre: true);
        Assert.Equal(new Rect(70, 70, 60, 60), box);
    }

    [Fact]
    public void AMovedBoxStopsAtTheEdgeOfItsBounds()
    {
        var box = new Rect(10, 10, 20, 20);
        var bounds = new Rect(0, 0, 100, 100);
        Assert.Equal(new Vector(-10, 5), box.AllowedMove(new Vector(-30, 5), bounds));
        Assert.Equal(new Vector(70, 70), box.AllowedMove(new Vector(90, 90), bounds));
    }

    [Fact]
    public void RoundsTheCropToWholePixels()
    {
        Assert.Equal(new Rect(1, 3, 11, 5), new Rect(1.4, 2.6, 10.2, 5).WholePixels);
    }

    [Fact]
    public void ARectKeepsCoreGraphicsSemantics()
    {
        // Negative sizes are standardised, as CGRect does on every read.
        var flipped = new Rect(50, 40, -40, -20);
        Assert.Equal(10, flipped.MinX);
        Assert.Equal(50, flipped.MaxX);
        Assert.Equal(new Rect(10, 20, 40, 20), flipped.Standardized);

        // Disjoint rects meet in the null rect, which is not the empty one.
        var apart = new Rect(0, 0, 10, 10).Intersection(new Rect(20, 20, 5, 5));
        Assert.True(apart.IsNull);
        Assert.True(new Rect(5, 5, 0, 0).IsEmpty && !new Rect(5, 5, 0, 0).IsNull);

        // Integral grows outward to whole numbers.
        Assert.Equal(new Rect(1, 2, 4, 3), new Rect(1.2, 2.7, 3.5, 2.1).Integral);

        // Union with the null rect is the other rect.
        Assert.Equal(new Rect(1, 1, 2, 2), Rect.Null.Union(new Rect(1, 1, 2, 2)));

        // Halves round away from zero, as Swift's rounded() does, never to even.
        Assert.Equal(3, Geometry.Round(2.5));
        Assert.Equal(-3, Geometry.Round(-2.5));
    }
}
