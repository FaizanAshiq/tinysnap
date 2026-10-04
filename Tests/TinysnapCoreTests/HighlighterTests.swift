import CoreGraphics
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
