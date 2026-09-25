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

    public init(image: CGImage, scale: CGFloat) {
        self.image = image
        self.scale = scale
        edgeColor = Self.dominantEdgeColor(of: image)
    }

    /// Read from a small copy, so a 5K capture costs one quick downsample. Border pixels
    /// are grouped by colour and the biggest group wins, which ignores a toolbar or a
    /// photo touching one edge. Transparent pixels, such as a window's rounded corners,
    /// are left out.
    private static func dominantEdgeColor(of image: CGImage) -> CGColor {
        let side = 64
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: side, height: side, bitsPerComponent: 8, bytesPerRow: side * 4,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let data = context.data else { return CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1) }
        context.interpolationQuality = .medium
        context.draw(image, in: CGRect(x: 0, y: 0, width: side, height: side))
        let bytes = data.bindMemory(to: UInt8.self, capacity: side * side * 4)

        var groups: [Int: (count: Int, red: Int, green: Int, blue: Int)] = [:]
        for y in 0..<side {
            for x in 0..<side where y == 0 || y == side - 1 || x == 0 || x == side - 1 {
                let at = (y * side + x) * 4
                let alpha = Int(bytes[at + 3])
                guard alpha >= 128 else { continue }
                // Premultiplied, so each channel is divided back out before grouping.
                let (red, green, blue) = (Int(bytes[at]) * 255 / alpha, Int(bytes[at + 1]) * 255 / alpha,
                                          Int(bytes[at + 2]) * 255 / alpha)
                let key = (red >> 4) << 8 | (green >> 4) << 4 | blue >> 4
                let group = groups[key, default: (0, 0, 0, 0)]
                groups[key] = (group.count + 1, group.red + red, group.green + green, group.blue + blue)
            }
        }
        guard let biggest = groups.values.max(by: { $0.count < $1.count }) else {
            return CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1)
        }
        let count = CGFloat(biggest.count) * 255
        return CGColor(srgbRed: CGFloat(biggest.red) / count, green: CGFloat(biggest.green) / count,
                       blue: CGFloat(biggest.blue) / count, alpha: 1)
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
public struct Document: Equatable {
    public let capture: Capture
    /// Applied only on export. Nil means the whole capture.
    public var crop: CGRect?
    /// Drawn bottom to top, in creation order.
    public var annotations: [Annotation]

    public init(capture: Capture, crop: CGRect? = nil, annotations: [Annotation] = []) {
        self.capture = capture
        self.crop = crop
        self.annotations = annotations
    }

    public var scale: CGFloat { capture.scale }
    /// What is exported: the crop, or the whole extent.
    public var outputRect: CGRect { crop ?? extent }

    /// Points of margin kept around a shape drawn past the capture's edge.
    static let growthMargin: CGFloat = 16

    /// The capture, grown to take in every annotation drawn past its edge, with a margin
    /// around it, on whole pixels. Computed rather than stored, so undo and delete shrink
    /// the canvas back as readily as drawing grows it.
    public var extent: CGRect {
        let margin = Self.growthMargin * scale
        return annotations.reduce(capture.bounds) { extent, annotation in
            let bounds = annotation.bounds(scale: scale)
            guard !capture.bounds.contains(bounds) else { return extent }
            return extent.union(bounds.insetBy(dx: -margin, dy: -margin))
        }
        .integral
    }

    public func annotation(_ id: Annotation.ID) -> Annotation? {
        annotations.first { $0.id == id }
    }

    /// Not stored: 1 plus the number of steps before this one, so deleting a step
    /// renumbers every step after it.
    public func stepNumber(of id: Annotation.ID) -> Int? {
        var number = 0
        for annotation in annotations {
            guard case .step = annotation.kind else { continue }
            number += 1
            if annotation.id == id { return number }
        }
        return nil
    }

    /// The topmost annotation under `point`.
    public func topmost(at point: CGPoint) -> Annotation.ID? {
        annotations.last { $0.contains(point, scale: scale) }?.id
    }

    /// What a click with a drawing tool picks up instead of drawing: the topmost
    /// annotation it lands on, or whose hover border it lands on. Landing on means inside
    /// for whatever changes its middle, a blur, an erase, text, an image, a filled box,
    /// and on the stroke for lines and outlines, whose empty middle is still drawn in. A
    /// spotlight's clear middle is where arrows and boxes go, so only its border counts.
    public func pickUp(at point: CGPoint, reach: CGFloat) -> Annotation.ID? {
        let border = borderHit(at: point, reach: reach)
        return annotations.last { annotation in
            if annotation.id == border { return true }
            if case .spotlight = annotation.kind { return false }
            return annotation.contains(point, scale: scale)
        }?.id
    }

    /// The topmost annotation whose hover border runs under `point`. The border is drawn
    /// half a reach outside the annotation's bounds, and anything within a reach of it
    /// counts, so the border can be clicked as readily as it is seen.
    public func borderHit(at point: CGPoint, reach: CGFloat) -> Annotation.ID? {
        guard reach > 0 else { return nil }
        return annotations.last { annotation in
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
