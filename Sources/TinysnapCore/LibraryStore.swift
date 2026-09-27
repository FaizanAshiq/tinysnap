import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

/// One capture in the library: a folder named for its capture time.
public struct LibraryEntry: Hashable, Sendable {
    public let folder: URL
    public let captured: Date

    public var name: String { folder.lastPathComponent }
    /// The captured pixels, never changed.
    public var originalURL: URL { folder.appendingPathComponent("original.png") }
    /// Crop and annotations, rewritten as they change.
    public var editsURL: URL { folder.appendingPathComponent("edits.json") }
    /// The rendered result, for Quick Look, drag, copy and pin.
    public var imageURL: URL { folder.appendingPathComponent("image.png") }
}

/// An entry read back: editable when its edits could be rebuilt exactly, otherwise a
/// plain capture of the best image left, so a damaged entry never loses the capture.
public struct OpenedEntry {
    public let document: Document
    public let isEditable: Bool
}

/// The folders behind the library. Plain file operations, safe to call from any thread;
/// the app serialises writes to one entry by writing it from one place.
public struct LibraryStore: Sendable {
    public let root: URL
    public static let keepDays = 30

    public static var defaultRoot: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Tinysnap/Library", isDirectory: true)
    }

    public init(root: URL = LibraryStore.defaultRoot) {
        self.root = root
    }

    // MARK: Writing

    /// Creates the entry and writes all three files. The folder's creation date is set
    /// to the capture time, whole seconds, which is what listing and the sweep read, so
    /// even an entry whose edits are damaged keeps its place and its age.
    public func add(_ capture: Capture, captured: Date, timeZone: TimeZone = .current) throws -> LibraryEntry {
        let whole = Date(timeIntervalSince1970: captured.timeIntervalSince1970.rounded(.down))
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let folder = root.appendingPathComponent(freeName(for: whole, timeZone: timeZone), isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
        try FileManager.default.setAttributes([.creationDate: whole], ofItemAtPath: folder.path)
        let entry = LibraryEntry(folder: folder, captured: whole)

        let png = try Self.png(capture.image, scale: capture.scale)
        try png.write(to: entry.originalURL, options: .atomic)
        try saveEdits(Document(capture: capture), to: entry)
        try png.write(to: entry.imageURL, options: .atomic)
        return entry
    }

    /// Rewrites `edits.json`, writes any pasted image or backdrop wallpaper not yet on
    /// disk, and removes those nothing uses any more. Their pixels never change, so one
    /// already written is left alone.
    public func saveEdits(_ document: Document, to entry: LibraryEntry) throws {
        let (json, images) = try DocumentArchive.encode(document, captured: entry.captured)
        for (name, image) in images where !FileManager.default.fileExists(atPath: entry.folder.appendingPathComponent(name).path) {
            try Self.png(image, scale: 1).write(to: entry.folder.appendingPathComponent(name), options: .atomic)
        }
        try json.write(to: entry.editsURL, options: .atomic)
        let files = (try? FileManager.default.contentsOfDirectory(atPath: entry.folder.path)) ?? []
        for file in files where (file.hasPrefix("pasted-") || file.hasPrefix("backdrop-")) && images[file] == nil {
            try? FileManager.default.removeItem(at: entry.folder.appendingPathComponent(file))
        }
    }

    /// Renders the document at its size, or at full resolution when it has none, crop applied.
    public func saveImage(_ document: Document, to entry: LibraryEntry) throws {
        guard let exported = Exporter.export(document, scale: .native),
              let png = Exporter.pngData(exported) else { throw CocoaError(.fileWriteUnknown) }
        try png.write(to: entry.imageURL, options: .atomic)
    }

    // MARK: Reading

    /// Newest first. A folder with neither image is not an entry.
    public func entries() -> [LibraryEntry] {
        let keys: [URLResourceKey] = [.isDirectoryKey, .creationDateKey]
        let folders = (try? FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: keys,
                                                                    options: .skipsHiddenFiles)) ?? []
        return folders.compactMap { folder -> LibraryEntry? in
            guard let values = try? folder.resourceValues(forKeys: Set(keys)), values.isDirectory == true,
                  let created = values.creationDate else { return nil }
            let entry = LibraryEntry(folder: folder, captured: created)
            let hasImage = FileManager.default.fileExists(atPath: entry.imageURL.path)
                || FileManager.default.fileExists(atPath: entry.originalURL.path)
            return hasImage ? entry : nil
        }
        .sorted { ($0.captured, $0.name) > ($1.captured, $1.name) }
    }

    public func open(_ entry: LibraryEntry) -> OpenedEntry? {
        if let original = Self.readImage(entry.originalURL),
           let json = try? Data(contentsOf: entry.editsURL),
           let edits = try? DocumentArchive.decode(json, image: { Self.readImage(entry.folder.appendingPathComponent($0))?.image }) {
            let capture = Capture(image: original.image, scale: edits.scale)
            return OpenedEntry(document: Document(capture: capture, crop: edits.crop, annotations: edits.annotations,
                                                  backdrop: edits.backdrop, resize: edits.resize),
                               isEditable: true)
        }
        guard let flat = Self.readImage(entry.imageURL) ?? Self.readImage(entry.originalURL) else { return nil }
        return OpenedEntry(document: Document(capture: Capture(image: flat.image, scale: flat.scale)), isEditable: false)
    }

    /// True after a crash between an edit and the next render: the edits are newer than
    /// the image, so the image is rendered again before it is shown.
    public func imageIsStale(_ entry: LibraryEntry) -> Bool {
        func modified(_ url: URL) -> Date? { try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate }
        guard let edits = modified(entry.editsURL), let image = modified(entry.imageURL) else { return false }
        return edits > image
    }

    /// The bytes every entry takes, for Settings.
    public func size() -> Int64 {
        guard let files = FileManager.default.enumerator(at: root, includingPropertiesForKeys: [.fileSizeKey]) else { return 0 }
        var total: Int64 = 0
        for case let url as URL in files {
            total += Int64((try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0)
        }
        return total
    }

    // MARK: Removing

    /// Deletes every entry more than 30 days old, except those named in `keeping`, which
    /// are open in an editor. Returns what went.
    @discardableResult
    public func sweep(now: Date = Date(), keeping: Set<String> = []) -> [LibraryEntry] {
        let cutoff = now.addingTimeInterval(-Double(Self.keepDays) * 86_400)
        let old = entries().filter { $0.captured < cutoff && !keeping.contains($0.name) }
        for entry in old { try? FileManager.default.removeItem(at: entry.folder) }
        return old
    }

    public func clear(keeping: Set<String> = []) {
        for entry in entries() where !keeping.contains(entry.name) {
            try? FileManager.default.removeItem(at: entry.folder)
        }
    }

    // MARK: Files

    /// "2026-09-25 07.42.10", or with " 2", " 3" and so on when that is taken.
    private func freeName(for date: Date, timeZone: TimeZone) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyy-MM-dd HH.mm.ss"
        let base = formatter.string(from: date)
        var name = base
        var number = 2
        while FileManager.default.fileExists(atPath: root.appendingPathComponent(name).path) {
            name = "\(base) \(number)"
            number += 1
        }
        return name
    }

    /// A PNG that records its pixels per point as DPI, the way exports do, so the scale
    /// comes back with it.
    static func png(_ image: CGImage, scale: CGFloat) throws -> Data {
        let exported = ExportedImage(image: image, dpi: 72 * scale,
                                     pointSize: CGSize(width: CGFloat(image.width) / scale, height: CGFloat(image.height) / scale))
        guard let data = Exporter.pngData(exported) else { throw CocoaError(.fileWriteUnknown) }
        return data
    }

    /// An image and its pixels per point, read from the DPI it was written with.
    public static func readImage(_ url: URL) -> (image: CGImage, scale: CGFloat)? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return nil }
        let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
        let dpi = (properties?[kCGImagePropertyDPIWidth] as? NSNumber)?.doubleValue ?? 72
        return (image, max(1, CGFloat(dpi / 72).rounded()))
    }
}
