import CoreGraphics

/// The output, and the output inside its backdrop, which is what exports and the canvas
/// show once a backdrop is on.
extension Renderer {
    /// The output flattened: the whole canvas drawn, then the crop cut out on whole
    /// output pixels. Drawing only the crop left a redaction or magnifier at its edge less
    /// to read back than the canvas had, so the export came out different from the screen.
    public static func renderOutput(_ document: Document, outputScale: CGFloat = 1,
                                    hiding hidden: Set<Annotation.ID> = []) -> CGImage? {
        guard let cut = outputCut(document, outputScale: outputScale),
              let full = render(document, region: document.extent, outputScale: outputScale, hiding: hidden) else { return nil }
        return full.cropping(to: cut)
    }

    /// Where the output is cut from a render of the whole extent, on whole output pixels
    /// rounded inwards. `Document.exportPixelSize(at:)` sizes exports with it too.
    static func outputCut(_ document: Document, outputScale: CGFloat) -> CGRect? {
        let region = document.extent
        // The render starts at the extent's corner, left of or above the capture once the
        // canvas has grown.
        let rect = document.outputRect.offsetBy(dx: -region.minX, dy: -region.minY)
        let left = (rect.minX * outputScale).rounded(.up)
        let top = (rect.minY * outputScale).rounded(.up)
        let cut = CGRect(x: left, y: top,
                         width: max(1, (rect.maxX * outputScale).rounded(.down) - left),
                         height: max(1, (rect.maxY * outputScale).rounded(.down) - top))
            .intersection(CGRect(origin: .zero, size: pixelSize(of: region, outputScale: outputScale)))
        return cut.isNull ? nil : cut
    }

    /// The output inside its backdrop, or the plain output when there is none.
    public static func renderFramed(_ document: Document, outputScale: CGFloat = 1,
                                    hiding hidden: Set<Annotation.ID> = []) -> CGImage? {
        var ground: FrameGround?
        return renderFramed(document, outputScale: outputScale, hiding: hidden, ground: &ground)
    }

    /// The same, drawn over `ground` while it still fits, and over a new one kept there
    /// when it does not. The shadow was most of what a frame cost, and a stroke, an undo
    /// or a restyle never moves it, so the canvas keeps one ground and pays for the output.
    public static func renderFramed(_ document: Document, outputScale: CGFloat = 1, hiding hidden: Set<Annotation.ID> = [],
                                    ground: inout FrameGround?) -> CGImage? {
        guard let content = renderOutput(document, outputScale: outputScale, hiding: hidden) else { return nil }
        guard let backdrop = document.backdrop else { return content }
        let perPoint = document.scale * outputScale
        let place = placement(of: content, in: backdrop, perPoint: perPoint)
        let key = FrameGround.Key(backdrop: backdrop, capture: ObjectIdentifier(document.capture),
                                  output: document.outputPixelRect, extent: document.extent, outputScale: outputScale)
        if ground?.key != key {
            // The shadow follows the output with nothing drawn on it: a window's see-through
            // corners shape it, the marks drawn on top do not.
            let bare = renderOutput(document, outputScale: outputScale, hiding: Set(document.annotations.map(\.id))) ?? content
            ground = makeGround(caster: bare, place: place, backdrop: backdrop, capture: document.capture, perPoint: perPoint)
                .map { FrameGround(image: $0, key: key) }
        }
        guard let ground else { return nil }
        return frame(content, over: ground.image, place: place)
    }

    /// The frame's size, where the output sits in it, and how round its corners are.
    private typealias Placement = (width: Int, height: Int, box: CGRect, corner: CGFloat)

    private static func placement(of content: CGImage, in backdrop: Backdrop, perPoint: CGFloat) -> Placement {
        let padding = framePadding(backdrop, perPoint: perPoint)
        let box = CGRect(x: padding, y: padding, width: content.width, height: content.height)
        let corner = min((backdrop.corners.points ?? .greatestFiniteMagnitude) * perPoint, min(box.width, box.height) / 2)
        return (content.width + padding * 2, content.height + padding * 2, box, corner)
    }

    /// The backdrop's padding on each side, in whole output pixels.
    static func framePadding(_ backdrop: Backdrop, perPoint: CGFloat) -> Int {
        Int((backdrop.padding.points * perPoint).rounded())
    }

