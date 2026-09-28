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

    /// Across and down through the middle of a card: both tags start on the same spot.
    private let across = MeasureLine(from: CGPoint(x: 50, y: 100), to: CGPoint(x: 250, y: 100))
    private let down = MeasureLine(from: CGPoint(x: 150, y: 20), to: CGPoint(x: 150, y: 180))

    private func tagRect(_ line: MeasureLine) -> CGRect {
        MeasureShape.tag(from: line.from, to: line.to, width: 2, scale: 1, at: line.labelAt).rect
    }

    /// Where a line is stroked, which no tag may sit on.
    private func stroke(_ line: MeasureLine) -> CGRect {
        CGRect(x: min(line.from.x, line.to.x) - 1, y: min(line.from.y, line.to.y) - 1,
               width: abs(line.to.x - line.from.x) + 2, height: abs(line.to.y - line.from.y) + 2)
    }

    @Test func acrossAndDownThroughTheMiddleOfACardKeepTheirTagsApart() {
        #expect(tagRect(across).intersects(tagRect(down)))
        let placed = MeasureShape.clearTags([across, down], width: 2, scale: 1)
        #expect(!tagRect(placed[0]).intersects(tagRect(placed[1])))
        // Neither line strikes through the other's tag, as the Down line ran through the
        // Across tag once the tags were only kept apart from each other.
        #expect(!tagRect(placed[0]).intersects(stroke(placed[1])))
        #expect(!tagRect(placed[1]).intersects(stroke(placed[0])))
        // Each tag slid along its own line and stayed clear of its ticks.
        #expect(tagRect(placed[0]).midY == 100 && tagRect(placed[1]).midX == 150)
        #expect(tagRect(placed[0]).minX > 50 + 4 && tagRect(placed[0]).maxX < 250 - 4)
        #expect(tagRect(placed[1]).minY > 20 + 4 && tagRect(placed[1]).maxY < 180 - 4)
    }

    @Test func tagsThatDoNotMeetStayOnTheirMiddles() {
        // Down beside the Across line's end, below it, so nothing crosses.
        let aside = MeasureLine(from: CGPoint(x: 240, y: 120), to: CGPoint(x: 240, y: 180))
        #expect(MeasureShape.clearTags([across, aside], width: 2, scale: 1) == [across, aside])
    }

    @Test func keepingBothLinesKeepsTheirTagsApart() {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 300, height: 200)))
        editor.choose(.measure)
        editor.keep([across, down])
        let kept = editor.display.annotations
        func rect(_ annotation: Annotation) -> CGRect {
            guard case let .measure(from, to) = annotation.kind else { return .null }
            return MeasureShape.tag(from: from, to: to, width: 2, scale: 1, at: annotation.labelAt).rect
        }
        #expect(kept.count == 2 && !rect(kept[0]).intersects(rect(kept[1])))
        // Picked up by its tag where the tag now is.
        #expect(kept[1].contains(CGPoint(x: rect(kept[1]).midX, y: rect(kept[1]).midY), scale: 1))
    }

    @Test func aKeptMeasurementStretchesAndItsLabelFollows() {
        let measure = Fixture.annotation(.measure(from: from, to: to))
        let stretched = measure.resized(dragging: .end, to: CGPoint(x: 290, y: 70), constrained: false)
        guard case let .measure(a, b) = stretched.kind else { Issue.record("not a measurement"); return }
        #expect(MeasureReading.label(forPixels: a.distance(to: b), scale: 1) == "240 pt")
    }
}
