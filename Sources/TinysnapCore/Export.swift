import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

public enum ExportScale: String, Codable, Sendable, CaseIterable {
    case native
    case oneX = "1x"
}

public struct ExportedImage {
    public let image: CGImage
    /// 72 times the pixels per point, never under 72, so the PNG pastes at the size it
    /// had on screen.
    public let dpi: CGFloat
    /// The size the image had on screen.
    public let pointSize: CGSize
}

public enum Exporter {
    /// Output pixels per capture pixel: the capture's own size, or the Export setting's
    /// when it has none. Native keeps every capture pixel, 1x draws one pixel per point.
    public static func outputScale(of document: Document, setting: ExportScale) -> CGFloat {
        document.resize ?? (setting == .native ? 1 : 1 / document.scale)
    }

    /// The output flattened at its size, inside its backdrop when there is one.
    public static func export(_ document: Document, scale: ExportScale) -> ExportedImage? {
        let outputScale = outputScale(of: document, setting: scale)
        guard let image = Renderer.renderFramed(document, outputScale: outputScale) else { return nil }
        // Never under 72, so a Retina capture keeps its size in points down to 50%, with
        // fewer pixels, and only shows smaller below that.
        let dpi = max(72, 72 * document.scale * outputScale)
        return ExportedImage(image: image, dpi: dpi,
                             pointSize: CGSize(width: CGFloat(image.width) * 72 / dpi, height: CGFloat(image.height) * 72 / dpi))
    }

    /// What text and codes are read from: every capture pixel, whatever size the capture
    /// exports at, with the annotations drawn, so nothing under a blur or an erase is read
    /// back out, and the crop applied, but no backdrop, which holds nothing to read. `area`,
    /// in capture pixels, narrows it to what was dragged over; nil when that misses the output.
    public static func readingImage(_ document: Document, in area: CGRect? = nil) -> CGImage? {
        guard let output = Renderer.renderOutput(document) else { return nil }
        guard let area else { return output }
        let origin = document.outputPixelRect.origin
        let cut = area.offsetBy(dx: -origin.x, dy: -origin.y).integral
            .intersection(CGRect(x: 0, y: 0, width: output.width, height: output.height))
        guard !cut.isNull, cut.width >= 1, cut.height >= 1 else { return nil }
        return output.cropping(to: cut)
    }

    public static func pngData(_ exported: ExportedImage) -> Data? {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else {
            return nil
        }
        let properties = [kCGImagePropertyDPIWidth: exported.dpi, kCGImagePropertyDPIHeight: exported.dpi] as CFDictionary
        CGImageDestinationAddImage(destination, exported.image, properties)
        guard CGImageDestinationFinalize(destination) else { return nil }
        return data as Data
    }
}

public enum FileNaming {
    /// "Tinysnap 2026-09-25 at 09.41.12.png", the macOS screenshot pattern, or with
    /// " 2", " 3" and so on before the extension when that name is taken.
    public static func fileName(for date: Date, timeZone: TimeZone = .current, isTaken: (String) -> Bool) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
        let base = "Tinysnap \(formatter.string(from: date))"

        var candidate = base + ".png"
        var number = 2
        while isTaken(candidate) {
            candidate = "\(base) \(number).png"
            number += 1
        }
        return candidate
    }
}
