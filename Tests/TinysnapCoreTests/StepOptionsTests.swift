import CoreGraphics
import Foundation
import Testing
@testable import TinysnapCore

/// Steps count from any start, in numbers or letters: a second capture of the same steps can
/// carry on from 4, and lettered callouts count apart from numbered steps.
struct StepOptionsTests {
    private func document(_ letters: [Bool], start: Int = 1) -> Document {
        let steps = letters.enumerated().map { index, letter in
            Fixture.annotation(.step(center: CGPoint(x: 20 + index * 30, y: 20)), style: Style(colorHex: Palette.red, letters: letter))
        }
        return Document(capture: Fixture.capture(width: 200, height: 60), annotations: steps, stepStart: start)
    }

    private func labels(_ document: Document) -> [String?] {
        document.annotations.map { document.stepLabel(of: $0.id) }
    }

    @Test func stepsCountFromTheChosenStart() {
        #expect(labels(document([false, false, false], start: 4)) == ["4", "5", "6"])
    }

    @Test func letterStepsCountApartFromNumberSteps() {
        #expect(labels(document([false, true, false, true])) == ["1", "A", "2", "B"])
        #expect(labels(document([true, true], start: 3)) == ["C", "D"])
    }

    @Test func lettersRunOnPastZ() {
        #expect(labels(document([true, true, true], start: 25)) == ["Y", "Z", "AA"])
    }

    /// A wide label, AAA, at the size of 1 ran past the disc: white letters spilt onto the capture.
    @Test func aLongLabelStaysInsideItsDisc() throws {
        let step = Fixture.annotation(.step(center: CGPoint(x: 50, y: 50)), style: Style(colorHex: Palette.red, size: .extraSmall, letters: true))
        let document = Document(capture: Fixture.capture(width: 100, height: 100, fill: Fixture.blue), annotations: [step],
                                stepStart: 703)
        let image = try #require(Renderer.render(document))
        let radius = step.bounds(scale: 1).width / 2
        let outside = (0..<100).flatMap { x in (0..<100).map { (x, $0) } }.filter { x, y in
            hypot(CGFloat(x) + 0.5 - 50, CGFloat(y) + 0.5 - 50) > radius + 1
        }
        #expect(outside.allSatisfy { Fixture.pixel(image, $0.0, $0.1).r < 60 })
    }

    @Test func theLayersPanelNamesAStepByItsLabel() {
        let lettered = document([true])
        #expect(lettered.layerName(of: lettered.annotations[0].id) == "Step A")
    }

    @Test func theStartIsSavedWithTheCaptureAndAFileWithoutOneStartsAtOne() throws {
        let captured = Date(timeIntervalSince1970: 1_700_000_000)
        let (json, _) = try DocumentArchive.encode(document([false], start: 7), captured: captured)
        #expect(try DocumentArchive.decode(json) { _ in nil }.stepStart == 7)
        let (plain, _) = try DocumentArchive.encode(document([false]), captured: captured)
        #expect(!String(decoding: plain, as: UTF8.self).contains("stepStart"))
        #expect(try DocumentArchive.decode(plain) { _ in nil }.stepStart == 1)
    }

    @Test func settingTheStartIsOneUndoableStepWithinLimits() {
        var session = EditorSession(document: document([false]))
        session.setStepStart(5)
        #expect(session.display.stepStart == 5)
        session.undo()
        #expect(session.display.stepStart == 1)
        session.setStepStart(0)
        #expect(session.display.stepStart == 1)
        session.setStepStart(5000)
        #expect(session.display.stepStart == Document.stepStartLimits.upperBound)
    }

    @Test func lettersAreSavedAndAStyleFromBeforeCountsInNumbers() throws {
        let style = Style(colorHex: Palette.red, letters: true)
        #expect(try JSONDecoder().decode(Style.self, from: JSONEncoder().encode(style)) == style)
        let old = Data(##"{"colorHex":"#FF3B30","size":"medium"}"##.utf8)
        #expect(try JSONDecoder().decode(Style.self, from: old).letters == false)
        #expect(Tool.step.hasCounter && !Tool.arrow.hasCounter)
    }
}
