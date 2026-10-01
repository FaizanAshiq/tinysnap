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

    @Test func scanQRCodeHasNoHotkeyUntilOneIsSet() throws {
        #expect(HotKeys.defaults.qr == nil)
        #expect(HotKeyAction.allCases.firstIndex(of: .qr) == HotKeyAction.allCases.firstIndex(of: .text)! + 1)
        // A file from before Scan QR Code existed leaves it unset.
        #expect(try Preferences.load(from: temporaryFile(containing: #"{"hotkeys": {"area": null}}"#)).hotkeys.qr == nil)
        var preferences = Preferences.defaults
        preferences.hotkeys.qr = HotKeyBinding(keyCode: 15, modifiers: [.command, .shift])
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("prefs-\(UUID().uuidString).json")
        try preferences.save(to: url)
        #expect(try Preferences.load(from: url).hotkeys.qr == preferences.hotkeys.qr)
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

    @Test func captureTextDefaultsToCommandShiftOAndOpenLibraryHasNoHotkey() {
        #expect(HotKeys.defaults.text == HotKeyBinding(keyCode: 31, modifiers: [.command, .shift]))
        #expect(HotKeys.defaults.text?.displayString == "⇧⌘O")
        #expect(HotKeys.defaults.library == nil)
    }

    @Test func theMenuListsCaptureTextThenScanQRCodeAndOpenLibraryLast() {
        #expect(HotKeyAction.allCases.map(\.title) == [
            "Capture Area", "Capture Window", "Capture Fullscreen", "Capture Text", "Scan QR Code", "Repeat Last Area",
            "Delayed Capture", "Open Library",
        ])
    }

    @Test func captureWindowStartsUnsetAndItsKeySurvivesSaving() throws {
        #expect(HotKeys.defaults.window == nil)
        var saved = Preferences.defaults
        saved.hotkeys.window = HotKeyBinding(keyCode: 13, modifiers: [.command, .shift])
        let url = try temporaryFile()
        try saved.save(to: url)
        let loaded = try Preferences.load(from: url)
        #expect(loaded.hotkeys.window == saved.hotkeys.window)
        #expect(loaded.hotkeys.action(using: HotKeyBinding(keyCode: 13, modifiers: [.shift, .command])) == .window)
    }

    @Test func aFileFromBeforeTheNewHotkeysGetsTheirDefaults() throws {
        let json = #"{"hotkeys": {"area": null, "fullscreen": null, "repeatArea": null, "delayed": null}}"#
        let preferences = try Preferences.load(from: temporaryFile(containing: json))
        #expect(preferences.hotkeys.area == nil)
        #expect(preferences.hotkeys.text == HotKeys.defaults.text)
        #expect(preferences.hotkeys.library == nil)
    }

    @Test func theNewHotkeysSurviveSavingAndAClearedOneStaysCleared() throws {
        var saved = Preferences.defaults
        saved.hotkeys.text = nil
        saved.hotkeys.library = HotKeyBinding(keyCode: 37, modifiers: [.command, .option])
        let url = try temporaryFile()
        try saved.save(to: url)
        let loaded = try Preferences.load(from: url)
        #expect(loaded.hotkeys.text == nil)
        #expect(loaded.hotkeys.library == saved.hotkeys.library)
        #expect(loaded.hotkeys.action(using: HotKeyBinding(keyCode: 37, modifiers: [.option, .command])) == .library)
    }

    @Test func aCaptureOpensTheEditorAndTheLibraryKeepsItByDefault() {
        #expect(Preferences.defaults.afterCapture == .editor)
        #expect(Preferences.defaults.keepLibrary)
    }

    @Test func afterCaptureAndKeepLibraryFallBackAloneAndSurviveSaving() throws {
        let bad = try Preferences.load(from: temporaryFile(containing: #"{"afterCapture": "popup", "keepLibrary": "yes", "delaySeconds": 5}"#))
        #expect(bad.afterCapture == .editor)
        #expect(bad.keepLibrary)
        #expect(bad.delaySeconds == 5)

        var saved = Preferences.defaults
        saved.afterCapture = .thumbnail
        saved.keepLibrary = false
        let url = try temporaryFile()
        try saved.save(to: url)
        let loaded = try Preferences.load(from: url)
        #expect(loaded.afterCapture == .thumbnail)
        #expect(!loaded.keepLibrary)
    }

    @Test func theLastBackdropSettingsAreRememberedAndFallBackAlone() throws {
        #expect(Preferences.defaults.backdrop == .defaults)
        let bad = try Preferences.load(from: temporaryFile(containing: #"{"backdrop": {"fill": "clear", "shadow": 3}, "delaySeconds": 5}"#))
        #expect(bad.backdrop.fill == .clear)
        #expect(bad.backdrop.shadow == Backdrop.defaults.shadow)
        #expect(bad.delaySeconds == 5)
    }

    @Test func theMeasureToolComesBackAsItWasLeftAndFallsBackAlone() throws {
        #expect(Preferences.defaults.measure == .defaults)
        let left = try Preferences.load(from: temporaryFile(containing: #"{"measure": {"down": true, "edgeContrast": 0.02, "guideSeen": true}}"#))
        #expect(left.measure == MeasureSettings(across: true, down: true, edgeContrast: 0.02, guideSeen: true))
        let bad = try Preferences.load(from: temporaryFile(containing: #"{"measure": 5, "delaySeconds": 4}"#))
        #expect(bad.measure == .defaults)
        #expect(bad.delaySeconds == 4)
    }

    @Test func theRememberedBackdropNeverHoldsOnToAWallpaper() {
        var backdrop = Backdrop.defaults
        backdrop.fill = .wallpaper
        backdrop.wallpaper = Backdrop.Wallpaper(image: PastedImage(Fixture.capture(width: 4, height: 4).image))
        var preferences = Preferences.defaults
        preferences.backdrop = backdrop
        // Kept, it would stand in for the desktop picture of another screen, or of a
        // desktop changed since.
        #expect(preferences.backdrop.wallpaper == nil)
        #expect(preferences.backdrop.fill == .wallpaper)
        let made = Preferences.defaults
        let copy = Preferences(hotkeys: made.hotkeys, saveFolder: made.saveFolder, exportScale: made.exportScale,
                               delaySeconds: made.delaySeconds, showMenuBarIcon: made.showMenuBarIcon,
                               showDockIconWhileCapturing: made.showDockIconWhileCapturing, colorHex: made.colorHex,
                               toolStyles: made.toolStyles, afterCapture: made.afterCapture, keepLibrary: made.keepLibrary,
                               backdrop: backdrop)
        #expect(copy.backdrop.wallpaper == nil)
    }

    @Test func printsHotkeysTheWayMacOSDoes() {
        #expect(HotKeys.defaults.area?.displayString == "⇧⌘2")
    }
}
