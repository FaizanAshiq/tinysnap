import CoreGraphics
import Foundation

/// What `edits.json` holds: everything about a document except its capture pixels,
/// which the library keeps beside it as `original.png`.
public struct ArchivedEdits {
    public var captured: Date
    public var scale: CGFloat
    public var crop: CGRect?
    public var annotations: [Annotation]
    public var backdrop: Backdrop?
    public var resize: CGFloat?
    /// 1 for a file from before steps had a start.
    public var stepStart: Int = 1
}

/// A document to and from `edits.json`, plus one PNG per pasted image. Coordinates stay
/// capture pixels, y growing downward, as everywhere in Core.
public enum DocumentArchive {
    public static let version = 1

    public enum Failure: Error, Equatable {
        case unsupportedVersion(Int)
        case unknownKind(String)
        case missingField(String)
        case missingImage(String)
        case badScale(CGFloat)
    }

    /// The JSON, and the pasted images it names, keyed by file name.
    public static func encode(_ document: Document, captured: Date) throws -> (json: Data, images: [String: CGImage]) {
        var images: [String: CGImage] = [:]
        let items = document.annotations.map { annotation -> Item in
            var item = Item(id: annotation.id, kind: "", style: annotation.style)
            // Only a tag slid off its middle is written, so everything else reads as before.
            if annotation.labelAt != 0.5 { item.labelAt = annotation.labelAt }
            // Written only when on, so an older Tinysnap reads the file as before.
            if annotation.isLocked { item.locked = true }
            if annotation.isHidden { item.hidden = true }
            item.serial = annotation.serial
            switch annotation.kind {
            case let .arrow(from, to): item.kind = "arrow"; item.from = pair(from); item.to = pair(to)
            case let .line(from, to): item.kind = "line"; item.from = pair(from); item.to = pair(to)
            case let .measure(from, to): item.kind = "measure"; item.from = pair(from); item.to = pair(to)
            case let .highlighter(from, to): item.kind = "highlighter"; item.from = pair(from); item.to = pair(to)
            case let .rectangle(rect): item.kind = "rectangle"; item.rect = Box(rect)
            case let .oval(rect): item.kind = "oval"; item.rect = Box(rect)
            case let .spotlight(rect): item.kind = "spotlight"; item.rect = Box(rect)
            case let .blur(rect): item.kind = "blur"; item.rect = Box(rect)
            case let .pixelate(rect): item.kind = "pixelate"; item.rect = Box(rect)
            case let .erase(rect): item.kind = "erase"; item.rect = Box(rect)
            case let .text(origin, string): item.kind = "text"; item.origin = pair(origin); item.string = string
            case let .freehand(points): item.kind = "freehand"; item.points = points.map(pair)
            case let .highlighterPath(points):
                // The ends as well, so an older Tinysnap draws it as a straight highlight.
                item.kind = "highlighter"; item.points = points.map(pair)
                item.from = pair(points.first ?? .zero); item.to = pair(points.last ?? .zero)
            case let .step(center): item.kind = "step"; item.center = pair(center)
            case let .magnifier(center, radius, zoom):
                item.kind = "magnifier"; item.center = pair(center); item.radius = radius; item.zoom = zoom
            case let .image(rect, pasted):
                let name = "pasted-\(annotation.id.uuidString).png"
                images[name] = pasted.image
                item.kind = "image"; item.rect = Box(rect); item.file = name
            }
            return item
        }
        var backdrop = document.backdrop.map { StoredBackdrop(settings: $0, file: nil) }
        if let wallpaper = document.backdrop?.wallpaper, document.backdrop?.fill == .wallpaper {
            // Named for the wallpaper, whose pixels never change, so it is written once.
            let name = "backdrop-\(wallpaper.id.uuidString).png"
            images[name] = wallpaper.image.image
            backdrop?.file = name
        }
        let file = File(version: version, captured: captured, scale: document.scale,
                        crop: document.crop.map(Box.init), annotations: items, backdrop: backdrop, resize: document.resize,
                        stepStart: document.stepStart == 1 ? nil : document.stepStart)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        return (try encoder.encode(file), images)
    }

