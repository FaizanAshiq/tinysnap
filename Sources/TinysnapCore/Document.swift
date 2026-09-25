import CoreGraphics
import Foundation

/// The pixels a capture produced. A class so undo snapshots share one copy.
public final class Capture: Equatable, Sendable {
    public let image: CGImage
    /// Pixels per point on the display it came from: 2 on Retina, 1 on a standard display.
    public let scale: CGFloat

    public init(image: CGImage, scale: CGFloat) {
        self.image = image
        self.scale = scale
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
    public var outputRect: CGRect { crop ?? capture.bounds }

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

    public mutating func replace(_ annotation: Annotation) {
        guard let index = annotations.firstIndex(where: { $0.id == annotation.id }) else { return }
        annotations[index] = annotation
    }

    public mutating func remove(_ id: Annotation.ID) {
        annotations.removeAll { $0.id == id }
    }
}
