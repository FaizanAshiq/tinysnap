import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// The spotlight can blur what is outside it instead of dimming it: the rest of the capture
/// stays in its own colours, just out of focus.
struct SpotlightBlurTests {
    /// Black and white columns two pixels wide, sharp everywhere.
    private let stripes = Fixture.capture(width: 200, height: 100) { context in
        context.setFillColor(Fixture.black)
        for x in stride(from: 0, to: 200, by: 4) { context.fill(CGRect(x: x, y: 0, width: 2, height: 100)) }
    }

    private func spot(blur: Bool) -> Annotation {
        Fixture.annotation(.spotlight(CGRect(x: 120, y: 20, width: 60, height: 60)),
                           style: Style(colorHex: Palette.red, corners: .square, blurOutside: blur))
    }

    @Test func outsideABlurringSpotlightGoesSoftAndInsideStaysSharp() throws {
        let image = try #require(Renderer.render(Document(capture: stripes, annotations: [spot(blur: true)])))
        // Outside: the columns run together into grey, neither black nor white.
        let outside = (20...60).map { Fixture.pixel(image, $0, 50) }
        #expect(outside.allSatisfy { $0.r > 60 && $0.r < 200 })
        // Inside: still black and white.
        #expect(Fixture.isClose(Fixture.pixel(image, 140, 50), (0, 0, 0)))
        #expect(Fixture.isClose(Fixture.pixel(image, 142, 50), (255, 255, 255)))
    }

    @Test func aDimmingSpotlightIsAsItWas() throws {
        let image = try #require(Renderer.render(Document(capture: stripes, annotations: [spot(blur: false)])))
        // White outside, at half: still a sharp column, only darker.
        #expect(Fixture.isClose(Fixture.pixel(image, 22, 50), (128, 128, 128), within: 3))
        #expect(Fixture.isClose(Fixture.pixel(image, 20, 50), (0, 0, 0)))
    }

    /// Past 100% the canvas draws what is in view again; the blur there matches the whole.
    @Test func aCloseUpOfTheBlurMatchesTheWhole() throws {
        let document = Document(capture: stripes, annotations: [spot(blur: true)])
        // Crisp, as the close-up draws the capture, so only the blur is compared.
        let whole = try #require(Renderer.render(document, outputScale: 2, sharpPixels: true))
        let close = try #require(Renderer.renderCloseUp(document, visible: CGRect(x: 20, y: 20, width: 40, height: 40),
                                                        outputScale: 2, framed: false))
        // A white column 3 pixels inside the view, where a blur cut off at the view would read
        // the edge instead of what lies past it.
        let x = Int((23 - close.region.minX) * 2), y = Int((40 - close.region.minY) * 2)
        let a = Fixture.pixel(close.image, x, y), b = Fixture.pixel(whole, 46, 80)
        #expect(abs(a.r - b.r) <= 3)
    }

    /// Every spotlight lights one shared area, so dimming or blurring one sets them all;
    /// otherwise picking Blur on any but the first did nothing.
    @Test func blurringOneSpotlightBlursThemAll() {
        let first = Fixture.annotation(.spotlight(CGRect(x: 10, y: 10, width: 30, height: 30)))
        let second = Fixture.annotation(.spotlight(CGRect(x: 100, y: 10, width: 30, height: 30)))
        var session = EditorSession(document: Document(capture: stripes, annotations: [first, second]), tool: .select)
        session.pointerDown(at: CGPoint(x: 115, y: 25), reach: 4)
        session.pointerUp()
        session.restyle { $0.blurOutside = true }
        #expect(session.display.annotations.allSatisfy { $0.style.blurOutside })
        session.undo()
        #expect(session.display.annotations.allSatisfy { !$0.style.blurOutside })
    }

    @Test func blurOutsideIsSavedAndAStyleFromBeforeDims() throws {
        let style = Style(colorHex: Palette.red, blurOutside: true)
        #expect(try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(style)) == style)
        let old = Data(##"{"colorHex":"#FF3B30","size":"medium"}"##.utf8)
        #expect(try JSONDecoder().decode(Style.self, from: old).blurOutside == false)
        #expect(Tool.spotlight.hasOutsideBlur && !Tool.blur.hasOutsideBlur)
    }
}
