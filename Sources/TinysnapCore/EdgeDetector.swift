import CoreGraphics

/// A capture's brightness, one byte a pixel, row 0 at the top. Read once per capture, so
/// the walks below cost nothing per pointer move. A byte rather than a double: a 5K
/// capture is 15 MB this way and eight times that the other.
public struct LuminanceBuffer: Sendable {
    public let width: Int
    public let height: Int
    private let values: [UInt8]

    public init?(image: CGImage) {
        guard let pixels = PixelBuffer(image: image) else { return nil }
        var values = [UInt8](repeating: 0, count: pixels.width * pixels.height)
        for index in values.indices {
            let at = index * 4
            let r = Double(pixels.bytes[at]), g = Double(pixels.bytes[at + 1]), b = Double(pixels.bytes[at + 2])
            values[index] = UInt8((0.2126 * r + 0.7152 * g + 0.0722 * b).rounded())
        }
        self.init(width: pixels.width, height: pixels.height, values: values)
    }

    init(width: Int, height: Int, values: [UInt8]) {
        self.width = width
        self.height = height
        self.values = values
    }

    /// 0 for black to 1 for white.
    public func luminance(x: Int, y: Int) -> Double {
        Double(values[y * width + x]) / 255
    }
}

/// Finds where the region under a point ends, by walking out from it until the
/// brightness differs from the point's by at least `threshold` and stays different for
/// `runLength` pixels. Ported from Caliper, which does the same on a live screen.
public struct EdgeDetector: Sendable {
    public enum Direction: Sendable {
        case left, right, up, down
    }

    /// Brightness difference, 0 to 1, that counts as an edge.
    public var threshold: Double
    /// How many pixels in a row must stay changed. Rejects noise and soft shadows.
    public var runLength: Int

    public init(threshold: Double, runLength: Int) {
        self.threshold = threshold
        self.runLength = runLength
    }

    /// An edge counts once it has held for one point, however many pixels that is. A
    /// fixed run of three pixels was a point and a half on a Retina capture, and the
    /// hairline between two table rows, a point thick, could never satisfy it.
    public init(threshold: Double, scale: CGFloat) {
        self.init(threshold: threshold, runLength: max(1, Int(scale.rounded())))
    }

    /// The last pixel still in the region along `direction`, or nil when the region runs
    /// to the capture's border without an edge.
    public func firstEdge(from origin: (x: Int, y: Int), direction: Direction, in buffer: LuminanceBuffer) -> Int? {
        let move = step(direction)
        let reference = buffer.luminance(x: origin.x, y: origin.y)
        var x = origin.x, y = origin.y
        while true {
            let nextX = x + move.dx, nextY = y + move.dy
            guard nextX >= 0, nextY >= 0, nextX < buffer.width, nextY < buffer.height else { return nil }
            if abs(buffer.luminance(x: nextX, y: nextY) - reference) >= threshold,
               holds(from: (nextX, nextY), direction: direction, reference: reference, in: buffer) {
                return direction == .left || direction == .right ? x : y
            }
            x = nextX
            y = nextY
        }
    }

    /// The region around `origin`, in pixels, closed by the capture's border wherever no
    /// edge closes it. Nil on a flat field, where no walk finds anything.
    public func bounds(around origin: (x: Int, y: Int), in buffer: LuminanceBuffer) -> CGRect? {
        let left = firstEdge(from: origin, direction: .left, in: buffer)
        let right = firstEdge(from: origin, direction: .right, in: buffer)
        let top = firstEdge(from: origin, direction: .up, in: buffer)
        let bottom = firstEdge(from: origin, direction: .down, in: buffer)
        guard left != nil || right != nil || top != nil || bottom != nil else { return nil }
        let x = left ?? 0, y = top ?? 0
        return CGRect(x: x, y: y, width: (right ?? buffer.width - 1) - x + 1, height: (bottom ?? buffer.height - 1) - y + 1)
    }

    /// A single stray pixel, or an antialiased fringe, is not an edge.
    private func holds(from start: (x: Int, y: Int), direction: Direction, reference: Double, in buffer: LuminanceBuffer) -> Bool {
        let move = step(direction)
        for offset in 0..<runLength {
            let x = start.x + move.dx * offset, y = start.y + move.dy * offset
            guard x >= 0, y >= 0, x < buffer.width, y < buffer.height else { return true }
            if abs(buffer.luminance(x: x, y: y) - reference) < threshold { return false }
        }
        return true
    }

    private func step(_ direction: Direction) -> (dx: Int, dy: Int) {
        switch direction {
        case .left: (-1, 0)
        case .right: (1, 0)
        case .up: (0, -1)
        case .down: (0, 1)
        }
    }
}
