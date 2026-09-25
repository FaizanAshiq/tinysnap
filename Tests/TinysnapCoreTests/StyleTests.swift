import Foundation
import Testing
@testable import TinysnapCore

struct StyleTests {
    @Test func anOverlaysOpacityAndDifferenceSurviveSaving() throws {
        let style = Style(colorHex: "#FF3B30", corners: .square, opacity: 0.5, difference: true)
        let saved = try JSONEncoder().encode(style)
        #expect(try JSONDecoder().decode(Style.self, from: saved) == style)
    }

    @Test func readsHexColours() {
        #expect(Palette.components(of: "#FF8000")?.red == 1)
        #expect(Palette.components(of: "00FF00")?.green == 1)
        #expect(Palette.components(of: "#FF80") == nil)
        #expect(Palette.components(of: "#GG0000") == nil)
        #expect(Palette.components(of: "+FFFFF") == nil)
    }

    @Test func writesHexBack() {
        #expect(Palette.hex(red: 1, green: 0.5, blue: 0) == "#FF8000")
    }

    @Test func findsToolsByLetterInEitherCase() {
        #expect(Tool.forKey("a") == .arrow)
        #expect(Tool.forKey("E") == .erase)
        #expect(Tool.forKey("z") == nil)
    }

    @Test func everyToolHasItsOwnLetter() {
        #expect(Set(Tool.allCases.map(\.key)).count == Tool.allCases.count)
    }

    @Test func sizesComeFromEachToolsTable() {
        #expect(Tool.arrow.points(for: .medium) == 4)
        #expect(Tool.arrow.points(for: .extraLarge) == 9)
        #expect(Tool.text.points(for: .large) == 28)
        #expect(Tool.crop.points(for: .medium) == nil)
    }

    @Test func thereAreFiveSizesAndStepsStopAtTheEnds() {
        #expect(StyleSize.allCases.count == 5)
        #expect(StyleSize.medium.thicker == .large)
        #expect(StyleSize.medium.thinner == .small)
        #expect(StyleSize.extraLarge.thicker == .extraLarge)
        #expect(StyleSize.extraSmall.thinner == .extraSmall)
    }

    @Test func eachBoxToolStartsWithItsOwnCorners() {
        #expect(Tool.rectangle.defaultStyle.corners == .medium)
        #expect(Tool.spotlight.defaultStyle.corners == .small)
        #expect(Tool.image.defaultStyle.corners == .square)
        #expect(Tool.rectangle.hasCorners && Tool.spotlight.hasCorners && Tool.blur.hasCorners)
        #expect(!Tool.erase.hasCorners && !Tool.arrow.hasCorners)
    }

    @Test func cornersSaveAndAnOldSquareSettingStillLoads() throws {
        let old = ##"{"colorHex": "#FF3B30", "sharpCorners": true}"##
        #expect(try JSONDecoder().decode(Style.self, from: Data(old.utf8)).corners == .square)
        let style = Style(colorHex: Palette.red, corners: .full)
        let decoded = try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(style))
        #expect(decoded == style)
    }

    @Test func aBadStyleValueFallsBackOnItsOwn() throws {
        // The overlay settings join the rest: missing or bad, each falls back alone.
        let old = try JSONDecoder().decode(Style.self, from: Data(##"{"colorHex": "#FF3B30"}"##.utf8))
        #expect(old.opacity == 1 && !old.difference)
        let bad = try JSONDecoder().decode(Style.self, from: Data(#"{"opacity": 7, "difference": "yes"}"#.utf8))
        #expect(bad.opacity == 1 && !bad.difference)
        let faint = try JSONDecoder().decode(Style.self, from: Data(#"{"opacity": 0.01}"#.utf8))
        #expect(faint.opacity == 0.1)

        let json = #"{"colorHex": "red", "size": "huge", "filled": true}"#
        let style = try JSONDecoder().decode(Style.self, from: Data(json.utf8))
        #expect(style == Style(colorHex: Palette.red, size: .medium, filled: true))
    }
}
