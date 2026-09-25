import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

struct BackdropTests {
    @Test func softeningAWallpaperKeepsItsSizeAndBlursItsEdges() throws {
        // Black on the left, white on the right: softened, the seam turns grey.
        let image = Fixture.capture(width: 200, height: 100) { context in
            context.setFillColor(Fixture.black)
            context.fill(CGRect(x: 0, y: 0, width: 100, height: 100))
        }.image
        let soft = try #require(Backdrop.soften(image))
        #expect(soft.width == 200 && soft.height == 100)
        let seam = Fixture.pixel(soft, 100, 50)
        #expect(seam.r > 40 && seam.r < 215)
        #expect(Fixture.pixel(soft, 5, 50).r < 40)
    }

    private func framed(_ document: Document, scale: ExportScale = .native) throws -> CGImage {
        try #require(Exporter.export(document, scale: scale)).image
    }

    private func alpha(_ image: CGImage, _ x: Int, _ y: Int) -> Int {
        Int(PixelBuffer(image: image)!.pixel(x: x, y: y).a)
    }

    private func document(fill: Backdrop.Fill, shadow: Backdrop.Shadow = .none, corners: CornerSize = .square,
                          capture: Capture = Fixture.capture(width: 200, height: 100, fill: Fixture.blue)) -> Document {
        Document(capture: capture, backdrop: Backdrop(fill: fill, colorHex: "#34C759", padding: .small, corners: corners, shadow: shadow))
    }

    @Test func theExportIsTheFramedOutputAtEitherScaleAndWithACrop() throws {
        var document = Document(capture: Fixture.capture(width: 1200, height: 800, scale: 2), backdrop: .defaults)
        var image = try framed(document)
        #expect(image.width == 1392 && image.height == 992)
        image = try framed(document, scale: .oneX)
        #expect(image.width == 696 && image.height == 496)
        document.crop = CGRect(x: 0, y: 0, width: 600, height: 160)
        image = try framed(document)
        #expect(image.width == 792 && image.height == 352)
    }

    @Test func aSolidFillPaintsThePaddingAndAClearOneLeavesItSeeThrough() throws {
        let solid = try framed(document(fill: .solid))
        #expect(Fixture.isClose(Fixture.pixel(solid, 2, 2), (52, 199, 89)))
        // Small padding is 24 pixels at 1x: the capture starts there.
        #expect(Fixture.isClose(Fixture.pixel(solid, 30, 30), (0, 0, 255)))

        let clear = try framed(document(fill: .clear))
        #expect(alpha(clear, 2, 2) == 0)
        #expect(alpha(clear, 30, 30) == 255)
    }

    @Test func aChangedOutputOverTheLastFrameMatchesAFreshFrameInAFractionOfTheTime() throws {
        // The canvas frames on every change while a shape is drawn or moved, and working
        // out the shadow each time made drawing on a full screen capture lag.
        var document = Document(capture: Fixture.capture(width: 3024, height: 1964, scale: 2), backdrop: .defaults)
        let last = try #require(Renderer.renderFramed(document))
        document.annotations.append(Fixture.annotation(.rectangle(CGRect(x: 100, y: 100, width: 400, height: 300))))
        let freshImage = try #require(Renderer.renderFramed(document))
        let overImage = try #require(Renderer.reframe(document, over: last))
        let fresh = try #require(PixelBuffer(image: freshImage)), over = try #require(PixelBuffer(image: overImage))
        // Padding, the new box's stroke, the capture, and the shadow under it.
        for (x, y) in [(10, 10), (196, 300), (1700, 1100), (3100, 2100)] {
            let a = over.pixel(x: x, y: y), b = fresh.pixel(x: x, y: y)
            #expect(Fixture.isClose((Int(a.r), Int(a.g), Int(a.b)), (Int(b.r), Int(b.g), Int(b.b)), within: 2))
        }
        func fastest(_ work: () -> Void) -> Duration { (0..<3).map { _ in ContinuousClock().measure(work) }.min()! }
        #expect(fastest { _ = Renderer.reframe(document, over: last) } * 3 < fastest { _ = Renderer.renderFramed(document) })
    }

    @Test func anOutputThatChangedSizeIsNotDrawnOverTheLastFrame() throws {
        var document = Document(capture: Fixture.capture(width: 200, height: 100), backdrop: .defaults)
        let last = try #require(Renderer.renderFramed(document))
        document.crop = CGRect(x: 0, y: 0, width: 100, height: 50)
        #expect(Renderer.reframe(document, over: last) == nil)
    }

    @Test func theShadowDarkensThePaddingJustBelowTheCapture() throws {
        let plain = try framed(document(fill: .solid))
        let shaded = try framed(document(fill: .solid, shadow: .soft))
        // Two pixels under the capture's bottom edge, which sits at 24 plus 100.
        let under = (x: 124, y: 126)
        #expect(Fixture.isClose(Fixture.pixel(plain, under.x, under.y), (52, 199, 89)))
        #expect(Fixture.pixel(shaded, under.x, under.y).g < 170)
    }

    @Test func roundedCornersShowTheFillInTheCorner() throws {
        let image = try framed(document(fill: .solid, corners: .large))
        #expect(Fixture.isClose(Fixture.pixel(image, 25, 25), (52, 199, 89)))
        #expect(Fixture.isClose(Fixture.pixel(image, 124, 74), (0, 0, 255)))
    }

