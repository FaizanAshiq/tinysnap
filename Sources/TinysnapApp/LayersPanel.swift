import AppKit
import TinysnapCore

/// A square button in the editor's right rail. On, it is filled with the accent colour, the
/// way a chosen chip is.
final class RailButton: NSButton {
    var isOn = false {
        didSet { if isOn != oldValue { restyle() } }
    }
    /// A switch reads as on or off to VoiceOver; a plain button, like Library, does not.
    private let isSwitch: Bool

    /// `tooltip` adds the shortcut to what VoiceOver reads as `label`.
    init(symbol: String, label: String, tooltip: String? = nil, action: Selector, target: AnyObject?, isSwitch: Bool = false) {
        self.isSwitch = isSwitch
        super.init(frame: NSRect(x: 0, y: 0, width: 32, height: 32))
        image = NSImage(systemSymbolName: symbol, accessibilityDescription: label)
        imagePosition = .imageOnly
        imageScaling = .scaleProportionallyDown
        isBordered = false
        toolTip = tooltip ?? label
        setAccessibilityLabel(label)
        self.action = action
        self.target = target
        wantsLayer = true
        layer?.cornerRadius = 8
        restyle()
    }

    required init?(coder: NSCoder) {
        fatalError("RailButton is created in code only")
    }

    private func restyle() {
        layer?.backgroundColor = isOn ? NSColor.controlAccentColor.cgColor : NSColor.clear.cgColor
        contentTintColor = isOn ? .white : .secondaryLabelColor
        if isSwitch { setAccessibilityValue(isOn ? "on" : "off") }
    }
}

/// The capture's shapes, top first, each with its eye, lock and duplicate. Floats over the
/// canvas under the style bar, in the same material, and is a native table, so the arrows,
/// VoiceOver and dragging a row all work as they do anywhere on the Mac.
final class LayersPanel: NSVisualEffectView, NSTableViewDataSource, NSTableViewDelegate {
    /// Room for a name beside four row buttons: duplicate, delete, eye and lock.
    static let width: CGFloat = 244
    static let rowHeight: CGFloat = 32
    private static let headerHeight: CGFloat = 36
    private static let rowType = NSPasteboard.PasteboardType("com.faizanashiq.tinysnap.layer")

    struct Row: Equatable {
        let id: Annotation.ID
        let tool: Tool
        let name: String
        let isLocked: Bool
        let isHidden: Bool
    }

    var onSelect: ((Annotation.ID?) -> Void)?
    /// A shape dragged to `index` in the document's list, bottom first.
    var onMove: ((Annotation.ID, Int) -> Void)?
    var onHide: ((Annotation.ID, Bool) -> Void)?
    var onLock: ((Annotation.ID, Bool) -> Void)?
    var onDuplicate: ((Annotation.ID) -> Void)?
    /// A row's bin: its shape deleted.
    var onDeleteRow: ((Annotation.ID) -> Void)?
    /// The row under the pointer, so the canvas can border its shape.
    var onHover: ((Annotation.ID?) -> Void)?
    /// A click in the list: the keys go back to the canvas.
    var onClicked: (() -> Void)?
    var onClose: (() -> Void)?
    /// Delete, Undo and Redo while the list has the keys: the canvas does them.
    var onDelete: (() -> Void)?
    var onUndo: (() -> Void)?
    var onRedo: (() -> Void)?

    private(set) var rows: [Row] = []
    private var selection: Annotation.ID?
    private let title = NSTextField(labelWithString: "Layers")
    private let count = NSTextField(labelWithString: "0")
    private let empty = NSTextField(labelWithString: "Shapes you draw show here")
    private let table = LayersTable()
    private let scroll = NSScrollView()
    /// Set while the table is told the selection, so that is not sent back as a choice.
    private var isSyncing = false

