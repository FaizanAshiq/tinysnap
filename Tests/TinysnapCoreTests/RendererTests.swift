import CoreGraphics
import Testing
@testable import TinysnapCore

struct RendererTests {
    @Test func aHalfSeeThroughOverlayLandsHalfwayBetweenItAndTheCapture() {
        let capture = Fixture.capture(width: 100, height: 100)
        let black = PastedImage(Fixture.capture(width: 10, height: 10, fill: Fixture.black).image)
        let overlay = Annotation(kind: .image(CGRect(x: 10, y: 10, width: 40, height: 40), black),
                                 style: Style(colorHex: Palette.red, corners: .square, opacity: 0.5))
        let image = render(capture, [overlay])
        #expect(Fixture.isClose(Fixture.pixel(image, 30, 30), (128, 128, 128), within: 4))
        #expect(Fixture.isClose(Fixture.pixel(image, 80, 80), (255, 255, 255)))
    }

    @Test func aDifferenceOverlayMatchingTheCaptureRendersBlack() {
        let capture = Fixture.capture(width: 100, height: 100, fill: Fixture.blue)
        let same = PastedImage(Fixture.capture(width: 10, height: 10, fill: Fixture.blue).image)
        let overlay = Annotation(kind: .image(CGRect(x: 10, y: 10, width: 40, height: 40), same),
                                 style: Style(colorHex: Palette.red, corners: .square, difference: true))
        let image = render(capture, [overlay])
        #expect(Fixture.isClose(Fixture.pixel(image, 30, 30), (0, 0, 0)))
        #expect(Fixture.isClose(Fixture.pixel(image, 80, 80), (0, 0, 255)))
    }

    @Test func aGrownCanvasTakesTheColourMostOfTheCaptureBorderHas() throws {
        let slate = CGColor(srgbRed: 0.2, green: 0.3, blue: 0.4, alpha: 1)
        // A white patch on the top edge, which the fill must not pick up.
        let capture = Fixture.capture(width: 100, height: 60, fill: slate) { context in
            context.setFillColor(Fixture.white)
            context.fill(CGRect(x: 0, y: 0, width: 20, height: 10))
        }
        let box = Fixture.annotation(.rectangle(CGRect(x: 80, y: 20, width: 60, height: 20)))
        let document = Document(capture: capture, annotations: [box])
        let image = try #require(Renderer.render(document))

        #expect(image.width == Int(document.extent.width) && image.height == Int(document.extent.height))
        // Past the capture's right edge and clear of the rectangle.
        #expect(Fixture.isClose(Fixture.pixel(image, image.width - 3, 3), (51, 77, 102)))
        #expect(Fixture.isClose(Fixture.pixel(image, image.width - 3, image.height - 3), (51, 77, 102)))
        // The capture itself is untouched, white patch and all.
        #expect(Fixture.isClose(Fixture.pixel(image, 5, 5), (255, 255, 255)))
        #expect(Fixture.isClose(Fixture.pixel(image, 50, 50), (51, 77, 102)))
    }

    private let white = (r: 255, g: 255, b: 255)

    private func render(_ capture: Capture, _ annotations: [Annotation], hiding hidden: Set<Annotation.ID> = []) -> CGImage {
        Renderer.render(Document(capture: capture, annotations: annotations), hiding: hidden)!
    }

    @Test func keepsTheCaptureTheRightWayUp() {
        let capture = Fixture.capture(width: 40, height: 40) {
            $0.setFillColor(Fixture.blue)
            $0.fill(CGRect(x: 0, y: 0, width: 40, height: 20))
        }
        let image = render(capture, [])
        #expect(Fixture.isClose(Fixture.pixel(image, 5, 5), (0, 0, 255)))
        #expect(Fixture.isClose(Fixture.pixel(image, 5, 35), white))
    }

    @Test func drawsARectangleWhereItIsStored() {
        let capture = Fixture.capture(width: 100, height: 100)
        let image = render(capture, [Fixture.annotation(.rectangle(CGRect(x: 10, y: 10, width: 50, height: 30)))])
        #expect(Fixture.isClose(Fixture.pixel(image, 10, 25), (255, 59, 48)))
        #expect(Fixture.isClose(Fixture.pixel(image, 35, 25), white))
        #expect(Fixture.isClose(Fixture.pixel(image, 80, 80), white))
    }

    @Test func aRectanglesCornersAreRoundedUnlessSetSquare() {
        let rect = CGRect(x: 20, y: 20, width: 60, height: 60)
        let rounded = render(Fixture.capture(width: 100, height: 100), [Fixture.annotation(.rectangle(rect))])
        let sharp = render(Fixture.capture(width: 100, height: 100),
                           [Fixture.annotation(.rectangle(rect), style: Style(colorHex: Palette.red, corners: .square))])
        #expect(Fixture.isClose(Fixture.pixel(rounded, 19, 19), white, within: 40))
        #expect(Fixture.isClose(Fixture.pixel(sharp, 19, 19), (255, 59, 48)))
        #expect(Fixture.isClose(Fixture.pixel(rounded, 19, 50), (255, 59, 48)))
    }

