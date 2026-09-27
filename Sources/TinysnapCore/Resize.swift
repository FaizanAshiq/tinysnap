import CoreGraphics

/// A capture's export size, as output pixels per capture pixel: 0.5 is half of full
/// resolution. The Size panel's chips and its pixel fields both come down to one of these.
extension Document {
    /// 1% to 400%.
    public static let resizeLimits: ClosedRange<CGFloat> = 0.01...4
    /// The longest side an export may have, so a 5K capture cannot ask for a gigabyte.
    public static let longestExportSide: CGFloat = 16_384

    /// The export's size in pixels at `resize`, from the same sums the renderer cuts and
    /// frames with, so the panel never shows a size the export does not make.
    public func exportPixelSize(at resize: CGFloat) -> CGSize {
        let cut = Renderer.outputCut(self, outputScale: resize)?.size ?? CGSize(width: 1, height: 1)
        guard let backdrop else { return cut }
        let padding = CGFloat(Renderer.framePadding(backdrop, perPoint: scale * resize) * 2)
        return CGSize(width: cut.width + padding, height: cut.height + padding)
    }

    /// The largest size within the limits whose export stays inside the longest side.
    public var largestResize: CGFloat {
        let tooLong = { (size: CGSize) in max(size.width, size.height) > Self.longestExportSide }
        guard tooLong(exportPixelSize(at: Self.resizeLimits.upperBound)) else { return Self.resizeLimits.upperBound }
        return bisect(tooLong).under
    }

    public func clampedResize(_ resize: CGFloat) -> CGFloat {
        min(max(resize, Self.resizeLimits.lowerBound), largestResize)
    }

    /// The size whose export is `pixels` wide, held to the limits.
    public func resize(forWidth pixels: Int) -> CGFloat {
        clampedResize(bisect { $0.width >= CGFloat(pixels) }.over)
    }

    /// The size whose export is `pixels` high, held to the limits.
    public func resize(forHeight pixels: Int) -> CGFloat {
        clampedResize(bisect { $0.height >= CGFloat(pixels) }.over)
    }

    /// Narrows the limits to where `reached` turns true. The sums round to whole pixels,
    /// so dividing the pixels wanted by the full size can land a pixel short.
    private func bisect(_ reached: (CGSize) -> Bool) -> (under: CGFloat, over: CGFloat) {
        var under = Self.resizeLimits.lowerBound, over = Self.resizeLimits.upperBound
        if reached(exportPixelSize(at: under)) { return (under, under) }
        for _ in 0..<40 {
            let middle = (under + over) / 2
            if reached(exportPixelSize(at: middle)) { over = middle } else { under = middle }
        }
        return (under, over)
    }
}
