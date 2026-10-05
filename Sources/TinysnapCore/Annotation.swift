import CoreGraphics
import Foundation

/// An image pasted or dropped onto a capture. A class, so every undo snapshot shares
/// the one copy of its pixels.
public final class PastedImage: Equatable, Sendable {
    public let image: CGImage

    public init(_ image: CGImage) {
        self.image = image
    }

    public static func == (lhs: PastedImage, rhs: PastedImage) -> Bool { lhs === rhs }
}

public struct Annotation: Equatable, Identifiable, Sendable {
    public let id: UUID
    public var kind: Kind
    public var style: Style
    /// Where a measurement's tag sits along its line, 0 at its start and 1 at its end.
    /// Off the middle only when it slid clear of another tag.
    public var labelAt: CGFloat
    /// Locked: nothing about it changes until it is unlocked, and drawing tools draw over it.
    public var isLocked: Bool
    /// Hidden: kept, but not drawn, clicked, copied or saved into an image.
    public var isHidden: Bool
    /// Its place in the order the shapes were drawn, from 1, which numbers its name in the
    /// layers list: Rectangle 1, Rectangle 2. 0 until a document gives it one.
    public var serial: Int

    public init(id: UUID = UUID(), kind: Kind, style: Style, labelAt: CGFloat = 0.5,
                isLocked: Bool = false, isHidden: Bool = false, serial: Int = 0) {
        self.id = id
        self.kind = kind
        self.style = style
        self.labelAt = labelAt
        self.isLocked = isLocked
        self.isHidden = isHidden
        self.serial = serial
    }

    public enum Kind: Equatable, Sendable {
        case arrow(from: CGPoint, to: CGPoint)
        case line(from: CGPoint, to: CGPoint)
        case rectangle(CGRect)
        case oval(CGRect)
        case text(origin: CGPoint, string: String)
        case highlighter(from: CGPoint, to: CGPoint)
        /// A highlight that follows the pointer.
        case highlighterPath([CGPoint])
        case freehand([CGPoint])
        case step(center: CGPoint)
        case spotlight(CGRect)
        case magnifier(center: CGPoint, radius: CGFloat, zoom: CGFloat)
        case image(CGRect, PastedImage)
        case blur(CGRect)
        case pixelate(CGRect)
        case erase(CGRect)
        /// Two ends in capture pixels. The label is the length, worked out when drawn.
        case measure(from: CGPoint, to: CGPoint)
    }

    /// The tool that draws this kind, and so whose remembered style it shares.
    public var tool: Tool {
        switch kind {
        case .arrow: .arrow
        case .line: .line
        case .rectangle: .rectangle
        case .oval: .oval
        case .text: .text
        case .highlighter, .highlighterPath: .highlighter
        case .freehand: .freehand
        case .step: .step
        case .spotlight: .spotlight
        case .magnifier: .magnifier
        case .image: .image
        case .blur: .blur
        case .pixelate: .pixelate
        case .erase: .erase
        case .measure: .measure
        }
    }

    /// The style's size in capture pixels: a stroke width, a font size, a step's
    /// diameter, a blur radius or a pixelate block.
    public func pixelSize(scale: CGFloat) -> CGFloat {
        (tool.points(for: style.size) ?? 0) * scale
    }

    /// The box used for the selection outline, in capture pixels.
    /// What grows the canvas when it passes the capture's edge: everything drawn, except a
    /// measurement's end ticks, which overhang the edges a reading ends on.
    func growthBounds(scale: CGFloat) -> CGRect {
        guard case let .measure(from, to) = kind else { return bounds(scale: scale) }
        let size = pixelSize(scale: scale)
        return CGRect(corner: from, corner: to)
            .union(MeasureShape.tag(from: from, to: to, width: size / scale, scale: scale, at: labelAt).rect)
    }

