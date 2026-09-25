/// Undo and redo as a stack of whole documents. The capture pixels are shared by
/// reference, so a snapshot costs only its annotation list.
public struct EditHistory {
    public private(set) var document: Document
    private var undoStack: [(document: Document, revision: Int)] = []
    private var redoStack: [(document: Document, revision: Int)] = []
    /// Every state gets its own number, so undoing back to the saved state reads as saved.
    private var revision = 0
    private var nextRevision = 1
    private var savedRevision = 0
    /// The key of the last commit, while the next one may still fold into it.
    private var lastMergeKey: String?

    public init(document: Document) {
        self.document = document
    }

    public var canUndo: Bool { !undoStack.isEmpty }
    public var canRedo: Bool { !redoStack.isEmpty }

    /// Edits made since the last copy, save or drag out. A fresh capture has none.
    public var isUnsaved: Bool { revision != savedRevision }

    /// Makes `new` the current document as one undoable step. A document that did not
    /// change records nothing, so a click that moved nothing leaves no empty undo step.
    ///
    /// Commits in a row with the same `mergeKey` fold into one step. The colour panel
    /// and the scroll wheel send a stream of small changes, and one drag of the colour
    /// wheel should not take dozens of undos to take back.
    public mutating func commit(_ new: Document, mergeKey: String? = nil) {
        guard new != document else { return }
        if mergeKey == nil || mergeKey != lastMergeKey {
            undoStack.append((document, revision))
        }
        redoStack.removeAll()
        document = new
        revision = nextRevision
        nextRevision += 1
        lastMergeKey = mergeKey
    }

    public mutating func undo() {
        guard let previous = undoStack.popLast() else { return }
        redoStack.append((document, revision))
        (document, revision) = previous
        lastMergeKey = nil
    }

    public mutating func redo() {
        guard let next = redoStack.popLast() else { return }
        undoStack.append((document, revision))
        (document, revision) = next
        lastMergeKey = nil
    }

    /// Also ends any merge, so undo after a save stops at the saved state.
    public mutating func markSaved() {
        savedRevision = revision
        lastMergeKey = nil
    }
}