    /// Throws for anything it can not rebuild exactly, so the library can fall back to
    /// the flat image rather than open a document with pieces missing.
    public static func decode(_ json: Data, image: (String) -> CGImage?) throws -> ArchivedEdits {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let file = try decoder.decode(File.self, from: json)
        guard file.version == version else { throw Failure.unsupportedVersion(file.version) }
        // Pixels per point: 1 to 3 on real displays. Anything outside a generous range gives
        // the capture no size or an absurd one, which the editor can not lay out.
        guard file.scale.isFinite, (1...8).contains(file.scale) else { throw Failure.badScale(file.scale) }
        let annotations = try file.annotations.map { item in
            Annotation(id: item.id, kind: try kind(of: item, image: image), style: item.style,
                       labelAt: item.labelAt.flatMap { (0...1).contains($0) ? $0 : nil } ?? 0.5,
                       isLocked: item.locked ?? false, isHidden: item.hidden ?? false, serial: max(0, item.serial ?? 0))
        }
        return ArchivedEdits(captured: file.captured, scale: file.scale, crop: file.crop?.rect, annotations: annotations,
                             backdrop: file.backdrop.map { backdrop(from: $0, image: image) }, resize: file.resize,
                             stepStart: file.stepStart ?? 1)
    }

    /// A wallpaper whose file is gone turns the fill into the gradient, rather than
    /// losing the entry over one picture.
    private static func backdrop(from stored: StoredBackdrop, image: (String) -> CGImage?) -> Backdrop {
        var backdrop = stored.settings
        guard backdrop.fill == .wallpaper else { return backdrop }
        guard let name = stored.file, let pixels = image(name) else {
            backdrop.fill = .gradient
            return backdrop
        }
        let id = UUID(uuidString: String(name.dropFirst("backdrop-".count).dropLast(".png".count))) ?? UUID()
        backdrop.wallpaper = Backdrop.Wallpaper(id: id, image: PastedImage(pixels))
        return backdrop
    }

    private static func kind(of item: Item, image: (String) -> CGImage?) throws -> Annotation.Kind {
        func need<T>(_ value: T?, _ name: String) throws -> T {
            guard let value else { throw Failure.missingField(name) }
            return value
        }
        func point(_ value: [CGFloat]?, _ name: String) throws -> CGPoint {
            let pair = try need(value, name)
            guard pair.count == 2 else { throw Failure.missingField(name) }
            return CGPoint(x: pair[0], y: pair[1])
        }
        func rect() throws -> CGRect { try need(item.rect, "rect").rect }

        switch item.kind {
        case "arrow": return .arrow(from: try point(item.from, "from"), to: try point(item.to, "to"))
        case "line": return .line(from: try point(item.from, "from"), to: try point(item.to, "to"))
        case "measure": return .measure(from: try point(item.from, "from"), to: try point(item.to, "to"))
        case "highlighter":
            if let points = item.points, points.count >= 2 { return .highlighterPath(try points.map { try point($0, "points") }) }
            return .highlighter(from: try point(item.from, "from"), to: try point(item.to, "to"))
        case "rectangle": return .rectangle(try rect())
        case "oval": return .oval(try rect())
        case "spotlight": return .spotlight(try rect())
        case "blur": return .blur(try rect())
        case "pixelate": return .pixelate(try rect())
        case "erase": return .erase(try rect())
        case "text": return .text(origin: try point(item.origin, "origin"), string: try need(item.string, "string"))
        case "freehand": return .freehand(try need(item.points, "points").map { try point($0, "points") })
        case "step": return .step(center: try point(item.center, "center"))
        case "magnifier":
            return .magnifier(center: try point(item.center, "center"), radius: try need(item.radius, "radius"),
                              zoom: try need(item.zoom, "zoom"))
        case "image":
            let name = try need(item.file, "file")
            guard let pixels = image(name) else { throw Failure.missingImage(name) }
            return .image(try rect(), PastedImage(pixels))
        default:
            throw Failure.unknownKind(item.kind)
        }
    }

