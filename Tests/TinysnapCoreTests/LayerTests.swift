import CoreGraphics
import Testing
@testable import TinysnapCore

struct LayerTests {
    private func document(_ annotations: [Annotation], width: Int = 400, height: Int = 300) -> Document {
        Document(capture: Fixture.capture(width: width, height: height, scale: 2), annotations: annotations)
    }

    private func session(_ tool: Tool) -> EditorSession {
        EditorSession(document: Document(capture: Fixture.capture(width: 400, height: 300, scale: 2)), tool: tool)
    }

    private func drag(_ session: inout EditorSession, from start: CGPoint, to end: CGPoint) {
        session.pointerDown(at: start, reach: 8)
        session.pointerDragged(to: end)
        session.pointerUp()
    }

    @Test func eachMoveChangesTheOrderOnceAndAMoveThatChangesNothingIsNoStep() {
        var editor = session(.rectangle)
        for x in [10.0, 110, 210] { drag(&editor, from: CGPoint(x: x, y: 10), to: CGPoint(x: x + 50, y: 60)) }
        let ids = editor.display.annotations.map(\.id)
        editor.select(ids[0])
        editor.arrange(.forward)
        #expect(editor.display.annotations.map(\.id) == [ids[1], ids[0], ids[2]])
        editor.arrange(.front)
        #expect(editor.display.annotations.map(\.id) == [ids[1], ids[2], ids[0]])
        editor.arrange(.forward)
        #expect(editor.display.annotations.map(\.id) == [ids[1], ids[2], ids[0]])
        editor.arrange(.back)
        #expect(editor.display.annotations.map(\.id) == [ids[0], ids[1], ids[2]])
        // Forward at the top changed nothing, so one undo goes back past Back only.
        editor.undo()
        #expect(editor.display.annotations.map(\.id) == [ids[1], ids[2], ids[0]])
        editor.arrange(.backward)
        #expect(editor.display.annotations.map(\.id) == [ids[1], ids[0], ids[2]])
    }

    @Test func aRowDraggedAnywhereLandsThereAsOneStep() {
        var editor = session(.rectangle)
        for x in [10.0, 110, 210] { drag(&editor, from: CGPoint(x: x, y: 10), to: CGPoint(x: x + 50, y: 60)) }
        let ids = editor.display.annotations.map(\.id)
        editor.moveLayer(ids[2], to: 0)
        #expect(editor.display.annotations.map(\.id) == [ids[2], ids[0], ids[1]])
        editor.undo()
        #expect(editor.display.annotations.map(\.id) == ids)
    }

    @Test func aLockedShapeCannotChangeAndADrawingToolDrawsOverIt() {
        var editor = session(.blur)
        drag(&editor, from: CGPoint(x: 100, y: 100), to: CGPoint(x: 200, y: 160))
        let blur = editor.display.annotations[0]
        editor.toggleLock()
        #expect(editor.display.annotations[0].isLocked)
        editor.nudge(dx: 5, dy: 0)
        editor.restyle { $0.size = .large }
        editor.deleteSelection()
        #expect(editor.display.annotations.count == 1)
        #expect(editor.display.annotations[0].kind == blur.kind && editor.display.annotations[0].style == blur.style)
        editor.choose(.rectangle)
        drag(&editor, from: CGPoint(x: 150, y: 130), to: CGPoint(x: 260, y: 220))
        #expect(editor.display.annotations.count == 2)
        #expect(editor.display.annotations[0].kind == blur.kind)
        editor.choose(.select)
        drag(&editor, from: CGPoint(x: 120, y: 110), to: CGPoint(x: 140, y: 120))
        #expect(editor.selection == blur.id)
        #expect(editor.display.annotations[0].kind == blur.kind)
    }

    @Test func aHiddenSelectionCanBeDeletedButNotNudged() {
        var editor = session(.rectangle)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        let box = editor.display.annotations[0]
        editor.setHidden(box.id, true)
        #expect(editor.selection == box.id)
        editor.nudge(dx: 5, dy: 0)
        #expect(editor.display.annotations[0].kind == box.kind)
        editor.deleteSelection()
        #expect(editor.display.annotations.isEmpty)
    }

