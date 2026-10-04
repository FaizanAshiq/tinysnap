import CoreGraphics
import Testing
@testable import TinysnapCore

/// Text on a box: filled text sits on a rounded box in its colour, with its letters in
/// black or white so they read on it, which keeps a label legible over a busy capture.
struct TextBoxTests {
    private let origin = CGPoint(x: 40, y: 30)

    private func note(filled: Bool, color: String = Palette.red) -> Annotation {
        Annotation(kind: .text(origin: origin, string: "Hello"), style: Style(colorHex: color, size: .large, filled: filled))
    }

    private func render(_ annotation: Annotation, typing: Annotation.ID? = nil) throws -> CGImage {
        let document = Document(capture: Fixture.capture(width: 240, height: 120, fill: Fixture.blue), annotations: [annotation])
        return try #require(Renderer.render(document, typing: typing))
    }

    /// Pixels inside the text's own box, where the letters are.
    private func letters(_ image: CGImage, _ annotation: Annotation) -> [(r: Int, g: Int, b: Int)] {
        let size = TextLayout.size(of: "Hello", points: annotation.pixelSize(scale: 1), scale: 1)
        return (Int(origin.x)..<Int(origin.x + size.width)).flatMap { x in
            (Int(origin.y)..<Int(origin.y + size.height)).map { Fixture.pixel(image, x, $0) }
        }
    }

    @Test func filledTextSitsOnABoxInItsColourWithLettersThatRead() throws {
        let filled = note(filled: true)
        let image = try render(filled)
        let box = filled.bounds(scale: 1)
        // The box reaches past the letters on every side.
        #expect(box.minX < origin.x - 3 && box.minY < origin.y)
        #expect(Fixture.isClose(Fixture.pixel(image, Int(box.minX) + 3, Int(box.midY)), (255, 59, 48)))
        #expect(letters(image, filled).filter { $0.r > 240 && $0.g > 240 && $0.b > 240 }.count > 30)
    }

    @Test func lettersOnALightBoxAreDark() throws {
        let yellow = note(filled: true, color: "#FFCC00")
        #expect(letters(try render(yellow), yellow).filter { $0.r + $0.g + $0.b < 150 }.count > 30)
    }

    @Test func plainTextHasNoBox() throws {
        let plain = note(filled: false)
        let image = try render(plain)
        #expect(plain.bounds(scale: 1).minX == origin.x)
        #expect(Fixture.isClose(Fixture.pixel(image, Int(origin.x) - 3, Int(origin.y) + 10), (0, 0, 255)))
    }

    /// While it is typed the text field shows the letters, so the render draws the box
    /// alone and the field's letters land on the same box the drawn text will have.
    @Test func theTextBeingTypedKeepsItsBoxAndLeavesItsLettersToTheField() throws {
        let filled = note(filled: true)
        let image = try render(filled, typing: filled.id)
        let pixels = letters(image, filled)
        #expect(pixels.allSatisfy { Fixture.isClose($0, (255, 59, 48)) })
    }

    /// Text set against the capture's left edge: its box reaches past it, so the canvas
    /// grows to take the box in rather than cutting it off.
    @Test func theBoxCountsWhenTheCanvasGrows() {
        func extent(filled: Bool) -> CGRect {
            let text = Annotation(kind: .text(origin: CGPoint(x: 0, y: 30), string: "Hello"),
                                  style: Style(colorHex: Palette.red, size: .large, filled: filled))
            return Document(capture: Fixture.capture(width: 240, height: 120), annotations: [text]).extent
        }
        #expect(extent(filled: false).minX == 0)
        #expect(extent(filled: true).minX < 0)
    }

    @Test func theTextFieldTypesInTheColourTheLettersAreDrawnIn() {
        #expect(TextLayout.letterColor(Style(colorHex: Palette.red)) == Palette.color(hex: Palette.red))
        #expect(TextLayout.letterColor(Style(colorHex: Palette.red, filled: true)) == CGColor(gray: 1, alpha: 1))
        #expect(TextLayout.letterColor(Style(colorHex: "#FFCC00", filled: true)) == CGColor(gray: 0, alpha: 1))
    }
}
