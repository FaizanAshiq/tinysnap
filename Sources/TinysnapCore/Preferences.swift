import Foundation

public enum ModifierKey: String, Sendable, Codable, CaseIterable {
    case control
    case option
    case shift
    case command
}

public struct HotKeyBinding: Equatable, Sendable, Codable {
    /// A Carbon virtual key code. 19 is the 2 key.
    public var keyCode: UInt32
    public var modifiers: [ModifierKey]

    public init(keyCode: UInt32, modifiers: [ModifierKey]) {
        self.keyCode = keyCode
        self.modifiers = modifiers
    }

    /// The binding written the way macOS prints it in a menu, for example "⇧⌘2".
    /// Modifiers always appear in the system order regardless of how they were
    /// stored, because a hand edited preferences file can list them any way round.
    public var displayString: String {
        let systemOrder: [(ModifierKey, String)] = [
            (.control, "⌃"), (.option, "⌥"), (.shift, "⇧"), (.command, "⌘"),
        ]
        let symbols = systemOrder
            .filter { modifiers.contains($0.0) }
            .map(\.1)
            .joined()
        return symbols + Self.label(for: keyCode)
    }

    /// Carbon virtual key codes. Only the keys worth binding a hotkey to, because
    /// anything missing still reads as "Key 250" rather than as nothing at all.
    private static let labels: [UInt32: String] = [
        0: "A", 1: "S", 2: "D", 3: "F", 4: "H", 5: "G", 6: "Z", 7: "X", 8: "C", 9: "V",
        11: "B", 12: "Q", 13: "W", 14: "E", 15: "R", 16: "Y", 17: "T", 31: "O", 32: "U",
        34: "I", 35: "P", 37: "L", 38: "J", 40: "K", 45: "N", 46: "M",
        18: "1", 19: "2", 20: "3", 21: "4", 22: "6", 23: "5", 25: "9", 26: "7", 28: "8", 29: "0",
        36: "Return", 48: "Tab", 49: "Space", 51: "Delete", 53: "Escape",
        123: "←", 124: "→", 125: "↓", 126: "↑",
    ]

    private static func label(for keyCode: UInt32) -> String {
        labels[keyCode] ?? "Key \(keyCode)"
    }
}

public enum HotKeyAction: String, CaseIterable, Sendable {
    case area, fullscreen, repeatArea, delayed

    public var title: String {
        switch self {
        case .area: "Capture Area"
        case .fullscreen: "Capture Fullscreen"
        case .repeatArea: "Repeat Last Area"
        case .delayed: "Delayed Capture"
        }
    }
}

public struct HotKeys: Equatable, Sendable, Codable {
    public var area: HotKeyBinding?
    public var fullscreen: HotKeyBinding?
    public var repeatArea: HotKeyBinding?
    public var delayed: HotKeyBinding?

    public static let defaults = HotKeys(
        area: HotKeyBinding(keyCode: 19, modifiers: [.command, .shift]),
        fullscreen: HotKeyBinding(keyCode: 18, modifiers: [.command, .shift]),
        repeatArea: nil,
        delayed: nil
    )

    public init(area: HotKeyBinding?, fullscreen: HotKeyBinding?, repeatArea: HotKeyBinding?, delayed: HotKeyBinding?) {
        self.area = area
        self.fullscreen = fullscreen
        self.repeatArea = repeatArea
        self.delayed = delayed
    }

    public subscript(action: HotKeyAction) -> HotKeyBinding? {
        get {
            switch action {
            case .area: area
            case .fullscreen: fullscreen
            case .repeatArea: repeatArea
            case .delayed: delayed
            }
        }
        set {
            switch action {
            case .area: area = newValue
            case .fullscreen: fullscreen = newValue
            case .repeatArea: repeatArea = newValue
            case .delayed: delayed = newValue
            }
        }
    }

    /// The action already using `binding`, so Settings can refuse a second one.
    public func action(using binding: HotKeyBinding) -> HotKeyAction? {
        HotKeyAction.allCases.first { action in
            guard let existing = self[action] else { return false }
            return existing.keyCode == binding.keyCode && Set(existing.modifiers) == Set(binding.modifiers)
        }
    }

    private enum CodingKeys: String, CodingKey {
        case area, fullscreen, repeatArea, delayed
    }

