import CoreGraphics
import Testing
@testable import TinysnapCore

struct DocumentTests {
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
