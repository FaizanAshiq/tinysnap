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

/// The shape the crop keeps: any, or width to height as named.
public enum CropRatio: String, Codable, Sendable, CaseIterable {
    case free, square = "1:1", fourThree = "4:3", sixteenNine = "16:9"

    /// Width over height, landscape; nil for a crop of any shape.
    public var value: CGFloat? {
        switch self {
        case .free: nil
        case .square: 1
        case .fourThree: 4.0 / 3
        case .sixteenNine: 16.0 / 9
        }
    }
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
    /// Text only: set in the bold face.
    public var bold: Bool
    /// Lines, and outlined boxes and ovals: drawn in dashes.
    public var dashed: Bool
    /// Steps only: counted A, B, C rather than 1, 2, 3, apart from the numbered ones.
    public var letters: Bool
    /// The highlighter only: following the pointer instead of drawing a straight stroke.
    public var freehand: Bool
    /// The spotlight only: what is outside it blurred instead of dimmed.
    public var blurOutside: Bool
    /// The crop tool only: the shape a crop keeps.
    public var cropRatio: CropRatio

    public static let opacityRange: ClosedRange<CGFloat> = 0.1...1

    public init(colorHex: String, size: StyleSize = .medium, filled: Bool = false, corners: CornerSize = .medium,
                opacity: CGFloat = 1, difference: Bool = false, align: TextAlign = .left, bold: Bool = false,
                dashed: Bool = false, letters: Bool = false, freehand: Bool = false, blurOutside: Bool = false,
                cropRatio: CropRatio = .free) {
        self.colorHex = colorHex
        self.size = size
        self.filled = filled
        self.corners = corners
        self.opacity = min(max(opacity, Self.opacityRange.lowerBound), Self.opacityRange.upperBound)
        self.difference = difference
        self.align = align
        self.bold = bold
        self.dashed = dashed
        self.letters = letters
        self.freehand = freehand
        self.blurOutside = blurOutside
        self.cropRatio = cropRatio
    }

    private enum CodingKeys: String, CodingKey {
        case colorHex, size, filled, corners, opacity, difference, align, bold, dashed, letters, freehand, blurOutside, cropRatio
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
        try container.encode(bold, forKey: .bold)
        try container.encode(dashed, forKey: .dashed)
        try container.encode(letters, forKey: .letters)
        try container.encode(freehand, forKey: .freehand)
        try container.encode(blurOutside, forKey: .blurOutside)
        try container.encode(cropRatio, forKey: .cropRatio)
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
        bold = (try? container.decodeIfPresent(Bool.self, forKey: .bold)) ?? false
        dashed = (try? container.decodeIfPresent(Bool.self, forKey: .dashed)) ?? false
        letters = (try? container.decodeIfPresent(Bool.self, forKey: .letters)) ?? false
        freehand = (try? container.decodeIfPresent(Bool.self, forKey: .freehand)) ?? false
        blurOutside = (try? container.decodeIfPresent(Bool.self, forKey: .blurOutside)) ?? false
        cropRatio = (try? container.decodeIfPresent(CropRatio.self, forKey: .cropRatio)) ?? .free
    }
}

public enum Palette {
    public static let red = "#FF3B30"
    public static let yellow = "#FFCC00"

    /// The eight swatches in the style popover, in the order shown.
    public static let swatches = [
        "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#007AFF", "#AF52DE", "#000000", "#FFFFFF",
    ]

    /// How many custom colours come back as swatches.
    public static let recentCount = 5

    /// `recent` with `hex` put first: a custom colour picked again moves up rather than
    /// showing twice, and one of the fixed swatches is left out, as it is there already.
    public static func recent(adding hex: String, to recent: [String]) -> [String] {
        let color = hex.uppercased()
        guard components(of: color) != nil, !swatches.contains(color) else { return recent }
        return Array(([color] + recent.filter { $0.uppercased() != color }).prefix(recentCount))
    }

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

    /// A colour typed as "#RRGGBB", with or without the # and in either case, as "#RRGGBB";
    /// nil for anything else.
    public static func hex(typed text: String) -> String? {
        let hex = "#" + text.trimmingCharacters(in: .whitespaces).drop { $0 == "#" }.uppercased()
        return components(of: hex) == nil ? nil : hex
    }

    /// How bright a colour looks, 0 to 1, from its sRGB components.
    static func luminance(of color: CGColor) -> CGFloat {
        let parts = color.converted(to: CGColorSpace(name: CGColorSpace.sRGB)!, intent: .defaultIntent, options: nil)?
            .components ?? [0, 0, 0]
        return 0.2126 * parts[0] + 0.7152 * parts[1] + 0.0722 * parts[2]
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
