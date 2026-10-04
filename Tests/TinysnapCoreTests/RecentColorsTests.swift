import Foundation
import Testing
@testable import TinysnapCore

/// Custom colours picked lately come back as swatches beside the eight fixed ones, so a
/// brand colour is one click away the next time.
struct RecentColorsTests {
    @Test func aCustomColourGoesFirstAndAFixedOneIsLeftOut() {
        #expect(Palette.recent(adding: "#123ABC", to: []) == ["#123ABC"])
        #expect(Palette.recent(adding: "#FF3B30", to: ["#123ABC"]) == ["#123ABC"])
    }

    @Test func aColourPickedAgainMovesToTheFrontOnce() {
        #expect(Palette.recent(adding: "#123abc", to: ["#00FF00", "#123ABC"]) == ["#123ABC", "#00FF00"])
    }

    @Test func onlyTheLastFiveAreKept() {
        let five = ["#000001", "#000002", "#000003", "#000004", "#000005"]
        #expect(Palette.recent(adding: "#000006", to: five) == ["#000006", "#000001", "#000002", "#000003", "#000004"])
    }

    @Test func theyAreSavedAndAFileFromBeforeHasNone() throws {
        var preferences = Preferences.defaults
        preferences.recentColors = ["#123ABC"]
        let decoded = try JSONDecoder().decode(Preferences.self, from: JSONEncoder().encode(preferences))
        #expect(decoded.recentColors == ["#123ABC"])
        let old = Data(##"{"colorHex":"#FF3B30"}"##.utf8)
        #expect(try JSONDecoder().decode(Preferences.self, from: old).recentColors == [])
    }
}
