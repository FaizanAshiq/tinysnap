import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

struct LibraryStoreTests {
    @Test func aWallpaperIsKeptBesideTheEditsAndGoesWhenTheBackdropDoes() throws {
        let library = LibraryStore(root: FileManager.default.temporaryDirectory.appendingPathComponent("tinysnap-library-\(UUID().uuidString)"))
        let entry = try library.add(Fixture.capture(width: 40, height: 30, scale: 2), captured: Date(timeIntervalSince1970: 1_790_304_130),
                                    timeZone: TimeZone(identifier: "UTC")!)
        var document = try #require(library.open(entry)).document
        var backdrop = Backdrop(fill: .wallpaper, colorHex: "#007AFF", padding: .small, corners: .medium, shadow: .soft)
        let wallpaper = Backdrop.Wallpaper(image: PastedImage(Fixture.capture(width: 8, height: 4).image))
        backdrop.wallpaper = wallpaper
        document.backdrop = backdrop
        try library.saveEdits(document, to: entry)
        let file = entry.folder.appendingPathComponent("backdrop-\(wallpaper.id.uuidString).png")
        #expect(FileManager.default.fileExists(atPath: file.path))
        #expect(try #require(library.open(entry)).document.backdrop == backdrop)

        document.backdrop = nil
        try library.saveEdits(document, to: entry)
        #expect(!FileManager.default.fileExists(atPath: file.path))
        #expect(try #require(library.open(entry)).document.backdrop == nil)
    }

    private let utc = TimeZone(identifier: "UTC")!
    /// 2026-09-25 02:42:10 UTC.
    private let captured = Date(timeIntervalSince1970: 1_790_304_130)

    private func store() throws -> LibraryStore {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("tinysnap-library-\(UUID().uuidString)")
        return LibraryStore(root: root)
    }

    private func exists(_ url: URL) -> Bool { FileManager.default.fileExists(atPath: url.path) }

    @Test func anEntryReopensWithItsSizeAndItsImageIsDrawnAtIt() throws {
        let library = try store()
        let entry = try library.add(Fixture.capture(width: 40, height: 30, scale: 2), captured: captured, timeZone: utc)
        var document = try #require(library.open(entry)).document
        document.resize = 0.5
        try library.saveEdits(document, to: entry)
        try library.saveImage(document, to: entry)
        #expect(try #require(library.open(entry)).document.resize == 0.5)
        #expect(try #require(LibraryStore.readImage(entry.imageURL)).image.width == 20)
    }

    @Test func aCaptureIsKeptAsThreeFilesInAFolderNamedForItsTime() throws {
        let library = try store()
        let capture = Fixture.capture(width: 40, height: 30, scale: 2)
        let entry = try library.add(capture, captured: captured.addingTimeInterval(0.6), timeZone: utc)

        #expect(entry.name == "2026-09-25 02.42.10")
        #expect(entry.captured == captured)
        #expect(exists(entry.originalURL) && exists(entry.editsURL) && exists(entry.imageURL))
        let opened = try #require(library.open(entry))
        #expect(opened.isEditable)
        #expect(opened.document.scale == 2)
        #expect(opened.document.capture.pixelSize == CGSize(width: 40, height: 30))
        #expect(opened.document.annotations.isEmpty)
    }

    @Test func capturesInTheSameSecondGetANumberAppended() throws {
        let library = try store()
        let names = try (0..<3).map { _ in try library.add(Fixture.capture(width: 4, height: 4), captured: captured, timeZone: utc).name }
        #expect(names == ["2026-09-25 02.42.10", "2026-09-25 02.42.10 2", "2026-09-25 02.42.10 3"])
    }

    @Test func editsComeBackEditableAndUnusedPastedImagesGo() throws {
        let library = try store()
        let entry = try library.add(Fixture.capture(width: 100, height: 80, scale: 2), captured: captured, timeZone: utc)
        let pasted = PastedImage(Fixture.capture(width: 6, height: 5, fill: Fixture.blue).image)
        let arrow = Fixture.annotation(.arrow(from: CGPoint(x: 5, y: 5), to: CGPoint(x: 60, y: 40)))
        let image = Fixture.annotation(.image(CGRect(x: 10, y: 10, width: 12, height: 10), pasted))
        var document = try #require(library.open(entry)).document
        document.crop = CGRect(x: 2, y: 2, width: 90, height: 70)
        document.annotations = [arrow, image]

        try library.saveEdits(document, to: entry)
        let pastedURL = entry.folder.appendingPathComponent("pasted-\(image.id.uuidString).png")
        #expect(exists(pastedURL))
        let reopened = try #require(library.open(entry)).document
        #expect(reopened.crop == document.crop)
        #expect(reopened.annotations.first == arrow)
        guard case let .image(rect, pixels) = reopened.annotations.last?.kind else {
            Issue.record("the pasted image did not come back")
            return
        }
        #expect(rect == CGRect(x: 10, y: 10, width: 12, height: 10))
        #expect(pixels.image.width == 6)

        document.annotations = [arrow]
        try library.saveEdits(document, to: entry)
        #expect(!exists(pastedURL))
    }

