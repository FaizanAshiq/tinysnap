namespace Tinysnap.Core;

/// <summary>Undo and redo as a stack of whole documents. The capture pixels are shared by
/// reference, so a snapshot costs only its annotation list.</summary>
public sealed class EditHistory(Document document)
{
    private readonly Stack<(Document Document, int Revision)> undoStack = new();
    private readonly Stack<(Document Document, int Revision)> redoStack = new();

    /// <summary>Every state gets its own number, so undoing back to the saved state reads as saved.</summary>
    private int revision;
    private int nextRevision = 1;
    private int savedRevision;

    /// <summary>The key of the last commit, while the next one may still fold into it.</summary>
    private string? lastMergeKey;

    public Document Document { get; private set; } = document;

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    /// <summary>Edits made since the last copy, save or drag out. A fresh capture has none.</summary>
    public bool IsUnsaved => revision != savedRevision;

    /// <summary>Makes <paramref name="next"/> the current document as one undoable step. A
    /// document that did not change records nothing, so a click that moved nothing leaves no
    /// empty undo step.
    ///
    /// Commits in a row with the same <paramref name="mergeKey"/> fold into one step. The
    /// colour panel and the scroll wheel send a stream of small changes, and one drag of the
    /// colour wheel should not take dozens of undos to take back.</summary>
    public void Commit(Document next, string? mergeKey = null)
    {
        if (next == Document) return;
        if (mergeKey is null || mergeKey != lastMergeKey) undoStack.Push((Document, revision));
        redoStack.Clear();
        Document = next;
        revision = nextRevision;
        nextRevision++;
        lastMergeKey = mergeKey;
    }

    public void Undo()
    {
        if (!undoStack.TryPop(out var previous)) return;
        redoStack.Push((Document, revision));
        (Document, revision) = previous;
        lastMergeKey = null;
    }

    public void Redo()
    {
        if (!redoStack.TryPop(out var next)) return;
        undoStack.Push((Document, revision));
        (Document, revision) = next;
        lastMergeKey = null;
    }

    /// <summary>Also ends any merge, so undo after a save stops at the saved state.</summary>
    public void MarkSaved()
    {
        savedRevision = revision;
        lastMergeKey = null;
    }
}
