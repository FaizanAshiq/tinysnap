import CoreGraphics

/// How a measurement is drawn: the line, a tick across each end, and its length on a tag
/// at the middle. The renderer and the Measure tool's live reading both draw through
/// here, so the reading looks exactly like what a click keeps.
public enum MeasureShape {
    /// Tick length and label size follow the line's width, all in points: an eight point
    /// tick at the medium two point line.
    static func tickLength(width: CGFloat) -> CGFloat { 4 + width * 2 }
    static func labelPoints(width: CGFloat) -> CGFloat { 9 + width * 1.5 }

    /// The tag's box and its text, in capture pixels. On the line's middle when the line
    /// has room for it with both ends showing; beside the line when it does not, above a
    /// line running across and right of one running down, since a small gap is often
    /// narrower than its own label and a centred tag hid what was measured.
    /// `at` is where along the line the tag sits, 0.5 unless it slid clear of another.
    static func tag(from: CGPoint, to: CGPoint, width: CGFloat, scale: CGFloat,
                    at: CGFloat = 0.5) -> (rect: CGRect, text: String, points: CGFloat) {
        let length = from.distance(to: to)
        let text = MeasureReading.label(forPixels: length, scale: scale)
        let points = labelPoints(width: width)
        let metrics = TextLayout.metrics(of: text, points: points, scale: scale, bold: true)
        let size = CGSize(width: metrics.width + 10 * scale, height: metrics.ascent + metrics.descent + 6 * scale)
        var middle = CGPoint(x: from.x + (to.x - from.x) * at, y: from.y + (to.y - from.y) * at)
        if length > 0 {
            // Pointing right, or down when upright, so beside means above or to the right.
            var along = CGVector(dx: (to.x - from.x) / length, dy: (to.y - from.y) / length)
            if along.dx < 0 || along.dx == 0 && along.dy < 0 { along = CGVector(dx: -along.dx, dy: -along.dy) }
            let tick = tickLength(width: width) * scale
            let alongExtent = abs(along.dx) * size.width + abs(along.dy) * size.height
            if length < alongExtent + tick * 2 {
                let beside = CGVector(dx: along.dy, dy: -along.dx)
                let away = tick / 2 + 2 * scale + abs(beside.dx) * size.width / 2 + abs(beside.dy) * size.height / 2
                middle = CGPoint(x: middle.x + beside.dx * away, y: middle.y + beside.dy * away)
            }
        }
        return (CGRect(x: middle.x - size.width / 2, y: middle.y - size.height / 2, width: size.width, height: size.height),
                text, points)
    }

    /// Everything drawn, ticks and tag included, for the selection outline.
    static func extent(from: CGPoint, to: CGPoint, width: CGFloat, scale: CGFloat, at: CGFloat = 0.5) -> CGRect {
        let tick = tickLength(width: width) * scale / 2
        return CGRect(corner: from, corner: to).insetBy(dx: -tick, dy: -tick)
            .union(tag(from: from, to: to, width: width, scale: scale, at: at).rect)
    }

    /// The lines of one reading with their tags clear of each other and of each other's
    /// line. Across and down through the middle of a card put both tags on the crossing, so
    /// each tag first slides along its own line off the other line, to whichever side is
    /// nearer its middle, and then the later one slides again if the two still meet.
    public static func clearTags(_ lines: [MeasureLine], width: CGFloat, scale: CGFloat) -> [MeasureLine] {
        guard lines.count == 2 else { return lines }
        var placed = lines
        func rect(_ line: MeasureLine) -> CGRect { tag(from: line.from, to: line.to, width: width, scale: scale, at: line.labelAt).rect }
        func stroke(_ line: MeasureLine) -> CGRect {
            CGRect(corner: line.from, corner: line.to).insetBy(dx: -width * scale / 2, dy: -width * scale / 2)
        }
        for (index, other) in [(0, 1), (1, 0)] where rect(placed[index]).intersects(stroke(placed[other])) {
            if let moved = slide(placed[index], clearOf: stroke(placed[other]), width: width, scale: scale) { placed[index] = moved }
        }
        guard rect(placed[0]).intersects(rect(placed[1])) else { return placed }
        if let moved = slide(placed[1], clearOf: rect(placed[0]), width: width, scale: scale) {
            placed[1] = moved
        } else if let moved = slide(placed[0], clearOf: rect(placed[1]), width: width, scale: scale) {
            placed[0] = moved
        }
        return placed
    }

