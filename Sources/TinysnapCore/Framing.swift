import CoreGraphics

/// The output, and the output inside its backdrop, which is what exports and the canvas
/// show once a backdrop is on.
extension Renderer {
    /// The output flattened: the whole canvas drawn, then the crop cut out on whole
    /// output pixels. Drawing only the crop left a redaction or magnifier at its edge less
    /// to read back than the canvas had, so the export came out different from the screen.
    public static func renderOutput(_ document: Document, outputScale: CGFloat = 1,
                                    hiding hidden: Set<Annotation.ID> = []) -> CGImage? {
        let region = document.extent
        guard let full = render(document, region: region, outputScale: outputScale, hiding: hidden) else { return nil }
        // The render starts at the extent's corner, left of or above the capture once the
        // canvas has grown.
        let rect = document.outputRect.offsetBy(dx: -region.minX, dy: -region.minY)
        let left = (rect.minX * outputScale).rounded(.up)
        let top = (rect.minY * outputScale).rounded(.up)
        let cut = CGRect(x: left, y: top,
                         width: max(1, (rect.maxX * outputScale).rounded(.down) - left),
                         height: max(1, (rect.maxY * outputScale).rounded(.down) - top))
            .intersection(CGRect(x: 0, y: 0, width: full.width, height: full.height))
        guard !cut.isNull else { return nil }
        return full.cropping(to: cut)
    }

    /// The output inside its backdrop, or the plain output when there is none.
    public static func renderFramed(_ document: Document, outputScale: CGFloat = 1,
                                    hiding hidden: Set<Annotation.ID> = []) -> CGImage? {
        guard let content = renderOutput(document, outputScale: outputScale, hiding: hidden) else { return nil }
        guard let backdrop = document.backdrop else { return content }
        return frame(content, in: backdrop, capture: document.capture, perPoint: document.scale * outputScale)
    }

    /// The document's output drawn over the last frame made for an output of its size:
    /// while a shape is drawn or moved only the output changes, and working out the
    /// shadow again was most of the time a frame took. Nil when the size has changed.
    /// Under see-through window corners the last output shows until a fresh frame.
    public static func reframe(_ document: Document, over last: CGImage, outputScale: CGFloat = 1,
                               hiding hidden: Set<Annotation.ID> = []) -> CGImage? {
        guard let backdrop = document.backdrop,
              let content = renderOutput(document, outputScale: outputScale, hiding: hidden) else { return nil }
        let place = placement(of: content, in: backdrop, perPoint: document.scale * outputScale)
        guard last.width == place.width, last.height == place.height,
              let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: place.width, height: place.height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        context.draw(last, in: CGRect(x: 0, y: 0, width: place.width, height: place.height))
        context.addPath(CGPath(roundedRect: place.box, cornerWidth: place.corner, cornerHeight: place.corner, transform: nil))
        context.clip()
        context.draw(content, in: place.box)
        return context.makeImage()
    }

    /// The frame's size, where the output sits in it, and how round its corners are.
    private static func placement(of content: CGImage, in backdrop: Backdrop,
                                  perPoint: CGFloat) -> (width: Int, height: Int, box: CGRect, corner: CGFloat) {
        let padding = Int((backdrop.padding.points * perPoint).rounded())
        let box = CGRect(x: padding, y: padding, width: content.width, height: content.height)
        let corner = min((backdrop.corners.points ?? .greatestFiniteMagnitude) * perPoint, min(box.width, box.height) / 2)
        return (content.width + padding * 2, content.height + padding * 2, box, corner)
    }

    /// The fill over everything, then a shadow under the output's rounded shape, then
    /// the output clipped to it. `perPoint` is output pixels per point.
    static func frame(_ content: CGImage, in backdrop: Backdrop, capture: Capture, perPoint: CGFloat) -> CGImage? {
        let (width, height, box, corner) = placement(of: content, in: backdrop, perPoint: perPoint)
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

        context.saveGState()
        if let (blur, drop, alpha) = shadow(backdrop.shadow) {
            context.setShadow(offset: CGSize(width: 0, height: -drop * perPoint), blur: blur * perPoint,
                              color: CGColor(gray: 0, alpha: alpha))
        }
        // One layer, so the shadow follows the clipped output's shape, see-through window
        // corners and all.
        context.beginTransparencyLayer(auxiliaryInfo: nil)
        context.addPath(CGPath(roundedRect: box, cornerWidth: corner, cornerHeight: corner, transform: nil))
        context.clip()
        context.draw(content, in: box)
        context.endTransparencyLayer()
        context.restoreGState()
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
