import CoreGraphics

/// The canvas past 100%: what is in view drawn at the screen's own resolution, so shapes stay
/// smooth however far in, while the capture's pixels stay sharp squares.
extension Renderer {
    /// More output pixels than this and the canvas shows the plain render enlarged instead.
    static let closeUpPixelLimit: CGFloat = 40_000_000

    /// `visible`, in capture pixels, drawn at `outputScale` with the region it covers. Nil when
    /// nothing is in view, or when a box that reads back makes it too big to draw.
    public static func renderCloseUp(_ document: Document, visible: CGRect, outputScale: CGFloat, framed: Bool,
                                     typing: Annotation.ID? = nil) -> (image: CGImage, region: CGRect)? {
        guard let region = closeUpRegion(document, visible: visible, framed: framed) else { return nil }
        let size = pixelSize(of: region, outputScale: outputScale)
        // ponytail: a huge blur half in view at a deep zoom shows as squares; render only its
        // visible part with the reach it needs if that ever matters.
        guard size.width * size.height <= closeUpPixelLimit,
              let image = render(document, region: region, outputScale: outputScale, typing: typing, sharpPixels: true)
        else { return nil }
        return (image, region)
    }

    /// `visible` on whole capture pixels, inside the output when framed, then grown to take
    /// in whole each box that reads back what is beneath it and reaches into view: a blur,
    /// pixelate or magnifier half in view then looks as it does whole, rather than counting
    /// from the edge of the view. Never past the extent.
    static func closeUpRegion(_ document: Document, visible: CGRect, framed: Bool) -> CGRect? {
        let shown = visible.intersection(framed ? document.outputPixelRect : document.extent)
        guard !shown.isNull, shown.width > 0, shown.height > 0 else { return nil }
        let reaches = document.annotations.filter { !$0.isHidden }.compactMap { readBack($0, scale: document.scale) }
        var region = shown.integral
        // A box taken in can reach another, as a magnifier over a blur does.
        var grown = true
        while grown {
            grown = false
            for reach in reaches where reach.intersects(region) && !region.contains(reach) {
                region = region.union(reach).integral
                grown = true
            }
        }
        return region.intersection(document.extent)
    }

    /// What an annotation reads back to draw itself, in capture pixels; nil when it draws
    /// without looking at what is beneath it.
    private static func readBack(_ annotation: Annotation, scale: CGFloat) -> CGRect? {
        switch annotation.kind {
        case let .blur(rect):
            // A Gaussian reaches three of its sizes.
            let reach = annotation.pixelSize(scale: scale) * 3
            return rect.insetBy(dx: -reach, dy: -reach)
        case let .pixelate(rect):
            return rect
        case let .erase(rect):
            return rect.insetBy(dx: -1, dy: -1)
        case let .magnifier(center, radius, _):
            return CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
        default:
            return nil
        }
    }
}