    @Test func aFullyRoundSpotlightOnASquareIsACircle() {
        let rect = CGRect(x: 20, y: 20, width: 60, height: 60)
        let square = render(Fixture.capture(width: 100, height: 100),
                            [Fixture.annotation(.spotlight(rect), style: Style(colorHex: Palette.red, corners: .square))])
        let circle = render(Fixture.capture(width: 100, height: 100),
                            [Fixture.annotation(.spotlight(rect), style: Style(colorHex: Palette.red, corners: .full))])
        #expect(Fixture.isClose(Fixture.pixel(square, 23, 23), white))
        #expect(Fixture.isClose(Fixture.pixel(circle, 23, 23), (128, 128, 128), within: 3))
        #expect(Fixture.isClose(Fixture.pixel(circle, 50, 50), white))
    }

    @Test func readsTheColourOfOnePixelAsHex() {
        let capture = Fixture.capture(width: 40, height: 40) {
            $0.setFillColor(Fixture.blue)
            $0.fill(CGRect(x: 0, y: 0, width: 40, height: 20))
        }
        #expect(ColorProbe.hex(of: capture.image, x: 5, y: 5) == "#0000FF")
        #expect(ColorProbe.hex(of: capture.image, x: 5, y: 35) == "#FFFFFF")
        #expect(ColorProbe.hex(of: capture.image, x: 50, y: 5) == nil)
    }

