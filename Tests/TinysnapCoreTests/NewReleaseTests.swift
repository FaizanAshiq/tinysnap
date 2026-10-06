import Foundation
import Testing
@testable import TinysnapCore

/// The Mac copy is installed by Homebrew, which updates it, so it only tells of a newer release:
/// the version in GitHub's answer for the latest one, when it is newer than this copy.
struct NewReleaseTests {
    private func latest(_ tag: String) -> Data { Data(#"{"tag_name": "\#(tag)", "name": "Tinysnap"}"#.utf8) }

    @Test func aNewerReleaseIsToldWithoutItsV() {
        #expect(NewRelease.version(in: latest("v1.5.0"), newerThan: "1.4.5") == "1.5.0")
    }

    @Test func theSameOrAnOlderReleaseIsNot() {
        #expect(NewRelease.version(in: latest("v1.4.5"), newerThan: "1.4.5") == nil)
        #expect(NewRelease.version(in: latest("v1.4.4"), newerThan: "1.4.5") == nil)
        #expect(NewRelease.version(in: latest("v1.5"), newerThan: "1.5.0") == nil)
    }

    /// Compared number by number, so 1.10 comes after 1.9, as text it would not.
    @Test func versionsCompareAsNumbers() {
        #expect(NewRelease.version(in: latest("v1.10.0"), newerThan: "1.9.2") == "1.10.0")
        #expect(NewRelease.version(in: latest("v2.0.0"), newerThan: "1.99.99") == "2.0.0")
    }

    @Test func anAnswerThatCannotBeReadTellsNothing() {
        #expect(NewRelease.version(in: Data("rate limited".utf8), newerThan: "1.4.5") == nil)
        #expect(NewRelease.version(in: Data(#"{"message": "Not Found"}"#.utf8), newerThan: "1.4.5") == nil)
        #expect(NewRelease.version(in: latest("nightly"), newerThan: "1.4.5") == nil)
    }
}
