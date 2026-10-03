import CoreGraphics
import Testing
@testable import TinysnapCore

/// Past 100% the canvas draws what is in view at the screen's own resolution: shapes smooth,
/// the capture's pixels sharp squares, and every redaction as it is in the whole render.
struct CloseUpTests {
    /// One pixel wide black and white stripes, the finest detail a capture has.
    private let stripes = Fixture.capture(width: 80, height: 80) { context in
        context.setFillColor(Fixture.black)
        for x in stride(from: 0, to: 80, by: 2) { context.fill(CGRect(x: x, y: 0, width: 1, height: 80)) }
    }

    /// The largest difference in any channel of any pixel, the two the same size.
    private func difference(_ a: CGImage, _ b: CGImage) -> Int {
        let first = PixelBuffer(image: a)!, second = PixelBuffer(image: b)!
        var most = 0
        for y in 0..<a.height {
            for x in 0..<a.width {
                let p = first.pixel(x: x, y: y), q = second.pixel(x: x, y: y)
                most = max(most, abs(Int(p.r) - Int(q.r)), abs(Int(p.g) - Int(q.g)), abs(Int(p.b) - Int(q.b)))
            }
        }
        return most
    }

    /// The whole document drawn at `scale`, its pixels sharp, cut to `region`.
    private func whole(_ document: Document, at scale: CGFloat, cutTo region: CGRect) throws -> CGImage {
        let full = try #require(Renderer.render(document, outputScale: scale, sharpPixels: true))
        let cut = CGRect(x: (region.minX - document.extent.minX) * scale, y: (region.minY - document.extent.minY) * scale,
                         width: region.width * scale, height: region.height * scale)
        return try #require(full.cropping(to: cut))
    }

    @Test func upCloseEachCapturePixelIsASharpSquare() throws {
        let document = Document(capture: stripes)
        let close = try #require(Renderer.renderCloseUp(document, visible: CGRect(x: 0, y: 0, width: 20, height: 20),
                                                        outputScale: 4, framed: false))
        #expect(close.region == CGRect(x: 0, y: 0, width: 20, height: 20))
        // Black from 0 to 3, white from 4 to 7: no grey run between them.
        #expect(Fixture.isClose(Fixture.pixel(close.image, 3, 10), (0, 0, 0)))
        #expect(Fixture.isClose(Fixture.pixel(close.image, 4, 10), (255, 255, 255)))
    }

    @Test func upCloseAShapeIsDrawnAtTheScreensResolution() throws {
        // A diagonal edge drawn at 4x falls across each capture pixel, not along its sides.
        let line = Fixture.annotation(.line(from: CGPoint(x: 0, y: 0), to: CGPoint(x: 40, y: 30)))
        let document = Document(capture: Fixture.capture(width: 60, height: 60), annotations: [line])
        let visible = CGRect(x: 0, y: 0, width: 60, height: 60)
        let close = try #require(Renderer.renderCloseUp(document, visible: visible, outputScale: 4, framed: false))
        // Antialiasing shifts up to a tenth of a pixel with where a render starts; the 1x render
        // enlarged is off by nearly 200 along the edge.
        #expect(difference(close.image, try whole(document, at: 4, cutTo: visible)) <= 32)
    }

    @Test func aPixelateBoxHalfInViewIsDrawnAsItIsWhole() throws {
        // Counted from the box's own corner with the same grain, not from the edge of the view.
        let box = Fixture.annotation(.pixelate(CGRect(x: 10, y: 10, width: 50, height: 50)),
                                     style: Style(colorHex: Palette.red, size: .small, corners: .square))
        let document = Document(capture: stripes, annotations: [box])
        let close = try #require(Renderer.renderCloseUp(document, visible: CGRect(x: 33, y: 33, width: 40, height: 40),
                                                        outputScale: 2, framed: false))
        #expect(close.region.contains(CGRect(x: 10, y: 10, width: 50, height: 50)))
        #expect(difference(close.image, try whole(document, at: 2, cutTo: close.region)) <= 1)
    }

    @Test func aBlurBoxHalfInViewIsDrawnAsItIsWhole() throws {
        let box = Fixture.annotation(.blur(CGRect(x: 20, y: 20, width: 30, height: 30)),
                                     style: Style(colorHex: Palette.red, corners: .square))
        let document = Document(capture: stripes, annotations: [box])
        let close = try #require(Renderer.renderCloseUp(document, visible: CGRect(x: 40, y: 40, width: 30, height: 30),
                                                        outputScale: 2, framed: false))
        #expect(close.region.contains(CGRect(x: 20, y: 20, width: 30, height: 30)))
        #expect(difference(close.image, try whole(document, at: 2, cutTo: close.region)) <= 3)
    }

    @Test func theViewGrowsOnlyForWhatReadsBackAndReachesIntoIt() throws {
        let document = Document(capture: Fixture.capture(width: 400, height: 300), annotations: [
            Fixture.annotation(.magnifier(center: CGPoint(x: 100, y: 100), radius: 30, zoom: 2)),
            Fixture.annotation(.pixelate(CGRect(x: 300, y: 200, width: 50, height: 50))),
            Fixture.annotation(.rectangle(CGRect(x: 0, y: 0, width: 400, height: 300))),
        ])
        let region = try #require(Renderer.closeUpRegion(document, visible: CGRect(x: 110.4, y: 110.4, width: 50, height: 50),
                                                         framed: false))
        // The lens whole, the far pixelate box and the outline left alone.
        #expect(region == CGRect(x: 70, y: 70, width: 91, height: 91))
    }

    @Test func framedTheViewStaysOnTheOutput() throws {
        let backdrop = Backdrop(fill: .solid, colorHex: Palette.red, padding: .medium, corners: .square, shadow: .none)
        let document = Document(capture: Fixture.capture(width: 200, height: 100),
                                crop: CGRect(x: 50, y: 0, width: 100, height: 100), backdrop: backdrop)
        let region = try #require(Renderer.closeUpRegion(document, visible: CGRect(x: 0, y: 0, width: 120, height: 60), framed: true))
        #expect(region == CGRect(x: 50, y: 0, width: 70, height: 60))
    }

    @Test func aCloseUpTooBigToDrawIsLeftOut() {
        // A huge blur box half in view at the deepest zoom: the canvas shows its pixels instead.
        let document = Document(capture: Fixture.capture(width: 3000, height: 3000),
                                annotations: [Fixture.annotation(.blur(CGRect(x: 0, y: 0, width: 3000, height: 3000)))])
        #expect(Renderer.renderCloseUp(document, visible: CGRect(x: 10, y: 10, width: 40, height: 40), outputScale: 32,
                                       framed: false) == nil)
    }
}
