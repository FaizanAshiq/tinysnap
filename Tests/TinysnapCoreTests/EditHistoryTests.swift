import CoreGraphics
import Testing
@testable import TinysnapCore

struct EditHistoryTests {
    private func document(with annotations: [Annotation] = []) -> Document {
        Document(capture: Self.capture, annotations: annotations)
    }

    private static let capture = Fixture.capture(width: 10, height: 10)

    @Test func undoAndRedoStepThroughCommits() {
        let first = Fixture.annotation(.step(center: .zero))
        var history = EditHistory(document: document())
        history.commit(document(with: [first]))
        #expect(history.document.annotations.count == 1)
        history.undo()
        #expect(history.document.annotations.isEmpty)
        history.redo()
        #expect(history.document.annotations.count == 1)
    }

    @Test func anUnchangedDocumentRecordsNothing() {
        var history = EditHistory(document: document())
        history.commit(document())
        #expect(!history.canUndo)
        #expect(!history.isUnsaved)
    }

    @Test func savingClearsUnsavedAndUndoingPastTheSaveSetsItAgain() {
        var history = EditHistory(document: document())
        #expect(!history.isUnsaved)
        history.commit(document(with: [Fixture.annotation(.step(center: .zero))]))
        #expect(history.isUnsaved)
        history.markSaved()
        #expect(!history.isUnsaved)
        history.undo()
        #expect(history.isUnsaved)
        history.redo()
        #expect(!history.isUnsaved)
    }

    @Test func commitsSharingAMergeKeyUndoAsOneStep() {
        var history = EditHistory(document: document())
        for x in 1...3 {
            history.commit(document(with: [Fixture.annotation(.step(center: CGPoint(x: x, y: 0)))]), mergeKey: "colour")
        }
        history.undo()
        #expect(history.document.annotations.isEmpty)
        #expect(!history.canUndo)
    }

    @Test func aDifferentKeyOrAPlainCommitStartsANewStep() {
        var history = EditHistory(document: document())
        let documents = (1...4).map { document(with: [Fixture.annotation(.step(center: CGPoint(x: $0, y: 0)))]) }
        history.commit(documents[0], mergeKey: "a")
        history.commit(documents[1], mergeKey: "b")
        history.commit(documents[2])
        history.commit(documents[3], mergeKey: "b")
        var steps = 0
        while history.canUndo {
            history.undo()
            steps += 1
        }
        #expect(steps == 4)
    }

    @Test func savingEndsAMergeSoUndoStopsAtTheSave() {
        var history = EditHistory(document: document())
        let saved = document(with: [Fixture.annotation(.step(center: .zero))])
        history.commit(saved, mergeKey: "colour")
        history.markSaved()
        history.commit(document(with: [Fixture.annotation(.step(center: CGPoint(x: 9, y: 9)))]), mergeKey: "colour")
        history.undo()
        #expect(history.document == saved)
        #expect(!history.isUnsaved)
    }

    @Test func aNewEditClearsRedo() {
        var history = EditHistory(document: document())
        history.commit(document(with: [Fixture.annotation(.step(center: .zero))]))
        history.undo()
        history.commit(document(with: [Fixture.annotation(.step(center: CGPoint(x: 5, y: 5)))]))
        #expect(!history.canRedo)
    }
}
