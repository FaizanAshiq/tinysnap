using System.Collections.Immutable;

namespace Tinysnap.Core.Tests;

public class LayerTests
{
    private static Document Doc(params Annotation[] annotations) =>
        new(Fixture.Capture(400, 300, 2), annotations: [.. annotations]);

    private static EditorSession Session(Tool tool) => new(new Document(Fixture.Capture(400, 300, 2)), tool);

    private static void Drag(EditorSession session, Point start, Point end)
    {
        session.PointerDown(start, reach: 8);
        session.PointerDragged(end);
        session.PointerUp();
    }

    private static List<Guid> Order(EditorSession session) => [.. session.Display.Annotations.Select(a => a.Id)];

    [Fact]
    public void EachMoveChangesTheOrderOnceAndAMoveThatChangesNothingIsNoStep()
    {
        var editor = Session(Tool.Rectangle);
        foreach (var x in new[] { 10.0, 110, 210 }) Drag(editor, new Point(x, 10), new Point(x + 50, 60));
        var ids = Order(editor);
        editor.Select(ids[0]);
        editor.Arrange(Arrangement.Forward);
        Assert.Equal([ids[1], ids[0], ids[2]], Order(editor));
        editor.Arrange(Arrangement.Front);
        Assert.Equal([ids[1], ids[2], ids[0]], Order(editor));
        editor.Arrange(Arrangement.Forward);
        Assert.Equal([ids[1], ids[2], ids[0]], Order(editor));
        editor.Arrange(Arrangement.Back);
        Assert.Equal(ids, Order(editor));
        // Forward at the top changed nothing, so one undo goes back past Back only.
        editor.Undo();
        Assert.Equal([ids[1], ids[2], ids[0]], Order(editor));
        editor.Arrange(Arrangement.Backward);
        Assert.Equal([ids[1], ids[0], ids[2]], Order(editor));
    }

    [Fact]
    public void ARowDraggedAnywhereLandsThereAsOneStep()
    {
        var editor = Session(Tool.Rectangle);
        foreach (var x in new[] { 10.0, 110, 210 }) Drag(editor, new Point(x, 10), new Point(x + 50, 60));
        var ids = Order(editor);
        editor.MoveLayer(ids[2], 0);
        Assert.Equal([ids[2], ids[0], ids[1]], Order(editor));
        editor.Undo();
        Assert.Equal(ids, Order(editor));
    }