    @Test func theImageIsTheRenderedCropAndGoesStaleWhenEditsAreNewer() throws {
        let library = try store()
        let entry = try library.add(Fixture.capture(width: 100, height: 80, scale: 2), captured: captured, timeZone: utc)
        var document = try #require(library.open(entry)).document
        document.crop = CGRect(x: 10, y: 10, width: 50, height: 40)
        document.annotations = [Fixture.annotation(.erase(CGRect(x: 10, y: 10, width: 20, height: 20)))]

        try library.saveEdits(document, to: entry)
        #expect(library.imageIsStale(entry))
        try library.saveImage(document, to: entry)
        #expect(!library.imageIsStale(entry))
        let flat = try #require(LibraryStore.readImage(entry.imageURL))
        #expect(flat.image.width == 50 && flat.image.height == 40)
        #expect(flat.scale == 2)
    }

    @Test func entriesListNewestFirstAndLeaveOutFoldersWithNoImage() throws {
        let library = try store()
        for offset in [0.0, 120, 60] {
            _ = try library.add(Fixture.capture(width: 4, height: 4), captured: captured.addingTimeInterval(offset), timeZone: utc)
        }
        try FileManager.default.createDirectory(at: library.root.appendingPathComponent("not a capture"),
                                                withIntermediateDirectories: true)
        #expect(library.entries().map(\.name) == ["2026-09-25 02.44.10", "2026-09-25 02.43.10", "2026-09-25 02.42.10"])
    }

    @Test func aDamagedEntryOpensFlatFromTheBestImageLeft() throws {
        let library = try store()
        let entry = try library.add(Fixture.capture(width: 30, height: 20, scale: 2), captured: captured, timeZone: utc)
        var document = try #require(library.open(entry)).document
        document.crop = CGRect(x: 0, y: 0, width: 10, height: 10)
        try library.saveImage(document, to: entry)
        try Data("{ not json".utf8).write(to: entry.editsURL)

        let flat = try #require(library.open(entry))
        #expect(!flat.isEditable)
        #expect(flat.document.capture.pixelSize == CGSize(width: 10, height: 10))
        #expect(flat.document.scale == 2)
        #expect(library.entries().map(\.captured) == [captured])

        try FileManager.default.removeItem(at: entry.imageURL)
        #expect(library.open(entry)?.document.capture.pixelSize == CGSize(width: 30, height: 20))
        try FileManager.default.removeItem(at: entry.originalURL)
        #expect(library.open(entry) == nil)
        #expect(library.entries().isEmpty)
    }

    @Test func theSweepDeletesOnlyEntriesPastThirtyDaysAndSparesOpenOnes() throws {
        let library = try store()
        let now = captured
        let day: TimeInterval = 86_400
        let fresh = try library.add(Fixture.capture(width: 4, height: 4), captured: now.addingTimeInterval(-29 * day), timeZone: utc)
        let old = try library.add(Fixture.capture(width: 4, height: 4), captured: now.addingTimeInterval(-31 * day), timeZone: utc)
        let open = try library.add(Fixture.capture(width: 4, height: 4), captured: now.addingTimeInterval(-40 * day), timeZone: utc)

        let removed = library.sweep(now: now, keeping: [open.name])
        #expect(removed.map(\.name) == [old.name])
        #expect(Set(library.entries().map(\.name)) == [fresh.name, open.name])
    }

    @Test func clearingSparesOpenEntriesAndTheSizeCountsEveryFile() throws {
        let library = try store()
        let kept = try library.add(Fixture.capture(width: 50, height: 50), captured: captured, timeZone: utc)
        _ = try library.add(Fixture.capture(width: 50, height: 50), captured: captured.addingTimeInterval(1), timeZone: utc)
        let before = library.size()
        #expect(before > 0)

        library.clear(keeping: [kept.name])
        #expect(library.entries().map(\.name) == [kept.name])
        #expect(library.size() < before)
    }
}
