import AppKit
import TinysnapCore

extension Tool {
    /// The toolbar's groups, by what the tools do: pick and frame, draw, label, focus,
    /// redact. Each group is set apart by a gap.
    static let toolbarGroups: [[Tool]] = [
        [.select, .crop],
        [.arrow, .line, .rectangle, .oval, .freehand, .highlighter],
        [.text, .step, .image],
        [.spotlight, .magnifier, .measure],
        [.blur, .pixelate, .erase],
    ]

    /// SF Symbols, so there is no asset catalog to compile.
    var symbolName: String {
        switch self {
        case .select: "cursorarrow"
        case .arrow: "arrow.up.right"
        case .line: "line.diagonal"
        case .rectangle: "rectangle"
        case .oval: "circle"
        case .text: "textformat"
        case .highlighter: "highlighter"
        case .freehand: "scribble"
        case .step: "1.circle"
        case .spotlight: "light.max"
        case .magnifier: "magnifyingglass.circle"
        case .image: "photo"
        case .crop: "crop"
        case .blur: "drop"
        case .pixelate: "square.grid.3x3"
        case .erase: "eraser"
        case .measure: "ruler"
        }
    }

    var symbol: NSImage {
        NSImage(systemSymbolName: symbolName, accessibilityDescription: title) ?? NSImage()
    }

    /// The tooltip, with the key that picks the tool.
    var tooltip: String {
        let shortcut = "\(title) (\(String(key).uppercased()))"
        return self == .select ? shortcut + ", or hold \u{2318} with any tool" : shortcut
    }
}
