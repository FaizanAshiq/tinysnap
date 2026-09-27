import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

struct MeasureReadingTests {
    /// A Retina capture: two black cards 32 pixels, 16 points, apart.
    private func twoCards() -> LuminanceBuffer {
        let capture = Fixture.capture(width: 400, height: 200, scale: 2) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 40, y: 60, width: 100, height: 80))
            $0.fill(CGRect(x: 172, y: 60, width: 100, height: 80))
        }
        return LuminanceBuffer(image: capture.image)!
    }

    private func settings(across: Bool = true, down: Bool = false) -> MeasureSettings {
        var settings = MeasureSettings.defaults
        settings.across = across
        settings.down = down
        return settings
    }

    @Test func acrossSpansTheGapThroughThePointer() {
        let lines = MeasureReading.lines(at: CGPoint(x: 156, y: 100), in: twoCards(), scale: 2, settings: settings())
        #expect(lines == [MeasureLine(from: CGPoint(x: 140, y: 100), to: CGPoint(x: 172, y: 100))])
        #expect(MeasureReading.label(forPixels: 32, scale: 2) == "16 pt")
    }

    @Test func bothOnGiveAcrossThenDown() {
        // Inside the first card: 100 by 80 pixels, 50 by 40 points.
        let lines = MeasureReading.lines(at: CGPoint(x: 90, y: 100), in: twoCards(), scale: 2,
                                         settings: settings(across: true, down: true))
        #expect(lines == [MeasureLine(from: CGPoint(x: 40, y: 100), to: CGPoint(x: 140, y: 100)),
                          MeasureLine(from: CGPoint(x: 90, y: 60), to: CGPoint(x: 90, y: 140))])
    }

    @Test func nothingIsShownWithBothOffOrOffTheCapture() {
        let buffer = twoCards()
        #expect(MeasureReading.lines(at: CGPoint(x: 156, y: 100), in: buffer, scale: 2, settings: settings(across: false)).isEmpty)
        #expect(MeasureReading.lines(at: CGPoint(x: -5, y: 100), in: buffer, scale: 2, settings: settings()).isEmpty)
        #expect(MeasureReading.lines(at: CGPoint(x: 156, y: 250), in: buffer, scale: 2, settings: settings()).isEmpty)
    }

    @Test func aSpanOfAPointOrLessIsARuleNotAGap() {
        // Two pixels apart on a Retina capture: one point.
        let capture = Fixture.capture(width: 100, height: 40, scale: 2) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 10, y: 0, width: 40, height: 40))
            $0.fill(CGRect(x: 52, y: 0, width: 40, height: 40))
        }
        let buffer = LuminanceBuffer(image: capture.image)!
        #expect(MeasureReading.lines(at: CGPoint(x: 50, y: 20), in: buffer, scale: 2, settings: settings()).isEmpty)
    }

    @Test func lengthsReadInPointsToTheFinestStepTheCaptureShows() {
        #expect(MeasureReading.label(forPixels: 33, scale: 2) == "16.5 pt")
        #expect(MeasureReading.label(forPixels: 16, scale: 1) == "16 pt")
        #expect(MeasureReading.label(forPixels: 33, scale: 1) == "33 pt")
        #expect(MeasureReading.label(forPixels: 1200, scale: 2) == "600 pt")
    }

    @Test func theEdgeContrastStepsInWholePercentsWithinItsRange() {
        var settings = MeasureSettings.defaults
        for _ in 0..<6 { settings.stepContrast(up: false, coarse: false) }
        #expect(settings.contrastLabel == "2%")
        settings.stepContrast(up: false, coarse: true)
        #expect(settings.edgeContrast == MeasureSettings.contrastRange.lowerBound)
        for _ in 0..<30 { settings.stepContrast(up: true, coarse: true) }
        #expect(settings.edgeContrast == MeasureSettings.contrastRange.upperBound)
    }

    @Test func theSettingsSaveAndEachBadValueFallsBackAlone() throws {
        let settings = MeasureSettings(across: false, down: true, edgeContrast: 0.03, guideSeen: true)
        #expect(try JSONDecoder().decode(MeasureSettings.self, from: JSONEncoder().encode(settings)) == settings)
        let bad = try JSONDecoder().decode(MeasureSettings.self, from: Data(#"{"down": true, "edgeContrast": 4, "across": "yes"}"#.utf8))
        #expect(bad == MeasureSettings(across: true, down: true, edgeContrast: 0.08, guideSeen: false))
    }
}
