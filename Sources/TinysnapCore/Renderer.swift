import CoreGraphics
import CoreImage
import Foundation

/// The one renderer. The canvas draws what this returns and export writes it, so what
/// you see is exactly what you copy.
public enum Renderer {
    /// Draws `region` of the document, in capture pixels, at `outputScale` output pixels
    /// per capture pixel. `hidden` leaves out annotations, used for the text being typed.
    public static func render(_ document: Document, region: CGRect? = nil, outputScale: CGFloat = 1,
                              hiding hidden: Set<Annotation.ID> = []) -> CGImage? {
        let region = region ?? document.capture.bounds
        // Rounded down: rounding 400.5 up gave a last column only half covered by the
        // capture, which came out half transparent.
        let width = max(1, Int((region.width * outputScale).rounded(.down)))
        let height = max(1, Int((region.height * outputScale).rounded(.down)))
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                                      bytesPerRow: 0, space: space,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }

        // From here on user space is capture pixels with y growing downward.
        context.translateBy(x: 0, y: CGFloat(height))
        context.scaleBy(x: outputScale, y: -outputScale)
        context.translateBy(x: -region.minX, y: -region.minY)

        let canvas = Canvas(context: context, region: region, outputScale: outputScale,
                            deviceSize: CGSize(width: width, height: height), scale: document.scale)
        canvas.draw(document.capture.image, in: document.capture.bounds)

        let visible = document.annotations.filter { !hidden.contains($0.id) }
        var spotlightDrawn = false
        for annotation in visible {
            if case .spotlight = annotation.kind {
                // Every spotlight lights one shared area, drawn at the first one's place.
                guard !spotlightDrawn else { continue }
                spotlightDrawn = true
                canvas.spotlight(visible.compactMap { if case let .spotlight(rect) = $0.kind { (rect, $0.style.corners) } else { nil } })
                continue
            }
            canvas.draw(annotation, stepNumber: document.stepNumber(of: annotation.id))
        }
        return context.makeImage()
    }
}

/// One render in progress. Redactions and the magnifier read back what has been drawn
/// so far, which is why they affect everything beneath them and nothing above.
struct Canvas {
    let context: CGContext
    let region: CGRect
    let outputScale: CGFloat
    let deviceSize: CGSize
    let scale: CGFloat

    private static let imageContext = CIContext(options: [.useSoftwareRenderer: false])

    /// Images are stored top row first, so each one is flipped into the y down space.
    func draw(_ image: CGImage, in rect: CGRect, crisp: Bool = false) {
        context.saveGState()
        context.interpolationQuality = crisp ? .none : .high
        context.translateBy(x: rect.minX, y: rect.maxY)
        context.scaleBy(x: 1, y: -1)
        context.draw(image, in: CGRect(origin: .zero, size: rect.size))
        context.restoreGState()
    }

