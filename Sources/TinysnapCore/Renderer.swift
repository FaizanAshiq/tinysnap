import CoreGraphics
import CoreImage
import Foundation

/// The one renderer. The canvas draws what this returns and export writes it, so what
/// you see is exactly what you copy.
public enum Renderer {
    /// A region's size in output pixels. Rounded down: rounding 400.5 up gave a last
    /// column only half covered by the capture, which came out half transparent.
    static func pixelSize(of region: CGRect, outputScale: CGFloat) -> CGSize {
        CGSize(width: max(1, (region.width * outputScale).rounded(.down)),
               height: max(1, (region.height * outputScale).rounded(.down)))
    }

    /// Draws `region` of the document, in capture pixels, at `outputScale` output pixels
    /// per capture pixel, the whole extent unless told otherwise. `hidden` leaves out
    /// annotations. `typing` is the text being typed, whose letters the editor's text field
    /// shows, so only its box is drawn. `sharpPixels` enlarges the capture as squares, as the
    /// canvas shows it past 100%, rather than smoothing it as an export does.
    public static func render(_ document: Document, region: CGRect? = nil, outputScale: CGFloat = 1,
                              hiding hidden: Set<Annotation.ID> = [], typing: Annotation.ID? = nil,
                              sharpPixels: Bool = false) -> CGImage? {
        let region = region ?? document.extent
        guard let canvas = start(document, region: region, outputScale: outputScale, typing: typing,
                                 sharpPixels: sharpPixels) else { return nil }
        let visible = document.annotations.filter { !$0.isHidden && !hidden.contains($0.id) }
        // An erase fills from what is around it, but not from outlines beneath it: one running
        // along its edge, as a box's does when the erase is drawn just inside it, filled the
        // whole erase with its colour. So when an erase lies over an outline, a second canvas
        // takes everything but the outlines, and every erase fills from that.
        let ground = visible.firstIndex(where: isOutline).flatMap { first in
            visible[first...].contains { if case .erase = $0.kind { true } else { false } }
                ? start(document, region: region, outputScale: outputScale, typing: typing, sharpPixels: sharpPixels)
                : nil
        }
        var spotlightDrawn = false
        for annotation in visible {
            if case .spotlight = annotation.kind {
                // Every spotlight lights one shared area, drawn at the first one's place and
                // dimmed or blurred as the first one says.
                guard !spotlightDrawn else { continue }
                spotlightDrawn = true
                let lit = visible.compactMap { if case let .spotlight(rect) = $0.kind { (rect, $0.style.corners) } else { nil } }
                canvas.spotlight(lit, blurring: annotation.style.blurOutside)
                ground?.spotlight(lit, blurring: annotation.style.blurOutside)
                continue
            }
            if case let .erase(rect) = annotation.kind, let ground {
                // The main canvas first: it reads the ground before the ground's own erase lands.
                canvas.erase(rect, from: ground)
                ground.erase(rect)
                continue
            }
            canvas.draw(annotation, stepLabel: document.stepLabel(of: annotation.id))
            if !isOutline(annotation) { ground?.draw(annotation, stepLabel: document.stepLabel(of: annotation.id)) }
        }
        return canvas.context.makeImage()
    }

    /// A canvas for `region` with the capture drawn, ready for the annotations.
    private static func start(_ document: Document, region: CGRect, outputScale: CGFloat, typing: Annotation.ID?,
                              sharpPixels: Bool) -> Canvas? {
        let size = pixelSize(of: region, outputScale: outputScale)
        let width = Int(size.width), height = Int(size.height)
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                                      bytesPerRow: 0, space: space,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }

        // From here on user space is capture pixels with y growing downward.
        context.translateBy(x: 0, y: CGFloat(height))
        context.scaleBy(x: outputScale, y: -outputScale)
        context.translateBy(x: -region.minX, y: -region.minY)

