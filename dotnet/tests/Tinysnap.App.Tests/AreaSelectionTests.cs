using Tinysnap.App.Capturing;
using Tinysnap.Core;

namespace Tinysnap.App.Tests;

public class AreaSelectionTests
{
    private static AreaSelection Screen() => new(new Size(800, 600));

    [Fact]
    public void ADragMakesABoxClippedToTheScreen()
    {
        var area = Screen();
        area.Down(new Point(100, 100), square: false, fromCentre: false);
        area.Drag(new Point(900, 700), square: false, fromCentre: false, spaceHeld: false);
        Assert.Equal(new Rect(100, 100, 700, 500), area.Selection);
        Assert.Equal(new Rect(100, 100, 700, 500), area.Up());
        Assert.Null(area.Selection);
    }

    [Fact]
    public void ShiftSquaresAndAltDrawsFromTheCentre()
    {
        var area = Screen();
        area.Down(new Point(100, 100), square: false, fromCentre: false);
        area.Drag(new Point(130, 110), square: true, fromCentre: true, spaceHeld: false);
        Assert.Equal(new Rect(70, 70, 60, 60), area.Selection);
    }

    [Fact]
    public void SpaceMovesTheWholeBoxAndStopsAtTheEdge()
    {
        var area = Screen();
        area.Down(new Point(100, 100), square: false, fromCentre: false);
        area.Drag(new Point(200, 150), square: false, fromCentre: false, spaceHeld: false);
        area.Drag(new Point(900, 150), square: false, fromCentre: false, spaceHeld: true);
        Assert.Equal(new Rect(700, 100, 100, 50), area.Selection);
    }

    [Fact]
    public void ArrowKeysNudgeTheCornerBeingDragged()
    {
        var area = Screen();
        area.Down(new Point(100, 100), square: false, fromCentre: false);
        area.Drag(new Point(200, 150), square: false, fromCentre: false, spaceHeld: false);
        area.Nudge(1, 0);
        area.Nudge(0, -1);
        Assert.Equal(new Rect(100, 100, 101, 49), area.Selection);
    }

    [Fact]
    public void ABoxUnderTwoPointsIsNoCapture()
    {
        var area = Screen();
        area.Down(new Point(100, 100), square: false, fromCentre: false);
        area.Drag(new Point(101, 150), square: false, fromCentre: false, spaceHeld: false);
        Assert.Null(area.Up());
    }

    [Fact]
    public void TheReadoutCountsPixelsAtTheScreensScale()
    {
        var area = Screen();
        area.Down(new Point(100, 100), square: false, fromCentre: false);
        area.Drag(new Point(500, 400), square: false, fromCentre: false, spaceHeld: false);
        Assert.Equal("600 × 450", area.Readout(1.5));
    }

    [Fact]
    public void TheReadoutFlipsInsideTheScreenEdge()
    {
        var area = Screen();
        var text = new Size(60, 16);
        Assert.Equal(new Rect(108, 108, 72, 22), area.ReadoutBox(new Point(100, 100), text));
        Assert.Equal(new Rect(710, 560, 72, 22), area.ReadoutBox(new Point(790, 590), text));
    }
}
