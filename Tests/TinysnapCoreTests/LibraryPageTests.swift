import Foundation
import Testing
@testable import TinysnapCore

struct LibraryPageTests {
    /// Newest first, as the store lists them.
    private func entries(_ count: Int) -> [LibraryEntry] {
        (0..<count).map { LibraryEntry(folder: URL(fileURLWithPath: "/library/\($0)"), captured: Date(timeIntervalSince1970: Double(-$0))) }
    }

    @Test func fiftyCapturesAPageNewestFirst() {
        let all = entries(120)
        let first = LibraryPage(all, number: 0)
        #expect(first.entries == Array(all[0..<50]))
        #expect(first.count == 3)
        #expect(first.title == "Page 1 of 3")
        #expect(!first.hasPrevious && first.hasNext)
        let last = LibraryPage(all, number: 2)
        #expect(last.entries == Array(all[100...]))
        #expect(last.hasPrevious && !last.hasNext)
    }

    @Test func fiftyExactlyIsOnePage() {
        #expect(LibraryPage(entries(50), number: 0).count == 1)
        #expect(LibraryPage(entries(51), number: 0).count == 2)
    }

    /// Trashing the only capture on the last page shows the page before, not an empty one.
    @Test func aPageThatNoLongerExistsShowsTheLastOne() {
        let page = LibraryPage(entries(50), number: 1)
        #expect(page.number == 0)
        #expect(page.entries.count == 50)
    }

    @Test func anEmptyLibraryIsOneEmptyPage() {
        let page = LibraryPage([], number: 0)
        #expect(page.count == 1)
        #expect(page.entries.isEmpty)
        #expect(!page.hasPrevious && !page.hasNext)
    }
}