    public func bounds(scale: CGFloat) -> CGRect {
        let size = pixelSize(scale: scale)
        switch kind {
        case let .arrow(from, to):
            let reach = ArrowShape.headHalfWidth(width: size, length: from.distance(to: to))
            return CGRect(corner: from, corner: to).insetBy(dx: -reach, dy: -reach)
        case let .line(from, to), let .highlighter(from, to):
            return CGRect(corner: from, corner: to).insetBy(dx: -size / 2, dy: -size / 2)
        case let .measure(from, to):
            return MeasureShape.extent(from: from, to: to, width: size / scale, scale: scale, at: labelAt)
        case let .rectangle(rect), let .oval(rect), let .spotlight(rect),
             let .blur(rect), let .pixelate(rect), let .erase(rect), let .image(rect, _):
            return rect
        case let .text(origin, string):
            let letters = CGRect(origin: origin, size: TextLayout.size(of: string, points: size / scale, scale: scale,
                                                                      bold: style.bold))
            guard style.filled else { return letters }
            let padding = TextLayout.boxPadding(points: size / scale)
            return letters.insetBy(dx: -padding.width * scale, dy: -padding.height * scale)
        case let .freehand(points), let .highlighterPath(points):
            guard let first = points.first else { return .null }
            let box = points.reduce(CGRect(origin: first, size: .zero)) { $0.union(CGRect(origin: $1, size: .zero)) }
            return box.insetBy(dx: -size / 2, dy: -size / 2)
        case let .step(center):
            return CGRect(x: center.x - size / 2, y: center.y - size / 2, width: size, height: size)
        case let .magnifier(center, radius, _):
            return CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
        }
    }

    /// Whether a click at `point` lands on this annotation. Thin strokes get at least
    /// four points of reach, or a 2 pixel line could never be picked up.
    public func contains(_ point: CGPoint, scale: CGFloat) -> Bool {
        let size = pixelSize(scale: scale)
        let reach = max(size / 2, 4 * scale)
        switch kind {
        case let .arrow(from, to):
            let head = ArrowShape.headHalfWidth(width: size, length: from.distance(to: to))
            return point.distance(toSegmentFrom: from, to: to) <= max(reach, head)
        case let .line(from, to), let .highlighter(from, to):
            return point.distance(toSegmentFrom: from, to: to) <= reach
        case let .measure(from, to):
            // The line, or its label, which is the easiest part of it to aim at.
            return point.distance(toSegmentFrom: from, to: to) <= reach
                || MeasureShape.tag(from: from, to: to, width: size / scale, scale: scale, at: labelAt).rect.contains(point)
        case let .rectangle(rect):
            if style.filled { return rect.insetBy(dx: -reach, dy: -reach).contains(point) }
            let inner = rect.insetBy(dx: reach, dy: reach)
            let inside = !inner.isNull && inner.contains(point)
            return rect.insetBy(dx: -reach, dy: -reach).contains(point) && !inside
        case let .oval(rect):
            if style.filled { return Self.ellipse(rect.insetBy(dx: -reach, dy: -reach), contains: point) }
            let inner = rect.insetBy(dx: reach, dy: reach)
            let inside = inner.width > 0 && inner.height > 0 && Self.ellipse(inner, contains: point)
            return Self.ellipse(rect.insetBy(dx: -reach, dy: -reach), contains: point) && !inside
        case let .spotlight(rect), let .blur(rect), let .pixelate(rect), let .erase(rect), let .image(rect, _):
            return rect.contains(point)
        case .text, .step:
            return bounds(scale: scale).insetBy(dx: -reach, dy: -reach).contains(point)
        case let .freehand(points), let .highlighterPath(points):
            if points.count == 1 { return point.distance(to: points[0]) <= reach }
            return zip(points, points.dropFirst()).contains { point.distance(toSegmentFrom: $0, to: $1) <= reach }
        case let .magnifier(center, radius, _):
            return point.distance(to: center) <= radius
        }
    }

    private static func ellipse(_ rect: CGRect, contains point: CGPoint) -> Bool {
        guard rect.width > 0, rect.height > 0 else { return false }
        let x = (point.x - rect.midX) / (rect.width / 2)
        let y = (point.y - rect.midY) / (rect.height / 2)
        return x * x + y * y <= 1
    }