    /// The line with its tag moved along it until it just clears `other`, on the side
    /// nearer the line's middle. Nil when neither side leaves the tag on the line with a
    /// tick's room at each end.
    private static func slide(_ line: MeasureLine, clearOf other: CGRect, width: CGFloat, scale: CGFloat) -> MeasureLine? {
        let length = line.from.distance(to: line.to)
        guard length > 0 else { return nil }
        let along = CGVector(dx: (line.to.x - line.from.x) / length, dy: (line.to.y - line.from.y) / length)
        let size = tag(from: line.from, to: line.to, width: width, scale: scale).rect.size
        let extent = abs(along.dx) * size.width + abs(along.dy) * size.height
        // The other tag's shadow on this line, as distances from its start.
        let corners = [CGPoint(x: other.minX, y: other.minY), CGPoint(x: other.maxX, y: other.minY),
                       CGPoint(x: other.minX, y: other.maxY), CGPoint(x: other.maxX, y: other.maxY)]
        let shadow = corners.map { ($0.x - line.from.x) * along.dx + ($0.y - line.from.y) * along.dy }
        let gap = 4 * scale
        let room = extent / 2 + tickLength(width: width) * scale
        let candidates = [shadow.min()! - gap - extent / 2, shadow.max()! + gap + extent / 2]
            .filter { $0 >= room && $0 <= length - room }
            .sorted { abs($0 - length / 2) < abs($1 - length / 2) }
        for distance in candidates {
            var moved = line
            moved.labelAt = distance / length
            if !tag(from: moved.from, to: moved.to, width: width, scale: scale, at: moved.labelAt).rect.intersects(other) {
                return moved
            }
        }
        return nil
    }

    /// `width` is the line's width in points. User space is capture pixels, y down.
    public static func draw(from: CGPoint, to: CGPoint, width: CGFloat, color: CGColor, scale: CGFloat,
                            at: CGFloat = 0.5, in context: CGContext) {
        let length = from.distance(to: to)
        guard length > 0 else { return }
        context.saveGState()
        defer { context.restoreGState() }
        context.setStrokeColor(color)
        context.setLineWidth(width * scale)
        context.setLineCap(.butt)
        // Each tick crosses its end at a right angle to the line.
        let half = tickLength(width: width) * scale / 2
        let across = CGVector(dx: -(to.y - from.y) / length * half, dy: (to.x - from.x) / length * half)
        var segments = [from, to]
        for end in [from, to] {
            segments += [CGPoint(x: end.x - across.dx, y: end.y - across.dy), CGPoint(x: end.x + across.dx, y: end.y + across.dy)]
        }
        context.strokeLineSegments(between: segments)

        let tag = tag(from: from, to: to, width: width, scale: scale, at: at)
        context.setFillColor(color)
        context.addPath(CGPath(roundedRect: tag.rect, cornerWidth: 4 * scale, cornerHeight: 4 * scale, transform: nil))
        context.fillPath()
        let metrics = TextLayout.metrics(of: tag.text, points: tag.points, scale: scale, bold: true)
        let origin = CGPoint(x: tag.rect.midX - metrics.width / 2, y: tag.rect.midY - (metrics.ascent + metrics.descent) / 2)
        TextLayout.draw(tag.text, at: origin, points: tag.points, scale: scale, color: textColor(on: color), bold: true, in: context)
    }

    /// White on a dark colour, black on a light one, so the length always reads.
    static func textColor(on color: CGColor) -> CGColor {
        let parts = color.converted(to: CGColorSpace(name: CGColorSpace.sRGB)!, intent: .defaultIntent, options: nil)?
            .components ?? [0, 0, 0]
        let luminance = 0.2126 * parts[0] + 0.7152 * parts[1] + 0.0722 * parts[2]
        return luminance > 0.6 ? CGColor(gray: 0, alpha: 1) : CGColor(gray: 1, alpha: 1)
    }
}
