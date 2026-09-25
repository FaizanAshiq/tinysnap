import CoreGraphics
import Testing
@testable import TinysnapCore

struct GeometryTests {
    @Test func snapsToTheNearestAxisExactly() {
        let origin = CGPoint(x: 10, y: 10)
        #expect(CGPoint(x: 110, y: 13).snapped45(from: origin) == CGPoint(x: 110, y: 10))
        #expect(CGPoint(x: 12, y: -90).snapped45(from: origin) == CGPoint(x: 10, y: -90))
    }

    @Test func snapsToADiagonalKeepingItsDirection() {
        #expect(CGPoint(x: -40, y: 60).snapped45(from: .zero) == CGPoint(x: -50, y: 50))
    }

    @Test func squaresABoxFromItsAnchor() {
        #expect(CGPoint(x: 30, y: -10).squared(from: .zero) == CGPoint(x: 30, y: -30))
    }

    @Test func measuresDistanceToASegment() {
        let segmentEnd = CGPoint(x: 10, y: 0)
        #expect(CGPoint(x: 5, y: 3).distance(toSegmentFrom: .zero, to: segmentEnd) == 3)
        #expect(CGPoint(x: 13, y: 4).distance(toSegmentFrom: .zero, to: segmentEnd) == 5)
    }

    @Test func buildsARectFromCornersInEitherOrder() {
        #expect(CGRect(corner: CGPoint(x: 50, y: 40), corner: CGPoint(x: 10, y: 20)) == CGRect(x: 10, y: 20, width: 40, height: 20))
    }

    @Test func draggingACornerPastTheOppositeOneFlipsTheBox() {
        let box = CGRect(x: 10, y: 10, width: 20, height: 20)
        #expect(box.resized(dragging: .topLeft, to: CGPoint(x: 40, y: 50), square: false) == CGRect(x: 30, y: 30, width: 10, height: 20))
    }

    @Test func draggingAnEdgeMovesOnlyThatEdge() {
        let box = CGRect(x: 10, y: 10, width: 20, height: 20)
        #expect(box.resized(dragging: .right, to: CGPoint(x: 50, y: 99), square: false) == CGRect(x: 10, y: 10, width: 40, height: 20))
    }

    @Test func aSquareCornerDragKeepsTheOppositeCorner() {
        let box = CGRect(x: 0, y: 0, width: 10, height: 10)
        #expect(box.resized(dragging: .bottomRight, to: CGPoint(x: 30, y: 12), square: true) == CGRect(x: 0, y: 0, width: 30, height: 30))
    }

    @Test func aDragDrawsABoxFromItsCorner() {
        let box = CGRect.dragged(from: CGPoint(x: 100, y: 100), to: CGPoint(x: 130, y: 120), square: false, fromCentre: false)
        #expect(box == CGRect(x: 100, y: 100, width: 30, height: 20))
    }

    @Test func optionDrawsTheBoxOutFromItsCentre() {
        let box = CGRect.dragged(from: CGPoint(x: 100, y: 100), to: CGPoint(x: 130, y: 120), square: false, fromCentre: true)
        #expect(box == CGRect(x: 70, y: 80, width: 60, height: 40))
    }

    @Test func shiftAndOptionDrawASquareFromItsCentre() {
        let box = CGRect.dragged(from: CGPoint(x: 100, y: 100), to: CGPoint(x: 130, y: 110), square: true, fromCentre: true)
        #expect(box == CGRect(x: 70, y: 70, width: 60, height: 60))
    }

    @Test func aMovedBoxStopsAtTheEdgeOfItsBounds() {
        let box = CGRect(x: 10, y: 10, width: 20, height: 20)
        let bounds = CGRect(x: 0, y: 0, width: 100, height: 100)
        #expect(box.allowedMove(by: CGVector(dx: -30, dy: 5), within: bounds) == CGVector(dx: -10, dy: 5))
        #expect(box.allowedMove(by: CGVector(dx: 90, dy: 90), within: bounds) == CGVector(dx: 70, dy: 70))
    }

    @Test func roundsTheCropToWholePixels() {
        #expect(CGRect(x: 1.4, y: 2.6, width: 10.2, height: 5).wholePixels == CGRect(x: 1, y: 3, width: 11, height: 5))
    }
}
