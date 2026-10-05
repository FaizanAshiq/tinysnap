import CoreGraphics
import Testing
@testable import TinysnapCore

/// Redact all text: the reader's words, with their boxes, matched for emails, phone numbers,
/// numbers or everything, and each match covered by an erase box.
struct TextRedactionTests {
    /// A line of words 10 pixels apart, each 8 wide per letter, on row `y`.
    private func line(_ words: [String], y: CGFloat) -> TextLine {
        var x: CGFloat = 10
        return TextLine(words: words.map { text in
            defer { x += CGFloat(text.count) * 8 + 10 }
            return TextWord(text: text, box: CGRect(x: x, y: y, width: CGFloat(text.count) * 8, height: 16))
        })
    }

    private var page: [TextLine] {
        [line(["Email", "marcus.reyes@example.com"], y: 10),
         line(["Phone", "+1", "(555)", "014-2297"], y: 40),
         line(["Order", "#10482", "total", "$129.00"], y: 70),
         line(["Prefers", "delivery", "after", "5", "pm."], y: 100)]
    }

    @Test func anEmailIsCoveredAndNothingElse() {
        let boxes = TextRedaction.boxes(in: page, for: .emails)
        #expect(boxes == [page[0].words[1].box])
    }

    @Test func aPhoneNumberSplitOverWordsIsCoveredWhole() {
        let words = page[1].words
        #expect(TextRedaction.boxes(in: page, for: .phones) == [words[1].box.union(words[2].box).union(words[3].box)])
    }

    /// A phone number has 15 digits at most, so a 16 digit card number is not one, and Phones
    /// said it had erased two phone numbers on a page with one. Numbers still takes the card.
    @Test func aCardNumberIsNotAPhoneNumber() {
        let card = [line(["Card", "4242", "4242", "4242", "4242"], y: 130)]
        #expect(TextRedaction.boxes(in: card, for: .phones).isEmpty)
        #expect(TextRedaction.boxes(in: card, for: .numbers).count == 1)
        #expect(TextRedaction.boxes(in: [line(["Tel", "+44", "20", "7946", "0958"], y: 10)], for: .phones).count == 1)
    }

    @Test func numbersCoverRunsOfFourDigitsOrMore() {
        let boxes = TextRedaction.boxes(in: page, for: .numbers)
        // The phone, the order number and the total; not the lone 5.
        #expect(boxes.count == 3)
        #expect(boxes.contains(page[2].words[1].box))
        #expect(!boxes.contains { $0.intersects(page[3].words[3].box) })
    }

    @Test func allTextCoversEveryLine() {
        let boxes = TextRedaction.boxes(in: page, for: .allText)
        #expect(boxes.count == 4)
        #expect(boxes[3] == page[3].words.map(\.box).reduce(page[3].words[0].box) { $0.union($1) })
    }

    @Test func redactingAddsAnEraseBoxForEachMatchAsOneStep() {
        var session = EditorSession(document: Document(capture: Fixture.capture(width: 400, height: 200)), tool: .erase)
        let boxes = TextRedaction.boxes(in: page, for: .numbers)
        session.redact(boxes)
        #expect(session.display.annotations.count == 3)
        #expect(session.display.annotations.allSatisfy { $0.tool == .erase })
        // Each box reaches a little past its text, so no edge of a letter shows.
        if case let .erase(rect) = session.display.annotations[0].kind { #expect(rect.contains(boxes[0])) }
        session.undo()
        #expect(session.display.annotations.isEmpty)
    }

    /// A redaction is about the capture's own pixels, so its boxes go under the shapes already
    /// drawn. On top, a box drawn round the card number had its outline cut, and its side, running
    /// along the erase's edge, streaked red across the erased text.
    @Test func aRedactionBesideABoxNeitherCutsItNorTakesItsColour() throws {
        let rectangle = Fixture.annotation(.rectangle(CGRect(x: 52, y: 60, width: 120, height: 40)))
        var session = EditorSession(document: Document(capture: Fixture.capture(width: 400, height: 200), annotations: [rectangle]),
                                    tool: .erase)
        // Padded by 2, the erase's left edge lies along the box's left side.
        session.redact([CGRect(x: 54, y: 70, width: 200, height: 20)])
        #expect(session.display.annotations.last?.id == rectangle.id)
        let image = try #require(Renderer.render(session.display, outputScale: 1, sharpPixels: true))
        // Inside the erase, just clear of the box's left side: white, not pink.
        #expect(Fixture.isClose(Fixture.pixel(image, 62, 80), (255, 255, 255), within: 6))
        // The box's right side runs on across the erase.
        #expect(Fixture.pixel(image, 172, 80).g < 100)
    }

    /// The reader sees the capture under the erases, so a second pass finds the same text again.
    @Test func redactingAgainSkipsWhatIsAlreadyErased() {
        var session = EditorSession(document: Document(capture: Fixture.capture(width: 400, height: 200)), tool: .erase)
        #expect(session.redact(TextRedaction.boxes(in: page, for: .numbers)) == 3)
        // The phone number was one of the numbers.
        #expect(session.redact(TextRedaction.boxes(in: page, for: .phones)) == 0)
        #expect(session.display.annotations.count == 3)
        // A line reaches past any number on it, so every line is new.
        #expect(session.redact(TextRedaction.boxes(in: page, for: .allText)) == 4)
        #expect(session.display.annotations.count == 7)
    }
}