    init() {
        super.init(frame: NSRect(x: 0, y: 0, width: Self.width, height: 120))
        material = .popover
        blendingMode = .withinWindow
        state = .active
        wantsLayer = true
        layer?.cornerRadius = 10
        layer?.borderWidth = 0.5
        layer?.borderColor = NSColor.separatorColor.cgColor
        autoresizingMask = [.minXMargin, .minYMargin]

        title.font = .systemFont(ofSize: 13, weight: .semibold)
        count.font = .monospacedDigitSystemFont(ofSize: 12, weight: .regular)
        count.textColor = .secondaryLabelColor
        count.alignment = .right
        empty.font = .systemFont(ofSize: 13)
        empty.textColor = .secondaryLabelColor

        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("layer"))
        table.addTableColumn(column)
        table.headerView = nil
        table.rowHeight = Self.rowHeight
        table.intercellSpacing = .zero
        table.backgroundColor = .clear
        table.style = .plain
        table.selectionHighlightStyle = .regular
        table.dataSource = self
        table.delegate = self
        table.registerForDraggedTypes([Self.rowType])
        // A line where the row will land. Opening a gap slid the rows out of a panel sized
        // to fit them, so the list scrolled under the pointer mid drag.
        table.draggingDestinationFeedbackStyle = .regular
        table.setAccessibilityLabel("Layers")
        table.onSpace = { [weak self] in self?.toggleHidden() }
        table.onEscape = { [weak self] in self?.onClose?() }
        table.onClicked = { [weak self] in self?.onClicked?() }
        table.onDelete = { [weak self] in self?.onDelete?() }
        table.onUndo = { [weak self] in self?.onUndo?() }
        table.onRedo = { [weak self] in self?.onRedo?() }
        scroll.documentView = table
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true

        [title, count, empty, scroll].forEach(addSubview)
        layoutParts()
    }

    required init?(coder: NSCoder) {
        fatalError("LayersPanel is created in code only")
    }

    /// The height that shows every row, before the canvas limits it.
    var fittingHeight: CGFloat {
        Self.headerHeight + CGFloat(max(rows.count, 1)) * Self.rowHeight + 8
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        layoutParts()
    }

    private func layoutParts() {
        let height = bounds.height
        title.frame = NSRect(x: 14, y: height - 26, width: 120, height: 18)
        count.frame = NSRect(x: bounds.width - 54, y: height - 26, width: 40, height: 18)
        empty.frame = NSRect(x: 14, y: height - Self.headerHeight - 24, width: bounds.width - 28, height: 18)
        scroll.frame = NSRect(x: 6, y: 4, width: bounds.width - 12, height: max(0, height - Self.headerHeight - 4))
        table.tableColumns.first?.width = scroll.frame.width
    }

    /// Shows `document`'s shapes. The table reloads only when a row changed, since this runs
    /// on every change to the canvas, a drag's every step included.
    func show(_ document: Document, selection: Annotation.ID?) {
        let rows = document.annotations.reversed().map { annotation in
            Row(id: annotation.id, tool: annotation.tool, name: document.layerName(of: annotation.id),
                isLocked: annotation.isLocked, isHidden: annotation.isHidden)
        }
        if rows != self.rows {
            self.rows = rows
            count.stringValue = "\(rows.count)"
            empty.isHidden = !rows.isEmpty
            // A reload can trim the table's selection; that is not the person choosing nothing.
            isSyncing = true
            table.reloadData()
            isSyncing = false
        }
        guard selection != self.selection || table.selectedRow != (rows.firstIndex { $0.id == selection } ?? -1) else { return }
        self.selection = selection
        isSyncing = true
        if let row = rows.firstIndex(where: { $0.id == selection }) {
            table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
            table.scrollRowToVisible(row)
        } else {
            table.deselectAll(nil)
        }
        isSyncing = false
    }

    /// The keys to the list, for the arrows, Space and Esc: the panel opened from the keyboard.
    func focusList() {
        window?.makeFirstResponder(table)
    }

    private func toggleHidden() {
        guard table.selectedRow >= 0, table.selectedRow < rows.count else { return }
        let row = rows[table.selectedRow]
        onHide?(row.id, !row.isHidden)
    }

    // MARK: Table

    func numberOfRows(in tableView: NSTableView) -> Int { rows.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let cell = tableView.makeView(withIdentifier: LayerCell.identifier, owner: self) as? LayerCell ?? LayerCell()
        cell.show(rows[row])
        cell.onHide = { [weak self] id, hidden in self?.onHide?(id, hidden) }
        cell.onLock = { [weak self] id, locked in self?.onLock?(id, locked) }
        cell.onDuplicate = { [weak self] id in self?.onDuplicate?(id) }
        cell.onDelete = { [weak self] id in self?.onDeleteRow?(id) }
        cell.onHover = { [weak self] id in self?.onHover?(id) }
        cell.onClicked = { [weak self] in self?.onClicked?() }
        return cell
    }

    func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? { LayerRowView() }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard !isSyncing else { return }
        let row = table.selectedRow
        let id = row >= 0 && row < rows.count ? rows[row].id : nil
        selection = id
        onSelect?(id)
    }

    func tableView(_ tableView: NSTableView, pasteboardWriterForRow row: Int) -> NSPasteboardWriting? {
        let item = NSPasteboardItem()
        item.setString(rows[row].id.uuidString, forType: Self.rowType)
        return item
    }

    func tableView(_ tableView: NSTableView, validateDrop info: NSDraggingInfo, proposedRow row: Int,
                   proposedDropOperation dropOperation: NSTableView.DropOperation) -> NSDragOperation {
        guard info.draggingSource as? NSTableView === table else { return [] }
        // Over a row, its upper half lands the shape above it and its lower half below,
        // so the ends of the list are as easy to reach as the middle.
        if dropOperation == .on {
            let point = tableView.convert(info.draggingLocation, from: nil)
            let below = point.y > tableView.rect(ofRow: row).midY
            tableView.setDropRow(below ? row + 1 : row, dropOperation: .above)
        }
        return .move
    }

    /// Rows are top first and the drop lands above `row`; the document wants a place in its
    /// list, bottom first, once the dragged shape has left its old one.
    func tableView(_ tableView: NSTableView, acceptDrop info: NSDraggingInfo, row: Int,
                   dropOperation: NSTableView.DropOperation) -> Bool {
        guard let string = info.draggingPasteboard.string(forType: Self.rowType), let id = UUID(uuidString: string),
              let from = rows.firstIndex(where: { $0.id == id }) else { return false }
        let to = from < row ? row - 1 : row
        onMove?(id, rows.count - 1 - to)
        return true
    }
}

