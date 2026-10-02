import CoreGraphics
import Testing
@testable import TinysnapCore

struct LayerTests {
    private func document(_ annotations: [Annotation], width: Int = 400, height: Int = 300) -> Document {
        Document(capture: Fixture.capture(width: width, height: height, scale: 2), annotations: annotations)
    }

    @Test func aHiddenShapeIsNotDrawnClickedOrBordered() {
        var box = Fixture.annotation(.rectangle(CGRect(x: 100, y: 100, width: 80, height: 60)),
                                     style: Style(colorHex: Palette.red, filled: true))
        box.isHidden = true
        let doc = document([box])
        #expect(doc.topmost(at: CGPoint(x: 140, y: 130)) == nil)
        #expect(doc.pickUp(at: CGPoint(x: 140, y: 130), reach: 8) == nil)
        #expect(doc.borderHit(at: CGPoint(x: 96, y: 130), reach: 8) == nil)
        let blank = Renderer.render(document([]))!
        let hidden = Renderer.render(doc)!
        #expect(Fixture.pixel(hidden, 140, 130) == Fixture.pixel(blank, 140, 130))
    }

    @Test func aLockedShapeIsSelectableButNotPickedUpByADrawingTool() {
        var blur = Fixture.annotation(.blur(CGRect(x: 100, y: 100, width: 80, height: 60)))
        blur.isLocked = true
        let doc = document([blur])
        #expect(doc.topmost(at: CGPoint(x: 140, y: 130)) == blur.id)
        #expect(doc.pickUp(at: CGPoint(x: 140, y: 130), reach: 8) == nil)
        #expect(doc.pickUp(at: CGPoint(x: 96, y: 130), reach: 8) == nil)
    }

    @Test func aHiddenShapePastTheEdgeDoesNotGrowTheCanvas() {
        var far = Fixture.annotation(.rectangle(CGRect(x: 700, y: 100, width: 80, height: 60)))
        far.isHidden = true
        #expect(document([far]).extent == CGRect(x: 0, y: 0, width: 400, height: 300))
    }

    @Test func layerNamesSayWhatEachShapeIs() {
        let text = Fixture.annotation(.text(origin: CGPoint(x: 10, y: 10), string: "Best week so far\nsecond line"))
        let first = Fixture.annotation(.step(center: CGPoint(x: 50, y: 50)))
        var hiddenStep = Fixture.annotation(.step(center: CGPoint(x: 80, y: 50)))
        hiddenStep.isHidden = true
        let second = Fixture.annotation(.step(center: CGPoint(x: 110, y: 50)))
        let blur = Fixture.annotation(.blur(CGRect(x: 0, y: 0, width: 20, height: 20)))
        let arrow = Fixture.annotation(.arrow(from: CGPoint(x: 0, y: 0), to: CGPoint(x: 20, y: 20)))
        let doc = document([text, first, hiddenStep, second, blur, arrow])
        #expect(doc.layerName(of: text.id) == "Best week so far")
        #expect(doc.layerName(of: first.id) == "Step 1")
        #expect(doc.layerName(of: second.id) == "Step 2")
        #expect(doc.layerName(of: hiddenStep.id) == "Step")
        #expect(doc.layerName(of: blur.id) == "Blur")
        #expect(doc.layerName(of: arrow.id) == "Arrow")
    }
}
