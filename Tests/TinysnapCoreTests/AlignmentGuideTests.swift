import CoreGraphics
import Testing
@testable import TinysnapCore

/// A shape dragged near another's edge or middle, or the output's, snaps to it and a guide
/// shows the line they share, so shapes line up without nudging pixel by pixel.
struct AlignmentGuideTests {
    /// A 400 by 300 capture with a box whose left edge is at x 100, and a second box to drag.
    private func session() -> (EditorSession, Annotation.ID) {
        // Filled, so a click anywhere on one picks it up.
        let solid = Style(colorHex: Palette.red, filled: true)
        let anchor = Fixture.annotation(.rectangle(CGRect(x: 100, y: 40, width: 60, height: 40)), style: solid)
        let moving = Fixture.annotation(.rectangle(CGRect(x: 200, y: 200, width: 36, height: 30)), style: solid)
        let document = Document(capture: Fixture.capture(width: 400, height: 300), annotations: [anchor, moving])
        return (EditorSession(document: document, tool: .select), moving.id)
    }

    private func rect(_ session: EditorSession, _ id: Annotation.ID) -> CGRect {
        session.display.annotation(id)!.bounds(scale: session.scale)
    }

    @Test func aLeftEdgeNearAnotherSnapsToItAndShowsAGuide() {
        var (editor, id) = session()
        // Grabbed at its middle and carried left until its left edge is at 103, 3 short of 100.
        editor.pointerDown(at: CGPoint(x: 218, y: 215), reach: 4)
        editor.pointerDragged(to: CGPoint(x: 121, y: 215))
        #expect(rect(editor, id).minX == 100)
        #expect(editor.guides.contains { $0.axis == .vertical && $0.position == 100 })
        editor.pointerUp()
        #expect(editor.guides.isEmpty)
    }

    @Test func farFromAnythingNothingSnaps() {
        var (editor, id) = session()
        editor.pointerDown(at: CGPoint(x: 218, y: 215), reach: 4)
        editor.pointerDragged(to: CGPoint(x: 238, y: 237))
        #expect(rect(editor, id).origin == CGPoint(x: 220, y: 222))
        #expect(editor.guides.isEmpty)
    }

    @Test func aMiddleSnapsToTheOutputsMiddle() {
        var (editor, id) = session()
        // Its middle carried to x 198, 2 short of the capture's middle at 200.
        editor.pointerDown(at: CGPoint(x: 218, y: 215), reach: 4)
        editor.pointerDragged(to: CGPoint(x: 198, y: 255))
        #expect(rect(editor, id).midX == 200)
    }

    @Test func holdingCommandDragsFreely() {
        var (editor, id) = session()
        editor.pointerDown(at: CGPoint(x: 218, y: 215), reach: 4)
        editor.pointerDragged(to: CGPoint(x: 121, y: 215), modifiers: .command)
        #expect(rect(editor, id).minX == 103)
        #expect(editor.guides.isEmpty)
    }

    /// The snap lets go once the pointer has carried the shape past it, rather than holding on.
    @Test func carryingOnPastASnapLetsGo() {
        var (editor, id) = session()
        editor.pointerDown(at: CGPoint(x: 218, y: 215), reach: 4)
        editor.pointerDragged(to: CGPoint(x: 121, y: 215))
        // On to a left edge at 82, its middle now on the other box's left edge, which takes nothing back.
        editor.pointerDragged(to: CGPoint(x: 100, y: 215))
        #expect(rect(editor, id).minX == 82)
    }
}
