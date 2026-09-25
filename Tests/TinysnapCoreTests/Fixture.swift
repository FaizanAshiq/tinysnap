import CoreGraphics
@testable import TinysnapCore

/// Synthetic captures for tests, painted in the same y down space as the rest of Core.
enum Fixture {
    static let white = CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1)
    static let black = CGColor(srgbRed: 0, green: 0, blue: 0, alpha: 1)
    static let blue = CGColor(srgbRed: 0, green: 0, blue: 1, alpha: 1)
    static let green = CGColor(srgbRed: 0, green: 1, blue: 0, alpha: 1)
    static let red = Style(colorHex: Palette.red)

    static func capture(width: Int, height: Int, scale: CGFloat = 1, fill: CGColor = white,
                        paint: ((CGContext) -> Void)? = nil) -> Capture {
        let space = CGColorSpace(name: CGColorSpace.sRGB)!
        let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.setFillColor(fill)
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        context.translateBy(x: 0, y: CGFloat(height))
        context.scaleBy(x: 1, y: -1)
        paint?(context)
        return Capture(image: context.makeImage()!, scale: scale)
    }

    static func annotation(_ kind: Annotation.Kind, style: Style = red) -> Annotation {
        Annotation(kind: kind, style: style)
    }
}