    @Test func duplicateGoesAboveTheOriginalOffsetUnlockedAndSelected() {
        var editor = session(.arrow)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 100, y: 60))
        drag(&editor, from: CGPoint(x: 10, y: 200), to: CGPoint(x: 100, y: 250))
        let first = editor.display.annotations[0]
        editor.select(first.id)
        editor.toggleLock()
        editor.duplicateSelection()
        let copy = editor.display.annotations[1]
        #expect(editor.display.annotations.count == 3)
        #expect(copy.id != first.id && !copy.isLocked && !copy.isHidden)
        #expect(copy.kind == first.moved(by: CGVector(dx: 24, dy: 24)).kind)
        #expect(copy.style == first.style)
        #expect(editor.selection == copy.id)
    }

    @Test func arrangingWhileTypingFinishesTheTextFirst() {
        var editor = session(.rectangle)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 110, y: 110))
        editor.choose(.text)
        editor.pointerDown(at: CGPoint(x: 200, y: 200), reach: 8)
        editor.updateTyping("Note")
        editor.arrange(.back)
        #expect(editor.typingID == nil)
        #expect(editor.display.annotations.first.map { editor.display.layerName(of: $0.id) } == "Note")
    }

    @Test func aHiddenOrLockedMagnifierIsNotZoomed() {
        var editor = session(.magnifier)
        editor.pointerDown(at: CGPoint(x: 200, y: 150), reach: 8)
        editor.pointerUp()
        let lens = editor.display.annotations[0]
        editor.setLocked(lens.id, true)
        #expect(editor.magnifier(at: CGPoint(x: 200, y: 150)) == nil)
        editor.zoomMagnifier(lens.id, steps: 2)
        #expect(editor.display.annotations[0].kind == lens.kind)
    }

    @Test func aHiddenShapeIsNotDrawnClickedOrBordered() {
        var box = Fixture.annotation(.rectangle(CGRect(x: 100, y: 100, width: 80, height: 60)),
                                     style: Style(colorHex: Palette.red, filled: true))
        box.isHidden = true
        let doc = document([box])
        #expect(doc.topmost(at: CGPoint(x: 140, y: 130)) == nil)
        #expect(doc.pickUp(at: CGPoint(x: 140, y: 130), reach: 8) == nil)
        #expect(doc.borderHit(at: CGPoint(x: 96, y: 130), reach: 8) == nil)
        let blank = Renderer.render(document([]))!
        let hidden = Renderer.render(doc)!
        #expect(Fixture.pixel(hidden, 140, 130) == Fixture.pixel(blank, 140, 130))
    }

    @Test func aLockedShapeIsSelectableButNotPickedUpByADrawingTool() {
        var blur = Fixture.annotation(.blur(CGRect(x: 100, y: 100, width: 80, height: 60)))
        blur.isLocked = true
        let doc = document([blur])
        #expect(doc.topmost(at: CGPoint(x: 140, y: 130)) == blur.id)
        #expect(doc.pickUp(at: CGPoint(x: 140, y: 130), reach: 8) == nil)
        #expect(doc.pickUp(at: CGPoint(x: 96, y: 130), reach: 8) == nil)
    }

    @Test func aHiddenShapePastTheEdgeDoesNotGrowTheCanvas() {
        var far = Fixture.annotation(.rectangle(CGRect(x: 700, y: 100, width: 80, height: 60)))
        far.isHidden = true
        #expect(document([far]).extent == CGRect(x: 0, y: 0, width: 400, height: 300))
    }

    @Test func layerNamesSayWhatEachShapeIs() {
        let text = Fixture.annotation(.text(origin: CGPoint(x: 10, y: 10), string: "Best week so far\nsecond line"))
        let first = Fixture.annotation(.step(center: CGPoint(x: 50, y: 50)))
        var hiddenStep = Fixture.annotation(.step(center: CGPoint(x: 80, y: 50)))
        hiddenStep.isHidden = true
        let second = Fixture.annotation(.step(center: CGPoint(x: 110, y: 50)))
        let blur = Fixture.annotation(.blur(CGRect(x: 0, y: 0, width: 20, height: 20)))
        let arrow = Fixture.annotation(.arrow(from: CGPoint(x: 0, y: 0), to: CGPoint(x: 20, y: 20)))
        let doc = document([text, first, hiddenStep, second, blur, arrow])
        #expect(doc.layerName(of: text.id) == "Best week so far")
        #expect(doc.layerName(of: first.id) == "Step 1")
        #expect(doc.layerName(of: second.id) == "Step 2")
        #expect(doc.layerName(of: hiddenStep.id) == "Step")
        #expect(doc.layerName(of: blur.id) == "Blur")
        #expect(doc.layerName(of: arrow.id) == "Arrow")
    }
}
