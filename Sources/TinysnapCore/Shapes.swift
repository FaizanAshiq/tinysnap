import CoreGraphics

/// A straight arrow whose body widens from a thin tail to a swept back head. The
/// renderer strokes the outline with round joins as well as filling it, which rounds
/// the tip, the barbs and the tail instead of leaving sharp peaks.
enum ArrowShape {
    /// Half the head's width, which is how far the arrow reaches either side of its line.
    static func headHalfWidth(width: CGFloat, length: CGFloat) -> CGFloat {
        headLength(width: width, length: length) * 0.62
    }

    private static func headLength(width: CGFloat, length: CGFloat) -> CGFloat {
        min(length * 0.5, width * 4.2)
    }

    static func path(from tail: CGPoint, to tip: CGPoint, width: CGFloat) -> CGPath {
        let path = CGMutablePath()
        let length = tail.distance(to: tip)
        guard length > 0 else { return path }

        let ux = (tip.x - tail.x) / length
        let uy = (tip.y - tail.y) / length
        let nx = -uy
        let ny = ux
        let head = headLength(width: width, length: length)
        let headHalf = headHalfWidth(width: width, length: length)
        let neckHalf = min(width * 0.6, headHalf * 0.5)
        let tailHalf = width * 0.18
        let base = CGPoint(x: tip.x - ux * head, y: tip.y - uy * head)
        // The barbs sit a little behind the neck, which sweeps the head back.
        let barb = CGPoint(x: base.x - ux * head * 0.12, y: base.y - uy * head * 0.12)

        func at(_ point: CGPoint, _ offset: CGFloat) -> CGPoint {
            CGPoint(x: point.x + nx * offset, y: point.y + ny * offset)
        }

        path.addLines(between: [
            at(tail, tailHalf), at(base, neckHalf), at(barb, headHalf), tip,
            at(barb, -headHalf), at(base, -neckHalf), at(tail, -tailHalf),
        ])
        path.closeSubpath()
        return path
    }
}

/// A smooth curve through a freehand stroke's recorded points, Catmull-Rom style.
enum Smoothing {
    static func path(through points: [CGPoint]) -> CGPath {
        let path = CGMutablePath()
        guard let first = points.first else { return path }
        path.move(to: first)
        guard points.count > 2 else {
            points.dropFirst().forEach { path.addLine(to: $0) }
            return path
        }
        for index in 0..<(points.count - 1) {
            let p0 = points[max(index - 1, 0)]
            let p1 = points[index]
            let p2 = points[index + 1]
            let p3 = points[min(index + 2, points.count - 1)]
            path.addCurve(to: p2,
                          control1: CGPoint(x: p1.x + (p2.x - p0.x) / 6, y: p1.y + (p2.y - p0.y) / 6),
                          control2: CGPoint(x: p2.x - (p3.x - p1.x) / 6, y: p2.y - (p3.y - p1.y) / 6))
        }
        return path
    }
}
