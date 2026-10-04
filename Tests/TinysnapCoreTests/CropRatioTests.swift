import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// The crop can keep a ratio, 1:1, 4:3 or 16:9, for slides and posts that want one.
struct CropRatioTests {
    private func session(_ ratio: CropRatio) -> EditorSession {
        var session = EditorSession(document: Document(capture: Fixture.capture(width: 400, height: 300)), tool: .crop)
        session.restyle { $0.cropRatio = ratio }
        return session
    }

    private func drag(_ session: inout EditorSession, from: CGPoint, to: CGPoint) {
        session.pointerDown(at: from, reach: 4)
        session.pointerDragged(to: to)
        session.pointerUp()
    }

    @Test func aNewCropKeepsTheRatio() throws {
        var editor = session(.sixteenNine)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 330, y: 100))
        let crop = try #require(editor.display.crop)
        #expect(abs(crop.width / crop.height - 16.0 / 9) < 0.02)
        #expect(crop.width == 320)
    }

    @Test func aDragTallerThanWideTurnsTheRatioUpright() throws {
        var editor = session(.fourThree)
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 60, y: 210))
        let crop = try #require(editor.display.crop)
        #expect(abs(crop.height / crop.width - 4.0 / 3) < 0.02)
    }

    @Test func draggingAnEdgeKeepsTheRatio() throws {
        var editor = session(.fourThree)
        drag(&editor, from: CGPoint(x: 100, y: 60), to: CGPoint(x: 260, y: 180))
        let first = try #require(editor.display.crop)
        // The right edge, halfway down.
        drag(&editor, from: CGPoint(x: first.maxX, y: first.midY), to: CGPoint(x: first.maxX + 40, y: first.midY))
        let crop = try #require(editor.display.crop)
        #expect(crop.width > first.width)
        #expect(abs(crop.width / crop.height - 4.0 / 3) < 0.02)
    }

    @Test func pickingARatioTrimsTheCropToItOnce() throws {
        var editor = session(.free)
        drag(&editor, from: CGPoint(x: 20, y: 20), to: CGPoint(x: 320, y: 220))
        editor.restyle { $0.cropRatio = .square }
        let crop = try #require(editor.display.crop)
        #expect(crop == CGRect(x: 70, y: 20, width: 200, height: 200))
        editor.undo()
        #expect(editor.display.crop == CGRect(x: 20, y: 20, width: 300, height: 200))
    }

    @Test func pickingARatioWithNothingCroppedTrimsTheWholeCapture() throws {
        var editor = session(.free)
        editor.restyle { $0.cropRatio = .sixteenNine }
        #expect(editor.display.crop == CGRect(x: 0, y: 38, width: 400, height: 225))
        editor.undo()
        #expect(editor.display.crop == nil)
        // A capture that already has the ratio stays uncropped.
        editor.restyle { $0.cropRatio = .fourThree }
        #expect(editor.display.crop == nil)
    }

    @Test func theRatioIsRememberedAndAStyleFromBeforeIsFree() throws {
        let style = Style(colorHex: Palette.red, cropRatio: .sixteenNine)
        #expect(try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(style)) == style)
        let old = Data(##"{"colorHex":"#FF3B30","size":"medium"}"##.utf8)
        #expect(try JSONDecoder().decode(Style.self, from: old).cropRatio == .free)
        #expect(Tool.crop.hasRatio && !Tool.rectangle.hasRatio)
    }
}
