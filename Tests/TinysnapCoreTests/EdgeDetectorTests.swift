import CoreGraphics
import Testing
@testable import TinysnapCore

struct EdgeDetectorTests {
    private let page = CGColor(srgbRed: 0.97, green: 0.97, blue: 0.97, alpha: 1)

    /// Two 40 pixel cards, 20 apart, on a 160 by 100 capture.
    private func cards(_ card: CGColor, on page: CGColor = Fixture.white) -> LuminanceBuffer {
        let capture = Fixture.capture(width: 160, height: 100, fill: page) {
            $0.setFillColor(card)
            $0.fill(CGRect(x: 20, y: 30, width: 40, height: 40))
            $0.fill(CGRect(x: 80, y: 30, width: 40, height: 40))
        }
        return LuminanceBuffer(image: capture.image)!
    }

    @Test func brightnessReadsWhiteAsOneAndBlackAsZero() throws {
        let buffer = try #require(LuminanceBuffer(image: Fixture.capture(width: 4, height: 4) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 0, y: 0, width: 2, height: 4))
        }.image))
        #expect(buffer.luminance(x: 0, y: 0) == 0)
        #expect(buffer.luminance(x: 3, y: 3) == 1)
    }

    @Test func theGapBetweenTwoCardsIsFoundToThePixel() {
        let region = EdgeDetector(threshold: 0.08, runLength: 1).bounds(around: (70, 50), in: cards(Fixture.black))
        // Nothing above or below the gap, so the capture's border closes it there.
        #expect(region == CGRect(x: 60, y: 0, width: 20, height: 100))
    }

    @Test func aCardAShadeOffThePageIsFoundOnlyAtALowContrast() {
        let buffer = cards(Fixture.white, on: page)
        #expect(EdgeDetector(threshold: 0.08, runLength: 1).bounds(around: (70, 50), in: buffer) == nil)
        #expect(EdgeDetector(threshold: 0.02, runLength: 1).bounds(around: (70, 50), in: buffer)
            == CGRect(x: 60, y: 0, width: 20, height: 100))
    }

    @Test func aRegionWithNoEdgeOnOneSideIsClosedByTheBorder() {
        let region = EdgeDetector(threshold: 0.08, runLength: 1).bounds(around: (5, 50), in: cards(Fixture.black))
        #expect(region == CGRect(x: 0, y: 0, width: 20, height: 100))
    }

    @Test func aFlatFieldHasNoRegion() {
        let buffer = LuminanceBuffer(image: Fixture.capture(width: 50, height: 50).image)!
        #expect(EdgeDetector(threshold: 0.08, runLength: 1).bounds(around: (25, 25), in: buffer) == nil)
    }

    @Test func aStrayPixelIsNotAnEdgeOnceARunIsAsked() {
        let buffer = LuminanceBuffer(image: Fixture.capture(width: 100, height: 20) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 70, y: 0, width: 1, height: 20))
        }.image)!
        // A Retina capture asks for two pixels, a point, before a change counts.
        #expect(EdgeDetector(threshold: 0.08, scale: 2).firstEdge(from: (50, 10), direction: .right, in: buffer) == nil)
        #expect(EdgeDetector(threshold: 0.08, scale: 1).firstEdge(from: (50, 10), direction: .right, in: buffer) == 69)
    }
}