    private static func pair(_ point: CGPoint) -> [CGFloat] { [point.x, point.y] }

    // MARK: The file's shape

    private struct File: Codable {
        var version: Int
        var captured: Date
        var scale: CGFloat
        var crop: Box?
        var annotations: [Item]
        var backdrop: StoredBackdrop?
        var resize: CGFloat?
        /// Written only when it is not 1, so an older Tinysnap reads the file as before.
        var stepStart: Int?

        init(version: Int, captured: Date, scale: CGFloat, crop: Box?, annotations: [Item], backdrop: StoredBackdrop?,
             resize: CGFloat?, stepStart: Int?) {
            (self.version, self.captured, self.scale, self.crop, self.annotations, self.backdrop, self.resize, self.stepStart) =
                (version, captured, scale, crop, annotations, backdrop, resize, stepStart)
        }

        private enum CodingKeys: String, CodingKey {
            case version, captured, scale, crop, annotations, backdrop, resize, stepStart
        }

        /// Strict for everything the document needs, lenient for the backdrop and the size:
        /// one that can not be read is left off, and the rest of the entry opens as it was.
        init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            version = try container.decode(Int.self, forKey: .version)
            captured = try container.decode(Date.self, forKey: .captured)
            scale = try container.decode(CGFloat.self, forKey: .scale)
            crop = try container.decodeIfPresent(Box.self, forKey: .crop)
            annotations = try container.decode([Item].self, forKey: .annotations)
            backdrop = (try? container.decodeIfPresent(StoredBackdrop.self, forKey: .backdrop)) ?? nil
            // A size past the limits is as unreadable as a word.
            let resize = (try? container.decodeIfPresent(CGFloat.self, forKey: .resize)) ?? nil
            self.resize = resize.flatMap { Document.resizeLimits.contains($0) ? $0 : nil }
            let start = (try? container.decodeIfPresent(Int.self, forKey: .stepStart)) ?? nil
            stepStart = start.flatMap { Document.stepStartLimits.contains($0) ? $0 : nil }
        }
    }

    /// The backdrop's settings with, for a wallpaper fill, the name of its picture's file.
    private struct StoredBackdrop: Codable {
        var settings: Backdrop
        var file: String?

        init(settings: Backdrop, file: String?) {
            self.settings = settings
            self.file = file
        }

        private enum CodingKeys: String, CodingKey {
            case file
        }

        init(from decoder: Decoder) throws {
            settings = try Backdrop(from: decoder)
            file = (try? decoder.container(keyedBy: CodingKeys.self).decodeIfPresent(String.self, forKey: .file)) ?? nil
        }

        func encode(to encoder: Encoder) throws {
            try settings.encode(to: encoder)
            var container = encoder.container(keyedBy: CodingKeys.self)
            try container.encodeIfPresent(file, forKey: .file)
        }
    }

    /// A rectangle as `{"x", "y", "width", "height"}`, which reads better by hand than
    /// the nested arrays `CGRect` encodes to.
    private struct Box: Codable {
        var x, y, width, height: CGFloat

        init(_ rect: CGRect) {
            (x, y, width, height) = (rect.minX, rect.minY, rect.width, rect.height)
        }

        var rect: CGRect { CGRect(x: x, y: y, width: width, height: height) }
    }

    /// One annotation. Only the fields its kind uses are written.
    private struct Item: Codable {
        var id: UUID
        var kind: String
        var style: Style
        var from, to, origin, center: [CGFloat]?
        var rect: Box?
        var string: String?
        var points: [[CGFloat]]?
        var radius, zoom: CGFloat?
        var file: String?
        var labelAt: CGFloat?
        var locked, hidden: Bool?
        /// The draw order that numbers a layer name; files from before have none.
        var serial: Int?

        init(id: UUID, kind: String, style: Style) {
            self.id = id
            self.kind = kind
            self.style = style
        }
    }
}
