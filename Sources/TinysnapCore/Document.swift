import CoreGraphics
import Foundation

/// The pixels a capture produced. A class so undo snapshots share one copy.
public final class Capture: Equatable, Sendable {
    public let image: CGImage
    /// Pixels per point on the display it came from: 2 on Retina, 1 on a standard display.
    public let scale: CGFloat
    /// The colour most of the capture's border has. A canvas grown past the capture is
    /// filled with it, so the screenshot looks as if it simply goes on.
    public let edgeColor: CGColor
    /// A backdrop gradient's two colours: the capture's commonest colour, then the
    /// commonest one clearly different from it, or a shade of the first when there is none.
    public let gradientColors: [CGColor]

    public init(image: CGImage, scale: CGFloat) {
        self.image = image
        self.scale = scale
        let colors = ColorSample(image)
        edgeColor = colors.edge
        gradientColors = colors.gradient
    }

    public var pixelSize: CGSize { CGSize(width: image.width, height: image.height) }
    public var bounds: CGRect { CGRect(origin: .zero, size: pixelSize) }
    public var pointSize: CGSize { CGSize(width: pixelSize.width / scale, height: pixelSize.height / scale) }

    public static func == (lhs: Capture, rhs: Capture) -> Bool { lhs === rhs }

    /// The part of a frozen display under `rect`, given in points from the display's
    /// top left corner. Clipped to the image, and nil when nothing of it is left.
    public static func crop(_ image: CGImage, points rect: CGRect, scale: CGFloat) -> Capture? {
        let pixels = CGRect(x: rect.minX * scale, y: rect.minY * scale,
                            width: rect.width * scale, height: rect.height * scale)
            .integral
            .intersection(CGRect(x: 0, y: 0, width: image.width, height: image.height))
        guard !pixels.isNull, pixels.width >= 1, pixels.height >= 1,
              let cropped = image.cropping(to: pixels) else { return nil }
        return Capture(image: cropped, scale: scale)
    }
}

/// One capture plus everything drawn on it. Plain values, so undo is a stack of these.
public struct Document: Equatable, Sendable {
    public let capture: Capture
    /// Applied only on export. Nil means the whole capture.
    public var crop: CGRect?
    /// Drawn bottom to top, in creation order.
    public var annotations: [Annotation]
    /// Nil while there is no backdrop.
    public var backdrop: Backdrop?
    /// The export's size as output pixels per capture pixel, 0.01 to 4. Nil follows the
    /// Export setting.
    public var resize: CGFloat?

    public init(capture: Capture, crop: CGRect? = nil, annotations: [Annotation] = [], backdrop: Backdrop? = nil,
                resize: CGFloat? = nil) {
        self.capture = capture
        self.crop = crop
        self.annotations = annotations
        self.backdrop = backdrop
        self.resize = resize
    }

    public var scale: CGFloat { capture.scale }
    /// What is exported: the crop, or the whole extent.
    public var outputRect: CGRect { crop ?? extent }

    /// The output on whole pixels, rounded inwards, which is what an export cuts.
    public var outputPixelRect: CGRect {
        let rect = outputRect
        let left = rect.minX.rounded(.up), top = rect.minY.rounded(.up)
        return CGRect(x: left, y: top, width: max(1, rect.maxX.rounded(.down) - left),
                      height: max(1, rect.maxY.rounded(.down) - top))
    }

    /// The framed output in capture pixels: the output with the backdrop's padding on
    /// every side. Nil without a backdrop.
    public var framedRect: CGRect? {
        guard let backdrop else { return nil }
        let padding = backdrop.padding.points * scale
        return outputPixelRect.insetBy(dx: -padding, dy: -padding)
    }

    /// Points of margin kept around a shape drawn past the capture's edge.
    static let growthMargin: CGFloat = 16

