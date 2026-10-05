import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// A highlight darkens a light capture the way a marker does, and lightens a dark one, where
/// darkening left it all but invisible: dark mode screenshots are as common as light ones.
struct HighlighterTests {
    private let dark = CGColor(srgbRed: 30 / 255, green: 32 / 255, blue: 38 / 255, alpha: 1)

    private func mark(_ y: CGFloat, _ hex: String = "#FFCC00") -> Annotation {
        Fixture.annotation(.highlighter(from: CGPoint(x: 20, y: y), to: CGPoint(x: 180, y: y)), style: Style(colorHex: hex))
    }

    private func render(_ capture: Capture, _ marks: [Annotation]) throws -> CGImage {
        try #require(Renderer.render(Document(capture: capture, annotations: marks)))
    }

    @Test func onALightCaptureItDarkensLikeAMarker() throws {
        let image = try render(Fixture.capture(width: 200, height: 100), [mark(50)])
        // Yellow multiplied over white at 40%.
        #expect(Fixture.isClose(Fixture.pixel(image, 100, 50), (255, 235, 153)))
    }

    @Test func onADarkCaptureItLightensSoItShows() throws {
        let image = try render(Fixture.capture(width: 200, height: 100, fill: dark), [mark(50)])
        let pixel = Fixture.pixel(image, 100, 50)
        // Clearly lighter than the ground, and yellow rather than grey.
        #expect(pixel.r > 100 && pixel.g > 90 && pixel.b < pixel.g - 20)
    }

    /// Read from the screenshot under each stroke: one over the dark half lightens, one over
    /// the light half darkens.
    @Test func eachStrokeFollowsWhatIsUnderIt() throws {
        let capture = Fixture.capture(width: 200, height: 100) { context in
            context.setFillColor(dark)
            context.fill(CGRect(x: 0, y: 0, width: 200, height: 50))
        }
        let image = try render(capture, [mark(25), mark(75)])
        #expect(Fixture.pixel(image, 100, 25).r > 100)
        #expect(Fixture.isClose(Fixture.pixel(image, 100, 75), (255, 235, 153)))
    }

    @Test func aWhiteHighlightShowsOnADarkCapture() throws {
        let image = try render(Fixture.capture(width: 200, height: 100, fill: dark), [mark(50, "#FFFFFF")])
        #expect(Fixture.pixel(image, 100, 50).r > 100)
    }
}

/// The highlighter can follow the pointer, for marking a word on a line that is not straight
/// or circling a patch, as well as drawing the straight stroke it always has.
struct FreehandHighlighterTests {
    private func traced() -> EditorSession {
        var session = EditorSession(document: Document(capture: Fixture.capture(width: 120, height: 80)), tool: .highlighter)
        session.restyle { $0.freehand = true }
        session.pointerDown(at: CGPoint(x: 20, y: 20), reach: 4)
        session.pointerDragged(to: CGPoint(x: 60, y: 60))
        session.pointerDragged(to: CGPoint(x: 100, y: 20))
        session.pointerUp()
        return session
    }

    @Test func aFreehandHighlightFollowsThePointer() throws {
        let session = traced()
        let mark = try #require(session.display.annotations.first)
        guard case let .highlighterPath(points) = mark.kind else { Issue.record("not a path"); return }
        #expect(points.count == 3)
        #expect(mark.tool == .highlighter)
        let image = try #require(Renderer.render(session.display))
        // The bottom of the V, which a straight stroke from end to end never reaches.
        #expect(!Fixture.isClose(Fixture.pixel(image, 60, 58), (255, 255, 255)))
    }

    @Test func aPathIsSavedWithItsEndsSoAnOlderTinysnapDrawsItStraight() throws {
        let document = traced().display
        let (json, _) = try DocumentArchive.encode(document, captured: Date(timeIntervalSince1970: 1_700_000_000))
        let text = String(decoding: json, as: UTF8.self)
        #expect(text.contains(#""kind" : "highlighter""#) && text.contains(#""from""#) && text.contains(#""points""#))
        let read = try DocumentArchive.decode(json) { _ in nil }
        #expect(read.annotations.map(\.kind) == document.annotations.map(\.kind))
    }

    @Test func freehandIsSavedAndAStyleFromBeforeDrawsStraight() throws {
        let style = Style(colorHex: Palette.yellow, freehand: true)
        #expect(try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(style)) == style)
        let old = Data(##"{"colorHex":"#FFCC00","size":"medium"}"##.utf8)
        #expect(try JSONDecoder().decode(Style.self, from: old).freehand == false)
        #expect(Tool.highlighter.hasFreehand && !Tool.line.hasFreehand)
    }

    /// Straight or Freehand sets how the next stroke is drawn; a stroke already drawn keeps its
    /// shape. Picked with one selected, it took an undo step that changed nothing.
    @Test func pickingFreehandWithAStrokeSelectedTakesNoUndoStep() {
        let stroke = Fixture.annotation(.highlighter(from: CGPoint(x: 10, y: 10), to: CGPoint(x: 90, y: 10)))
        var session = EditorSession(document: Document(capture: Fixture.capture(width: 100, height: 50), annotations: [stroke]),
                                    tool: .highlighter)
        session.select(stroke.id)
        session.restyle { $0.freehand = true }
        #expect(!session.history.canUndo)
        #expect(session.style(for: .highlighter).freehand)
        #expect(session.selectedAnnotation?.style.freehand == true)
        // A change that shows, such as the colour, still takes its step.
        session.restyle { $0.colorHex = "#007AFF" }
        #expect(session.history.canUndo)
    }
}
