import CoreGraphics
import Testing
@testable import TinysnapCore

/// Boxes and ovals can be see-through, so a filled box tints what is under it instead of
/// hiding it.
struct SeeThroughTests {
    private func render(_ kind: Annotation.Kind, filled: Bool, opacity: CGFloat) throws -> CGImage {
        let shape = Fixture.annotation(kind, style: Style(colorHex: Palette.red, size: .extraLarge, filled: filled, opacity: opacity))
        return try #require(Renderer.render(Document(capture: Fixture.capture(width: 100, height: 100), annotations: [shape])))
    }

    @Test func aHalfSeeThroughFilledBoxTintsWhatIsUnderIt() throws {
        let image = try render(.rectangle(CGRect(x: 10, y: 10, width: 80, height: 80)), filled: true, opacity: 0.5)
        // Red over white, half and half.
        #expect(Fixture.isClose(Fixture.pixel(image, 50, 50), (255, 157, 152), within: 4))
    }

    @Test func aHalfSeeThroughOvalAndOutlineAreLighterToo() throws {
        let oval = try render(.oval(CGRect(x: 10, y: 10, width: 80, height: 80)), filled: true, opacity: 0.5)
        #expect(Fixture.isClose(Fixture.pixel(oval, 50, 50), (255, 157, 152), within: 4))
        let outline = try render(.rectangle(CGRect(x: 10, y: 10, width: 80, height: 80)), filled: false, opacity: 0.5)
        #expect(Fixture.isClose(Fixture.pixel(outline, 10, 50), (255, 157, 152), within: 4))
    }

    @Test func boxesOvalsAndPastedImagesHaveOpacity() {
        #expect(Tool.rectangle.hasOpacity && Tool.oval.hasOpacity && Tool.image.hasOpacity)
        #expect(!Tool.arrow.hasOpacity && !Tool.text.hasOpacity)
        // Only a pasted image is compared against the capture with the difference blend.
        #expect(Tool.image.hasOverlay && !Tool.rectangle.hasOverlay)
    }
}
