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
}