    func draw(_ annotation: Annotation, stepNumber: Int?) {
        let size = annotation.pixelSize(scale: scale)
        let color = Palette.color(hex: annotation.style.colorHex)
        context.saveGState()
        defer { context.restoreGState() }
        context.setLineCap(.round)
        context.setLineJoin(.round)

        switch annotation.kind {
        case let .arrow(from, to):
            let path = ArrowShape.path(from: from, to: to, width: size)
            context.setFillColor(color)
            context.setStrokeColor(color)
            // Half the width again as a round joined outline, so no corner is a peak.
            context.setLineWidth(size * 0.5)
            context.addPath(path)
            context.drawPath(using: .fillStroke)
        case let .line(from, to):
            context.setStrokeColor(color)
            context.setLineWidth(size)
            context.strokeLineSegments(between: [from, to])
        case let .rectangle(rect):
            // Half the stroke again, so the inside of an outline is as round as the outside.
            let radius = radius(annotation.style.corners, for: rect, extra: annotation.style.filled ? 0 : size / 2)
            context.addPath(CGPath(roundedRect: rect, cornerWidth: radius, cornerHeight: radius, transform: nil))
            if annotation.style.filled {
                context.setFillColor(color)
                context.fillPath()
            } else {
                context.setStrokeColor(color)
                context.setLineWidth(size)
                context.strokePath()
            }
        case let .oval(rect):
            if annotation.style.filled {
                context.setFillColor(color)
                context.fillEllipse(in: rect)
            } else {
                context.setStrokeColor(color)
                context.setLineWidth(size)
                context.strokeEllipse(in: rect)
            }
        case let .text(origin, string):
            TextLayout.draw(string, at: origin, points: size / scale, scale: scale, color: color, in: context)
        case let .highlighter(from, to):
            context.setBlendMode(.multiply)
            context.setLineCap(.round)
            context.setStrokeColor(Palette.color(hex: annotation.style.colorHex, alpha: 0.4))
            context.setLineWidth(size)
            context.strokeLineSegments(between: [from, to])
        case let .freehand(points):
            context.setStrokeColor(color)
            context.setLineWidth(size)
            context.addPath(Smoothing.path(through: points))
            context.strokePath()
        case let .step(center):
            drawStep(number: stepNumber ?? 0, center: center, diameter: size, color: color)
        case let .image(rect, pasted):
            let corner = radius(annotation.style.corners, for: rect)
            context.addPath(CGPath(roundedRect: rect, cornerWidth: corner, cornerHeight: corner, transform: nil))
            context.clip()
            draw(pasted.image, in: rect)
        case let .magnifier(center, radius, zoom):
            magnify(center: center, radius: radius, zoom: zoom)
        case let .blur(rect):
            blur(rect, radius: size * outputScale, corners: annotation.style.corners)
        case let .pixelate(rect):
            pixelate(rect, block: size * outputScale, corners: annotation.style.corners)
        case let .erase(rect):
            erase(rect)
        case .spotlight:
            break
        }
    }

    private func drawStep(number: Int, center: CGPoint, diameter: CGFloat, color: CGColor) {
        let circle = CGRect(x: center.x - diameter / 2, y: center.y - diameter / 2, width: diameter, height: diameter)
        context.setFillColor(color)
        context.fillEllipse(in: circle)

        let label = String(number)
        let points = diameter / scale * 0.55
        let metrics = TextLayout.metrics(of: label, points: points, scale: scale, bold: true)
        let origin = CGPoint(x: center.x - metrics.width / 2,
                             y: center.y - (metrics.ascent + metrics.descent) / 2)
        TextLayout.draw(label, at: origin, points: points, scale: scale, color: CGColor(gray: 1, alpha: 1), bold: true, in: context)
    }

    /// A box's corner radius in capture pixels, never more than half its shorter side.
    func radius(_ corners: CornerSize, for rect: CGRect, extra: CGFloat = 0) -> CGFloat {
        let half = max(0, min(rect.width, rect.height) / 2)
        guard let points = corners.points else { return half }
        return points == 0 ? 0 : min(points * scale + extra, half)
    }

    func spotlight(_ rects: [(CGRect, CornerSize)]) {
        context.saveGState()
        context.beginTransparencyLayer(auxiliaryInfo: nil)
        context.setFillColor(CGColor(gray: 0, alpha: 0.5))
        context.fill(region)
        context.setBlendMode(.clear)
        for (rect, corners) in rects where rect.width > 0 && rect.height > 0 {
            let corner = radius(corners, for: rect)
            context.addPath(CGPath(roundedRect: rect, cornerWidth: corner, cornerHeight: corner, transform: nil))
            context.fillPath()
        }
        context.endTransparencyLayer()
        context.restoreGState()
    }

    // MARK: Reading back what is drawn so far

    /// A box in capture pixels as whole output pixels, top left origin, clipped to the
    /// output. Nil when nothing of it is on the output at all.
    private func deviceRect(for rect: CGRect) -> CGRect? {
        let device = CGRect(x: (rect.minX - region.minX) * outputScale,
                            y: (rect.minY - region.minY) * outputScale,
                            width: rect.width * outputScale,
                            height: rect.height * outputScale)
            .integral
            .intersection(CGRect(origin: .zero, size: deviceSize))
        return device.isNull || device.width < 1 || device.height < 1 ? nil : device
    }

