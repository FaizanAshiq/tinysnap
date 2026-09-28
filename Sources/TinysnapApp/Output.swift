import AppKit
import TinysnapCore

/// Copy and save for everything that holds a finished image: the editor, pins, the
/// thumbnail and the library, so each one lands on the clipboard and on disk the same way.
@MainActor
enum Output {
    /// A finished image at the export scale in Settings: native keeps every pixel, 1x
    /// draws one pixel per point.
    static func exported(_ image: CGImage, scale: CGFloat, as exportScale: ExportScale) -> (ExportedImage, Data)? {
        guard let exported = Exporter.export(Document(capture: Capture(image: image, scale: scale)), scale: exportScale),
              let png = Exporter.pngData(exported) else { return nil }
        return (exported, png)
    }

    /// The PNG, and a TIFF sized in points for apps that read only that.
    static func copy(_ exported: ExportedImage, png: Data) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setData(png, forType: .png)
        let representation = NSBitmapImageRep(cgImage: exported.image)
        representation.size = exported.pointSize
        if let tiff = representation.tiffRepresentation { pasteboard.setData(tiff, forType: .tiff) }
    }

    /// Into the save folder under a fresh name. Throws with a sentence fit for an alert.
    @discardableResult
    static func save(_ png: Data, in folder: URL) throws -> URL {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw OutputError("The save folder \(folder.path) does not exist. Choose another in Settings.")
        }
        let name = FileNaming.fileName(for: Date()) { FileManager.default.fileExists(atPath: folder.appendingPathComponent($0).path) }
        let url = folder.appendingPathComponent(name)
        do {
            try png.write(to: url, options: .atomic)
        } catch {
            throw OutputError("Tinysnap could not save to \(folder.path). \(error.localizedDescription)")
        }
        return url
    }

    /// A tick in place of the button's symbol for a moment: copying and saving change
    /// nothing on screen, so without it a click looks as if it did nothing. A second
    /// press while the tick shows leaves it be, so the tick never becomes the symbol.
    static func showDone(on sender: Any?) {
        if let button = sender as? NSButton, let image = button.image, image !== tick {
            button.image = tick
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { button.image = image }
        } else if let item = sender as? NSToolbarItem, let image = item.image, image !== tick {
            item.image = tick
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { item.image = image }
        }
    }

    private static let tick = NSImage(systemSymbolName: "checkmark", accessibilityDescription: "Done")

    /// An alert for a failed output, over `window` when there is one.
    static func show(_ error: Error, over window: NSWindow?) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = (error as? OutputError)?.message ?? error.localizedDescription
        if let window { alert.beginSheetModal(for: window) } else { alert.runModal() }
    }
}

struct OutputError: Error {
    let message: String
    init(_ message: String) { self.message = message }
}
