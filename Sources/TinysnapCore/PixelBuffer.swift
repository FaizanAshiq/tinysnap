import CoreGraphics

/// Raw sRGB bytes, premultiplied RGBA, row 0 at the top. Used where the renderer has
/// to work pixel by pixel: erase fills and redaction noise.
struct PixelBuffer {
    let width: Int
    let height: Int
    var bytes: [UInt8]

    /// The sizes are held in locals first. Reading them off self inside the closure
    /// would capture a half built value, which the compiler refuses.
    init?(image: CGImage) {
        let pixelWidth = image.width
        let pixelHeight = image.height
        var buffer = [UInt8](repeating: 0, count: pixelWidth * pixelHeight * 4)
        let drew = buffer.withUnsafeMutableBytes { raw -> Bool in
            guard let context = Self.context(raw.baseAddress, width: pixelWidth, height: pixelHeight) else { return false }
            context.draw(image, in: CGRect(x: 0, y: 0, width: pixelWidth, height: pixelHeight))
            return true
        }
        guard drew else { return nil }
        width = pixelWidth
        height = pixelHeight
        bytes = buffer
    }

    func makeImage() -> CGImage? {
        var copy = bytes
        let pixelWidth = width
        let pixelHeight = height
        return copy.withUnsafeMutableBytes { raw in
            Self.context(raw.baseAddress, width: pixelWidth, height: pixelHeight)?.makeImage()
        }
    }

    func pixel(x: Int, y: Int) -> (r: UInt8, g: UInt8, b: UInt8, a: UInt8) {
        let index = (y * width + x) * 4
        return (bytes[index], bytes[index + 1], bytes[index + 2], bytes[index + 3])
    }

    private static func context(_ data: UnsafeMutableRawPointer?, width: Int, height: Int) -> CGContext? {
        guard let space = CGColorSpace(name: CGColorSpace.sRGB) else { return nil }
        return CGContext(data: data, width: width, height: height, bitsPerComponent: 8,
                         bytesPerRow: width * 4, space: space,
                         bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
    }

    /// Fills the `inner` box by blending the pixels just outside each of its edges,
    /// weighted by how near each edge is. A flat background comes back exactly, a
    /// gradient comes back as a gradient. An edge that has nothing outside it, because
    /// the box touches the border of the buffer, is left out of the blend.
    mutating func eraseFill(inner: (x: Int, y: Int, width: Int, height: Int)) {
        let left = inner.x - 1
        let right = inner.x + inner.width
        let top = inner.y - 1
        let bottom = inner.y + inner.height
        let hasLeft = left >= 0
        let hasRight = right < width
        let hasTop = top >= 0
        let hasBottom = bottom < height

        guard hasLeft || hasRight || hasTop || hasBottom else {
            fillWithAverage(inner: inner)
            return
        }

        let rowBytes = width * 4
        bytes.withUnsafeMutableBufferPointer { pixels in
            // The edges sit outside the box, so filling it never changes a pixel that is
            // still to be read.
            for y in inner.y..<(inner.y + inner.height) {
                for x in inner.x..<(inner.x + inner.width) {
                    var r = 0.0, g = 0.0, b = 0.0, a = 0.0, weights = 0.0
                    func add(_ sx: Int, _ sy: Int, _ distance: Int) {
                        let weight = 1 / Double(distance)
                        let index = sy * rowBytes + sx * 4
                        r += Double(pixels[index]) * weight
                        g += Double(pixels[index + 1]) * weight
                        b += Double(pixels[index + 2]) * weight
                        a += Double(pixels[index + 3]) * weight
                        weights += weight
                    }
                    if hasLeft { add(left, y, x - left) }
                    if hasRight { add(right, y, right - x) }
                    if hasTop { add(x, top, y - top) }
                    if hasBottom { add(x, bottom, bottom - y) }
                    let index = y * rowBytes + x * 4
                    pixels[index] = UInt8((r / weights).rounded())
                    pixels[index + 1] = UInt8((g / weights).rounded())
                    pixels[index + 2] = UInt8((b / weights).rounded())
                    pixels[index + 3] = UInt8((a / weights).rounded())
                }
            }
        }
    }

    /// Only when the box covers the whole buffer: there is nothing outside to blend.
    private mutating func fillWithAverage(inner: (x: Int, y: Int, width: Int, height: Int)) {
        var total = [0, 0, 0, 0]
        let count = max(1, inner.width * inner.height)
        for index in stride(from: 0, to: bytes.count, by: 4) {
            for channel in 0..<4 { total[channel] += Int(bytes[index + channel]) }
        }
        let average = total.map { UInt8($0 / count) }
        for index in stride(from: 0, to: bytes.count, by: 4) {
            for channel in 0..<4 { bytes[index + channel] = average[channel] }
        }
    }

    /// Replaces each `block` square, counted from the top left corner, with its average.
    /// Averaged by hand: Core Graphics' downscaling samples rather than averages, which
    /// left one pixel wide detail standing inside the blocks.
    mutating func pixelate(block: Int) {
        let block = max(1, block)
        let pixelWidth = width
        let pixelHeight = height
        bytes.withUnsafeMutableBufferPointer { pixels in
            for top in stride(from: 0, to: pixelHeight, by: block) {
                for left in stride(from: 0, to: pixelWidth, by: block) {
                    let bottom = min(top + block, pixelHeight)
                    let right = min(left + block, pixelWidth)
                    var r = 0, g = 0, b = 0, a = 0
                    for y in top..<bottom {
                        for x in left..<right {
                            let index = (y * pixelWidth + x) * 4
                            r += Int(pixels[index])
                            g += Int(pixels[index + 1])
                            b += Int(pixels[index + 2])
                            a += Int(pixels[index + 3])
                        }
                    }
                    let count = (bottom - top) * (right - left)
                    for y in top..<bottom {
                        for x in left..<right {
                            let index = (y * pixelWidth + x) * 4
                            pixels[index] = UInt8(r / count)
                            pixels[index + 1] = UInt8(g / count)
                            pixels[index + 2] = UInt8(b / count)
                            pixels[index + 3] = UInt8(a / count)
                        }
                    }
                }
            }
        }
    }

    /// Fine grey noise, the same for the same canvas position every time it is drawn,
    /// so a blur does not shimmer while it is dragged. `origin` is where this buffer
    /// sits on the canvas.
    mutating func addNoise(amplitude: Int, origin: (x: Int, y: Int)) {
        let span = UInt32(amplitude * 2 + 1)
        let pixelWidth = width
        bytes.withUnsafeMutableBufferPointer { out in
            for y in 0..<height {
                for x in 0..<pixelWidth {
                    var hash = UInt32(truncatingIfNeeded: (x + origin.x) &* 73_856_093 ^ (y + origin.y) &* 19_349_663)
                    hash ^= hash >> 13
                    hash = hash &* 0x5BD1_E995
                    hash ^= hash >> 15
                    let noise = Int(hash % span) - amplitude
                    let index = (y * pixelWidth + x) * 4
                    let alpha = Int(out[index + 3])
                    for channel in 0..<3 {
                        out[index + channel] = UInt8(max(0, min(alpha, Int(out[index + channel]) + noise)))
                    }
                }
            }
        }
    }
}
