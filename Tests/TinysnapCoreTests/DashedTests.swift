import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// Dashed lines, boxes and ovals: the stroke breaks into dashes with the capture showing
/// between them.
struct DashedTests {
    private func style(_ dashed: Bool) -> Style { Style(colorHex: Palette.red, size: .extraLarge, dashed: dashed) }

    /// How many of the pixels along y = 50, from x = 20 to 180, show the capture's white.
    private func gaps(_ kind: Annotation.Kind, dashed: Bool) throws -> Int {
        let image = try #require(Renderer.render(Document(capture: Fixture.capture(width: 200, height: 100),
                                                          annotations: [Fixture.annotation(kind, style: style(dashed))])))
        return (20...180).filter { Fixture.isClose(Fixture.pixel(image, $0, 50), (255, 255, 255), within: 40) }.count
    }

    @Test func aDashedLineBreaksAndASolidOneDoesNot() throws {
        let line = Annotation.Kind.line(from: CGPoint(x: 10, y: 50), to: CGPoint(x: 190, y: 50))
        #expect(try gaps(line, dashed: false) == 0)
        #expect(try gaps(line, dashed: true) > 30)
    }

    @Test func aDashedBoxEdgeBreaks() throws {
        // The box's bottom edge runs along y = 50.
        let box = Annotation.Kind.rectangle(CGRect(x: 10, y: 10, width: 180, height: 40))
        #expect(try gaps(box, dashed: false) == 0)
        #expect(try gaps(box, dashed: true) > 30)
    }

    @Test func dashedIsSavedAndAStyleFromBeforeReadsSolid() throws {
        let dashed = style(true)
        #expect(try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(dashed)) == dashed)
        let old = Data(##"{"colorHex":"#FF3B30","size":"medium"}"##.utf8)
        #expect(try JSONDecoder().decode(Style.self, from: old).dashed == false)
    }

    @Test func linesBoxesAndOvalsCanBeDashed() {
        #expect(Tool.line.hasDash && Tool.rectangle.hasDash && Tool.oval.hasDash)
        #expect(!Tool.arrow.hasDash && !Tool.text.hasDash && !Tool.highlighter.hasDash)
    }
}