    @Test func theGradientRunsCornerToCornerInTheCapturesColours() throws {
        let capture = Fixture.capture(width: 200, height: 100, fill: Fixture.blue)
        let image = try framed(document(fill: .gradient, capture: capture))
        let (first, second) = (rgb(capture.gradientColors[0]), rgb(capture.gradientColors[1]))
        #expect(Fixture.isClose(Fixture.pixel(image, 0, 0), first, within: 8))
        #expect(Fixture.isClose(Fixture.pixel(image, image.width - 1, image.height - 1), second, within: 8))
    }

    @Test func aWallpaperFillsTheFrameAndWithoutOneTheGradientDoes() throws {
        var withWallpaper = document(fill: .wallpaper)
        withWallpaper.backdrop?.wallpaper = Backdrop.Wallpaper(image: PastedImage(Fixture.capture(width: 30, height: 20, fill: Fixture.black).image))
        #expect(Fixture.isClose(Fixture.pixel(try framed(withWallpaper), 2, 2), (0, 0, 0)))

        let without = document(fill: .wallpaper)
        let first = rgb(without.capture.gradientColors[0])
        #expect(Fixture.isClose(Fixture.pixel(try framed(without), 0, 0), first, within: 8))
    }

    private func rgb(_ color: CGColor) -> (Int, Int, Int) {
        let parts = color.converted(to: CGColorSpace(name: CGColorSpace.sRGB)!, intent: .defaultIntent, options: nil)?.components ?? [0, 0, 0]
        return (Int((parts[0] * 255).rounded()), Int((parts[1] * 255).rounded()), Int((parts[2] * 255).rounded()))
    }

    @Test func theGradientRunsFromTheCommonestColourToTheCommonestDifferentOne() {
        let pale = CGColor(srgbRed: 0.93, green: 0.94, blue: 0.96, alpha: 1)
        let blue = CGColor(srgbRed: 0.2, green: 0.4, blue: 0.9, alpha: 1)
        // Mostly pale page, a blue header along the top: pale wins, blue comes second.
        let capture = Fixture.capture(width: 300, height: 200, fill: pale) { context in
            context.setFillColor(blue)
            context.fill(CGRect(x: 0, y: 0, width: 300, height: 50))
        }
        let colors = capture.gradientColors.map(rgb)
        #expect(colors.count == 2)
        #expect(Fixture.isClose(colors[0], (237, 240, 245), within: 6))
        #expect(Fixture.isClose(colors[1], (51, 102, 230), within: 6))
    }

    @Test func aCaptureOfOneColourGetsAShadeOfItAsTheSecond() {
        let slate = CGColor(srgbRed: 0.2, green: 0.3, blue: 0.4, alpha: 1)
        let colors = Fixture.capture(width: 120, height: 80, fill: slate).gradientColors.map(rgb)
        #expect(Fixture.isClose(colors[0], (51, 77, 102), within: 4))
        #expect(colors[1].0 > colors[0].0 + 20 && colors[1].1 > colors[0].1 + 20 && colors[1].2 > colors[0].2 + 20)
    }

    @Test func theSettingsSaveAndEachBadValueFallsBackAlone() throws {
        let backdrop = Backdrop(fill: .solid, colorHex: "#34C759", padding: .large, corners: .square, shadow: .strong)
        let saved = try JSONEncoder().encode(backdrop)
        #expect(try JSONDecoder().decode(Backdrop.self, from: saved) == backdrop)

        let bad = try JSONDecoder().decode(Backdrop.self, from: Data(#"{"fill": "plaid", "padding": "huge", "colorHex": "green", "shadow": "strong"}"#.utf8))
        #expect(bad == Backdrop(fill: .gradient, colorHex: Backdrop.defaults.colorHex, padding: .medium, corners: .medium, shadow: .strong))
    }

    @Test func aWallpaperIsNeverWrittenIntoTheSettings() throws {
        var backdrop = Backdrop.defaults
        backdrop.fill = .wallpaper
        backdrop.wallpaper = Backdrop.Wallpaper(image: PastedImage(Fixture.capture(width: 4, height: 4).image))
        let saved = try JSONEncoder().encode(backdrop)
        #expect(try JSONDecoder().decode(Backdrop.self, from: saved).wallpaper == nil)
    }

    @Test func theFramedOutputIsTheOutputWithThePaddingOnEverySide() {
        var document = Document(capture: Fixture.capture(width: 1200, height: 800, scale: 2))
        #expect(document.framedRect == nil)
        document.backdrop = .defaults
        // Medium padding is 48 points, 96 pixels on a 2x capture.
        #expect(document.framedRect == CGRect(x: -96, y: -96, width: 1392, height: 992))
        document.crop = CGRect(x: 0, y: 0, width: 600, height: 160)
        #expect(document.framedRect == CGRect(x: -96, y: -96, width: 792, height: 352))
    }

    @Test func settingABackdropIsOneUndoableStep() {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 100, height: 100)))
        editor.setBackdrop(.defaults)
        #expect(editor.display.backdrop == .defaults)
        editor.undo()
        #expect(editor.display.backdrop == nil)
        editor.redo()
        #expect(editor.display.backdrop == .defaults)
    }

    @Test func aStreamOfColourChangesUndoesAsOne() {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 100, height: 100)))
        editor.setBackdrop(.defaults)
        for hex in ["#111111", "#222222", "#333333"] {
            var backdrop = Backdrop.defaults
            backdrop.fill = .solid
            backdrop.colorHex = hex
            editor.setBackdrop(backdrop, merging: true)
        }
        editor.undo()
        #expect(editor.display.backdrop == .defaults)
    }
}
