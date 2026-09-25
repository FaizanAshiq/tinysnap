import CoreGraphics
import Testing
@testable import TinysnapCore

struct EditorSessionTests {
    private func session(_ tool: Tool = .arrow, width: Int = 400, height: Int = 300, scale: CGFloat = 2) -> EditorSession {
        EditorSession(document: Document(capture: Fixture.capture(width: width, height: height, scale: scale)), tool: tool)
    }

    private func session(document: Document? = nil, colorHex: String) -> EditorSession {
        EditorSession(document: document ?? Document(capture: Fixture.capture(width: 400, height: 300, scale: 2)),
                      colorHex: colorHex)
    }

    private func drag(_ session: inout EditorSession, from start: CGPoint, to end: CGPoint, modifiers: Modifiers = []) {
        session.pointerDown(at: start, reach: 8)
        session.pointerDragged(to: end, modifiers: modifiers)
        session.pointerUp()
    }

    @Test func draggingDrawsAndSelectsAnArrow() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 60))
        #expect(editor.display.annotations.count == 1)
        #expect(editor.selection == editor.display.annotations[0].id)
        #expect(editor.history.canUndo)
        #expect(editor.isUnsaved)
    }

    @Test func aClickWithoutADragCreatesNothing() {
        var editor = session()
        editor.pointerDown(at: CGPoint(x: 10, y: 10), reach: 8)
        editor.pointerUp()
        #expect(editor.display.annotations.isEmpty)
        #expect(!editor.history.canUndo)
        #expect(editor.selection == nil)
    }

    @Test func shiftSnapsTheArrowOntoAnAxis() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 14), modifiers: .shift)
        #expect(editor.display.annotations[0].kind == .arrow(from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 10)))
    }

    @Test func holdingSpaceMovesTheBoxBeingDrawnThenResizingCarriesOn() {
        var editor = session(.erase)
        editor.pointerDown(at: CGPoint(x: 10, y: 10), reach: 8)
        editor.pointerDragged(to: CGPoint(x: 50, y: 40))
        #expect(editor.display.annotations[0].kind == .erase(CGRect(x: 10, y: 10, width: 40, height: 30)))
        editor.pointerDragged(to: CGPoint(x: 70, y: 60), modifiers: .space)
        #expect(editor.display.annotations[0].kind == .erase(CGRect(x: 30, y: 30, width: 40, height: 30)))
        editor.pointerDragged(to: CGPoint(x: 100, y: 90))
        editor.pointerUp()
        #expect(editor.display.annotations[0].kind == .erase(CGRect(x: 30, y: 30, width: 70, height: 60)))
    }

    @Test func optionDrawsTheBoxBeingDrawnFromItsCentre() {
        var editor = session(.rectangle)
        editor.pointerDown(at: CGPoint(x: 100, y: 100), reach: 8)
        editor.pointerDragged(to: CGPoint(x: 130, y: 110), modifiers: [.shift, .option])
        editor.pointerUp()
        #expect(editor.display.annotations[0].kind == .rectangle(CGRect(x: 70, y: 70, width: 60, height: 60)))
    }

    @Test func holdingSpaceMovesAnArrowBeingDrawnWithItsAngleKept() {
        var editor = session()
        editor.pointerDown(at: CGPoint(x: 10, y: 10), reach: 8)
        editor.pointerDragged(to: CGPoint(x: 60, y: 10))
        editor.pointerDragged(to: CGPoint(x: 80, y: 30), modifiers: .space)
        editor.pointerUp()
        #expect(editor.display.annotations[0].kind == .arrow(from: CGPoint(x: 30, y: 30), to: CGPoint(x: 80, y: 30)))
    }

    @Test func optionLeavesLinesDrawnFromTheirStart() {
        var editor = session(.line)
        editor.pointerDown(at: CGPoint(x: 10, y: 10), reach: 8)
        editor.pointerDragged(to: CGPoint(x: 60, y: 10), modifiers: .option)
        editor.pointerUp()
        #expect(editor.display.annotations[0].kind == .line(from: CGPoint(x: 10, y: 10), to: CGPoint(x: 60, y: 10)))
    }

    @Test func holdingSpaceMovesAFreehandStrokeAndDrawingCarriesOnFromThere() {
        var editor = session(.freehand)
        editor.pointerDown(at: CGPoint(x: 10, y: 10), reach: 8)
        editor.pointerDragged(to: CGPoint(x: 20, y: 10))
        editor.pointerDragged(to: CGPoint(x: 30, y: 20), modifiers: .space)
        editor.pointerDragged(to: CGPoint(x: 40, y: 20))
        editor.pointerUp()
        #expect(editor.display.annotations[0].kind == .freehand([CGPoint(x: 20, y: 20), CGPoint(x: 30, y: 20), CGPoint(x: 40, y: 20)]))
    }

    @Test func holdingSpaceMovesTheCropBeingDrawnAndStopsAtTheEdge() {
        var editor = session()
        editor.choose(.crop)
        editor.pointerDown(at: CGPoint(x: 100, y: 100), reach: 8)
        editor.pointerDragged(to: CGPoint(x: 200, y: 150))
        editor.pointerDragged(to: CGPoint(x: 900, y: 150), modifiers: .space)
        editor.pointerUp()
        #expect(editor.display.crop == CGRect(x: 300, y: 100, width: 100, height: 50))
    }

    @Test func optionDrawsANewCropFromItsCentre() {
        var editor = session()
        editor.choose(.crop)
        editor.pointerDown(at: CGPoint(x: 200, y: 150), reach: 8)
        editor.pointerDragged(to: CGPoint(x: 250, y: 170), modifiers: .option)
        editor.pointerUp()
        #expect(editor.display.crop == CGRect(x: 150, y: 130, width: 100, height: 40))
    }

    @Test func aDrawingToolDrawsOverWhatIsAlreadyThere() {
        var editor = session(.line)
        drag(&editor, from: CGPoint(x: 10, y: 100), to: CGPoint(x: 300, y: 100))
        editor.choose(.arrow)
        drag(&editor, from: CGPoint(x: 150, y: 101), to: CGPoint(x: 150, y: 160))
        #expect(editor.display.annotations.count == 2)
    }

    @Test func holdingCommandPicksUpWithAnyToolAndMovesIt() {
        var editor = session(.line)
        drag(&editor, from: CGPoint(x: 10, y: 100), to: CGPoint(x: 300, y: 100))
        editor.choose(.arrow)
        editor.pointerDown(at: CGPoint(x: 150, y: 102), modifiers: .command, reach: 8)
        editor.pointerDragged(to: CGPoint(x: 150, y: 142), modifiers: .command)
        editor.pointerUp()
        #expect(editor.display.annotations.count == 1)
        #expect(editor.display.annotations[0].kind == .line(from: CGPoint(x: 10, y: 140), to: CGPoint(x: 300, y: 140)))
        #expect(editor.tool == .arrow)
    }

    @Test func holdingCommandPicksUpAnEraseAnywhereInside() {
        var editor = session(.erase)
        drag(&editor, from: CGPoint(x: 100, y: 100), to: CGPoint(x: 300, y: 250))
        editor.choose(.arrow)
        editor.pointerDown(at: CGPoint(x: 200, y: 180), modifiers: .command, reach: 8)
        editor.pointerUp()
        #expect(editor.selection == editor.display.annotations[0].id)
    }

    @Test func theSelectToolPicksUpAnEraseAnywhereInside() {
        var editor = session(.erase)
        drag(&editor, from: CGPoint(x: 100, y: 100), to: CGPoint(x: 300, y: 250))
        editor.pointerDown(at: CGPoint(x: 350, y: 280), reach: 8)
        editor.pointerUp()
        editor.choose(.select)
        editor.pointerDown(at: CGPoint(x: 200, y: 180), reach: 8)
        editor.pointerUp()
        #expect(editor.selection == editor.display.annotations[0].id)
    }

    @Test func hoverNamesWhatIsUnderThePointerWithAnyToolButCrop() {
        var editor = session(.blur)
        drag(&editor, from: CGPoint(x: 100, y: 100), to: CGPoint(x: 300, y: 250))
        let blur = editor.display.annotations[0].id
        editor.choose(.arrow)
        #expect(editor.hovered(at: CGPoint(x: 200, y: 180)) == blur)
        #expect(editor.hovered(at: CGPoint(x: 350, y: 280)) == nil)
        editor.choose(.crop)
        #expect(editor.hovered(at: CGPoint(x: 200, y: 180)) == nil)
    }

    @Test func anotherToolDrawsWhileSomethingIsStillSelected() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 540, y: 580), to: CGPoint(x: 1020, y: 440))
        editor.choose(.rectangle)
        drag(&editor, from: CGPoint(x: 80, y: 224), to: CGPoint(x: 460, y: 384))
        #expect(editor.display.annotations.count == 2)
    }

    @Test func draggingTheSelectionsBodyMovesIt() {
        var editor = session(.rectangle)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        drag(&editor, from: CGPoint(x: 30, y: 10), to: CGPoint(x: 40, y: 10))
        #expect(editor.display.annotations[0].kind == .rectangle(CGRect(x: 20, y: 10, width: 100, height: 100)))
        #expect(editor.display.annotations.count == 1)
    }

    @Test func draggingAHandleResizes() {
        var editor = session(.rectangle)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        drag(&editor, from: CGPoint(x: 110, y: 110), to: CGPoint(x: 150, y: 130))
        #expect(editor.display.annotations[0].kind == .rectangle(CGRect(x: 10, y: 10, width: 140, height: 120)))
    }

    @Test func theSelectToolPicksUpAndDropsTheSelection() {
        var editor = session(.rectangle)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        editor.choose(.select)
        editor.pointerDown(at: CGPoint(x: 300, y: 250), reach: 8)
        editor.pointerUp()
        #expect(editor.selection == nil)
        editor.pointerDown(at: CGPoint(x: 10, y: 60), reach: 8)
        editor.pointerUp()
        #expect(editor.selection == editor.display.annotations[0].id)
    }

    @Test func deleteAndNudgeAreEachOneUndoableStep() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 10))
        editor.nudge(dx: 10, dy: 0)
        #expect(editor.display.annotations[0].kind == .arrow(from: CGPoint(x: 20, y: 10), to: CGPoint(x: 110, y: 10)))
        editor.deleteSelection()
        #expect(editor.display.annotations.isEmpty)
        editor.undo()
        #expect(editor.display.annotations.count == 1)
        editor.undo()
        #expect(editor.display.annotations[0].kind == .arrow(from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 10)))
    }

    @Test func aNewTextLeftEmptyLeavesNoTrace() {
        var editor = session(.text)
        editor.pointerDown(at: CGPoint(x: 20, y: 20), reach: 8)
        #expect(editor.typingID != nil)
        editor.updateTyping("   ")
        #expect(editor.escape() == .finishedTyping)
        #expect(editor.display.annotations.isEmpty)
        #expect(!editor.history.canUndo)
    }

    @Test func typedTextIsCommittedAsOneStep() {
        var editor = session(.text)
        editor.pointerDown(at: CGPoint(x: 20, y: 20), reach: 8)
        editor.updateTyping("Hello")
        editor.finishTyping()
        #expect(editor.display.annotations[0].kind == .text(origin: CGPoint(x: 20, y: 20), string: "Hello"))
        editor.undo()
        #expect(editor.display.annotations.isEmpty)
    }

    @Test func clickingAwayFinishesTypingAndDoesNothingElse() {
        var editor = session(.text)
        editor.pointerDown(at: CGPoint(x: 20, y: 20), reach: 8)
        editor.updateTyping("Hello")
        editor.pointerDown(at: CGPoint(x: 300, y: 200), reach: 8)
        #expect(editor.typingID == nil)
        #expect(editor.display.annotations.count == 1)
    }

    @Test func emptyingAnExistingTextDeletesItUndoably() {
        var editor = session(.text)
        editor.pointerDown(at: CGPoint(x: 20, y: 20), reach: 8)
        editor.updateTyping("Hi")
        editor.finishTyping()

        editor.pointerDown(at: CGPoint(x: 24, y: 30), reach: 8)
        #expect(editor.typingID == editor.display.annotations[0].id)
        editor.updateTyping("")
        editor.finishTyping()
        #expect(editor.display.annotations.isEmpty)
        editor.undo()
        #expect(editor.display.annotations.count == 1)
    }

    @Test func escapeFinishesTypingThenDeselectsThenCloses() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 60))
        #expect(editor.escape() == .deselected)
        #expect(editor.escape() == .close)
    }

    @Test func eachToolRemembersItsOwnStyle() {
        var editor = session(.rectangle)
        let blue = Style(colorHex: "#007AFF", size: .large)
        editor.restyle { $0 = blue }
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        #expect(editor.display.annotations[0].style == blue)
        // The colour is shared, the size is the arrow's own.
        #expect(editor.style(for: .arrow) == Style(colorHex: "#007AFF", size: .medium))
    }

    @Test func oneColourIsSharedByEveryTool() {
        var editor = session(.line)
        editor.restyle { $0.colorHex = "#007AFF" }
        #expect(editor.colorHex == "#007AFF")
        editor.choose(.rectangle)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        #expect(editor.display.annotations[0].style.colorHex == "#007AFF")
        #expect(editor.style(for: .highlighter).colorHex == "#007AFF")
    }

    @Test func recolouringTheSelectionSetsTheColourForEveryTool() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 60))
        editor.restyle { $0.colorHex = "#34C759" }
        #expect(editor.style(for: .oval).colorHex == "#34C759")
    }

    @Test func changingOnlyTheSizeOfAnOldAnnotationKeepsTheSharedColour() {
        var editor = session(document: Document(capture: Fixture.capture(width: 400, height: 300, scale: 2), annotations: [
            Fixture.annotation(.line(from: CGPoint(x: 10, y: 10), to: CGPoint(x: 200, y: 10))),
        ]), colorHex: "#007AFF")
        editor.choose(.select)
        editor.pointerDown(at: CGPoint(x: 100, y: 10), reach: 8)
        editor.pointerUp()
        editor.restyle { $0.size = $0.size.thicker }
        #expect(editor.colorHex == "#007AFF")
        #expect(editor.display.annotations[0].style.colorHex == Palette.red)
    }

    @Test func startsWithTheRememberedColour() {
        let editor = session(colorHex: "#AF52DE")
        #expect(editor.style(for: .arrow).colorHex == "#AF52DE")
    }

    @Test func restylingTheSelectionIsUndoable() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 60))
        editor.restyle { $0.colorHex = "#007AFF" }
        #expect(editor.display.annotations[0].style.colorHex == "#007AFF")
        editor.undo()
        #expect(editor.display.annotations[0].style.colorHex == Palette.red)
    }

    @Test func restylingChangesOnlyThePartThatChanged() {
        var editor = session(.rectangle)
        editor.restyle {
            $0.size = .large
            $0.filled = true
        }
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        editor.restyle { $0.colorHex = "#007AFF" }
        #expect(editor.display.annotations[0].style == Style(colorHex: "#007AFF", size: .large, filled: true))
    }

    @Test func aRunOfColourPanelChangesUndoesAsOneStep() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 60))
        for hex in ["#111111", "#222222", "#333333"] {
            editor.restyle(merging: true) { $0.colorHex = hex }
        }
        editor.undo()
        #expect(editor.display.annotations.count == 1)
        #expect(editor.display.annotations[0].style.colorHex == Palette.red)
    }

    private func zoom(of id: Annotation.ID, in editor: EditorSession) -> CGFloat? {
        guard case let .magnifier(_, _, zoom) = editor.display.annotation(id)?.kind else { return nil }
        return zoom
    }

    @Test func scrollingOverAMagnifierZoomsItInHalfStepsWithinLimits() throws {
        var editor = session(.magnifier)
        editor.pointerDown(at: CGPoint(x: 200, y: 150), reach: 8)
        editor.pointerUp()
        let lens = try #require(editor.magnifier(at: CGPoint(x: 210, y: 150)))
        #expect(zoom(of: lens, in: editor) == 2)
        editor.zoomMagnifier(lens, steps: 1)
        #expect(zoom(of: lens, in: editor) == 2.5)
        editor.zoomMagnifier(lens, steps: 5)
        #expect(zoom(of: lens, in: editor) == 4)
        editor.zoomMagnifier(lens, steps: -10)
        #expect(zoom(of: lens, in: editor) == 1.5)
    }

    @Test func thereIsNoMagnifierToZoomAwayFromOne() {
        var editor = session(.magnifier)
        editor.pointerDown(at: CGPoint(x: 200, y: 150), reach: 8)
        editor.pointerUp()
        #expect(editor.magnifier(at: CGPoint(x: 395, y: 295)) == nil)
    }

    @Test func aRunOfZoomStepsUndoesAsOneStep() throws {
        var editor = session(.magnifier)
        editor.pointerDown(at: CGPoint(x: 200, y: 150), reach: 8)
        editor.pointerUp()
        let lens = try #require(editor.magnifier(at: CGPoint(x: 200, y: 150)))
        for _ in 0..<3 { editor.zoomMagnifier(lens, steps: 1) }
        editor.undo()
        #expect(zoom(of: lens, in: editor) == 2)
    }

    @Test func aPastedImageLargerThanTheCaptureIsScaledToFit() {
        var editor = session()
        let pasted = PastedImage(Fixture.capture(width: 8, height: 2).image)
        editor.insert(pasted, pointSize: CGSize(width: 400, height: 100))
        #expect(editor.display.annotations[0].kind == .image(CGRect(x: 0, y: 100, width: 400, height: 100), pasted))
        #expect(editor.tool == .select)
        #expect(editor.selection == editor.display.annotations[0].id)
    }

    @Test func cropStaysInsideTheCaptureOnWholePixels() {
        var editor = session()
        editor.choose(.crop)
        drag(&editor, from: .zero, to: CGPoint(x: -50, y: 20.4))
        #expect(editor.display.crop == CGRect(x: 0, y: 20, width: 400, height: 280))
        #expect(editor.isUnsaved)
    }

    @Test func savingMarksTheSessionSaved() {
        var editor = session()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 60))
        editor.markSaved()
        #expect(!editor.isUnsaved)
    }
}
