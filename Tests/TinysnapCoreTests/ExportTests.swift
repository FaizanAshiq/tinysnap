import CoreGraphics
import Foundation
import ImageIO
import Testing
@testable import TinysnapCore

struct ExportTests {
    @Test func anExportTakesInShapesDrawnPastTheCapture() throws {
        let box = Fixture.annotation(.rectangle(CGRect(x: -40, y: 10, width: 60, height: 20)))
        let document = Document(capture: Fixture.capture(width: 100, height: 60, scale: 2), annotations: [box])
        let exported = try #require(Exporter.export(document, scale: .native))
        #expect(exported.image.width == Int(document.extent.width))
        #expect(exported.image.height == Int(document.extent.height))
        #expect(document.extent.minX < 0)
    }

    @Test func aCropOnAGrownCanvasCutsFromTheRightPlace() throws {
        let box = Fixture.annotation(.rectangle(CGRect(x: -40, y: 10, width: 60, height: 20)))
        var document = Document(capture: Fixture.capture(width: 100, height: 60, fill: Fixture.blue), annotations: [box])
        document.crop = CGRect(x: 60, y: 30, width: 20, height: 20)
        let exported = try #require(Exporter.export(document, scale: .native))
        #expect(exported.image.width == 20 && exported.image.height == 20)
        #expect(Fixture.isClose(Fixture.pixel(exported.image, 10, 10), (0, 0, 255)))
    }

    @Test func nativeExportKeepsEveryPixelAndTagsRetinaDPI() throws {
        let document = Document(capture: Fixture.capture(width: 800, height: 600, scale: 2))
        let exported = try #require(Exporter.export(document, scale: .native))
        #expect(exported.image.width == 800)
        #expect(exported.dpi == 144)
        #expect(exported.pointSize == CGSize(width: 400, height: 300))

        let png = try #require(Exporter.pngData(exported))
        let source = try #require(CGImageSourceCreateWithData(png as CFData, nil))
        let properties = try #require(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])
        let dpi = try #require(properties[kCGImagePropertyDPIWidth] as? Double)
        #expect(abs(dpi - 144) < 0.5)
    }

    @Test func oneXExportHalvesARetinaCapture() throws {
        let document = Document(capture: Fixture.capture(width: 800, height: 600, scale: 2))
        let exported = try #require(Exporter.export(document, scale: .oneX))
        #expect(exported.image.width == 400)
        #expect(exported.image.height == 300)
        #expect(exported.dpi == 72)
    }

    @Test func aStandardDisplayCaptureIsTheSameAtEitherScale() throws {
        let document = Document(capture: Fixture.capture(width: 300, height: 200, scale: 1))
        #expect(try #require(Exporter.export(document, scale: .oneX)).image.width == 300)
        #expect(try #require(Exporter.export(document, scale: .native)).dpi == 72)
    }

    @Test func exportUsesTheCropAndCutsOffWhatIsOutsideIt() throws {
        var document = Document(capture: Fixture.capture(width: 400, height: 300))
        document.crop = CGRect(x: 100, y: 100, width: 200, height: 100)
        document.annotations = [Fixture.annotation(.rectangle(CGRect(x: 0, y: 0, width: 50, height: 50)),
                                                   style: Style(colorHex: "#000000", filled: true))]
        let exported = try #require(Exporter.export(document, scale: .native))
        #expect(exported.image.width == 200 && exported.image.height == 100)
        #expect(Fixture.isClose(Fixture.pixel(exported.image, 0, 0), (255, 255, 255)))
    }

    @Test func exportMatchesTheCanvasAtTheCropEdge() throws {
        // A gradient, so reading back less of it than the canvas does changes the result.
        let capture = Fixture.capture(width: 400, height: 200) { context in
            for x in 0..<400 {
                let value = CGFloat(x) / 399
                context.setFillColor(CGColor(srgbRed: value, green: 1 - value, blue: 0.5, alpha: 1))
                context.fill(CGRect(x: x, y: 0, width: 1, height: 200))
            }
        }
        var document = Document(capture: capture, annotations: [
            Fixture.annotation(.erase(CGRect(x: 150, y: 50, width: 100, height: 100))),
            Fixture.annotation(.magnifier(center: CGPoint(x: 215, y: 100), radius: 30, zoom: 2)),
        ])
        document.crop = CGRect(x: 0, y: 0, width: 200, height: 200)
        let canvas = try #require(Renderer.render(document))
        let exported = try #require(Exporter.export(document, scale: .native)).image
        for x in [150, 170, 186, 190, 199] {
            #expect(Fixture.isClose(Fixture.pixel(exported, x, 100), Fixture.pixel(canvas, x, 100), within: 0))
        }
    }

