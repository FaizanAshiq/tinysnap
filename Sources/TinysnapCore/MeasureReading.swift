import CoreGraphics
import Foundation

/// What the Measure tool shows and remembers: which lines, how big a change in
/// brightness counts as an edge, and whether its guide has been seen.
public struct MeasureSettings: Equatable, Sendable {
    public var across: Bool
    public var down: Bool
    /// 0.01 to 0.9. Lower finds fainter edges.
    public var edgeContrast: Double
    public var guideSeen: Bool

    /// Caliper's default edge contrast, so the two agree.
    public static let defaults = MeasureSettings(across: true, down: false, edgeContrast: 0.08, guideSeen: false)
    public static let contrastRange: ClosedRange<Double> = 0.01...0.9

    public init(across: Bool, down: Bool, edgeContrast: Double, guideSeen: Bool) {
        self.across = across
        self.down = down
        self.edgeContrast = edgeContrast
        self.guideSeen = guideSeen
    }

    /// One percent a step, five with Shift, kept to whole percents so repeated steps
    /// never drift.
    public mutating func stepContrast(up: Bool, coarse: Bool) {
        let step = (coarse ? 5.0 : 1.0) * (up ? 1 : -1)
        let percent = (edgeContrast * 100).rounded() + step
        edgeContrast = min(max(percent / 100, Self.contrastRange.lowerBound), Self.contrastRange.upperBound)
    }

    /// The edge contrast as the panel shows it.
    public var contrastLabel: String { "\(Int((edgeContrast * 100).rounded()))%" }
}

/// Each value falls back on its own, as Preferences does.
extension MeasureSettings: Codable {
    private enum CodingKeys: String, CodingKey {
        case across, down, edgeContrast, guideSeen
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let fallback = Self.defaults
        across = (try? container.decodeIfPresent(Bool.self, forKey: .across)) ?? fallback.across
        down = (try? container.decodeIfPresent(Bool.self, forKey: .down)) ?? fallback.down
        let contrast = (try? container.decodeIfPresent(Double.self, forKey: .edgeContrast)) ?? nil
        edgeContrast = contrast.flatMap { Self.contrastRange.contains($0) ? $0 : nil } ?? fallback.edgeContrast
        guideSeen = (try? container.decodeIfPresent(Bool.self, forKey: .guideSeen)) ?? fallback.guideSeen
    }
}

/// One line of a reading, in capture pixels.
public struct MeasureLine: Equatable, Sendable {
    public let from: CGPoint
    public let to: CGPoint

    public init(from: CGPoint, to: CGPoint) {
        self.from = from
        self.to = to
    }
}

public enum MeasureReading {
    /// The Across and Down lines through `point`, in capture pixels, for whichever are
    /// on. Empty off the capture, on a flat field, and for a span of a point or less,
    /// which is a rule rather than a space worth naming.
    public static func lines(at point: CGPoint, in buffer: LuminanceBuffer, scale: CGFloat,
                             settings: MeasureSettings) -> [MeasureLine] {
        let x = Int(point.x.rounded(.down)), y = Int(point.y.rounded(.down))
        guard settings.across || settings.down, x >= 0, y >= 0, x < buffer.width, y < buffer.height,
              let region = EdgeDetector(threshold: settings.edgeContrast, scale: scale).bounds(around: (x, y), in: buffer)
        else { return [] }
        var lines: [MeasureLine] = []
        if settings.across, region.width / scale > 1 {
            lines.append(MeasureLine(from: CGPoint(x: region.minX, y: point.y), to: CGPoint(x: region.maxX, y: point.y)))
        }
        if settings.down, region.height / scale > 1 {
            lines.append(MeasureLine(from: CGPoint(x: point.x, y: region.minY), to: CGPoint(x: point.x, y: region.maxY)))
        }
        return lines
    }

    /// `16 pt`, `16.5 pt`: a length in points, to the half point on a Retina capture and
    /// the whole point on a 1x one, the finest each can show.
    public static func label(forPixels length: CGFloat, scale: CGFloat) -> String {
        let step: CGFloat = scale >= 2 ? 0.5 : 1
        let points = (length / scale / step).rounded() * step
        let text = points == points.rounded() ? String(Int(points)) : String(format: "%.1f", points)
        return "\(text) pt"
    }
}