/// Space hides or shows the chosen row, Esc closes the panel, Delete deletes its shape,
/// and a click hands the keys back to the canvas. The arrows move between rows, as in any
/// list.
private final class LayersTable: NSTableView {
    var onSpace: (() -> Void)?
    var onEscape: (() -> Void)?
    var onClicked: (() -> Void)?
    var onDelete: (() -> Void)?
    var onUndo: (() -> Void)?
    var onRedo: (() -> Void)?

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 49: onSpace?()
        case 53: onEscape?()
        case 51, 117: onDelete?()
        default: super.keyDown(with: event)
        }
    }

    /// The window answers Undo itself before anything past it in the chain is asked, so the
    /// list passes it to the canvas.
    @objc func undo(_ sender: Any?) { onUndo?() }
    @objc func redo(_ sender: Any?) { onRedo?() }

    override func mouseDown(with event: NSEvent) {
        super.mouseDown(with: event)
        onClicked?()
    }
}

/// The chosen row is filled with the accent colour whether or not the list has the keys,
/// as the toolbar marks the chosen tool.
private final class LayerRowView: NSTableRowView {
    override var isEmphasized: Bool {
        get { true }
        set {}
    }
}

/// One shape: its tool's symbol and name, then duplicate and delete (on hover and when
/// chosen), the eye and the lock.
private final class LayerCell: NSTableCellView {
    static let identifier = NSUserInterfaceItemIdentifier("LayerCell")

    var onHide: ((Annotation.ID, Bool) -> Void)?
    var onLock: ((Annotation.ID, Bool) -> Void)?
    var onDuplicate: ((Annotation.ID) -> Void)?
    var onDelete: ((Annotation.ID) -> Void)?
    var onHover: ((Annotation.ID?) -> Void)?
    /// A button clicked: the keys go back to the canvas.
    var onClicked: (() -> Void)?

    private let icon = NSImageView()
    private let name = NSTextField(labelWithString: "")
    private let duplicate = NSButton()
    private let delete = NSButton()
    private let eye = NSButton()
    private let lock = NSButton()
    private var row: LayersPanel.Row?
    private var hovering = false
    private var tracking: NSTrackingArea?

