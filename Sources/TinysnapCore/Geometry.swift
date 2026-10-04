import CoreGraphics

// Every coordinate in TinysnapCore is in capture pixels with y growing downward, the
// way the captured image is stored. The one flip to AppKit's y up space happens in
// the canvas view, and nothing below it flips again.

/// A grab point on a selected annotation or on the crop box.
public enum Handle: CaseIterable, Sendable {
    case start, end
    case topLeft, top, topRight, right, bottomRight, bottom, bottomLeft, left
}

extension CGPoint {
    public func distance(to other: CGPoint) -> CGFloat {
        hypot(other.x - x, other.y - y)
    }

    public func offset(by vector: CGVector) -> CGPoint {
        CGPoint(x: x + vector.dx, y: y + vector.dy)
    }

    /// This point moved onto the nearest of the eight 45 degree directions from
    /// `origin`. Projected rather than rotated, and written without trigonometry,
    /// so a 100 pixel drag along an axis stays exactly 100 instead of picking up
    /// the hypotenuse or a rounding error.
    public func snapped45(from origin: CGPoint) -> CGPoint {
        let dx = x - origin.x
        let dy = y - origin.y
        let ax = abs(dx)
        let ay = abs(dy)
        let tan22 = 0.41421356237

        if ay <= ax * tan22 { return CGPoint(x: origin.x + dx, y: origin.y) }
        if ax <= ay * tan22 { return CGPoint(x: origin.x, y: origin.y + dy) }

        let diagonal = (ax + ay) / 2
        return CGPoint(x: origin.x + (dx < 0 ? -diagonal : diagonal),
                       y: origin.y + (dy < 0 ? -diagonal : diagonal))
    }

    /// The corner that makes a square with `anchor`, keeping the drag's direction.
    public func squared(from anchor: CGPoint) -> CGPoint { fitted(from: anchor, ratio: 1) }

    /// This point moved so the box from `anchor` to it has `ratio`, width over height, on the
    /// drag's longer side. A drag taller than it is wide turns the ratio upright.
    public func fitted(from anchor: CGPoint, ratio: CGFloat) -> CGPoint {
        let dx = x - anchor.x
        let dy = y - anchor.y
        let shape = abs(dx) >= abs(dy) ? ratio : 1 / ratio
        let width = max(abs(dx), abs(dy) * shape)
        let height = width / shape
        return CGPoint(x: anchor.x + (dx < 0 ? -width : width), y: anchor.y + (dy < 0 ? -height : height))
    }

    public func distance(toSegmentFrom a: CGPoint, to b: CGPoint) -> CGFloat {
        let abx = b.x - a.x
        let aby = b.y - a.y
        let lengthSquared = abx * abx + aby * aby
        guard lengthSquared > 0 else { return distance(to: a) }
        let t = max(0, min(1, ((x - a.x) * abx + (y - a.y) * aby) / lengthSquared))
        return distance(to: CGPoint(x: a.x + t * abx, y: a.y + t * aby))
    }
}