    [Fact]
    public void ALockedShapeCannotChangeAndADrawingToolDrawsOverIt()
    {
        var editor = Session(Tool.Blur);
        Drag(editor, new Point(100, 100), new Point(200, 160));
        var blur = editor.Display.Annotations[0];
        editor.ToggleLock();
        Assert.True(editor.Display.Annotations[0].IsLocked);
        editor.Nudge(5, 0);
        editor.Restyle(s => s with { Size = StyleSize.Large });
        editor.DeleteSelection();
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(blur.Kind, editor.Display.Annotations[0].Kind);
        Assert.Equal(blur.Style, editor.Display.Annotations[0].Style);
        editor.Choose(Tool.Rectangle);
        Drag(editor, new Point(150, 130), new Point(260, 220));
        Assert.Equal(2, editor.Display.Annotations.Length);
        Assert.Equal(blur.Kind, editor.Display.Annotations[0].Kind);
        editor.Choose(Tool.Select);
        Drag(editor, new Point(120, 110), new Point(140, 120));
        Assert.Equal(blur.Id, editor.Selection);
        Assert.Equal(blur.Kind, editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void AHiddenSelectionCanBeDeletedButNotNudged()
    {
        var editor = Session(Tool.Rectangle);
        Drag(editor, new Point(10, 10), new Point(110, 110));
        var box = editor.Display.Annotations[0];
        editor.SetHidden(box.Id, true);
        Assert.Equal(box.Id, editor.Selection);
        editor.Nudge(5, 0);
        Assert.Equal(box.Kind, editor.Display.Annotations[0].Kind);
        editor.DeleteSelection();
        Assert.Empty(editor.Display.Annotations);
    }

    [Fact]
    public void DuplicateGoesAboveTheOriginalOffsetUnlockedAndSelected()
    {
        var editor = Session(Tool.Arrow);
        Drag(editor, new Point(10, 10), new Point(100, 60));
        Drag(editor, new Point(10, 200), new Point(100, 250));
        var first = editor.Display.Annotations[0];
        editor.Select(first.Id);
        editor.ToggleLock();
        editor.DuplicateSelection();
        var copy = editor.Display.Annotations[1];
        Assert.Equal(3, editor.Display.Annotations.Length);
        Assert.True(copy.Id != first.Id && !copy.IsLocked && !copy.IsHidden);
        Assert.Equal(first.Moved(new Vector(24, 24)).Kind, copy.Kind);
        Assert.Equal(first.Style, copy.Style);
        Assert.Equal(copy.Id, editor.Selection);
    }

    [Fact]
    public void ArrangingWhileTypingFinishesTheTextFirst()
    {
        var editor = Session(Tool.Rectangle);
        Drag(editor, new Point(10, 10), new Point(110, 110));
        editor.Choose(Tool.Text);
        editor.PointerDown(new Point(200, 200), reach: 8);
        editor.UpdateTyping("Note");
        editor.Arrange(Arrangement.Back);
        Assert.Null(editor.TypingId);
        Assert.Equal("Note", editor.Display.LayerName(editor.Display.Annotations[0].Id));
    }

    [Fact]
    public void AHiddenOrLockedMagnifierIsNotZoomed()
    {
        var editor = Session(Tool.Magnifier);
        editor.PointerDown(new Point(200, 150), reach: 8);
        editor.PointerUp();
        var lens = editor.Display.Annotations[0];
        editor.SetLocked(lens.Id, true);
        Assert.Null(editor.Magnifier(new Point(200, 150)));
        editor.ZoomMagnifier(lens.Id, 2);
        Assert.Equal(lens.Kind, editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void AHiddenShapeIsNotDrawnClickedOrBordered()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(100, 100, 80, 60)), new Style(Palette.Red, filled: true))
            with { IsHidden = true };
        var doc = Doc(box);
        Assert.Null(doc.Topmost(new Point(140, 130)));
        Assert.Null(doc.PickUp(new Point(140, 130), 8));
        Assert.Null(doc.BorderHit(new Point(96, 130), 8));
        var blank = Renderer.Render(Doc())!;
        var hidden = Renderer.Render(doc)!;
        Assert.Equal(Fixture.Pixel(blank, 140, 130), Fixture.Pixel(hidden, 140, 130));
    }

    [Fact]
    public void ALockedShapeIsSelectableButNotPickedUpByADrawingTool()
    {
        var blur = Fixture.Annotation(new AnnotationKind.Blur(new Rect(100, 100, 80, 60))) with { IsLocked = true };
        var doc = Doc(blur);
        Assert.Equal(blur.Id, doc.Topmost(new Point(140, 130)));
        Assert.Null(doc.PickUp(new Point(140, 130), 8));
        Assert.Null(doc.PickUp(new Point(96, 130), 8));
    }

    [Fact]
    public void AHiddenShapePastTheEdgeDoesNotGrowTheCanvas()
    {
        var far = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(700, 100, 80, 60))) with { IsHidden = true };
        Assert.Equal(new Rect(0, 0, 400, 300), Doc(far).Extent);
    }

    [Fact]
    public void LayerNamesSayWhatEachShapeIs()
    {
        var text = Fixture.Annotation(new AnnotationKind.Text(new Point(10, 10), "Best week so far\nsecond line"));
        var first = Fixture.Annotation(new AnnotationKind.Step(new Point(50, 50)));
        var hiddenStep = Fixture.Annotation(new AnnotationKind.Step(new Point(80, 50))) with { IsHidden = true };
        var second = Fixture.Annotation(new AnnotationKind.Step(new Point(110, 50)));
        var blur = Fixture.Annotation(new AnnotationKind.Blur(new Rect(0, 0, 20, 20)));
        var arrow = Fixture.Annotation(new AnnotationKind.Arrow(new Point(0, 0), new Point(20, 20)));
        var doc = Doc(text, first, hiddenStep, second, blur, arrow);
        Assert.Equal("Best week so far", doc.LayerName(text.Id));
        Assert.Equal("Step 1", doc.LayerName(first.Id));
        Assert.Equal("Step 2", doc.LayerName(second.Id));
        Assert.Equal("Step", doc.LayerName(hiddenStep.Id));
        Assert.Equal("Blur", doc.LayerName(blur.Id));
        Assert.Equal("Arrow", doc.LayerName(arrow.Id));
    }
}
