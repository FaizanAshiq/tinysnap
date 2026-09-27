import CoreGraphics
import Testing
@testable import TinysnapCore

struct MeasureTests {
    private let from = CGPoint(x: 50, y: 70)
    private let to = CGPoint(x: 250, y: 70)

    private func render(_ annotations: [Annotation]) -> CGImage {
        Renderer.render(Document(capture: Fixture.capture(width: 300, height: 100), annotations: annotations))!
    }

    @Test func aMeasurementDrawsItsLineTicksAndLabelInItsColour() {
        let image = render([Fixture.annotation(.measure(from: from, to: to))])
        let tag = MeasureShape.tag(from: from, to: to, width: 2, scale: 1).rect
        // Along the line, clear of the tag; up a tick at the start; inside the tag's margin.
        #expect(Fixture.isClose(Fixture.pixel(image, 80, 70), (255, 59, 48)))
        #expect(Fixture.isClose(Fixture.pixel(image, 50, 67), (255, 59, 48)))
        #expect(Fixture.isClose(Fixture.pixel(image, Int(tag.minX) + 2, 70), (255, 59, 48)))
        #expect(Fixture.isClose(Fixture.pixel(image, 80, 60), (255, 255, 255)))
    }

    @Test func aShortMeasurementKeepsItsTagClearOfTheLineSoTheGapShows() {
        // A 16 point gap is narrower than its own label. Centred, the tag hid the line.
        let across = MeasureShape.tag(from: CGPoint(x: 50, y: 70), to: CGPoint(x: 66, y: 70), width: 2, scale: 1).rect
        #expect(across.maxY <= 66)
        let down = MeasureShape.tag(from: CGPoint(x: 50, y: 70), to: CGPoint(x: 50, y: 86), width: 2, scale: 1).rect
        #expect(down.minX >= 54)
        // A long line has room, so its tag stays on the middle of it.
        #expect(MeasureShape.tag(from: from, to: to, width: 2, scale: 1).rect.midY == 70)
    }

    @Test func theLabelReadsWhiteOnADarkColourAndBlackOnALightOne() {
        #expect(MeasureShape.textColor(on: Palette.color(hex: "#FF3B30")) == CGColor(gray: 1, alpha: 1))
        #expect(MeasureShape.textColor(on: Palette.color(hex: "#FFCC00")) == CGColor(gray: 0, alpha: 1))
    }

    @Test func aMeasurementIsPickedUpByItsLineOrItsLabel() {
        let measure = Fixture.annotation(.measure(from: from, to: to))
        let tag = MeasureShape.tag(from: from, to: to, width: 2, scale: 1).rect
        #expect(measure.contains(CGPoint(x: 80, y: 71), scale: 1))
        #expect(measure.contains(CGPoint(x: tag.midX, y: tag.minY + 1), scale: 1))
        #expect(!measure.contains(CGPoint(x: 80, y: 90), scale: 1))
        #expect(measure.bounds(scale: 1).contains(tag))
    }

    @Test func keepingAReadingAddsItsLinesUnselectedInOneStep() {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 300, height: 100)))
        editor.choose(.measure)
        editor.keep([MeasureLine(from: from, to: to), MeasureLine(from: CGPoint(x: 150, y: 10), to: CGPoint(x: 150, y: 90))])
        #expect(editor.display.annotations.count == 2)
        #expect(editor.selection == nil)
        editor.undo()
        #expect(editor.display.annotations.isEmpty)
    }

    @Test func theMeasureToolNeverDrawsByDragging() {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 300, height: 100)))
        editor.choose(.measure)
        editor.pointerDown(at: CGPoint(x: 20, y: 20), reach: 4)
        editor.pointerDragged(to: CGPoint(x: 200, y: 80))
        editor.pointerUp()
        #expect(editor.display.annotations.isEmpty)
    }

    @Test func aClickOnNothingLetsGoOfTheSelectionSoTheNextReadingCanBeKept() {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 300, height: 100)))
        editor.choose(.measure)
        editor.keep([MeasureLine(from: from, to: to)])
        editor.pointerDown(at: CGPoint(x: 80, y: 70), reach: 4)
        editor.pointerUp()
        #expect(editor.selection != nil)
        editor.pointerDown(at: CGPoint(x: 80, y: 20), reach: 4)
        editor.pointerUp()
        #expect(editor.selection == nil)
    }

    @Test func aKeptMeasurementStretchesAndItsLabelFollows() {
        let measure = Fixture.annotation(.measure(from: from, to: to))
        let stretched = measure.resized(dragging: .end, to: CGPoint(x: 290, y: 70), constrained: false)
        guard case let .measure(a, b) = stretched.kind else { Issue.record("not a measurement"); return }
        #expect(MeasureReading.label(forPixels: a.distance(to: b), scale: 1) == "240 pt")
    }
}
