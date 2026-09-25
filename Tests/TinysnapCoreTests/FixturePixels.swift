import CoreGraphics
@testable import TinysnapCore

/// Reading pixels back needs the renderer's PixelBuffer, so these arrive with it.
extension Fixture {
    static func pixel(_ image: CGImage, _ x: Int, _ y: Int) -> (r: Int, g: Int, b: Int) {
        let p = PixelBuffer(image: image)!.pixel(x: x, y: y)
        return (Int(p.r), Int(p.g), Int(p.b))
    }

    static func isClose(_ pixel: (r: Int, g: Int, b: Int), _ expected: (r: Int, g: Int, b: Int), within tolerance: Int = 3) -> Bool {
        abs(pixel.r - expected.r) <= tolerance && abs(pixel.g - expected.g) <= tolerance && abs(pixel.b - expected.b) <= tolerance
    }
}