    @Test func aOneXExportOfAnOddSizedRetinaCaptureHasNoHalfCoveredEdge() throws {
        let document = Document(capture: Fixture.capture(width: 801, height: 601, scale: 2))
        let exported = try #require(Exporter.export(document, scale: .oneX))
        #expect(exported.image.width == 400 && exported.image.height == 300)
        let buffer = try #require(PixelBuffer(image: exported.image))
        #expect(buffer.pixel(x: 399, y: 150).a == 255)
        #expect(buffer.pixel(x: 200, y: 299).a == 255)
        #expect(exported.pointSize == CGSize(width: 400, height: 300))
    }

    @Test func aOneXExportOfAnOddCropAtTheCaptureEdgeHasNoHalfCoveredEdge() throws {
        // Only a crop that reaches the capture's edge has nothing beyond it to fill the
        // last half pixel.
        var document = Document(capture: Fixture.capture(width: 800, height: 600, scale: 2))
        document.crop = CGRect(x: 1, y: 1, width: 799, height: 599)
        let exported = try #require(Exporter.export(document, scale: .oneX))
        let buffer = try #require(PixelBuffer(image: exported.image))
        #expect(buffer.pixel(x: exported.image.width - 1, y: 0).a == 255)
        #expect(buffer.pixel(x: 0, y: exported.image.height - 1).a == 255)
    }

    @Test func anExportIsDrawnAtTheCapturesOwnSize() throws {
        var document = Document(capture: Fixture.capture(width: 800, height: 600, scale: 2), resize: 2)
        // A size of its own wins over the setting.
        let doubled = try #require(Exporter.export(document, scale: .oneX))
        #expect(doubled.image.width == 1600 && doubled.image.height == 1200)
        document.resize = 0.5
        #expect(try #require(Exporter.export(document, scale: .native)).image.width == 400)
    }

    @Test func theWholeFrameIsDrawnAtTheSize() throws {
        var document = Document(capture: Fixture.capture(width: 800, height: 600, scale: 2), backdrop: .defaults)
        document.crop = CGRect(x: 100, y: 100, width: 200, height: 100)
        let full = try #require(Exporter.export(document, scale: .native)).image
        document.resize = 0.5
        let half = try #require(Exporter.export(document, scale: .native)).image
        #expect(abs(half.width - full.width / 2) <= 1 && abs(half.height - full.height / 2) <= 1)
    }

    @Test func aRetinaCaptureKeepsItsSizeInPointsDownToHalf() throws {
        var document = Document(capture: Fixture.capture(width: 1200, height: 800, scale: 2))
        for (resize, dpi) in [(2, 288), (1, 144), (0.75, 108), (0.5, 72), (0.25, 72)] as [(CGFloat, CGFloat)] {
            document.resize = resize
            #expect(try #require(Exporter.export(document, scale: .native)).dpi == dpi)
        }
        document.resize = 0.75
        #expect(try #require(Exporter.export(document, scale: .native)).pointSize == CGSize(width: 600, height: 400))
        // Below half it shows smaller: 300 pixels at 72 dpi.
        document.resize = 0.25
        #expect(try #require(Exporter.export(document, scale: .native)).pointSize == CGSize(width: 300, height: 200))
    }

    @Test func textIsReadAtFullSizeWhateverSizeTheCaptureExportsAt() throws {
        let document = Document(capture: Fixture.capture(width: 800, height: 600, scale: 2), resize: 0.25)
        let read = try #require(Exporter.exportForReading(document))
        #expect(read.image.width == 800 && read.dpi == 144)
    }

    @Test func namesFilesLikeMacOSAndCountsUpOnClashes() throws {
        let utc = try #require(TimeZone(identifier: "UTC"))
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = utc
        let date = try #require(calendar.date(from: DateComponents(year: 2026, month: 9, day: 25, hour: 9, minute: 41, second: 12)))

        #expect(FileNaming.fileName(for: date, timeZone: utc) { _ in false } == "Tinysnap 2026-09-25 at 09.41.12.png")
        let taken: Set = ["Tinysnap 2026-09-25 at 09.41.12.png", "Tinysnap 2026-09-25 at 09.41.12 2.png"]
        #expect(FileNaming.fileName(for: date, timeZone: utc) { taken.contains($0) } == "Tinysnap 2026-09-25 at 09.41.12 3.png")
    }
}