    /// A key that is there but null means no hotkey. A key that is missing, or holds
    /// something unreadable, means the default.
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        func binding(_ key: CodingKeys, _ fallback: HotKeyBinding?) -> HotKeyBinding? {
            guard container.contains(key) else { return fallback }
            if (try? container.decodeNil(forKey: key)) == true { return nil }
            return (try? container.decode(HotKeyBinding.self, forKey: key)) ?? fallback
        }
        area = binding(.area, Self.defaults.area)
        fullscreen = binding(.fullscreen, Self.defaults.fullscreen)
        repeatArea = binding(.repeatArea, Self.defaults.repeatArea)
        delayed = binding(.delayed, Self.defaults.delayed)
    }

    /// Nil is written as null, not left out. Left out, a hotkey the user cleared would
    /// read back as the default on the next launch.
    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        for (key, value) in [(CodingKeys.area, area), (.fullscreen, fullscreen), (.repeatArea, repeatArea), (.delayed, delayed)] {
            if let value { try container.encode(value, forKey: key) } else { try container.encodeNil(forKey: key) }
        }
    }
}

public struct Preferences: Equatable, Sendable, Codable {
    public var hotkeys: HotKeys
    public var saveFolder: String
    public var exportScale: ExportScale
    public var delaySeconds: Int
    /// Hidden, Tinysnap is reached through its hotkeys, and opening the app again shows
    /// Settings.
    public var showMenuBarIcon: Bool
    /// A Dock icon, and a place in Command Tab, while any capture window is open, so a
    /// capture is not lost behind other apps.
    public var showDockIconWhileCapturing: Bool
    /// The one colour every tool draws with, the last one picked.
    public var colorHex: String
    /// The last style used per tool, keyed by the tool's raw value. Only the size and the
    /// box shape count; the colour is `colorHex`.
    public var toolStyles: [String: Style]

    public static let defaults = Preferences(
        hotkeys: .defaults,
        saveFolder: "~/Desktop",
        exportScale: .native,
        delaySeconds: 3,
        showMenuBarIcon: true,
        showDockIconWhileCapturing: true,
        colorHex: Palette.red,
        toolStyles: [:]
    )

    public static let delayRange = 1...60

    public init(hotkeys: HotKeys, saveFolder: String, exportScale: ExportScale, delaySeconds: Int,
                showMenuBarIcon: Bool, showDockIconWhileCapturing: Bool, colorHex: String, toolStyles: [String: Style]) {
        self.hotkeys = hotkeys
        self.saveFolder = saveFolder
        self.exportScale = exportScale
        self.delaySeconds = delaySeconds
        self.showMenuBarIcon = showMenuBarIcon
        self.showDockIconWhileCapturing = showDockIconWhileCapturing
        self.colorHex = colorHex
        self.toolStyles = toolStyles
    }

    /// Every key is optional and a bad value falls back on its own, so a file written
    /// by an older version, or a hand edited one, still loads.
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let fallback = Preferences.defaults
        hotkeys = (try? container.decodeIfPresent(HotKeys.self, forKey: .hotkeys)) ?? fallback.hotkeys
        saveFolder = (try? container.decodeIfPresent(String.self, forKey: .saveFolder)) ?? fallback.saveFolder
        exportScale = (try? container.decodeIfPresent(ExportScale.self, forKey: .exportScale)) ?? fallback.exportScale
        let delay = (try? container.decodeIfPresent(Int.self, forKey: .delaySeconds)) ?? fallback.delaySeconds
        delaySeconds = min(max(delay, Self.delayRange.lowerBound), Self.delayRange.upperBound)
        showMenuBarIcon = (try? container.decodeIfPresent(Bool.self, forKey: .showMenuBarIcon)) ?? fallback.showMenuBarIcon
        showDockIconWhileCapturing = (try? container.decodeIfPresent(Bool.self, forKey: .showDockIconWhileCapturing))
            ?? fallback.showDockIconWhileCapturing
        let hex = (try? container.decodeIfPresent(String.self, forKey: .colorHex)) ?? nil
        colorHex = hex.flatMap { Palette.components(of: $0) == nil ? nil : $0 } ?? fallback.colorHex
        toolStyles = (try? container.decodeIfPresent([String: Style].self, forKey: .toolStyles)) ?? fallback.toolStyles
    }

    public var saveFolderURL: URL {
        URL(fileURLWithPath: (saveFolder as NSString).expandingTildeInPath, isDirectory: true)
    }

    /// The remembered styles, for starting an editor session.
    public var styles: [Tool: Style] {
        Dictionary(uniqueKeysWithValues: toolStyles.compactMap { key, style in Tool(rawValue: key).map { ($0, style) } })
    }

    public static var defaultFileURL: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Tinysnap/preferences.json")
    }

    /// Returns defaults when the file does not exist, so first launch needs no setup.
    public static func load(from url: URL) throws -> Preferences {
        guard FileManager.default.fileExists(atPath: url.path) else { return .defaults }
        let data = try Data(contentsOf: url)
        return try JSONDecoder().decode(Preferences.self, from: data)
    }

    public func save(to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(self).write(to: url, options: .atomic)
    }
}