    init() {
        super.init(frame: NSRect(x: 0, y: 0, width: LayersPanel.width - 12, height: LayersPanel.rowHeight))
        identifier = Self.identifier
        name.font = .systemFont(ofSize: 13)
        name.lineBreakMode = .byTruncatingTail
        textField = name
        imageView = icon
        icon.symbolConfiguration = .init(pointSize: 13, weight: .regular)
        for (button, action) in [(duplicate, #selector(duplicated)), (delete, #selector(deleted)), (eye, #selector(eyed)),
                                 (lock, #selector(locked))] {
            button.isBordered = false
            button.imagePosition = .imageOnly
            button.target = self
            button.action = action
            addSubview(button)
        }
        duplicate.image = NSImage(systemSymbolName: "plus.square.on.square", accessibilityDescription: nil)
        delete.image = NSImage(systemSymbolName: "trash", accessibilityDescription: nil)
        [icon, name].forEach(addSubview)
        wantsLayer = true
        layer?.cornerRadius = 6
    }

    required init?(coder: NSCoder) {
        fatalError("LayerCell is created in code only")
    }

    override func layout() {
        super.layout()
        let middle = bounds.midY
        icon.frame = NSRect(x: 8, y: middle - 8, width: 16, height: 16)
        lock.frame = NSRect(x: bounds.maxX - 26, y: middle - 9, width: 18, height: 18)
        eye.frame = NSRect(x: bounds.maxX - 50, y: middle - 9, width: 20, height: 18)
        delete.frame = NSRect(x: bounds.maxX - 74, y: middle - 9, width: 18, height: 18)
        duplicate.frame = NSRect(x: bounds.maxX - 98, y: middle - 9, width: 18, height: 18)
        name.frame = NSRect(x: 32, y: middle - 9, width: duplicate.frame.minX - 36, height: 18)
    }

    func show(_ row: LayersPanel.Row) {
        self.row = row
        icon.image = row.tool.symbol
        name.stringValue = row.name
        icon.alphaValue = row.isHidden ? 0.4 : 1
        name.alphaValue = row.isHidden ? 0.4 : 1
        eye.image = NSImage(systemSymbolName: row.isHidden ? "eye.slash" : "eye", accessibilityDescription: nil)
        lock.image = NSImage(systemSymbolName: row.isLocked ? "lock.fill" : "lock.open", accessibilityDescription: nil)
        lock.alphaValue = row.isLocked ? 1 : 0.45
        duplicate.setAccessibilityLabel("Duplicate \(row.name)")
        delete.setAccessibilityLabel("Delete \(row.name)")
        delete.toolTip = "Delete, or the Delete key"
        // A locked shape cannot be deleted until it is unlocked. Dimmed by hand, as a tint
        // colour keeps a disabled button looking as bright as the rest.
        delete.isEnabled = !row.isLocked
        delete.alphaValue = row.isLocked ? 0.4 : 1
        eye.setAccessibilityLabel("\(row.isHidden ? "Show" : "Hide") \(row.name)")
        lock.setAccessibilityLabel("\(row.isLocked ? "Unlock" : "Lock") \(row.name)")
        eye.toolTip = row.isHidden ? "Show" : "Hide"
        lock.toolTip = row.isLocked ? "Unlock (⌘L)" : "Lock (⌘L)"
        duplicate.toolTip = "Duplicate (⌘D)"
        setAccessibilityLabel(row.name + (row.isLocked ? ", locked" : "") + (row.isHidden ? ", hidden" : ""))
        restyle()
    }

    override var backgroundStyle: NSView.BackgroundStyle {
        didSet { restyle() }
    }

    private func restyle() {
        let chosen = backgroundStyle == .emphasized
        let tint: NSColor = chosen ? .white : .labelColor
        [duplicate, delete, eye, lock].forEach { $0.contentTintColor = tint }
        icon.contentTintColor = tint
        name.textColor = tint
        duplicate.isHidden = !(chosen || hovering)
        delete.isHidden = duplicate.isHidden
        // A faint wash under the pointer; the chosen row has its accent fill instead.
        layer?.backgroundColor = hovering && !chosen ? NSColor.labelColor.withAlphaComponent(0.07).cgColor : NSColor.clear.cgColor
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) {
        hovering = true
        restyle()
        onHover?(row?.id)
    }

    override func mouseExited(with event: NSEvent) {
        hovering = false
        restyle()
        onHover?(nil)
    }

    @objc private func duplicated() {
        if let row { onDuplicate?(row.id) }
        onClicked?()
    }

    @objc private func deleted() {
        if let row { onDelete?(row.id) }
        onClicked?()
    }

    @objc private func eyed() {
        if let row { onHide?(row.id, !row.isHidden) }
        onClicked?()
    }

    @objc private func locked() {
        if let row { onLock?(row.id, !row.isLocked) }
        onClicked?()
    }
}
