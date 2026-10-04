namespace Tinysnap.Core.Tests;

public class EditorSessionTests
{
    [Fact]
    public void ANewPasteStartsSolidEvenAfterAnOverlayWasFaded()
    {
        var editor = Session();
        var pasted = new PastedImage(Fixture.CaptureImage(20, 20));
        editor.Insert(pasted, new Size(20, 20));
        editor.Restyle(s => s with { Opacity = 0.5, Difference = true });
        editor.Insert(pasted, new Size(20, 20));
        var style = editor.Display.Annotations[1].Style;
        Assert.True(style.Opacity == 1 && !style.Difference);
    }

    [Fact]
    public void ADrawingToolPicksUpAShapeByItsBorderWithoutCommand()
    {
        var editor = Session(Tool.Blur);
        Drag(editor, new Point(100, 100), new Point(200, 160));
        var blur = editor.Display.Annotations[0];
        var bounds = blur.Bounds(editor.Scale);
        editor.Choose(Tool.Arrow);
        // The hover border is drawn half a reach outside the shape, here 4 pixels.
        var onBorder = new Point(bounds.MinX - 4, bounds.MidY);
        Assert.Equal(blur.Id, editor.Hovered(onBorder, reach: 8));

        Drag(editor, onBorder, new Point(onBorder.X + 30, onBorder.Y + 20));
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(blur.Id, editor.Selection);
        Assert.Equal(bounds.MinX + 30, editor.Display.Annotations[0].Bounds(editor.Scale).MinX);
    }

    [Fact]
    public void ADrawingToolPicksUpABlurFromItsMiddleAndMovesIt()
    {
        var editor = Session(Tool.Blur);
        Drag(editor, new Point(100, 100), new Point(200, 160));
        var blur = editor.Display.Annotations[0];
        editor.Choose(Tool.Arrow);
        Drag(editor, new Point(150, 130), new Point(180, 150));
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(blur.Id, editor.Selection);
        Assert.Equal(blur.Bounds(editor.Scale).MinX + 30, editor.Display.Annotations[0].Bounds(editor.Scale).MinX);
    }

    [Fact]
    public void InsideAnOutlinedBoxOrASpotlightADrawingToolStillDraws()
    {
        foreach (var tool in new[] { Tool.Rectangle, Tool.Spotlight })
        {
            var editor = Session(tool);
            Drag(editor, new Point(100, 100), new Point(300, 250));
            editor.Choose(Tool.Arrow);
            Drag(editor, new Point(200, 175), new Point(260, 220));
            Assert.Equal(2, editor.Display.Annotations.Length);
        }
    }

    [Fact]
    public void ACropCanReachAShapeDrawnPastTheCapture()
    {
        var editor = Session(Tool.Rectangle);
        Drag(editor, new Point(350, 100), new Point(500, 200));
        Assert.True(editor.Display.Extent.MaxX > 500);
        editor.Choose(Tool.Crop);
        Drag(editor, new Point(300, 50), new Point(510, 250));
        Assert.Equal(new Rect(300, 50, 210, 200), editor.Display.Crop);
    }

    private static EditorSession Session(Tool tool = Tool.Arrow, int width = 400, int height = 300, double scale = 2) =>
        new(new Document(Fixture.Capture(width, height, scale)), tool);

    private static EditorSession Session(string colorHex, Document? document = null) =>
        new(document ?? new Document(Fixture.Capture(400, 300, 2)), colorHex: colorHex);

    private static void Drag(EditorSession session, Point start, Point end, Modifiers modifiers = Modifiers.None)
    {
        session.PointerDown(start, reach: 8);
        session.PointerDragged(end, modifiers);
        session.PointerUp();
    }

