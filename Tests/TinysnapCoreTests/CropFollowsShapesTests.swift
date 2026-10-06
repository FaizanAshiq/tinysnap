import CoreGraphics
import Testing
@testable import TinysnapCore

/// A shape drawn, moved or resized past the crop takes the crop out with it, the way one drawn past
/// the capture's edge grows the canvas, so nothing drawn is cut from what is copied or saved.
struct CropFollowsShapesTests {
    private let crop = CGRect(x: 100, y: 100, width: 200, height: 150)

    private func cropped(_ tool: Tool = .rectangle, ratio: CropRatio = .free) -> EditorSession {
        var editor = EditorSession(document: Document(capture: Fixture.capture(width: 400, height: 300)), tool: .crop)
        editor.restyle { $0.cropRatio = ratio }
        drag(&editor, from: CGPoint(x: 100, y: 100), to: CGPoint(x: 300, y: 250))
        editor.choose(tool)
        return editor
    }

    private func drag(_ session: inout EditorSession, from start: CGPoint, to end: CGPoint) {
        session.pointerDown(at: start, reach: 4)
        session.pointerDragged(to: end)
        session.pointerUp()
    }

    @Test func aShapeDrawnPastTheCropTakesItOut() {
        var editor = cropped()
        #expect(editor.display.crop == crop)
        drag(&editor, from: CGPoint(x: 150, y: 150), to: CGPoint(x: 340, y: 200))
        // Out to the box's right side and the margin a shape past the capture's edge gets.
        #expect(editor.display.crop == CGRect(x: 100, y: 100, width: 340 + 16 - 100, height: 150))
    }

    /// As reported: an arrow from outside the crop, pointing in, had its tail cut off.
    @Test func anArrowFromOutsideTheCropPointingInTakesItOut() throws {
        var editor = cropped(.arrow)
        drag(&editor, from: CGPoint(x: 60, y: 60), to: CGPoint(x: 200, y: 180))
        let grown = try #require(editor.display.crop)
        #expect(grown.contains(editor.display.annotations[0].bounds(scale: 1)))
        #expect(grown.maxX == crop.maxX && grown.maxY == crop.maxY)
    }

    /// Measured from the last step: dragged out and back, the shape leaves the crop as it was.
    @Test func aShapeDraggedOutAndBackLeavesTheCrop() throws {
        var editor = cropped()
        drag(&editor, from: CGPoint(x: 150, y: 150), to: CGPoint(x: 200, y: 200))
        // Picked up by its left side: an outlined box has nothing to hold in its middle.
        editor.choose(.select)
        editor.pointerDown(at: CGPoint(x: 150, y: 175), reach: 4)
        editor.pointerDragged(to: CGPoint(x: 290, y: 175))
        #expect(try #require(editor.display.crop).maxX > crop.maxX)
        editor.pointerDragged(to: CGPoint(x: 155, y: 175))
        editor.pointerUp()
        #expect(editor.display.crop == crop)
    }

    @Test func undoPutsTheCropBackWithTheShape() {
        var editor = cropped()
        drag(&editor, from: CGPoint(x: 150, y: 150), to: CGPoint(x: 340, y: 200))
        editor.undo()
        #expect(editor.display.annotations.isEmpty)
        #expect(editor.display.crop == crop)
    }

    /// Wholly outside, a shape is somewhere the crop leaves out, as one cropped away is.
    @Test func aShapeWhollyOutsideTheCropLeavesIt() {
        var editor = cropped()
        drag(&editor, from: CGPoint(x: 10, y: 10), to: CGPoint(x: 60, y: 60))
        #expect(editor.display.crop == crop)
    }

    /// They hide what is under them rather than add anything, and Redact's boxes reach a little
    /// past the text they cover.
    @Test func blurPixelateEraseAndRedactLeaveTheCrop() {
        for tool in [Tool.blur, .pixelate, .erase] {
            var editor = cropped(tool)
            drag(&editor, from: CGPoint(x: 250, y: 150), to: CGPoint(x: 350, y: 200))
            #expect(editor.display.crop == crop, "\(tool)")
        }
        var editor = cropped()
        editor.redact([CGRect(x: 280, y: 150, width: 40, height: 10)])
        #expect(editor.display.crop == crop)
    }

    @Test func aCropWithARatioKeepsItAsItGrows() throws {
        var editor = cropped(ratio: .square)
        let square = try #require(editor.display.crop)
        #expect(square.width == square.height)
        drag(&editor, from: CGPoint(x: 150, y: 150), to: CGPoint(x: 330, y: 200))
        let grown = try #require(editor.display.crop)
        #expect(grown.width == grown.height)
        #expect(grown.width > square.width)
        #expect(grown.contains(editor.display.annotations[0].bounds(scale: 1)))
    }
}
