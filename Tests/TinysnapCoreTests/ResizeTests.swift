import CoreGraphics
import Testing
@testable import TinysnapCore

struct ResizeTests {
    /// A 2x capture as it is, grown past its left edge, cropped on odd pixels, and cropped
    /// inside a backdrop: every sum the renderer rounds.
    private func documents() -> [Document] {
        let capture = Fixture.capture(width: 301, height: 201, scale: 2)
        let past = Fixture.annotation(.rectangle(CGRect(x: -40, y: 10, width: 60, height: 20)))
        let crop = CGRect(x: 33.5, y: 17.25, width: 200.5, height: 101)
        return [
            Document(capture: capture),
            Document(capture: capture, annotations: [past]),
            Document(capture: capture, crop: crop),
            Document(capture: capture, crop: crop, backdrop: .defaults),
        ]
    }

    @Test func theSizeShownIsExactlyTheSizeAnExportMakes() throws {
        for document in documents() {
            for resize in [0.01, 0.25, 0.37, 0.5, 0.583, 1, 1.5, 2, 3.3] as [CGFloat] {
                var sized = document
                sized.resize = resize
                let image = try #require(Exporter.export(sized, scale: .native)).image
                #expect(document.exportPixelSize(at: resize) == CGSize(width: image.width, height: image.height))
            }
        }
    }

    @Test func aTypedWidthOrHeightGivesExactlyThatSize() {
        for document in documents().dropLast() {
            // All within 1% to 400% of the smallest of them, a crop 101 pixels high.
            for pixels in stride(from: 5, through: 380, by: 7) {
                #expect(document.exportPixelSize(at: document.resize(forWidth: pixels)).width == CGFloat(pixels))
                #expect(document.exportPixelSize(at: document.resize(forHeight: pixels)).height == CGFloat(pixels))
            }
        }
    }

    @Test func aTypedWidthKeepsTheAspectRatio() {
        let document = Document(capture: Fixture.capture(width: 1200, height: 800, scale: 2))
        #expect(document.exportPixelSize(at: document.resize(forWidth: 700)) == CGSize(width: 700, height: 466))
    }

    @Test func dividingThePixelsWantedByTheFullSizeCanLandAPixelShort() {
        // Why the fields search rather than divide: on the odd crop, some widths come out
        // one short of what was typed.
        let cropped = documents()[2]
        let full = cropped.exportPixelSize(at: 1).width
        let short = (1...400).filter { cropped.exportPixelSize(at: CGFloat($0) / full).width != CGFloat($0) }
        #expect(!short.isEmpty)
    }

    @Test func withABackdropATypedWidthLandsWithinAPixel() {
        // The padding grows on both sides at once, so the width moves two pixels at a time there.
        let framed = documents()[3]
        for pixels in 250...320 {
            let width = framed.exportPixelSize(at: framed.resize(forWidth: pixels)).width
            #expect(width >= CGFloat(pixels) && width <= CGFloat(pixels + 1))
        }
    }

    @Test func theSizeIsHeldBetweenOnePercentAndFourHundred() {
        let document = Document(capture: Fixture.capture(width: 300, height: 200))
        #expect(document.clampedResize(0.001) == 0.01)
        #expect(document.clampedResize(9) == 4)
        #expect(document.resize(forWidth: 5000) == 4)
        #expect(document.resize(forWidth: 0) == 0.01)
    }

    @Test func noExportIsLongerThanSixteenThousandPixels() {
        // 5000 pixels across at 400% would be 20,000.
        let wide = Document(capture: Fixture.capture(width: 5000, height: 10))
        #expect(wide.largestResize < 4)
        #expect(wide.exportPixelSize(at: wide.largestResize).width <= 16_384)
        #expect(wide.exportPixelSize(at: wide.largestResize + 0.001).width > 16_384)
        #expect(wide.clampedResize(4) == wide.largestResize)
    }

    @Test func aNewSizeIsOneUndoableStepHeldToTheLimits() {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 300, height: 200)))
        editor.setResize(2)
        editor.setResize(9)
        #expect(editor.display.resize == 4)
        editor.undo()
        #expect(editor.display.resize == 2)
        editor.undo()
        #expect(editor.display.resize == nil)
    }

    @Test func withNoSizeOfItsOwnTheSettingIsTheStartingSize() {
        var document = Document(capture: Fixture.capture(width: 800, height: 600, scale: 2))
        #expect(Exporter.outputScale(of: document, setting: .native) == 1)
        #expect(Exporter.outputScale(of: document, setting: .oneX) == 0.5)
        document.resize = 2
        #expect(Exporter.outputScale(of: document, setting: .oneX) == 2)
    }
}