extension CGRect {
    /// The rectangle spanned by two corners, given in either order.
    public init(corner a: CGPoint, corner b: CGPoint) {
        self.init(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
    }

    public var center: CGPoint { CGPoint(x: midX, y: midY) }

    /// The eight resize handles, corners first.
    public var handlePoints: [(Handle, CGPoint)] {
        [
            (.topLeft, CGPoint(x: minX, y: minY)),
            (.topRight, CGPoint(x: maxX, y: minY)),
            (.bottomRight, CGPoint(x: maxX, y: maxY)),
            (.bottomLeft, CGPoint(x: minX, y: maxY)),
            (.top, CGPoint(x: midX, y: minY)),
            (.right, CGPoint(x: maxX, y: midY)),
            (.bottom, CGPoint(x: midX, y: maxY)),
            (.left, CGPoint(x: minX, y: midY)),
        ]
    }

    /// This rectangle with one handle dragged to `point`. Always computed from the
    /// rectangle as it was when the drag began, so dragging a corner past the opposite
    /// one flips the box cleanly instead of the handle swapping meaning mid drag.
    public func resized(dragging handle: Handle, to point: CGPoint, square: Bool) -> CGRect {
        resized(dragging: handle, to: point, ratio: square ? 1 : nil)
    }

    /// With a `ratio`, width over height, a corner keeps it and an edge takes the other side
    /// along with it, about that side's middle.
    public func resized(dragging handle: Handle, to point: CGPoint, ratio: CGFloat?) -> CGRect {
        switch handle {
        case .topLeft: return corner(anchor: CGPoint(x: maxX, y: maxY), to: point, ratio: ratio)
        case .topRight: return corner(anchor: CGPoint(x: minX, y: maxY), to: point, ratio: ratio)
        case .bottomRight: return corner(anchor: CGPoint(x: minX, y: minY), to: point, ratio: ratio)
        case .bottomLeft: return corner(anchor: CGPoint(x: maxX, y: minY), to: point, ratio: ratio)
        case .top, .bottom:
            let edge = handle == .top
                ? CGRect(corner: CGPoint(x: minX, y: point.y), corner: CGPoint(x: maxX, y: maxY))
                : CGRect(corner: CGPoint(x: minX, y: minY), corner: CGPoint(x: maxX, y: point.y))
            guard let ratio else { return edge }
            let width = edge.height * (width >= height ? ratio : 1 / ratio)
            return CGRect(x: midX - width / 2, y: edge.minY, width: width, height: edge.height)
        case .left, .right:
            let edge = handle == .left
                ? CGRect(corner: CGPoint(x: point.x, y: minY), corner: CGPoint(x: maxX, y: maxY))
                : CGRect(corner: CGPoint(x: minX, y: minY), corner: CGPoint(x: point.x, y: maxY))
            guard let ratio else { return edge }
            let height = edge.width / (width >= height ? ratio : 1 / ratio)
            return CGRect(x: edge.minX, y: midY - height / 2, width: edge.width, height: height)
        case .start, .end: return self
        }
    }

    private func corner(anchor: CGPoint, to point: CGPoint, ratio: CGFloat?) -> CGRect {
        CGRect(corner: anchor, corner: ratio.map { point.fitted(from: anchor, ratio: $0) } ?? point)
    }

    /// The largest box of `ratio` inside this one, about its middle, upright when this one is.
    public func trimmed(toRatio ratio: CGFloat) -> CGRect {
        let shape = width >= height ? ratio : 1 / ratio
        let size = width / height > shape ? CGSize(width: height * shape, height: height)
                                          : CGSize(width: width, height: width / shape)
        return CGRect(x: midX - size.width / 2, y: midY - size.height / 2, width: size.width, height: size.height)
    }

    /// The box a drag from `anchor` to `point` draws, the way Photoshop does: from the
    /// corner, or with `fromCentre` (Option) out from `anchor` as its centre, and square
    /// when `square` (Shift). The editor and the capture overlay both draw with this.
    public static func dragged(from anchor: CGPoint, to point: CGPoint, square: Bool, fromCentre: Bool) -> CGRect {
        dragged(from: anchor, to: point, ratio: square ? 1 : nil, fromCentre: fromCentre)
    }

    /// The same, keeping `ratio`, width over height, when there is one.
    public static func dragged(from anchor: CGPoint, to point: CGPoint, ratio: CGFloat?, fromCentre: Bool) -> CGRect {
        let end = ratio.map { point.fitted(from: anchor, ratio: $0) } ?? point
        guard fromCentre else { return CGRect(corner: anchor, corner: end) }
        let halfWidth = abs(end.x - anchor.x)
        let halfHeight = abs(end.y - anchor.y)
        return CGRect(x: anchor.x - halfWidth, y: anchor.y - halfHeight, width: halfWidth * 2, height: halfHeight * 2)
    }

    /// How far this box can move by `vector` and stay inside `bounds`, for moving a box
    /// with Space held.
    public func allowedMove(by vector: CGVector, within bounds: CGRect) -> CGVector {
        CGVector(dx: min(max(vector.dx, bounds.minX - minX), bounds.maxX - maxX),
                 dy: min(max(vector.dy, bounds.minY - minY), bounds.maxY - maxY))
    }

    /// Snapped outward to whole pixels, for the crop box.
    public var wholePixels: CGRect {
        CGRect(corner: CGPoint(x: minX.rounded(), y: minY.rounded()),
               corner: CGPoint(x: maxX.rounded(), y: maxY.rounded()))
    }
}