    /// The fill over everything, then the shadow `caster` throws under the output's
    /// rounded shape, with the caster taken back out: its pixels are the output's to draw.
    /// `perPoint` is output pixels per point.
    private static func makeGround(caster: CGImage, place: Placement, backdrop: Backdrop, capture: Capture,
                                   perPoint: CGFloat) -> CGImage? {
        let (width, height, box, corner) = place
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        // Plain Core Graphics space here, y growing upward: the frame is symmetric, and
        // only the shadow's direction and the gradient's corners need to know.
        let whole = CGRect(x: 0, y: 0, width: width, height: height)
        // Top left to bottom right, in the capture's own two colours.
        func paintGradient() {
            guard let gradient = CGGradient(colorsSpace: space, colors: capture.gradientColors as CFArray, locations: [0, 1]) else { return }
            context.drawLinearGradient(gradient, start: CGPoint(x: 0, y: whole.maxY), end: CGPoint(x: whole.maxX, y: 0), options: [])
        }
        switch backdrop.fill {
        case .solid:
            context.setFillColor(Palette.color(hex: backdrop.colorHex))
            context.fill(whole)
        case .clear:
            break
        case .gradient:
            paintGradient()
        case .wallpaper:
            // A wallpaper that could not be read draws the gradient instead.
            guard let picture = backdrop.wallpaper?.image.image else {
                paintGradient()
                break
            }
            let fit = max(whole.width / CGFloat(picture.width), whole.height / CGFloat(picture.height))
            let size = CGSize(width: CGFloat(picture.width) * fit, height: CGFloat(picture.height) * fit)
            context.interpolationQuality = .high
            context.draw(picture, in: CGRect(x: (whole.width - size.width) / 2, y: (whole.height - size.height) / 2,
                                             width: size.width, height: size.height))
        }

        if let (blur, drop, alpha) = shadow(backdrop.shadow),
           let layer = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                 space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) {
            let shape = CGPath(roundedRect: box, cornerWidth: corner, cornerHeight: corner, transform: nil)
            layer.saveGState()
            layer.setShadow(offset: CGSize(width: 0, height: -drop * perPoint), blur: blur * perPoint,
                            color: CGColor(gray: 0, alpha: alpha))
            // One layer, so the shadow follows the clipped output's shape, see-through
            // window corners and all.
            layer.beginTransparencyLayer(auxiliaryInfo: nil)
            layer.addPath(shape)
            layer.clip()
            layer.draw(caster, in: box)
            layer.endTransparencyLayer()
            layer.restoreGState()
            // Then the caster out again, leaving its shadow where the output does not cover it.
            layer.setBlendMode(.destinationOut)
            layer.addPath(shape)
            layer.clip()
            layer.draw(caster, in: box)
            if let shade = layer.makeImage() { context.draw(shade, in: whole) }
        }
        return context.makeImage()
    }

    /// The output over its ground, clipped to its rounded shape.
    private static func frame(_ content: CGImage, over ground: CGImage, place: Placement) -> CGImage? {
        let (width, height, box, corner) = place
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        context.draw(ground, in: CGRect(x: 0, y: 0, width: width, height: height))
        context.addPath(CGPath(roundedRect: box, cornerWidth: corner, cornerHeight: corner, transform: nil))
        context.clip()
        context.draw(content, in: box)
        return context.makeImage()
    }

    /// Blur and drop in points, and how dark.
    private static func shadow(_ shadow: Backdrop.Shadow) -> (CGFloat, CGFloat, CGFloat)? {
        switch shadow {
        case .none: nil
        case .soft: (24, 10, 0.3)
        case .strong: (40, 18, 0.45)
        }
    }
}

/// A frame without its output: the backdrop's fill and the shadow the output throws on it.
public struct FrameGround {
    public let image: CGImage
    fileprivate let key: Key

    /// Everything the ground depends on. The annotations are not in it, which is the point.
    fileprivate struct Key: Equatable {
        let backdrop: Backdrop
        let capture: ObjectIdentifier
        let output: CGRect
        let extent: CGRect
        let outputScale: CGFloat
    }
}
