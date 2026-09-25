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
    /// 72 times the pixels per point, so the PNG pastes at the size it had on screen.
    public let dpi: CGFloat
    /// The size the image had on screen.
    public let pointSize: CGSize
}

public enum Exporter {
    /// The output flattened, inside its backdrop when there is one. Native keeps every
    /// capture pixel, 1x draws one pixel per point.
    public static func export(_ document: Document, scale: ExportScale) -> ExportedImage? {
        let outputScale: CGFloat = scale == .native ? 1 : 1 / document.scale
        guard let image = Renderer.renderFramed(document, outputScale: outputScale) else { return nil }
        let dpi = scale == .native ? 72 * document.scale : 72
        return ExportedImage(image: image, dpi: dpi,
                             pointSize: CGSize(width: CGFloat(image.width) * 72 / dpi, height: CGFloat(image.height) * 72 / dpi))
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
