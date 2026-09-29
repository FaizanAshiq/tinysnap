import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// The Windows port keeps the same library. Each app writes one entry holding every kind of
/// edit, and each app's tests open the other's. `TINYSNAP_WRITE_FIXTURES=1 ./test.sh` writes
/// this app's entry again; otherwise the writer does nothing.
struct CompatibilityTests {
    private static let fixtures = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("dotnet/tests/fixtures")
    private static let captured = Date(timeIntervalSince1970: 1_790_304_130)

    /// Fixed, so both apps build the same annotations.
    private static func id(_ number: Int) -> UUID {
        UUID(uuidString: String(format: "00000000-0000-4000-8000-%012d", number))!
    }

    /// Every annotation kind, styles off their defaults, a crop, a size, a measurement whose
    /// tag slid off its middle, a pasted image and a wallpaper backdrop. The C# tests build
    /// the same document.
    static func everything(scale: CGFloat) -> Document {
        let red = Style(colorHex: "#FF3B30")
        let pasted = PastedImage(Fixture.capture(width: 8, height: 6, fill: Fixture.blue).image)
        let annotations = [
            Annotation(id: id(1), kind: .arrow(from: CGPoint(x: 10, y: 20), to: CGPoint(x: 110.5, y: 80)), style: red),
            Annotation(id: id(2), kind: .line(from: CGPoint(x: 5, y: 5), to: CGPoint(x: 50, y: 5)),
                       style: Style(colorHex: "#AF52DE", size: .extraSmall)),
            Annotation(id: id(3), kind: .rectangle(CGRect(x: 20, y: 30, width: 100, height: 60)),
                       style: Style(colorHex: "#007AFF", size: .large, filled: true, corners: .full)),
            Annotation(id: id(4), kind: .oval(CGRect(x: 40, y: 40, width: 30, height: 20)), style: red),
            Annotation(id: id(5), kind: .text(origin: CGPoint(x: 12, y: 14), string: "Two\nlines, Größe اردو ✓"),
                       style: Style(colorHex: "#000000", size: .extraLarge)),
            Annotation(id: id(6), kind: .highlighter(from: CGPoint(x: 0, y: 90), to: CGPoint(x: 90, y: 90)),
                       style: Style(colorHex: "#FFCC00")),
            Annotation(id: id(7), kind: .freehand([CGPoint(x: 1, y: 2), CGPoint(x: 3, y: 4), CGPoint(x: 5, y: 7)]), style: red),
            Annotation(id: id(8), kind: .step(center: CGPoint(x: 70, y: 70)), style: red),
            Annotation(id: id(9), kind: .spotlight(CGRect(x: 10, y: 10, width: 40, height: 40)),
                       style: Style(colorHex: "#FF3B30", corners: .small)),
            Annotation(id: id(10), kind: .magnifier(center: CGPoint(x: 150, y: 100), radius: 30, zoom: 2.5), style: red),
            Annotation(id: id(11), kind: .image(CGRect(x: 100, y: 10, width: 40, height: 30), pasted),
                       style: Style(colorHex: "#34C759", corners: .square, opacity: 0.5, difference: true)),
            Annotation(id: id(12), kind: .blur(CGRect(x: 0, y: 0, width: 20, height: 20)), style: red),
            Annotation(id: id(13), kind: .pixelate(CGRect(x: 20, y: 0, width: 20, height: 20)), style: red),
            Annotation(id: id(14), kind: .erase(CGRect(x: 40, y: 0, width: 20, height: 20)), style: red),
            Annotation(id: id(15), kind: .measure(from: CGPoint(x: 150, y: 20), to: CGPoint(x: 150, y: 180)), style: red,
                       labelAt: 0.3),
        ]
        let wallpaper = Backdrop.Wallpaper(id: id(99), image: PastedImage(Fixture.capture(width: 16, height: 10, fill: Fixture.green).image))
        let backdrop = Backdrop(fill: .wallpaper, colorHex: "#34C759", padding: .large, corners: .large, shadow: .strong,
                                wallpaper: wallpaper)
        var document = Document(capture: Fixture.capture(width: 300, height: 200, scale: scale),
                                crop: CGRect(x: 4, y: 6, width: 250, height: 150), annotations: annotations, backdrop: backdrop)
        document.resize = 0.5
        return document
    }

    @Test func writesTheMacFixtures() throws {
        guard ProcessInfo.processInfo.environment["TINYSNAP_WRITE_FIXTURES"] != nil else { return }
        let library = LibraryStore(root: FileManager.default.temporaryDirectory
            .appendingPathComponent("tinysnap-fixtures-\(UUID().uuidString)"))
        let document = Self.everything(scale: 2)
        let entry = try library.add(document.capture, captured: Self.captured, timeZone: TimeZone(identifier: "UTC")!)
        try library.saveEdits(document, to: entry)
        try library.saveImage(document, to: entry)
        let target = Self.fixtures.appendingPathComponent("mac/everything")
        try? FileManager.default.removeItem(at: target)
        try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        try FileManager.default.moveItem(at: entry.folder, to: target)
    }

    @Test func readsTheDotnetFixtures() throws {
        let folder = Self.fixtures.appendingPathComponent("dotnet/everything")
        let library = LibraryStore(root: folder.deletingLastPathComponent())
        let opened = try #require(library.open(LibraryEntry(folder: folder, captured: Self.captured)))
        #expect(opened.isEditable)

        let document = opened.document, expected = Self.everything(scale: 1.5)
        #expect(document.scale == 1.5)
        #expect(document.capture.pixelSize == expected.capture.pixelSize)
        #expect(document.crop == expected.crop)
        #expect(document.resize == expected.resize)
        #expect(document.backdrop == expected.backdrop)
        #expect(document.backdrop?.wallpaper?.image.image.width == 16)
        // A pasted image comes back as new pixels, so it is checked by size and swapped for
        // the original to compare everything else exactly.
        let restored = document.annotations.map { annotation -> Annotation in
            guard case let .image(rect, image) = annotation.kind,
                  case let .image(_, original)? = expected.annotations.first(where: { $0.id == annotation.id })?.kind
            else { return annotation }
            #expect(image.image.width == 8 && image.image.height == 6)
            var same = annotation
            same.kind = .image(rect, original)
            return same
        }
        #expect(restored == expected.annotations)
    }
}
