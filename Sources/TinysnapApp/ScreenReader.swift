import AppKit
import ScreenCaptureKit
import TinysnapCore

extension NSScreen {
    /// The display id AppKit keeps in deviceDescription.
    var displayID: CGDirectDisplayID {
        (deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
    }
}

/// One display as it looked the moment a capture began.
struct FrozenDisplay {
    let displayID: CGDirectDisplayID
    /// The display's frame in AppKit's global coordinates: points, y up.
    let frame: CGRect
    let scale: CGFloat
    let image: CGImage

    /// The part under `rect`, in points from the display's top left corner.
    func capture(of rect: CGRect) -> Capture? {
        Capture.crop(image, points: rect, scale: scale)
    }
}

/// A window that can be clicked in window mode.
struct PickableWindow {
    let window: SCWindow
    /// In AppKit's global coordinates.
    let frame: CGRect
}

/// Freezes displays and captures single windows.
@MainActor
enum ScreenReader {
    /// Pays ScreenCaptureKit's one off start up cost at launch. The first call after
    /// launch takes seconds; without this the first capture would be the slow one. It
    /// reads no pixels, and it is skipped entirely without permission so nothing prompts.
    static func warmUp() async {
        guard ScreenAccess.isGranted else { return }
        _ = try? await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
    }

    /// Every display frozen, or only `onlyDisplay`, plus the windows window mode can
    /// pick. Taken before the overlay is up, so nothing of Tinysnap's is in it apart
    /// from any editor already open, which is on screen and fair game.
    ///
    /// An empty list with the permission granted means macOS lists Tinysnap and is
    /// handing over nothing anyway, which happens after a rebuild with a new signature.
    static func freeze(onlyDisplay: CGDirectDisplayID? = nil) async -> (displays: [FrozenDisplay], windows: [PickableWindow]) {
        guard let content = try? await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true) else {
            return ([], [])
        }

        var frozen: [FrozenDisplay] = []
        for display in content.displays where onlyDisplay == nil || display.displayID == onlyDisplay {
            guard let screen = NSScreen.screens.first(where: { $0.displayID == display.displayID }) else { continue }
            // SCDisplay reports points. The factor comes from the display itself rather
            // than being assumed to be 2, or a standard display would come back doubled.
            let scale = screen.backingScaleFactor
            let configuration = SCStreamConfiguration()
            configuration.width = Int(CGFloat(display.width) * scale)
            configuration.height = Int(CGFloat(display.height) * scale)
            configuration.captureResolution = .best
            configuration.showsCursor = false
            // sRGB rather than the display's own space, so a colour comes out as painted
            // instead of round tripped through P3.
            configuration.colorSpaceName = CGColorSpace.sRGB

            let filter = SCContentFilter(display: display, excludingWindows: [])
            guard let image = try? await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration) else {
                continue
            }
            frozen.append(FrozenDisplay(displayID: display.displayID, frame: screen.frame, scale: scale, image: image))
        }
        return (frozen, pickableWindows(in: content))
    }

    /// One window on its own, rounded corners left transparent, no shadow.
    static func capture(_ picked: PickableWindow) async -> Capture? {
        let filter = SCContentFilter(desktopIndependentWindow: picked.window)
        let scale = CGFloat(filter.pointPixelScale)
        let configuration = SCStreamConfiguration()
        configuration.width = Int(filter.contentRect.width * scale)
        configuration.height = Int(filter.contentRect.height * scale)
        configuration.captureResolution = .best
        configuration.showsCursor = false
        configuration.ignoreShadowsSingleWindow = true
        configuration.colorSpaceName = CGColorSpace.sRGB
        guard let image = try? await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration) else {
            return nil
        }
        return Capture(image: image, scale: scale)
    }

    /// Ordinary windows only, front to back, in AppKit coordinates.
    private static func pickableWindows(in content: SCShareableContent) -> [PickableWindow] {
        let order = frontToBack()
        // ScreenCaptureKit frames count y down from the top of the primary display.
        let primaryHeight = NSScreen.screens.first?.frame.height ?? 0
        return content.windows
            .filter { $0.windowLayer == 0 && $0.isOnScreen && $0.frame.width >= 40 && $0.frame.height >= 40 }
            .sorted { (order[$0.windowID] ?? .max) < (order[$1.windowID] ?? .max) }
            .map { window in
                PickableWindow(window: window, frame: CGRect(x: window.frame.minX, y: primaryHeight - window.frame.maxY,
                                                             width: window.frame.width, height: window.frame.height))
            }
    }

    /// ScreenCaptureKit's list promises no order, the window server's list is front to back.
    private static func frontToBack() -> [CGWindowID: Int] {
        let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
        var order: [CGWindowID: Int] = [:]
        for (index, info) in list.enumerated() {
            if let number = info[kCGWindowNumber as String] as? Int { order[CGWindowID(number)] = index }
        }
        return order
    }
}