    public func moved(by vector: CGVector) -> Annotation {
        var copy = self
        func shift(_ rect: CGRect) -> CGRect { rect.offsetBy(dx: vector.dx, dy: vector.dy) }
        switch kind {
        case let .arrow(from, to): copy.kind = .arrow(from: from.offset(by: vector), to: to.offset(by: vector))
        case let .line(from, to): copy.kind = .line(from: from.offset(by: vector), to: to.offset(by: vector))
        case let .highlighter(from, to): copy.kind = .highlighter(from: from.offset(by: vector), to: to.offset(by: vector))
        case let .measure(from, to): copy.kind = .measure(from: from.offset(by: vector), to: to.offset(by: vector))
        case let .rectangle(rect): copy.kind = .rectangle(shift(rect))
        case let .oval(rect): copy.kind = .oval(shift(rect))
        case let .spotlight(rect): copy.kind = .spotlight(shift(rect))
        case let .blur(rect): copy.kind = .blur(shift(rect))
        case let .pixelate(rect): copy.kind = .pixelate(shift(rect))
        case let .erase(rect): copy.kind = .erase(shift(rect))
        case let .image(rect, image): copy.kind = .image(shift(rect), image)
        case let .text(origin, string): copy.kind = .text(origin: origin.offset(by: vector), string: string)
        case let .freehand(points): copy.kind = .freehand(points.map { $0.offset(by: vector) })
        case let .highlighterPath(points): copy.kind = .highlighterPath(points.map { $0.offset(by: vector) })
        case let .step(center): copy.kind = .step(center: center.offset(by: vector))
        case let .magnifier(center, radius, zoom): copy.kind = .magnifier(center: center.offset(by: vector), radius: radius, zoom: zoom)
        }
        return copy
    }

    /// Where the resize handles sit. Text, steps and freehand strokes only move.
    public func handles(scale: CGFloat) -> [(Handle, CGPoint)] {
        switch kind {
        case let .arrow(from, to), let .line(from, to), let .highlighter(from, to), let .measure(from, to):
            return [(.start, from), (.end, to)]
        case let .rectangle(rect), let .oval(rect), let .spotlight(rect),
             let .blur(rect), let .pixelate(rect), let .erase(rect), let .image(rect, _):
            return rect.handlePoints
        case .magnifier:
            return Array(bounds(scale: scale).handlePoints.prefix(4))
        case .text, .freehand, .highlighterPath, .step:
            return []
        }
    }

    /// This annotation with one handle dragged to `point`. `constrained` is Shift:
    /// 45 degree steps for lines, squares for boxes.
    public func resized(dragging handle: Handle, to point: CGPoint, constrained: Bool) -> Annotation {
        var copy = self
        func line(_ from: CGPoint, _ to: CGPoint) -> (CGPoint, CGPoint) {
            switch handle {
            case .start: return (constrained ? point.snapped45(from: to) : point, to)
            case .end: return (from, constrained ? point.snapped45(from: from) : point)
            default: return (from, to)
            }
        }
        func box(_ rect: CGRect) -> CGRect { rect.resized(dragging: handle, to: point, square: constrained) }

        switch kind {
        case let .arrow(from, to): let (a, b) = line(from, to); copy.kind = .arrow(from: a, to: b)
        case let .line(from, to): let (a, b) = line(from, to); copy.kind = .line(from: a, to: b)
        case let .highlighter(from, to): let (a, b) = line(from, to); copy.kind = .highlighter(from: a, to: b)
        case let .measure(from, to): let (a, b) = line(from, to); copy.kind = .measure(from: a, to: b)
        case let .rectangle(rect): copy.kind = .rectangle(box(rect))
        case let .oval(rect): copy.kind = .oval(box(rect))
        case let .spotlight(rect): copy.kind = .spotlight(box(rect))
        case let .blur(rect): copy.kind = .blur(box(rect))
        case let .pixelate(rect): copy.kind = .pixelate(box(rect))
        case let .erase(rect): copy.kind = .erase(box(rect))
        case let .image(rect, image): copy.kind = .image(box(rect), image)
        case let .magnifier(center, _, zoom):
            let radius = max(abs(point.x - center.x), abs(point.y - center.y), 8)
            copy.kind = .magnifier(center: center, radius: radius, zoom: zoom)
        case .text, .freehand, .highlighterPath, .step:
            break
        }
        return copy
    }

    /// Too small to keep. A click without a drag makes these, and they would be
    /// invisible objects the user never meant to create.
    public func isDegenerate(scale: CGFloat) -> Bool {
        let minimum = 2 * scale
        switch kind {
        case let .arrow(from, to), let .line(from, to), let .highlighter(from, to), let .measure(from, to):
            return from.distance(to: to) < minimum
        case let .rectangle(rect), let .oval(rect), let .spotlight(rect),
             let .blur(rect), let .pixelate(rect), let .erase(rect), let .image(rect, _):
            return rect.width < minimum || rect.height < minimum
        case let .freehand(points), let .highlighterPath(points):
            return points.count < 2
        case let .text(_, string):
            return string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case .step, .magnifier:
            return false
        }
    }
}