    [Fact]
    public void DraggingDrawsAndSelectsAnArrow()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 60));
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(editor.Display.Annotations[0].Id, editor.Selection);
        Assert.True(editor.History.CanUndo);
        Assert.True(editor.IsUnsaved);
    }

    [Fact]
    public void AClickWithoutADragCreatesNothing()
    {
        var editor = Session();
        editor.PointerDown(new Point(10, 10), reach: 8);
        editor.PointerUp();
        Assert.Empty(editor.Display.Annotations);
        Assert.False(editor.History.CanUndo);
        Assert.Null(editor.Selection);
    }

    [Fact]
    public void ShiftSnapsTheArrowOntoAnAxis()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 14), Modifiers.Shift);
        Assert.Equal(new AnnotationKind.Arrow(new Point(10, 10), new Point(100, 10)), editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void HoldingSpaceMovesTheBoxBeingDrawnThenResizingCarriesOn()
    {
        var editor = Session(Tool.Erase);
        editor.PointerDown(new Point(10, 10), reach: 8);
        editor.PointerDragged(new Point(50, 40));
        Assert.Equal(new AnnotationKind.Erase(new Rect(10, 10, 40, 30)), editor.Display.Annotations[0].Kind);
        editor.PointerDragged(new Point(70, 60), Modifiers.Space);
        Assert.Equal(new AnnotationKind.Erase(new Rect(30, 30, 40, 30)), editor.Display.Annotations[0].Kind);
        editor.PointerDragged(new Point(100, 90));
        editor.PointerUp();
        Assert.Equal(new AnnotationKind.Erase(new Rect(30, 30, 70, 60)), editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void OptionDrawsTheBoxBeingDrawnFromItsCentre()
    {
        var editor = Session(Tool.Rectangle);
        editor.PointerDown(new Point(100, 100), reach: 8);
        editor.PointerDragged(new Point(130, 110), Modifiers.Shift | Modifiers.Option);
        editor.PointerUp();
        Assert.Equal(new AnnotationKind.Rectangle(new Rect(70, 70, 60, 60)), editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void HoldingSpaceMovesAnArrowBeingDrawnWithItsAngleKept()
    {
        var editor = Session();
        editor.PointerDown(new Point(10, 10), reach: 8);
        editor.PointerDragged(new Point(60, 10));
        editor.PointerDragged(new Point(80, 30), Modifiers.Space);
        editor.PointerUp();
        Assert.Equal(new AnnotationKind.Arrow(new Point(30, 30), new Point(80, 30)), editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void OptionLeavesLinesDrawnFromTheirStart()
    {
        var editor = Session(Tool.Line);
        editor.PointerDown(new Point(10, 10), reach: 8);
        editor.PointerDragged(new Point(60, 10), Modifiers.Option);
        editor.PointerUp();
        Assert.Equal(new AnnotationKind.Line(new Point(10, 10), new Point(60, 10)), editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void HoldingSpaceMovesAFreehandStrokeAndDrawingCarriesOnFromThere()
    {
        var editor = Session(Tool.Freehand);
        editor.PointerDown(new Point(10, 10), reach: 8);
        editor.PointerDragged(new Point(20, 10));
        editor.PointerDragged(new Point(30, 20), Modifiers.Space);
        editor.PointerDragged(new Point(40, 20));
        editor.PointerUp();
        Assert.Equal(new AnnotationKind.Freehand([new Point(20, 20), new Point(30, 20), new Point(40, 20)]),
                     editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void HoldingSpaceMovesTheCropBeingDrawnAndStopsAtTheEdge()
    {
        var editor = Session();
        editor.Choose(Tool.Crop);
        editor.PointerDown(new Point(100, 100), reach: 8);
        editor.PointerDragged(new Point(200, 150));
        editor.PointerDragged(new Point(900, 150), Modifiers.Space);
        editor.PointerUp();
        Assert.Equal(new Rect(300, 100, 100, 50), editor.Display.Crop);
    }

    [Fact]
    public void OptionDrawsANewCropFromItsCentre()
    {
        var editor = Session();
        editor.Choose(Tool.Crop);
        editor.PointerDown(new Point(200, 150), reach: 8);
        editor.PointerDragged(new Point(250, 170), Modifiers.Option);
        editor.PointerUp();
        Assert.Equal(new Rect(150, 130, 100, 40), editor.Display.Crop);
    }

    [Fact]
    public void ADrawingToolPicksUpALineByItsStrokeAndDrawsBesideIt()
    {
        var editor = Session(Tool.Line);
        Drag(editor, new Point(10, 100), new Point(300, 100));
        var line = editor.Display.Annotations[0];
        editor.Choose(Tool.Arrow);
        Drag(editor, new Point(150, 101), new Point(150, 121));
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(line.Id, editor.Selection);

        Drag(editor, new Point(150, 180), new Point(150, 240));
        Assert.Equal(2, editor.Display.Annotations.Length);
    }

    [Fact]
    public void HoldingCommandPicksUpWithAnyToolAndMovesIt()
    {
        var editor = Session(Tool.Line);
        Drag(editor, new Point(10, 100), new Point(300, 100));
        editor.Choose(Tool.Arrow);
        editor.PointerDown(new Point(150, 102), Modifiers.Command, reach: 8);
        editor.PointerDragged(new Point(150, 142), Modifiers.Command);
        editor.PointerUp();
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(new AnnotationKind.Line(new Point(10, 140), new Point(300, 140)), editor.Display.Annotations[0].Kind);
        Assert.Equal(Tool.Arrow, editor.Tool);
    }

    [Fact]
    public void HoldingCommandPicksUpAnEraseAnywhereInside()
    {
        var editor = Session(Tool.Erase);
        Drag(editor, new Point(100, 100), new Point(300, 250));
        editor.Choose(Tool.Arrow);
        editor.PointerDown(new Point(200, 180), Modifiers.Command, reach: 8);
        editor.PointerUp();
        Assert.Equal(editor.Display.Annotations[0].Id, editor.Selection);
    }

    [Fact]
    public void TheSelectToolPicksUpAnEraseAnywhereInside()
    {
        var editor = Session(Tool.Erase);
        Drag(editor, new Point(100, 100), new Point(300, 250));
        editor.PointerDown(new Point(350, 280), reach: 8);
        editor.PointerUp();
        editor.Choose(Tool.Select);
        editor.PointerDown(new Point(200, 180), reach: 8);
        editor.PointerUp();
        Assert.Equal(editor.Display.Annotations[0].Id, editor.Selection);
    }

    [Fact]
    public void HoverNamesWhatIsUnderThePointerWithAnyToolButCrop()
    {
        var editor = Session(Tool.Blur);
        Drag(editor, new Point(100, 100), new Point(300, 250));
        var blur = editor.Display.Annotations[0].Id;
        editor.Choose(Tool.Arrow);
        Assert.Equal(blur, editor.Hovered(new Point(200, 180)));
        Assert.Null(editor.Hovered(new Point(350, 280)));
        editor.Choose(Tool.Crop);
        Assert.Null(editor.Hovered(new Point(200, 180)));
    }

    [Fact]
    public void AnotherToolDrawsWhileSomethingIsStillSelected()
    {
        var editor = Session();
        Drag(editor, new Point(540, 580), new Point(1020, 440));
        editor.Choose(Tool.Rectangle);
        Drag(editor, new Point(80, 224), new Point(460, 384));
        Assert.Equal(2, editor.Display.Annotations.Length);
    }

    [Fact]
    public void DraggingTheSelectionsBodyMovesIt()
    {
        var editor = Session(Tool.Rectangle);
        Drag(editor, new Point(10, 10), new Point(110, 110));
        // Ctrl, so the box near the capture's top edge is not lined up on it.
        Drag(editor, new Point(30, 10), new Point(40, 10), Modifiers.Command);
        Assert.Equal(new AnnotationKind.Rectangle(new Rect(20, 10, 100, 100)), editor.Display.Annotations[0].Kind);
        Assert.Single(editor.Display.Annotations);
    }

    [Fact]
    public void DraggingAHandleResizes()
    {
        var editor = Session(Tool.Rectangle);
        Drag(editor, new Point(10, 10), new Point(110, 110));
        Drag(editor, new Point(110, 110), new Point(150, 130));
        Assert.Equal(new AnnotationKind.Rectangle(new Rect(10, 10, 140, 120)), editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void TheSelectToolPicksUpAndDropsTheSelection()
    {
        var editor = Session(Tool.Rectangle);
        Drag(editor, new Point(10, 10), new Point(110, 110));
        editor.Choose(Tool.Select);
        editor.PointerDown(new Point(300, 250), reach: 8);
        editor.PointerUp();
        Assert.Null(editor.Selection);
        editor.PointerDown(new Point(10, 60), reach: 8);
        editor.PointerUp();
        Assert.Equal(editor.Display.Annotations[0].Id, editor.Selection);
    }

    [Fact]
    public void DeleteAndNudgeAreEachOneUndoableStep()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 10));
        editor.Nudge(10, 0);
        Assert.Equal(new AnnotationKind.Arrow(new Point(20, 10), new Point(110, 10)), editor.Display.Annotations[0].Kind);
        editor.DeleteSelection();
        Assert.Empty(editor.Display.Annotations);
        editor.Undo();
        Assert.Single(editor.Display.Annotations);
        editor.Undo();
        Assert.Equal(new AnnotationKind.Arrow(new Point(10, 10), new Point(100, 10)), editor.Display.Annotations[0].Kind);
    }

    [Fact]
    public void ANewTextLeftEmptyLeavesNoTrace()
    {
        var editor = Session(Tool.Text);
        editor.PointerDown(new Point(20, 20), reach: 8);
        Assert.NotNull(editor.TypingId);
        editor.UpdateTyping("   ");
        Assert.Equal(EscapeResult.FinishedTyping, editor.Escape());
        Assert.Empty(editor.Display.Annotations);
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void TypedTextIsCommittedAsOneStep()
    {
        var editor = Session(Tool.Text);
        editor.PointerDown(new Point(20, 20), reach: 8);
        editor.UpdateTyping("Hello");
        editor.FinishTyping();
        Assert.Equal(new AnnotationKind.Text(new Point(20, 20), "Hello"), editor.Display.Annotations[0].Kind);
        editor.Undo();
        Assert.Empty(editor.Display.Annotations);
    }

    [Fact]
    public void ClickingAwayFinishesTypingAndDoesNothingElse()
    {
        var editor = Session(Tool.Text);
        editor.PointerDown(new Point(20, 20), reach: 8);
        editor.UpdateTyping("Hello");
        editor.PointerDown(new Point(300, 200), reach: 8);
        Assert.Null(editor.TypingId);
        Assert.Single(editor.Display.Annotations);
    }

    [Fact]
    public void EmptyingAnExistingTextDeletesItUndoably()
    {
        var editor = Session(Tool.Text);
        editor.PointerDown(new Point(20, 20), reach: 8);
        editor.UpdateTyping("Hi");
        editor.FinishTyping();

        editor.PointerDown(new Point(24, 30), reach: 8);
        Assert.Equal(editor.Display.Annotations[0].Id, editor.TypingId);
        editor.UpdateTyping("");
        editor.FinishTyping();
        Assert.Empty(editor.Display.Annotations);
        editor.Undo();
        Assert.Single(editor.Display.Annotations);
    }

    [Fact]
    public void EscapeFinishesTypingThenDeselectsThenCloses()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 60));
        Assert.Equal(EscapeResult.Deselected, editor.Escape());
        Assert.Equal(EscapeResult.Close, editor.Escape());
    }

    [Fact]
    public void EachToolRemembersItsOwnStyle()
    {
        var editor = Session(Tool.Rectangle);
        var blue = new Style("#007AFF", StyleSize.Large);
        editor.Restyle(_ => blue);
        Drag(editor, new Point(10, 10), new Point(110, 110));
        Assert.Equal(blue, editor.Display.Annotations[0].Style);
        // The colour is shared, the size is the arrow's own.
        Assert.Equal(new Style("#007AFF", StyleSize.Medium), editor.StyleFor(Tool.Arrow));
    }

    [Fact]
    public void OneColourIsSharedByEveryTool()
    {
        var editor = Session(Tool.Line);
        editor.Restyle(s => s with { ColorHex = "#007AFF" });
        Assert.Equal("#007AFF", editor.ColorHex);
        editor.Choose(Tool.Rectangle);
        Drag(editor, new Point(10, 10), new Point(110, 110));
        Assert.Equal("#007AFF", editor.Display.Annotations[0].Style.ColorHex);
        Assert.Equal("#007AFF", editor.StyleFor(Tool.Highlighter).ColorHex);
    }

    [Fact]
    public void RecolouringTheSelectionSetsTheColourForEveryTool()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 60));
        editor.Restyle(s => s with { ColorHex = "#34C759" });
        Assert.Equal("#34C759", editor.StyleFor(Tool.Oval).ColorHex);
    }

    [Fact]
    public void ChangingOnlyTheSizeOfAnOldAnnotationKeepsTheSharedColour()
    {
        var document = new Document(Fixture.Capture(400, 300, 2), annotations:
        [
            Fixture.Annotation(new AnnotationKind.Line(new Point(10, 10), new Point(200, 10))),
        ]);
        var editor = Session("#007AFF", document);
        editor.Choose(Tool.Select);
        editor.PointerDown(new Point(100, 10), reach: 8);
        editor.PointerUp();
        editor.Restyle(s => s with { Size = s.Size.Thicker() });
        Assert.Equal("#007AFF", editor.ColorHex);
        Assert.Equal(Palette.Red, editor.Display.Annotations[0].Style.ColorHex);
    }

    [Fact]
    public void StartsWithTheRememberedColour()
    {
        var editor = Session(colorHex: "#AF52DE");
        Assert.Equal("#AF52DE", editor.StyleFor(Tool.Arrow).ColorHex);
    }

    [Fact]
    public void RestylingTheSelectionIsUndoable()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 60));
        editor.Restyle(s => s with { ColorHex = "#007AFF" });
        Assert.Equal("#007AFF", editor.Display.Annotations[0].Style.ColorHex);
        editor.Undo();
        Assert.Equal(Palette.Red, editor.Display.Annotations[0].Style.ColorHex);
    }

    [Fact]
    public void RestylingChangesOnlyThePartThatChanged()
    {
        var editor = Session(Tool.Rectangle);
        editor.Restyle(s => s with { Size = StyleSize.Large, Filled = true });
        Drag(editor, new Point(10, 10), new Point(110, 110));
        editor.Restyle(s => s with { ColorHex = "#007AFF" });
        Assert.Equal(new Style("#007AFF", StyleSize.Large, filled: true), editor.Display.Annotations[0].Style);
    }

    [Fact]
    public void ARunOfColourPanelChangesUndoesAsOneStep()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 60));
        foreach (var hex in new[] { "#111111", "#222222", "#333333" })
            editor.Restyle(s => s with { ColorHex = hex }, merging: true);
        editor.Undo();
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(Palette.Red, editor.Display.Annotations[0].Style.ColorHex);
    }

    private static double? Zoom(Guid id, EditorSession editor) =>
        editor.Display.Annotation(id)?.Kind is AnnotationKind.Magnifier(_, _, var zoom) ? zoom : null;

    [Fact]
    public void ScrollingOverAMagnifierZoomsItInHalfStepsWithinLimits()
    {
        var editor = Session(Tool.Magnifier);
        editor.PointerDown(new Point(200, 150), reach: 8);
        editor.PointerUp();
        var lens = Assert.NotNull(editor.Magnifier(new Point(210, 150)));
        Assert.Equal(2, Zoom(lens, editor));
        editor.ZoomMagnifier(lens, 1);
        Assert.Equal(2.5, Zoom(lens, editor));
        editor.ZoomMagnifier(lens, 5);
        Assert.Equal(4, Zoom(lens, editor));
        editor.ZoomMagnifier(lens, -10);
        Assert.Equal(1.5, Zoom(lens, editor));
    }

    [Fact]
    public void ThereIsNoMagnifierToZoomAwayFromOne()
    {
        var editor = Session(Tool.Magnifier);
        editor.PointerDown(new Point(200, 150), reach: 8);
        editor.PointerUp();
        Assert.Null(editor.Magnifier(new Point(395, 295)));
    }

    [Fact]
    public void ARunOfZoomStepsUndoesAsOneStep()
    {
        var editor = Session(Tool.Magnifier);
        editor.PointerDown(new Point(200, 150), reach: 8);
        editor.PointerUp();
        var lens = Assert.NotNull(editor.Magnifier(new Point(200, 150)));
        for (var i = 0; i < 3; i++) editor.ZoomMagnifier(lens, 1);
        editor.Undo();
        Assert.Equal(2, Zoom(lens, editor));
    }

    [Fact]
    public void APastedImageLargerThanTheCaptureIsScaledToFit()
    {
        var editor = Session();
        var pasted = new PastedImage(Fixture.CaptureImage(8, 2));
        editor.Insert(pasted, new Size(400, 100));
        Assert.Equal(new AnnotationKind.Image(new Rect(0, 100, 400, 100), pasted), editor.Display.Annotations[0].Kind);
        Assert.Equal(editor.Display.Annotations[0].Id, editor.Selection);
    }

    [Fact]
    public void PastingKeepsTheToolInHandAndTheImageStillMoves()
    {
        // The tool changes only when the person picks one. The pasted image is selected,
        // so it moves under whatever tool is out.
        var editor = Session(Tool.Arrow);
        editor.Insert(new PastedImage(Fixture.CaptureImage(40, 20)), new Size(40, 20));
        Assert.Equal(Tool.Arrow, editor.Tool);
        var pasted = editor.Display.Annotations[0];
        Assert.Equal(pasted.Id, editor.Selection);
        var before = pasted.Bounds(editor.Scale);
        // Ctrl, so the image is not lined up on the capture's middle on the way.
        Drag(editor, new Point(before.MidX, before.MidY), new Point(before.MidX + 30, before.MidY), Modifiers.Command);
        Assert.Single(editor.Display.Annotations);
        Assert.Equal(before.MinX + 30, editor.Display.Annotations[0].Bounds(editor.Scale).MinX);
    }

    [Fact]
    public void CropStaysInsideTheCaptureOnWholePixels()
    {
        var editor = Session();
        editor.Choose(Tool.Crop);
        Drag(editor, Point.Zero, new Point(-50, 20.4));
        Assert.Equal(new Rect(0, 20, 400, 280), editor.Display.Crop);
        Assert.True(editor.IsUnsaved);
    }

    [Fact]
    public void SavingMarksTheSessionSaved()
    {
        var editor = Session();
        Drag(editor, new Point(10, 10), new Point(100, 60));
        editor.MarkSaved();
        Assert.False(editor.IsUnsaved);
    }
}
