import CoreGraphics
import Testing
@testable import TinysnapCore

struct AnnotationTests {
    @Test func aThinLineCanStillBeGrabbed() {
        // Small is 2 points, 4 pixels at 2x, but the reach is at least 4 points.
        let line = Fixture.annotation(.line(from: .zero, to: CGPoint(x: 100, y: 0)),
                                      style: Style(colorHex: Palette.red, size: .small))
        #expect(line.contains(CGPoint(x: 50, y: 7), scale: 2))
        #expect(!line.contains(CGPoint(x: 50, y: 9), scale: 2))
    }

    @Test func anOutlinedBoxIsHitOnItsEdgeNotItsMiddle() {
        let box = Fixture.annotation(.rectangle(CGRect(x: 0, y: 0, width: 100, height: 100)))
        #expect(box.contains(CGPoint(x: 1, y: 50), scale: 2))
        #expect(!box.contains(CGPoint(x: 50, y: 50), scale: 2))
    }

    @Test func aFilledBoxIsHitAnywhereInside() {
        let box = Fixture.annotation(.rectangle(CGRect(x: 0, y: 0, width: 100, height: 100)),
                                     style: Style(colorHex: Palette.red, filled: true))
        #expect(box.contains(CGPoint(x: 50, y: 50), scale: 2))
    }

    @Test func anOvalOutlineIsHitOnTheCurveNotTheCorner() {
        let oval = Fixture.annotation(.oval(CGRect(x: 0, y: 0, width: 100, height: 100)))
        #expect(oval.contains(CGPoint(x: 50, y: 1), scale: 2))
        #expect(!oval.contains(CGPoint(x: 3, y: 3), scale: 2))
    }

    @Test func movingShiftsEveryPoint() {
        let arrow = Fixture.annotation(.arrow(from: .zero, to: CGPoint(x: 10, y: 10)))
        #expect(arrow.moved(by: CGVector(dx: 5, dy: -5)).kind == .arrow(from: CGPoint(x: 5, y: -5), to: CGPoint(x: 15, y: 5)))
    }

    @Test func draggingALineEndWithShiftSnaps() {
        let line = Fixture.annotation(.line(from: .zero, to: CGPoint(x: 10, y: 0)))
        let moved = line.resized(dragging: .end, to: CGPoint(x: 100, y: 4), constrained: true)
        #expect(moved.kind == .line(from: .zero, to: CGPoint(x: 100, y: 0)))
    }

    @Test func draggingAMagnifierCornerChangesItsRadius() {
        let lens = Fixture.annotation(.magnifier(center: CGPoint(x: 100, y: 100), radius: 80, zoom: 2))
        let resized = lens.resized(dragging: .topLeft, to: CGPoint(x: 40, y: 70), constrained: false)
        #expect(resized.kind == .magnifier(center: CGPoint(x: 100, y: 100), radius: 60, zoom: 2))
    }

    @Test func textBoundsGrowWithEachLine() {
        let one = Fixture.annotation(.text(origin: .zero, string: "Hi")).bounds(scale: 2)
        let two = Fixture.annotation(.text(origin: .zero, string: "Hi\nthere")).bounds(scale: 2)
        #expect(two.height > one.height * 1.9)
        #expect(two.width > one.width)
    }

    @Test func aClickWithoutADragIsTooSmallToKeep() {
        #expect(Fixture.annotation(.arrow(from: .zero, to: CGPoint(x: 1, y: 1))).isDegenerate(scale: 2))
        #expect(Fixture.annotation(.text(origin: .zero, string: " \n ")).isDegenerate(scale: 2))
        #expect(!Fixture.annotation(.step(center: .zero)).isDegenerate(scale: 2))
    }

    @Test func textStepsAndStrokesHaveNoResizeHandles() {
        #expect(Fixture.annotation(.step(center: .zero)).handles(scale: 2).isEmpty)
        #expect(Fixture.annotation(.line(from: .zero, to: CGPoint(x: 9, y: 9))).handles(scale: 2).count == 2)
        #expect(Fixture.annotation(.blur(CGRect(x: 0, y: 0, width: 9, height: 9))).handles(scale: 2).count == 8)
    }
}
