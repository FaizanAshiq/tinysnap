namespace Tinysnap.Core.Tests;

/// <summary>A shape dragged near another's edge or middle, or the output's, snaps to it and a
/// guide shows the line they share, so shapes line up without nudging pixel by pixel.</summary>
public class AlignmentGuideTests
{
    /// <summary>A 400 by 300 capture with a box whose left edge is at x 100, and a second box to drag.
    /// Filled, so a click anywhere on one picks it up.</summary>
    private static (EditorSession Session, Guid Id) Session()
    {
        var solid = new Style(Palette.Red, filled: true);
        var anchor = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(100, 40, 60, 40)), solid);
        var moving = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(200, 200, 36, 30)), solid);
        return (new EditorSession(new Document(Fixture.Capture(400, 300), annotations: [anchor, moving]), Tool.Select), moving.Id);
    }

    private static Rect Box(EditorSession session, Guid id) => session.Display.Annotation(id)!.Bounds(session.Scale);

    [Fact]
    public void ALeftEdgeNearAnotherSnapsToItAndShowsAGuide()
    {
        var (session, id) = Session();
        // Grabbed at its middle and carried left until its left edge is at 103, 3 short of 100.
        session.PointerDown(new Point(218, 215), reach: 4);
        session.PointerDragged(new Point(121, 215));
        Assert.Equal(100, Box(session, id).MinX);
        Assert.Contains(session.Guides, g => g.Axis == GuideAxis.Vertical && g.Position == 100);
        session.PointerUp();
        Assert.Empty(session.Guides);
    }

    [Fact]
    public void FarFromAnythingNothingSnaps()
    {
        var (session, id) = Session();
        session.PointerDown(new Point(218, 215), reach: 4);
        session.PointerDragged(new Point(238, 237));
        Assert.Equal(new Point(220, 222), Box(session, id).Origin);
        Assert.Empty(session.Guides);
    }

    [Fact]
    public void AMiddleSnapsToTheOutputsMiddle()
    {
        var (session, id) = Session();
        // Its middle carried to x 198, 2 short of the capture's middle at 200.
        session.PointerDown(new Point(218, 215), reach: 4);
        session.PointerDragged(new Point(198, 255));
        Assert.Equal(200, Box(session, id).MidX);
    }

    [Fact]
    public void HoldingCtrlDragsFreely()
    {
        var (session, id) = Session();
        session.PointerDown(new Point(218, 215), reach: 4);
        session.PointerDragged(new Point(121, 215), Modifiers.Command);
        Assert.Equal(103, Box(session, id).MinX);
        Assert.Empty(session.Guides);
    }

    /// <summary>The snap lets go once the pointer has carried the shape past it, rather than holding on.</summary>
    [Fact]
    public void CarryingOnPastASnapLetsGo()
    {
        var (session, id) = Session();
        session.PointerDown(new Point(218, 215), reach: 4);
        session.PointerDragged(new Point(121, 215));
        // On to a left edge at 82, its middle now on the other box's left edge, which takes nothing back.
        session.PointerDragged(new Point(100, 215));
        Assert.Equal(82, Box(session, id).MinX);
    }
}
