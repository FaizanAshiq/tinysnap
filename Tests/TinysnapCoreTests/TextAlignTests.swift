import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// Text set left, centred or right: each line placed inside the box the widest line makes.
struct TextAlignTests {
    private let text = "A much wider line\nab"

    @Test func eachLineSitsLeftCentredOrRightInsideTheText() {
        let wide = TextLayout.size(of: "A much wider line", points: 20, scale: 1).width
        let short = TextLayout.size(of: "ab", points: 20, scale: 1).width
        #expect(TextLayout.lineOffsets(of: text, points: 20, align: .left) == [0, 0])
        let centred = TextLayout.lineOffsets(of: text, points: 20, align: .center)
        #expect(centred[0] == 0 && abs(centred[1] - (wide - short) / 2) < 0.01)
        let right = TextLayout.lineOffsets(of: text, points: 20, align: .right)
        #expect(right[0] == 0 && abs(right[1] - (wide - short)) < 0.01)
    }

    /// The leftmost dark pixel of the second line, drawn with `align`.
    private func secondLineStart(_ align: TextAlign) throws -> Int {
        let note = Annotation(kind: .text(origin: CGPoint(x: 10, y: 10), string: text),
                              style: Style(colorHex: "#000000", align: align))
        let image = try #require(Renderer.render(Document(capture: Fixture.capture(width: 400, height: 120), annotations: [note])))
        let box = TextLayout.size(of: text, points: note.pixelSize(scale: 1), scale: 1)
        let pixels = try #require(PixelBuffer(image: image))
        let rows = Int(10 + box.height * 0.55)..<Int(10 + box.height * 0.95)
        for x in 0..<image.width where rows.contains(where: { pixels.pixel(x: x, y: $0).r < 100 }) { return x }
        return image.width
    }

    @Test func theRendererDrawsEachAlignment() throws {
        let left = try secondLineStart(.left), centred = try secondLineStart(.center), right = try secondLineStart(.right)
        #expect(centred > left + 20)
        #expect(right > centred + 20)
    }

    @Test func alignmentIsSavedAndAStyleFromBeforeReadsLeft() throws {
        let style = Style(colorHex: Palette.red, align: .right)
        #expect(try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(style)) == style)
        let old = Data(##"{"colorHex":"#FF3B30","size":"medium"}"##.utf8)
        #expect(try JSONDecoder().decode(Style.self, from: old).align == .left)
    }
}