    private func captureRect(for device: CGRect) -> CGRect {
        CGRect(x: device.minX / outputScale + region.minX, y: device.minY / outputScale + region.minY,
               width: device.width / outputScale, height: device.height / outputScale)
    }

    private func blur(_ rect: CGRect, radius blurRadius: CGFloat, corners: CornerSize) {
        guard let device = deviceRect(for: rect), let snapshot = context.makeImage() else { return }
        // Core Image counts y up from the bottom.
        let flipped = CGRect(x: device.minX, y: deviceSize.height - device.maxY, width: device.width, height: device.height)
        let blurred = CIImage(cgImage: snapshot).clampedToExtent()
            .applyingGaussianBlur(sigma: max(1, blurRadius))
            .cropped(to: flipped)
        guard let patch = Self.imageContext.createCGImage(blurred, from: flipped),
              var buffer = PixelBuffer(image: patch) else { return }
        buffer.addNoise(amplitude: 12, origin: (Int(device.minX), Int(device.minY)))
        guard let noisy = buffer.makeImage() else { return }
        drawRounded(noisy, in: captureRect(for: device), crisp: false, corners: corners)
    }

    /// Blur and pixelate boxes take the corners their style sets, like every other box.
    private func drawRounded(_ image: CGImage, in rect: CGRect, crisp: Bool, corners: CornerSize) {
        let corner = radius(corners, for: rect)
        context.saveGState()
        context.addPath(CGPath(roundedRect: rect, cornerWidth: corner, cornerHeight: corner, transform: nil))
        context.clip()
        draw(image, in: rect, crisp: crisp)
        context.restoreGState()
    }

    /// Square blocks, each the average of the pixels it covers, counted from the box's
    /// top left corner.
    private func pixelate(_ rect: CGRect, block: CGFloat, corners: CornerSize) {
        guard let device = deviceRect(for: rect), let snapshot = context.makeImage(),
              let patch = snapshot.cropping(to: device), var buffer = PixelBuffer(image: patch) else { return }
        buffer.pixelate(block: max(2, Int(block.rounded())))
        buffer.addNoise(amplitude: 12, origin: (Int(device.minX), Int(device.minY)))
        guard let blocks = buffer.makeImage() else { return }
        drawRounded(blocks, in: captureRect(for: device), crisp: true, corners: corners)
    }

    private func erase(_ rect: CGRect) {
        guard let device = deviceRect(for: rect), let snapshot = context.makeImage() else { return }
        // One pixel of surroundings on each side, where the output has them.
        let around = device.insetBy(dx: -1, dy: -1).intersection(CGRect(origin: .zero, size: deviceSize))
        guard let patch = snapshot.cropping(to: around), var buffer = PixelBuffer(image: patch) else { return }
        buffer.eraseFill(inner: (x: Int(device.minX - around.minX), y: Int(device.minY - around.minY),
                                 width: Int(device.width), height: Int(device.height)))
        guard let filled = buffer.makeImage() else { return }
        draw(filled, in: captureRect(for: around), crisp: true)
    }

    /// Enlarges everything drawn so far inside a circle, pixels kept sharp.
    private func magnify(center: CGPoint, radius: CGFloat, zoom: CGFloat) {
        guard radius > 0, let snapshot = context.makeImage() else { return }
        let lens = CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)

        context.saveGState()
        context.addEllipse(in: lens)
        context.clip()
        context.translateBy(x: center.x, y: center.y)
        context.scaleBy(x: zoom, y: zoom)
        context.translateBy(x: -center.x, y: -center.y)
        draw(snapshot, in: region, crisp: true)
        context.restoreGState()

        context.setStrokeColor(CGColor(gray: 1, alpha: 1))
        context.setLineWidth(3 * scale)
        context.strokeEllipse(in: lens)
        context.setStrokeColor(CGColor(gray: 0, alpha: 0.3))
        context.setLineWidth(1 * scale)
        context.strokeEllipse(in: lens.insetBy(dx: -1.5 * scale, dy: -1.5 * scale))
    }
}
