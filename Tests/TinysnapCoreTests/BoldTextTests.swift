import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// Bold text: drawn, measured and typed in the bold face, so its box fits it.
struct BoldTextTests {
    private func note(bold: Bool) -> Annotation {
        Annotation(kind: .text(origin: CGPoint(x: 10, y: 10), string: "Weekly summary"),
                   style: Style(colorHex: "#000000", size: .large, bold: bold))
    }

    @Test func boldTextIsWiderAndItsBoundsFitIt() {
        #expect(note(bold: true).bounds(scale: 1).width > note(bold: false).bounds(scale: 1).width + 3)
    }

    @Test func boldTextDrawsHeavierLetters() throws {
        func ink(_ bold: Bool) throws -> Int {
            let image = try #require(Renderer.render(Document(capture: Fixture.capture(width: 300, height: 60),
                                                              annotations: [note(bold: bold)])))
            let pixels = try #require(PixelBuffer(image: image))
            return (0..<image.width).reduce(0) { sum, x in
                sum + (0..<image.height).filter { pixels.pixel(x: x, y: $0).r < 100 }.count
            }
        }
        #expect(try ink(true) > ink(false) * 5 / 4)
    }

    @Test func boldIsSavedAndAStyleFromBeforeReadsRegular() throws {
        let style = Style(colorHex: Palette.red, bold: true)
        #expect(try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(style)) == style)
        let old = Data(##"{"colorHex":"#FF3B30","size":"medium"}"##.utf8)
        #expect(try JSONDecoder().decode(Style.self, from: old).bold == false)
    }

    @Test func onlyTextHasBold() {
        #expect(Tool.text.hasBold)
        #expect(!Tool.arrow.hasBold && !Tool.step.hasBold)
    }
}
