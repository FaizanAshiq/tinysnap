import CoreGraphics
import CoreImage
import Foundation

/// The frame drawn around the output for sharing: a fill, padding, rounded corners on the
/// capture and a shadow under it. A document has none until one is turned on.
public struct Backdrop: Equatable, Sendable {
    public enum Fill: String, Codable, Sendable, CaseIterable {
        /// Two colours taken from the capture itself.
        case gradient
        case solid
        /// The desktop picture, softened.
        case wallpaper
        /// Nothing: the PNG is see-through around the capture and its shadow.
        case clear
    }

    public enum Padding: String, Codable, Sendable, CaseIterable {
        case small, medium, large

        public var points: CGFloat {
            switch self {
            case .small: 24
            case .medium: 48
            case .large: 88
            }
        }
    }

    public enum Shadow: String, Codable, Sendable, CaseIterable {
        case none, soft, strong
    }

    /// The desktop picture a wallpaper fill shows, softened once, when it was read. The id
    /// names its file in a library entry, so it is written once rather than on every edit.
    public struct Wallpaper: Equatable, Sendable {
        public let id: UUID
        public let image: PastedImage

        public init(id: UUID = UUID(), image: PastedImage) {
            self.id = id
            self.image = image
        }

        public static func == (lhs: Wallpaper, rhs: Wallpaper) -> Bool { lhs.id == rhs.id }
    }

    public var fill: Fill
    /// The solid fill's colour.
    public var colorHex: String
    public var padding: Padding
    public var corners: CornerSize
    public var shadow: Shadow
    /// Nil when no wallpaper could be read, which draws the gradient instead.
    public var wallpaper: Wallpaper?

    /// The corners the panel offers: square, round and rounder.
    public static let cornerChoices: [CornerSize] = [.square, .medium, .large]

    public static let defaults = Backdrop(fill: .gradient, colorHex: "#007AFF", padding: .medium, corners: .medium, shadow: .soft)

    /// A wallpaper softened once, when it is read, so it sits behind the capture without
    /// competing with it. The blur scales with the picture, and its edges are held so they
    /// do not fade to nothing.
    public static func soften(_ image: CGImage) -> CGImage? {
        let source = CIImage(cgImage: image)
        let radius = Double(max(image.width, image.height)) * 0.012
        guard let blur = CIFilter(name: "CIGaussianBlur") else { return nil }
        blur.setValue(source.clampedToExtent(), forKey: kCIInputImageKey)
        blur.setValue(radius, forKey: kCIInputRadiusKey)
        guard let output = blur.outputImage?.cropped(to: source.extent) else { return nil }
        return CIContext(options: [.useSoftwareRenderer: false]).createCGImage(output, from: source.extent)
    }

    public init(fill: Fill, colorHex: String, padding: Padding, corners: CornerSize, shadow: Shadow, wallpaper: Wallpaper? = nil) {
        self.fill = fill
        self.colorHex = colorHex
        self.padding = padding
        self.corners = corners
        self.shadow = shadow
        self.wallpaper = wallpaper
    }
}

/// The settings only. A wallpaper's pixels are never part of them: Preferences keeps no
/// image, and a library entry keeps it as its own file.
extension Backdrop: Codable {
    private enum CodingKeys: String, CodingKey {
        case fill, colorHex, padding, corners, shadow
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(fill, forKey: .fill)
        try container.encode(colorHex, forKey: .colorHex)
        try container.encode(padding, forKey: .padding)
        try container.encode(corners, forKey: .corners)
        try container.encode(shadow, forKey: .shadow)
    }

    /// Each value falls back on its own, as Style and Preferences do.
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let fallback = Backdrop.defaults
        fill = (try? container.decodeIfPresent(Fill.self, forKey: .fill)) ?? fallback.fill
        let hex = (try? container.decodeIfPresent(String.self, forKey: .colorHex)) ?? nil
        colorHex = hex.flatMap { Palette.components(of: $0) == nil ? nil : $0 } ?? fallback.colorHex
        padding = (try? container.decodeIfPresent(Padding.self, forKey: .padding)) ?? fallback.padding
        let corners = (try? container.decodeIfPresent(CornerSize.self, forKey: .corners)) ?? nil
        self.corners = corners.flatMap { Self.cornerChoices.contains($0) ? $0 : nil } ?? fallback.corners
        shadow = (try? container.decodeIfPresent(Shadow.self, forKey: .shadow)) ?? fallback.shadow
        wallpaper = nil
    }
}