    @Test func eraseMatchesAFlatBackground() {
        let grey = CGColor(srgbRed: 0.5, green: 0.5, blue: 0.5, alpha: 1)
        let capture = Fixture.capture(width: 100, height: 100, fill: grey) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 40, y: 40, width: 20, height: 20))
        }
        let background = Fixture.pixel(capture.image, 5, 5)
        let image = render(capture, [Fixture.annotation(.erase(CGRect(x: 35, y: 35, width: 30, height: 30)))])
        #expect(Fixture.isClose(Fixture.pixel(image, 50, 50), background, within: 1))
        #expect(Fixture.isClose(Fixture.pixel(image, 36, 36), background, within: 1))
    }

    @Test func eraseAtTheEdgeBlendsTheSidesItHas() {
        let capture = Fixture.capture(width: 100, height: 100, fill: Fixture.green) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 0, y: 40, width: 20, height: 20))
        }
        let image = render(capture, [Fixture.annotation(.erase(CGRect(x: 0, y: 35, width: 30, height: 30)))])
        #expect(Fixture.isClose(Fixture.pixel(image, 2, 50), (0, 255, 0), within: 1))
    }

    @Test func eraseCarriesAGradientThrough() {
        let capture = Fixture.capture(width: 256, height: 60) { context in
            for x in 0..<256 {
                let value = CGFloat(x) / 255
                context.setFillColor(CGColor(srgbRed: value, green: value, blue: value, alpha: 1))
                context.fill(CGRect(x: x, y: 0, width: 1, height: 60))
            }
        }
        let image = render(capture, [Fixture.annotation(.erase(CGRect(x: 108, y: 10, width: 40, height: 40)))])
        #expect(abs(Fixture.pixel(image, 128, 30).r - 128) <= 3)
    }

    @Test func aRedactionHidesWhatIsBelowAndLeavesWhatIsAbove() {
        let capture = Fixture.capture(width: 200, height: 100)
        let square = Fixture.annotation(.rectangle(CGRect(x: 20, y: 20, width: 40, height: 40)),
                                        style: Style(colorHex: "#000000", filled: true))
        let erase = Fixture.annotation(.erase(CGRect(x: 10, y: 10, width: 80, height: 80)))
        let line = Fixture.annotation(.line(from: CGPoint(x: 0, y: 40), to: CGPoint(x: 200, y: 40)),
                                      style: Style(colorHex: "#00FF00", size: .large))
        let image = render(capture, [square, erase, line])
        #expect(Fixture.isClose(Fixture.pixel(image, 30, 30), white))
        #expect(Fixture.isClose(Fixture.pixel(image, 30, 40), (0, 255, 0)))
    }

    @Test func blurChangesThePixelsInItsBox() {
        let capture = Fixture.capture(width: 100, height: 100) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 0, y: 0, width: 50, height: 100))
        }
        let image = render(capture, [Fixture.annotation(.blur(CGRect(x: 30, y: 30, width: 40, height: 40)))])
        let edge = Fixture.pixel(image, 50, 50)
        #expect(edge.r > 40 && edge.r < 215)
        #expect(Fixture.isClose(Fixture.pixel(image, 90, 90), white))
    }

    @Test func pixelateTurnsFineDetailIntoFlatBlocks() {
        let capture = Fixture.capture(width: 64, height: 64) { context in
            context.setFillColor(Fixture.black)
            for x in stride(from: 0, to: 64, by: 2) { context.fill(CGRect(x: x, y: 0, width: 1, height: 64)) }
        }
        let image = render(capture, [Fixture.annotation(.pixelate(CGRect(x: 0, y: 0, width: 64, height: 64)),
                                                        style: Style(colorHex: Palette.red, size: .small))])
        // Inside one block, away from the box's rounded corner.
        let a = Fixture.pixel(image, 9, 9)
        let b = Fixture.pixel(image, 14, 14)
        #expect(abs(a.r - 128) <= 40)
        #expect(abs(a.r - b.r) <= 25)
    }

    @Test func redactionsPastTheEdgeOfTheCaptureDoNotCrash() {
        let capture = Fixture.capture(width: 50, height: 50)
        let annotations = [
            Fixture.annotation(.blur(CGRect(x: -20, y: -20, width: 40, height: 40))),
            Fixture.annotation(.pixelate(CGRect(x: 30, y: 30, width: 100, height: 100))),
            Fixture.annotation(.erase(CGRect(x: -10, y: 45, width: 100, height: 100))),
            Fixture.annotation(.erase(CGRect(x: 60, y: 60, width: 10, height: 10))),
            Fixture.annotation(.magnifier(center: .zero, radius: 30, zoom: 2)),
        ]
        let image = render(capture, annotations)
        // The canvas grows to take them all in.
        let extent = Document(capture: capture, annotations: annotations).extent
        #expect(image.width == Int(extent.width) && image.height == Int(extent.height))
    }

    @Test func theMagnifierEnlargesWhatIsUnderIt() {
        let capture = Fixture.capture(width: 200, height: 200) {
            $0.setFillColor(Fixture.black)
            $0.fill(CGRect(x: 98, y: 0, width: 4, height: 200))
        }
        #expect(Fixture.isClose(Fixture.pixel(capture.image, 103, 100), white))
        let image = render(capture, [Fixture.annotation(.magnifier(center: CGPoint(x: 100, y: 100), radius: 50, zoom: 2))])
        #expect(Fixture.isClose(Fixture.pixel(image, 103, 100), (0, 0, 0)))
        #expect(Fixture.isClose(Fixture.pixel(image, 107, 100), white))
    }

    @Test func aSpotlightDimsOnlyOutsideItsBox() {
        let image = render(Fixture.capture(width: 100, height: 100),
                           [Fixture.annotation(.spotlight(CGRect(x: 20, y: 20, width: 40, height: 40)))])
        #expect(Fixture.isClose(Fixture.pixel(image, 40, 40), white))
        #expect(Fixture.isClose(Fixture.pixel(image, 80, 80), (128, 128, 128), within: 3))
    }

    @Test func overlappingSpotlightsLightOneSharedArea() {
        let image = render(Fixture.capture(width: 100, height: 100), [
            Fixture.annotation(.spotlight(CGRect(x: 10, y: 10, width: 40, height: 40))),
            Fixture.annotation(.spotlight(CGRect(x: 30, y: 30, width: 40, height: 40))),
        ])
        #expect(Fixture.isClose(Fixture.pixel(image, 35, 35), white))
        #expect(Fixture.isClose(Fixture.pixel(image, 65, 65), white))
        #expect(Fixture.isClose(Fixture.pixel(image, 90, 10), (128, 128, 128), within: 3))
    }

    @Test func textIsDrawnUprightInsideItsBounds() {
        let text = Fixture.annotation(.text(origin: CGPoint(x: 10, y: 10), string: "Hello"))
        let image = render(Fixture.capture(width: 300, height: 100), [text])
        let bounds = text.bounds(scale: 1)
        let buffer = PixelBuffer(image: image)!
        var inside = 0
        var outside = 0
        for y in 0..<100 {
            for x in 0..<300 where buffer.pixel(x: x, y: y).g < 200 {
                if bounds.insetBy(dx: -1, dy: -1).contains(CGPoint(x: x, y: y)) { inside += 1 } else { outside += 1 }
            }
        }
        #expect(inside > 20)
        #expect(outside == 0)
    }

    @Test func aStepIsAFilledCircleInItsColour() {
        let step = Fixture.annotation(.step(center: CGPoint(x: 50, y: 50)))
        let image = render(Fixture.capture(width: 100, height: 100), [step])
        #expect(Fixture.isClose(Fixture.pixel(image, 50 - 13, 50), (255, 59, 48)))
        #expect(Fixture.isClose(Fixture.pixel(image, 50 - 20, 50), white))
    }

    @Test func hiddenAnnotationsAreLeftOut() {
        let box = Fixture.annotation(.rectangle(CGRect(x: 0, y: 0, width: 50, height: 50)),
                                     style: Style(colorHex: "#000000", filled: true))
        let image = render(Fixture.capture(width: 100, height: 100), [box], hiding: [box.id])
        #expect(Fixture.isClose(Fixture.pixel(image, 25, 25), white))
    }

    @Test func aFullScreenFiveKCaptureRendersWithRedactions() {
        let capture = Fixture.capture(width: 6016, height: 3384, scale: 2)
        let image = render(capture, [
            Fixture.annotation(.blur(CGRect(x: 1000, y: 1000, width: 2000, height: 1000))),
            Fixture.annotation(.pixelate(CGRect(x: 3000, y: 500, width: 1000, height: 1000))),
            Fixture.annotation(.erase(CGRect(x: 100, y: 100, width: 500, height: 500))),
        ])
        #expect(image.width == 6016 && image.height == 3384)
    }
}
