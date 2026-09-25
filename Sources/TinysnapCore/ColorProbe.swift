import CoreGraphics
import Foundation

/// The colour under the pointer, for the editor's readout that Tab copies.
public enum ColorProbe {
    /// "#RRGGBB" of one pixel, counted from the top left corner, or nil outside the
    /// image. The image is drawn shifted into a one pixel bitmap rather than decoded
    /// whole, so this is cheap enough to run on every pointer move.
    public static func hex(of image: CGImage, x: Int, y: Int) -> String? {
        guard x >= 0, y >= 0, x < image.width, y < image.height,
              let space = CGColorSpace(name: CGColorSpace.sRGB) else { return nil }
        var pixel: [UInt8] = [0, 0, 0, 0]
        let height = image.height
        let width = image.width
        let drew = pixel.withUnsafeMutableBytes { raw -> Bool in
            guard let context = CGContext(data: raw.baseAddress, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                                          space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
                return false
            }
            context.interpolationQuality = .none
            // Core Graphics counts y up from the bottom.
            context.draw(image, in: CGRect(x: -x, y: -(height - 1 - y), width: width, height: height))
            return true
        }
        guard drew else { return nil }
        let alpha = Int(pixel[3])
        guard alpha > 0 else { return nil }
        func channel(_ value: UInt8) -> Int { min(255, Int(value) * 255 / alpha) }
        return String(format: "#%02X%02X%02X", channel(pixel[0]), channel(pixel[1]), channel(pixel[2]))
    }
}
