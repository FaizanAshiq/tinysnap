import AppKit
import ImageIO
import TinysnapCore

/// The desktop picture behind a screen, as a backdrop fill: read from its file at no more
/// than 1600 pixels, then softened. Nil when there is no picture file to read, as with a
/// video wallpaper, or when macOS keeps the file from Tinysnap, and the gradient is drawn.
@MainActor
enum WallpaperReader {
    static func softened(for screen: NSScreen?) -> Backdrop.Wallpaper? {
        guard let screen = screen ?? NSScreen.main, let url = NSWorkspace.shared.desktopImageURL(for: screen),
              let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let options = [kCGImageSourceCreateThumbnailFromImageAlways: true,
                       kCGImageSourceThumbnailMaxPixelSize: 1600,
                       kCGImageSourceCreateThumbnailWithTransform: true] as CFDictionary
        guard let picture = CGImageSourceCreateThumbnailAtIndex(source, 0, options),
              let softened = Backdrop.soften(picture) else { return nil }
        return Backdrop.Wallpaper(image: PastedImage(softened))
    }
}
