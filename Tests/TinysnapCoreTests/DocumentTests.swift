import CoreGraphics
import Testing
@testable import TinysnapCore

struct DocumentTests {
    @Test func theExtentIsTheCaptureWhileEverythingIsInsideIt() {
        let box = Fixture.annotation(.rectangle(CGRect(x: 40, y: 20, width: 50, height: 30)))
        let document = Document(capture: Fixture.capture(width: 200, height: 100, scale: 2), annotations: [box])
        #expect(document.extent == CGRect(x: 0, y: 0, width: 200, height: 100))
        #expect(document.outputRect == document.extent)
    }

    @Test func aShapePastTheEdgeGrowsTheExtentByItsBoundsAndAMargin() {
        let box = Fixture.annotation(.rectangle(CGRect(x: -50, y: 60, width: 100, height: 80)))
        let document = Document(capture: Fixture.capture(width: 200, height: 100, scale: 2), annotations: [box])
        // 16 points of margin is 32 pixels on a 2x capture.
        let grown = box.bounds(scale: 2).insetBy(dx: -32, dy: -32)
        #expect(document.extent == CGRect(x: 0, y: 0, width: 200, height: 100).union(grown).integral)
        #expect(document.extent.minX < 0 && document.extent.maxY > 100)
        #expect(document.extent.maxX == 200 && document.extent.minY == 0)
    }

    /// A measurement read from edge to edge, as across a whole capture whose card is too faint
    /// to stop at, ends on the edges; its end ticks overhang them, and grew the canvas.
    @Test func aMeasurementToTheEdgesGrowsNothing() {
        let across = Fixture.annotation(.measure(from: CGPoint(x: 0, y: 50), to: CGPoint(x: 200, y: 50)))
        let down = Fixture.annotation(.measure(from: CGPoint(x: 120, y: 0), to: CGPoint(x: 120, y: 100)))
        let document = Document(capture: Fixture.capture(width: 200, height: 100, scale: 2), annotations: [across, down])
        #expect(document.extent == CGRect(x: 0, y: 0, width: 200, height: 100))
        // One that goes past an edge still grows it.
        let past = Fixture.annotation(.measure(from: CGPoint(x: 120, y: -40), to: CGPoint(x: 120, y: 100)))
        #expect(Document(capture: Fixture.capture(width: 200, height: 100, scale: 2), annotations: [past]).extent.minY < 0)
    }

    @Test func aCropStillDecidesWhatIsOutputOnAGrownCanvas() {
        let box = Fixture.annotation(.rectangle(CGRect(x: 150, y: 10, width: 100, height: 20)))
        var document = Document(capture: Fixture.capture(width: 200, height: 100), annotations: [box])
        document.crop = CGRect(x: 10, y: 10, width: 50, height: 50)
        #expect(document.outputRect == CGRect(x: 10, y: 10, width: 50, height: 50))
    }

    @Test func stepsNumberThemselvesInOrderAndRenumberOnDelete() {
        var document = Document(capture: Fixture.capture(width: 200, height: 100))
        let steps = (0..<3).map { Fixture.annotation(.step(center: CGPoint(x: $0 * 20, y: 10))) }
        let box = Fixture.annotation(.rectangle(CGRect(x: 0, y: 0, width: 10, height: 10)))
        document.annotations = [steps[0], box, steps[1], steps[2]]

        #expect(document.stepNumber(of: steps[2].id) == 3)
        document.remove(steps[1].id)
        #expect(document.stepNumber(of: steps[2].id) == 2)
        #expect(document.stepNumber(of: box.id) == nil)
    }

    @Test func findsTheTopmostAnnotationUnderAPoint() {
        var document = Document(capture: Fixture.capture(width: 200, height: 100))
        let filled = Style(colorHex: Palette.red, filled: true)
        let below = Fixture.annotation(.rectangle(CGRect(x: 0, y: 0, width: 50, height: 50)), style: filled)
        let above = Fixture.annotation(.rectangle(CGRect(x: 20, y: 20, width: 50, height: 50)), style: filled)
        document.annotations = [below, above]

        #expect(document.topmost(at: CGPoint(x: 30, y: 30)) == above.id)
        #expect(document.topmost(at: CGPoint(x: 5, y: 5)) == below.id)
        #expect(document.topmost(at: CGPoint(x: 150, y: 90)) == nil)
    }

    @Test func cropsAFrozenDisplayFromPointsToPixels() throws {
        let display = Fixture.capture(width: 200, height: 100, scale: 2).image
        let capture = try #require(Capture.crop(display, points: CGRect(x: 10, y: 5, width: 20, height: 10), scale: 2))
        #expect(capture.pixelSize == CGSize(width: 40, height: 20))
        #expect(capture.pointSize == CGSize(width: 20, height: 10))
        #expect(Capture.crop(display, points: CGRect(x: 90, y: 40, width: 50, height: 50), scale: 2)?.pixelSize == CGSize(width: 20, height: 20))
        #expect(Capture.crop(display, points: CGRect(x: 500, y: 0, width: 10, height: 10), scale: 2) == nil)
    }

    @Test func aWindowCutFromTheFrozenDisplayKeepsItsColoursAndTakesItsOwnShape() throws {
        // The frozen display shows the window as it looked; the window's own capture, taken
        // once it lost the focus, is paler but has the true rounded corners.
        let frozen = Fixture.capture(width: 40, height: 40, fill: Fixture.blue).image
        let alone = Fixture.capture(width: 40, height: 40, fill: Fixture.white) { context in
            context.clear(CGRect(x: 0, y: 0, width: 8, height: 8))
        }.image
        let shaped = try #require(Capture.shaped(frozen, like: alone))
        let pixels = try #require(PixelBuffer(image: shaped))
        #expect(pixels.pixel(x: 2, y: 2).a == 0)
        #expect(Fixture.isClose(Fixture.pixel(shaped, 20, 20), (0, 0, 255)))
    }

    @Test func cropsAStandardDisplayOnePixelPerPoint() throws {
        let display = Fixture.capture(width: 200, height: 100, scale: 1).image
        let capture = try #require(Capture.crop(display, points: CGRect(x: 10, y: 5, width: 20, height: 10), scale: 1))
        #expect(capture.pixelSize == CGSize(width: 20, height: 10))
    }

    @Test func theOutputIsTheCropWhenThereIsOne() {
        var document = Document(capture: Fixture.capture(width: 200, height: 100))
        #expect(document.outputRect == CGRect(x: 0, y: 0, width: 200, height: 100))
        document.crop = CGRect(x: 10, y: 10, width: 50, height: 20)
        #expect(document.outputRect == CGRect(x: 10, y: 10, width: 50, height: 20))
    }
}
