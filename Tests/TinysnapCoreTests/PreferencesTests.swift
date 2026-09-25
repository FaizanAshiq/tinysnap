import Foundation
import Testing
@testable import TinysnapCore

struct PreferencesTests {
    private func temporaryFile(containing json: String? = nil) throws -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("tinysnap-tests-\(UUID().uuidString).json")
        if let json { try Data(json.utf8).write(to: url) }
        return url
    }

    @Test func aMissingFileGivesDefaults() throws {
        #expect(try Preferences.load(from: temporaryFile()) == .defaults)
    }

    @Test func aPartialFileKeepsTheRestAtDefaults() throws {
        let preferences = try Preferences.load(from: temporaryFile(containing: #"{"delaySeconds": 5}"#))
        #expect(preferences.delaySeconds == 5)
        #expect(preferences.hotkeys == .defaults)
    }

    @Test func nullMeansNoHotkeyAndMissingMeansTheDefault() throws {
        let preferences = try Preferences.load(from: temporaryFile(containing: #"{"hotkeys": {"area": null}}"#))
        #expect(preferences.hotkeys.area == nil)
        #expect(preferences.hotkeys.fullscreen == HotKeys.defaults.fullscreen)
    }

    @Test func aClearedHotkeyStaysClearedAfterSaving() throws {
        var preferences = Preferences.defaults
        preferences.hotkeys.area = nil
        let url = try temporaryFile()
        try preferences.save(to: url)
        #expect(try Preferences.load(from: url).hotkeys.area == nil)
    }

    @Test func badValuesFallBackOneByOne() throws {
        let json = #"""
        {"exportScale": "3x", "delaySeconds": -5, "saveFolder": 7,
         "toolStyles": {"arrow": {"colorHex": "#007AFF", "size": "huge"}}}
        """#
        let preferences = try Preferences.load(from: temporaryFile(containing: json))
        #expect(preferences.exportScale == .native)
        #expect(preferences.delaySeconds == 1)
        #expect(preferences.saveFolder == "~/Desktop")
        #expect(preferences.toolStyles["arrow"] == Style(colorHex: "#007AFF", size: .medium))
    }

    @Test func expandsTheSaveFolderFromHome() {
        #expect(Preferences.defaults.saveFolderURL.path == NSHomeDirectory() + "/Desktop")
    }

    @Test func stylesMapBackToToolsAndSkipUnknownOnes() {
        var preferences = Preferences.defaults
        let style = Style(colorHex: "#34C759")
        preferences.toolStyles = ["arrow": style, "nonsense": style]
        #expect(preferences.styles == [.arrow: style])
    }

    @Test func findsTheActionAlreadyUsingACombinationInAnyModifierOrder() {
        let same = HotKeyBinding(keyCode: 19, modifiers: [.shift, .command])
        #expect(HotKeys.defaults.action(using: same) == .area)
        #expect(HotKeys.defaults.action(using: HotKeyBinding(keyCode: 19, modifiers: [.command])) == nil)
    }

    @Test func oneRememberedColourStartsRedAndABadOneFallsBack() throws {
        #expect(Preferences.defaults.colorHex == Palette.red)
        #expect(try Preferences.load(from: temporaryFile(containing: #"{"colorHex": "blue"}"#)).colorHex == Palette.red)
        var saved = Preferences.defaults
        saved.colorHex = "#007AFF"
        let url = try temporaryFile()
        try saved.save(to: url)
        #expect(try Preferences.load(from: url).colorHex == "#007AFF")
    }

    @Test func theMenuBarAndDockIconsShowByDefault() {
        #expect(Preferences.defaults.showMenuBarIcon)
        #expect(Preferences.defaults.showDockIconWhileCapturing)
    }

    @Test func eitherIconCanBeTurnedOffAndStaysOff() throws {
        let preferences = try Preferences.load(from: temporaryFile(containing: #"{"showMenuBarIcon": false, "showDockIconWhileCapturing": "no"}"#))
        #expect(!preferences.showMenuBarIcon)
        #expect(preferences.showDockIconWhileCapturing)

        var saved = Preferences.defaults
        saved.showDockIconWhileCapturing = false
        let url = try temporaryFile()
        try saved.save(to: url)
        #expect(try !Preferences.load(from: url).showDockIconWhileCapturing)
    }

    @Test func printsHotkeysTheWayMacOSDoes() {
        #expect(HotKeys.defaults.area?.displayString == "⇧⌘2")
    }
}
