import CoreGraphics

/// Five steps, thinnest first. `small`, `medium` and `large` keep their old names, so
/// a preferences file from before there were five still loads.
public enum StyleSize: String, Codable, Sendable, CaseIterable {
    case extraSmall, small, medium, large, extraLarge

    private var index: Int { Self.allCases.firstIndex(of: self) ?? 2 }

    /// One step down, for the [ key. Stops at the thinnest.
    public var thinner: StyleSize { Self.allCases[max(0, index - 1)] }
    /// One step up, for the ] key. Stops at the thickest.
    public var thicker: StyleSize { Self.allCases[min(Self.allCases.count - 1, index + 1)] }
}

/// How round a box's corners are, from square to fully round.
public enum CornerSize: String, Codable, Sendable, CaseIterable {
    case square, small, medium, large, full

    /// The radius in points, or nil for fully round: half the box's shorter side, which
    /// makes a square spotlight a circle.
    public var points: CGFloat? {
        switch self {
        case .square: 0
        case .small: 4
        case .medium: 10
        case .large: 20
        case .full: nil
        }
    }
}

/// Where each line of a text sits in the box its widest line makes.
public enum TextAlign: String, Codable, Sendable, CaseIterable {
    case left, center, right
}

public struct Style: Equatable, Codable, Sendable {
    public var colorHex: String
    public var size: StyleSize
    /// Rectangles and ovals filled instead of outlined, and text set on a box in its colour.
    public var filled: Bool
    /// Rectangles, spotlights, blurs, pixelates and pasted images.
    public var corners: CornerSize
    /// Pasted images only: 0.1 to 1, for lining one up against the capture.
    public var opacity: CGFloat
    /// Pasted images only: drawn with the difference blend, so where the image matches
    /// what is under it the result is black and changes stand out.
    public var difference: Bool
    /// Text only: each line set left, centred or right.
    public var align: TextAlign

    public static let opacityRange: ClosedRange<CGFloat> = 0.1...1

    public init(colorHex: String, size: StyleSize = .medium, filled: Bool = false, corners: CornerSize = .medium,
                opacity: CGFloat = 1, difference: Bool = false, align: TextAlign = .left) {
        self.colorHex = colorHex
        self.size = size
        self.filled = filled
        self.corners = corners
        self.opacity = min(max(opacity, Self.opacityRange.lowerBound), Self.opacityRange.upperBound)
        self.difference = difference
        self.align = align
    }

    private enum CodingKeys: String, CodingKey {
        case colorHex, size, filled, corners, opacity, difference, align
        /// Read only: the square corner switch this replaced.
        case sharpCorners
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(colorHex, forKey: .colorHex)
        try container.encode(size, forKey: .size)
        try container.encode(filled, forKey: .filled)
        try container.encode(corners, forKey: .corners)
        try container.encode(opacity, forKey: .opacity)
        try container.encode(difference, forKey: .difference)
        try container.encode(align, forKey: .align)
    }

    /// Every key is optional and a bad value falls back on its own, so one hand edited
    /// mistake in preferences.json costs that one value rather than the whole file.
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let hex = (try? container.decodeIfPresent(String.self, forKey: .colorHex)) ?? nil
        colorHex = hex.flatMap { Palette.components(of: $0) == nil ? nil : $0 } ?? Palette.red
        size = (try? container.decodeIfPresent(StyleSize.self, forKey: .size)) ?? .medium
        filled = (try? container.decodeIfPresent(Bool.self, forKey: .filled)) ?? false
        let wasSquare = (try? container.decodeIfPresent(Bool.self, forKey: .sharpCorners)) ?? false
        corners = (try? container.decodeIfPresent(CornerSize.self, forKey: .corners)) ?? (wasSquare == true ? .square : .medium)
        let opacity = (try? container.decodeIfPresent(CGFloat.self, forKey: .opacity)) ?? nil
        // Out of range means a hand edit gone wrong, which is read as fully solid.
        self.opacity = opacity.flatMap { $0 > 1 ? nil : max($0, Self.opacityRange.lowerBound) } ?? 1
        difference = (try? container.decodeIfPresent(Bool.self, forKey: .difference)) ?? false
        align = (try? container.decodeIfPresent(TextAlign.self, forKey: .align)) ?? .left
    }
}

public enum Palette {
    public static let red = "#FF3B30"
    public static let yellow = "#FFCC00"

    /// The eight swatches in the style popover, in the order shown.
    public static let swatches = [
        "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#007AFF", "#AF52DE", "#000000", "#FFFFFF",
    ]

    /// The sRGB components of "#RRGGBB", or nil for anything else.
    public static func components(of hex: String) -> (red: CGFloat, green: CGFloat, blue: CGFloat)? {
        var digits = Substring(hex)
        if digits.hasPrefix("#") { digits = digits.dropFirst() }
        guard digits.count == 6, digits.allSatisfy(\.isHexDigit),
              let value = UInt32(digits, radix: 16) else { return nil }
        return (CGFloat((value >> 16) & 0xFF) / 255,
                CGFloat((value >> 8) & 0xFF) / 255,
                CGFloat(value & 0xFF) / 255)
    }

    public static func color(hex: String, alpha: CGFloat = 1) -> CGColor {
        let rgb = components(of: hex) ?? (1, 59 / 255, 48 / 255)
        return CGColor(srgbRed: rgb.red, green: rgb.green, blue: rgb.blue, alpha: alpha)
    }

    public static func hex(red: CGFloat, green: CGFloat, blue: CGFloat) -> String {
        func byte(_ value: CGFloat) -> Int { Int((max(0, min(1, value)) * 255).rounded()) }
        return String(format: "#%02X%02X%02X", byte(red), byte(green), byte(blue))
    }
}