    /// The capture, grown to take in every annotation drawn past its edge, with a margin
    /// around it, on whole pixels. Computed rather than stored, so undo and delete shrink
    /// the canvas back as readily as drawing grows it.
    public var extent: CGRect {
        let margin = Self.growthMargin * scale
        // A hidden shape is left out of the output, so it grows nothing.
        return annotations.filter { !$0.isHidden }.reduce(capture.bounds) { extent, annotation in
            let bounds = annotation.bounds(scale: scale)
            guard !capture.bounds.contains(bounds) else { return extent }
            return extent.union(bounds.insetBy(dx: -margin, dy: -margin))
        }
        .integral
    }

    public func annotation(_ id: Annotation.ID) -> Annotation? {
        annotations.first { $0.id == id }
    }

    /// Not stored: 1 plus the number of shown steps before this one, so deleting or hiding
    /// a step renumbers every step after it. Nil for a hidden step, which shows no number.
    public func stepNumber(of id: Annotation.ID) -> Int? {
        var number = 0
        for annotation in annotations where !annotation.isHidden {
            guard case .step = annotation.kind else { continue }
            number += 1
            if annotation.id == id { return number }
        }
        return nil
    }

    /// What the layers panel calls a shape: its tool, its text's first line, a step's number,
    /// a measurement's length as its tag gives it.
    public func layerName(of id: Annotation.ID) -> String {
        guard let annotation = annotation(id) else { return "" }
        switch annotation.kind {
        case let .text(_, string):
            let line = string.split(whereSeparator: \.isNewline).first.map(String.init)?
                .trimmingCharacters(in: .whitespaces) ?? ""
            return line.isEmpty ? "Text" : String(line.prefix(40))
        case .step: return stepNumber(of: id).map { "Step \($0)" } ?? "Step"
        case let .measure(from, to): return "Measure " + MeasureReading.label(forPixels: from.distance(to: to), scale: scale)
        // Their tool titles carry a warning that has no place in a list of names.
        case .blur: return "Blur"
        case .pixelate: return "Pixelate"
        case .erase: return "Erase"
        default: return annotation.tool.title
        }
    }

    /// The topmost shown annotation under `point`. A locked one counts: it can be selected.
    public func topmost(at point: CGPoint) -> Annotation.ID? {
        annotations.last { !$0.isHidden && $0.contains(point, scale: scale) }?.id
    }

    /// What a click with a drawing tool picks up instead of drawing: the topmost
    /// annotation it lands on, or whose hover border it lands on. Landing on means inside
    /// for whatever changes its middle, a blur, an erase, text, an image, a filled box,
    /// and on the stroke for lines and outlines, whose empty middle is still drawn in. A
    /// spotlight's clear middle is where arrows and boxes go, so only its border counts.
    /// Locked and hidden shapes are never picked up: the click draws over them.
    public func pickUp(at point: CGPoint, reach: CGFloat) -> Annotation.ID? {
        let border = borderHit(at: point, reach: reach, pickable: true)
        return annotations.last { annotation in
            guard !annotation.isLocked, !annotation.isHidden else { return false }
            if annotation.id == border { return true }
            if case .spotlight = annotation.kind { return false }
            return annotation.contains(point, scale: scale)
        }?.id
    }

    /// The topmost shown annotation whose hover border runs under `point`, leaving out
    /// locked ones when `pickable`. The border is drawn half a reach outside the
    /// annotation's bounds, and anything within a reach of it counts, so the border can be
    /// clicked as readily as it is seen.
    public func borderHit(at point: CGPoint, reach: CGFloat, pickable: Bool = false) -> Annotation.ID? {
        guard reach > 0 else { return nil }
        return annotations.last { annotation in
            guard !annotation.isHidden, !(pickable && annotation.isLocked) else { return false }
            let border = annotation.bounds(scale: scale).insetBy(dx: -reach / 2, dy: -reach / 2)
            let inner = border.insetBy(dx: reach, dy: reach)
            let inside = inner.width > 0 && inner.height > 0 && inner.contains(point)
            return border.insetBy(dx: -reach, dy: -reach).contains(point) && !inside
        }?.id
    }

    public mutating func replace(_ annotation: Annotation) {
        guard let index = annotations.firstIndex(where: { $0.id == annotation.id }) else { return }
        annotations[index] = annotation
    }

    public mutating func remove(_ id: Annotation.ID) {
        annotations.removeAll { $0.id == id }
    }
}
