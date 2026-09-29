namespace Tinysnap.Core.Tests;

public class EditHistoryTests
{
    private static readonly Capture SharedCapture = Fixture.Capture(10, 10);

    private static Document Document(params Annotation[] annotations) => new(SharedCapture, annotations: [.. annotations]);

    [Fact]
    public void UndoAndRedoStepThroughCommits()
    {
        var first = Fixture.Annotation(new AnnotationKind.Step(Point.Zero));
        var history = new EditHistory(Document());
        history.Commit(Document(first));
        Assert.Single(history.Document.Annotations);
        history.Undo();
        Assert.Empty(history.Document.Annotations);
        history.Redo();
        Assert.Single(history.Document.Annotations);
    }

    [Fact]
    public void AnUnchangedDocumentRecordsNothing()
    {
        var history = new EditHistory(Document());
        history.Commit(Document());
        Assert.False(history.CanUndo);
        Assert.False(history.IsUnsaved);
    }

    [Fact]
    public void SavingClearsUnsavedAndUndoingPastTheSaveSetsItAgain()
    {
        var history = new EditHistory(Document());
        Assert.False(history.IsUnsaved);
        history.Commit(Document(Fixture.Annotation(new AnnotationKind.Step(Point.Zero))));
        Assert.True(history.IsUnsaved);
        history.MarkSaved();
        Assert.False(history.IsUnsaved);
        history.Undo();
        Assert.True(history.IsUnsaved);
        history.Redo();
        Assert.False(history.IsUnsaved);
    }

    [Fact]
    public void CommitsSharingAMergeKeyUndoAsOneStep()
    {
        var history = new EditHistory(Document());
        for (var x = 1; x <= 3; x++)
            history.Commit(Document(Fixture.Annotation(new AnnotationKind.Step(new Point(x, 0)))), mergeKey: "colour");
        history.Undo();
        Assert.Empty(history.Document.Annotations);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ADifferentKeyOrAPlainCommitStartsANewStep()
    {
        var history = new EditHistory(Document());
        var documents = Enumerable.Range(1, 4)
            .Select(x => Document(Fixture.Annotation(new AnnotationKind.Step(new Point(x, 0))))).ToArray();
        history.Commit(documents[0], mergeKey: "a");
        history.Commit(documents[1], mergeKey: "b");
        history.Commit(documents[2]);
        history.Commit(documents[3], mergeKey: "b");
        var steps = 0;
        while (history.CanUndo)
        {
            history.Undo();
            steps++;
        }
        Assert.Equal(4, steps);
    }

    [Fact]
    public void SavingEndsAMergeSoUndoStopsAtTheSave()
    {
        var history = new EditHistory(Document());
        var saved = Document(Fixture.Annotation(new AnnotationKind.Step(Point.Zero)));
        history.Commit(saved, mergeKey: "colour");
        history.MarkSaved();
        history.Commit(Document(Fixture.Annotation(new AnnotationKind.Step(new Point(9, 9)))), mergeKey: "colour");
        history.Undo();
        Assert.Equal(saved, history.Document);
        Assert.False(history.IsUnsaved);
    }

    [Fact]
    public void ANewEditClearsRedo()
    {
        var history = new EditHistory(Document());
        history.Commit(Document(Fixture.Annotation(new AnnotationKind.Step(Point.Zero))));
        history.Undo();
        history.Commit(Document(Fixture.Annotation(new AnnotationKind.Step(new Point(5, 5)))));
        Assert.False(history.CanRedo);
    }
}
