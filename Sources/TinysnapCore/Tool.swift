import CoreGraphics

public enum Tool: String, CaseIterable, Codable, Sendable {
    case select, arrow, line, rectangle, oval, text, highlighter, freehand
    case step, spotlight, magnifier, image, crop, blur, pixelate, erase, measure

    public var key: Character {
        switch self {
        case .select: "v"
        case .arrow: "a"
        case .line: "l"
        case .rectangle: "r"
        case .oval: "o"
        case .text: "t"
        case .highlighter: "h"
        case .freehand: "f"
        case .step: "n"
        case .spotlight: "s"
        case .magnifier: "m"
        case .image: "i"
        case .crop: "c"
        case .blur: "b"
        case .pixelate: "p"
        case .erase: "e"
        case .measure: "d"
        }
    }

    public static func forKey(_ character: Character) -> Tool? {
        let lowered = Character(character.lowercased())
        return allCases.first { $0.key == lowered }
    }

    public var title: String {
        switch self {
        case .select: "Select"
        case .arrow: "Arrow"
        case .line: "Line"
        case .rectangle: "Rectangle"
        case .oval: "Oval"
        case .text: "Text"
        case .highlighter: "Highlighter"
        case .freehand: "Freehand"
        case .step: "Step Number"
        case .spotlight: "Spotlight"
        case .magnifier: "Magnifier"
        case .image: "Image"
        case .crop: "Crop"
        case .blur: "Blur, can be partly reversed"
        case .pixelate: "Pixelate, can be partly reversed"
        case .erase: "Erase, the only guaranteed redaction"
        case .measure: "Measure"
        }
    }

    /// The five sizes in points, thinnest first, or nil for a tool without a size.
    private var sizeTable: [CGFloat]? {
        switch self {
        case .arrow, .line, .rectangle, .oval, .freehand: [2, 3, 4, 6, 9]
        case .text: [12, 16, 20, 28, 40]
        case .highlighter: [10, 14, 20, 26, 34]
        case .step: [20, 26, 32, 40, 50]
        case .blur: [3, 5, 8, 12, 18]
        case .pixelate: [6, 8, 12, 16, 24]
        case .measure: [1, 1.5, 2, 3, 4]
        case .select, .spotlight, .magnifier, .image, .crop, .erase: nil
        }
    }

    public func points(for size: StyleSize) -> CGFloat? {
        guard let table = sizeTable, let index = StyleSize.allCases.firstIndex(of: size) else { return nil }
        return table[index]
    }

    public var hasSize: Bool { sizeTable != nil }

    public var hasColor: Bool {
        switch self {
        case .arrow, .line, .rectangle, .oval, .text, .highlighter, .freehand, .step, .measure: true
        case .select, .spotlight, .magnifier, .image, .crop, .blur, .pixelate, .erase: false
        }
    }

    /// Boxes and ovals filled instead of outlined, and text set on a box in its colour.
    public var hasFill: Bool { self == .rectangle || self == .oval || self == .text }

    /// Text only: its lines set left, centred or right.
    public var hasAlign: Bool { self == .text }

    /// Text only: set in the bold face.
    public var hasBold: Bool { self == .text }

    /// Pasted images: opacity and the difference blend, for comparing against the capture.
    public var hasOverlay: Bool { self == .image }

    /// Boxes whose corner radius can be set. Not erase: rounding it would leave the
    /// corners of what it hides showing.
    public var hasCorners: Bool {
        switch self {
        case .rectangle, .spotlight, .blur, .pixelate, .image: true
        default: false
        }
    }

    public var defaultStyle: Style {
        let corners: CornerSize
        switch self {
        case .image: corners = .square
        case .spotlight, .blur, .pixelate: corners = .small
        default: corners = .medium
        }
        return Style(colorHex: Palette.red, corners: corners)
    }

    /// A new magnifier is 160 points across.
    public static let magnifierRadiusPoints: CGFloat = 80
}