        let canvas = Canvas(context: context, region: region, outputScale: outputScale,
                            deviceSize: CGSize(width: width, height: height), scale: document.scale,
                            anchor: document.extent.origin, typing: typing, capture: document.capture)
        // Past the capture, the canvas carries on in the capture's edge colour.
        if !document.capture.bounds.contains(region) {
            context.setFillColor(document.capture.edgeColor)
            context.fill(region)
        }
        canvas.draw(document.capture.image, in: document.capture.bounds, crisp: sharpPixels)
        return canvas
    }

    /// A thin line drawn over the capture: an arrow, a line, a freehand stroke, a measurement,
    /// or a box or oval left unfilled.
    private static func isOutline(_ annotation: Annotation) -> Bool {
        switch annotation.kind {
        case .arrow, .line, .freehand, .measure: true
        case .rectangle, .oval: !annotation.style.filled
        default: false
        }
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
    /// Where the whole render starts, so a redaction's grain lies the same in a render of
    /// part of the document as in the whole.
    let anchor: CGPoint
    /// The text being typed: its box is drawn, its letters are left to the text field.
    let typing: Annotation.ID?
    /// What a highlight reads to choose between darkening and lightening.
    let capture: Capture

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

    func draw(_ annotation: Annotation, stepLabel: String?) {
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
            dash(annotation, width: size)
            context.strokeLineSegments(between: [from, to])
        case let .measure(from, to):
            MeasureShape.draw(from: from, to: to, width: size / scale, color: color, scale: scale,
                              at: annotation.labelAt, in: context)
        case let .rectangle(rect):
            context.setAlpha(annotation.style.opacity)
            // Half the stroke again, so the inside of an outline is as round as the outside.
            let radius = radius(annotation.style.corners, for: rect, extra: annotation.style.filled ? 0 : size / 2)
            context.addPath(CGPath(roundedRect: rect, cornerWidth: radius, cornerHeight: radius, transform: nil))
            if annotation.style.filled {
                context.setFillColor(color)
                context.fillPath()
            } else {
                context.setStrokeColor(color)
                context.setLineWidth(size)
                dash(annotation, width: size)
                context.strokePath()
            }
        case let .oval(rect):
            context.setAlpha(annotation.style.opacity)
            if annotation.style.filled {
                context.setFillColor(color)
                context.fillEllipse(in: rect)
            } else {
                context.setStrokeColor(color)
                context.setLineWidth(size)
                dash(annotation, width: size)
                context.strokeEllipse(in: rect)
            }
        case let .text(origin, string):
            if annotation.style.filled {
                let corner = TextLayout.boxCorner(points: size / scale) * scale
                context.addPath(CGPath(roundedRect: annotation.bounds(scale: scale), cornerWidth: corner, cornerHeight: corner,
                                       transform: nil))
                context.setFillColor(color)
                context.fillPath()
            }
            guard annotation.id != typing else { break }
            TextLayout.draw(string, at: origin, points: size / scale, scale: scale, color: TextLayout.letterColor(annotation.style),
                            bold: annotation.style.bold, align: annotation.style.align, in: context)
        case let .highlighter(from, to):
            highlight(annotation, width: size)
            context.strokeLineSegments(between: [from, to])
        case let .highlighterPath(points):
            highlight(annotation, width: size)
            // One path, stroked once, so where it crosses itself it is no darker.
            context.addPath(Smoothing.path(through: points))
            context.strokePath()
        case let .freehand(points):
            context.setStrokeColor(color)
            context.setLineWidth(size)
            context.addPath(Smoothing.path(through: points))
            context.strokePath()
        case let .step(center):
            drawStep(label: stepLabel ?? "", center: center, diameter: size, color: color)
        case let .image(rect, pasted):
            let corner = radius(annotation.style.corners, for: rect)
            context.addPath(CGPath(roundedRect: rect, cornerWidth: corner, cornerHeight: corner, transform: nil))
            context.clip()
            context.setAlpha(annotation.style.opacity)
            if annotation.style.difference { context.setBlendMode(.difference) }
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

    /// Darkens a light capture like a marker, and lightens a dark one, where darkening would
    /// hardly show. Read from the screenshot, so it never changes with the zoom.
    private func highlight(_ annotation: Annotation, width: CGFloat) {
        context.setBlendMode(capture.isDark(under: annotation.bounds(scale: scale)) ? .screen : .multiply)
        context.setStrokeColor(Palette.color(hex: annotation.style.colorHex, alpha: 0.4))
        context.setLineWidth(width)
    }

    /// Dashes two widths long with three between: the round ends take half a width off each
    /// side of a gap, so dash and gap read about even.
    private func dash(_ annotation: Annotation, width: CGFloat) {
        guard annotation.style.dashed else { return }
        context.setLineDash(phase: 0, lengths: [width * 2, width * 3])
    }

    private func drawStep(label: String, center: CGPoint, diameter: CGFloat, color: CGColor) {
        let circle = CGRect(x: center.x - diameter / 2, y: center.y - diameter / 2, width: diameter, height: diameter)
        context.setFillColor(color)
        context.fillEllipse(in: circle)

        // A long label, 100 or AA, is made smaller until it fits inside the disc.
        var points = diameter / scale * 0.55
        let natural = TextLayout.metrics(of: label, points: points, scale: scale, bold: true).width
        if natural > diameter * 0.78 { points *= diameter * 0.78 / natural }
        let metrics = TextLayout.metrics(of: label, points: points, scale: scale, bold: true)
        let origin = CGPoint(x: center.x - metrics.width / 2,
                             y: center.y - (metrics.ascent + metrics.descent) / 2)
        TextLayout.draw(label, at: origin, points: points, scale: scale, color: MeasureShape.textColor(on: color), bold: true,
                        in: context)
    }

    /// A box's corner radius in capture pixels, never more than half its shorter side.
    func radius(_ corners: CornerSize, for rect: CGRect, extra: CGFloat = 0) -> CGFloat {
        let half = max(0, min(rect.width, rect.height) / 2)
        guard let points = corners.points else { return half }
        return points == 0 ? 0 : min(points * scale + extra, half)
    }

    /// How far a blurring spotlight softens what is outside it, in points.
    static let spotlightBlurPoints: CGFloat = 6

    func spotlight(_ rects: [(CGRect, CornerSize)], blurring: Bool) {
        if blurring {
            blurOutside(rects)
            return
        }
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

    /// Everything drawn so far, blurred, shown only outside the lit boxes.
    private func blurOutside(_ rects: [(CGRect, CornerSize)]) {
        guard let snapshot = context.makeImage() else { return }
        let whole = CGRect(origin: .zero, size: deviceSize)
        let blurred = CIImage(cgImage: snapshot).clampedToExtent()
            .applyingGaussianBlur(sigma: Self.spotlightBlurPoints * scale * outputScale)
            .cropped(to: whole)
        guard let image = Self.imageContext.createCGImage(blurred, from: whole) else { return }
        let outside = CGMutablePath()
        outside.addRect(region)
        for (rect, corners) in rects where rect.width > 0 && rect.height > 0 {
            let corner = radius(corners, for: rect)
            outside.addRoundedRect(in: rect, cornerWidth: corner, cornerHeight: corner)
        }
        context.saveGState()
        context.addPath(outside)
        context.clip(using: .evenOdd)
        draw(image, in: region)
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

    /// A box's corner in the output pixels of the whole render, which seeds its noise.
    private func grain(_ device: CGRect) -> (x: Int, y: Int) {
        (Int(device.minX + ((region.minX - anchor.x) * outputScale).rounded()),
         Int(device.minY + ((region.minY - anchor.y) * outputScale).rounded()))
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
        buffer.addNoise(amplitude: 12, origin: grain(device))
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
        buffer.addNoise(amplitude: 12, origin: grain(device))
        guard let blocks = buffer.makeImage() else { return }
        drawRounded(blocks, in: captureRect(for: device), crisp: true, corners: corners)
    }

    /// Fills from what `source` has drawn when given, the canvas without outlines, or else from
    /// everything drawn here so far. Only the pixels around the box are read.
    func erase(_ rect: CGRect, from source: Canvas? = nil) {
        guard let device = deviceRect(for: rect) else { return }
        // One pixel of surroundings on each side, where the output has them.
        let around = device.insetBy(dx: -1, dy: -1).intersection(CGRect(origin: .zero, size: deviceSize))
        guard var buffer = PixelBuffer(context: (source ?? self).context, rect: around) else { return }
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
