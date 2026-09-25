import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

struct DocumentArchiveTests {
    private let captured = Date(timeIntervalSince1970: 1_790_300_530)

    private func everyKind(pasted: PastedImage) -> [Annotation] {
        let blue = Style(colorHex: "#007AFF", size: .large, filled: true, corners: .full)
        return [
            Fixture.annotation(.arrow(from: CGPoint(x: 10, y: 20), to: CGPoint(x: 110.5, y: 80))),
            Fixture.annotation(.line(from: CGPoint(x: 5, y: 5), to: CGPoint(x: 50, y: 5))),
            Annotation(kind: .rectangle(CGRect(x: 20, y: 30, width: 100, height: 60)), style: blue),
            Fixture.annotation(.oval(CGRect(x: 40, y: 40, width: 30, height: 20))),
            Fixture.annotation(.text(origin: CGPoint(x: 12, y: 14), string: "Two\nlines")),
            Fixture.annotation(.highlighter(from: CGPoint(x: 0, y: 90), to: CGPoint(x: 90, y: 90))),
            Fixture.annotation(.freehand([CGPoint(x: 1, y: 2), CGPoint(x: 3, y: 4), CGPoint(x: 5, y: 7)])),
            Fixture.annotation(.step(center: CGPoint(x: 70, y: 70))),
            Fixture.annotation(.spotlight(CGRect(x: 10, y: 10, width: 40, height: 40))),
            Fixture.annotation(.magnifier(center: CGPoint(x: 150, y: 100), radius: 30, zoom: 2.5)),
            Fixture.annotation(.image(CGRect(x: 100, y: 10, width: 40, height: 30), pasted)),
            Fixture.annotation(.blur(CGRect(x: 0, y: 0, width: 20, height: 20))),
            Fixture.annotation(.pixelate(CGRect(x: 20, y: 0, width: 20, height: 20))),
            Fixture.annotation(.erase(CGRect(x: 40, y: 0, width: 20, height: 20))),
        ]
    }

    @Test func everyKindStyleCropAndPastedImageComeBack() throws {
        let pasted = PastedImage(Fixture.capture(width: 8, height: 6, fill: Fixture.blue).image)
        let document = Document(capture: Fixture.capture(width: 200, height: 120, scale: 2),
                                crop: CGRect(x: 4, y: 6, width: 150, height: 100), annotations: everyKind(pasted: pasted))

        let (json, images) = try DocumentArchive.encode(document, captured: captured)
        let edits = try DocumentArchive.decode(json) { images[$0] }

        #expect(edits.captured == captured)
        #expect(edits.scale == 2)
        #expect(edits.crop == document.crop)
        // A pasted image comes back as new pixels, so it is swapped for the original to
        // compare everything else exactly.
        let restored = edits.annotations.map { annotation -> Annotation in
            guard case let .image(rect, image) = annotation.kind else { return annotation }
            #expect(image.image.width == 8 && image.image.height == 6)
            var same = annotation
            same.kind = .image(rect, pasted)
            return same
        }
        #expect(restored == document.annotations)
    }

    @Test func eachPastedImageIsNamedForItsAnnotation() throws {
        let pasted = PastedImage(Fixture.capture(width: 4, height: 4).image)
        let annotation = Fixture.annotation(.image(CGRect(x: 0, y: 0, width: 4, height: 4), pasted))
        let document = Document(capture: Fixture.capture(width: 20, height: 20), annotations: [annotation])
        let (_, images) = try DocumentArchive.encode(document, captured: captured)
        #expect(Array(images.keys) == ["pasted-\(annotation.id.uuidString).png"])
    }

    @Test func theExampleInTheSpecDecodes() throws {
        let json = #"""
        {
          "version": 1,
          "captured": "2026-09-25T07:42:10+05:00",
          "scale": 2,
          "crop": { "x": 40, "y": 20, "width": 1100, "height": 740 },
          "annotations": [
            {
              "id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10",
              "kind": "arrow",
              "from": [120, 610],
              "to": [470, 330],
              "style": { "colorHex": "#FF3B30", "size": "medium", "filled": false, "corners": "medium" }
            },
            {
              "id": "0B7E54C2-91A4-4F0E-8C3D-2E6F1A9B7D44",
              "kind": "image",
              "rect": { "x": 700, "y": 90, "width": 320, "height": 200 },
              "file": "pasted-0B7E54C2-91A4-4F0E-8C3D-2E6F1A9B7D44.png",
              "style": { "colorHex": "#FF3B30", "size": "medium", "filled": false, "corners": "square" }
            }
          ]
        }
        """#
        var asked: [String] = []
        let edits = try DocumentArchive.decode(Data(json.utf8)) { name in
            asked.append(name)
            return Fixture.capture(width: 2, height: 2).image
        }
        #expect(edits.scale == 2)
        #expect(edits.crop == CGRect(x: 40, y: 20, width: 1100, height: 740))
        #expect(edits.captured == Date(timeIntervalSince1970: 1_790_304_130))
        #expect(edits.annotations.first?.kind == .arrow(from: CGPoint(x: 120, y: 610), to: CGPoint(x: 470, y: 330)))
        #expect(edits.annotations.last?.style.corners == .square)
        #expect(asked == ["pasted-0B7E54C2-91A4-4F0E-8C3D-2E6F1A9B7D44.png"])
    }

    @Test func anythingItCanNotRebuildExactlyThrows() {
        let valid = #"{"version": 1, "captured": "2026-09-25T07:42:10Z", "scale": 2, "annotations": []}"#
        #expect(throws: Never.self) { try DocumentArchive.decode(Data(valid.utf8)) { _ in nil } }

        let otherVersion = valid.replacingOccurrences(of: #""version": 1"#, with: #""version": 2"#)
        let unknownKind = valid.replacingOccurrences(of: "[]", with: #"[{"id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10", "kind": "sticker", "style": {}}]"#)
        let missingField = valid.replacingOccurrences(of: "[]", with: #"[{"id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10", "kind": "arrow", "from": [1, 2], "style": {}}]"#)
        let missingImage = valid.replacingOccurrences(of: "[]", with: #"[{"id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10", "kind": "image", "rect": {"x": 0, "y": 0, "width": 4, "height": 4}, "file": "pasted-gone.png", "style": {}}]"#)
        // A scale that would give the capture no size, or an absurd one, crashed the editor.
        let badScales = ["0", "-2", "1e300"].map { valid.replacingOccurrences(of: #""scale": 2"#, with: #""scale": \#($0)"#) }
        for broken in [otherVersion, unknownKind, missingField, missingImage, "{", ""] + badScales {
            #expect(throws: (any Error).self) { try DocumentArchive.decode(Data(broken.utf8)) { _ in nil } }
        }
    }
}
