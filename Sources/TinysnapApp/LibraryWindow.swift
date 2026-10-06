import AppKit
import Quartz
import TinysnapCore

/// Every kept capture, newest first, grouped by day. Return or double-click edits,
/// Space previews, Command C copies, Command S saves, dragging drops the image anywhere,
/// Delete trashes; the toolbar and each tile's hover buttons do the same without a key.
@MainActor
final class LibraryWindowController: NSWindowController, NSWindowDelegate, NSCollectionViewDataSource,
    NSCollectionViewDelegate, QLPreviewPanelDataSource, NSToolbarDelegate, NSToolbarItemValidation, NSMenuItemValidation {
    private let library: LibraryStore
    private let preferences: () -> Preferences
    /// Entries open in an editor, which are not trashed or rendered over.
    private let keeping: () -> Set<String>
    private let onOpen: (LibraryEntry) -> Void
    private let onPin: (LibraryEntry) -> Void
    /// Has an entry's open editor write its image now, so nothing handed out from here
    /// lags behind edits still on screen, a redaction least of all.
    private let flush: (LibraryEntry) -> Void
    /// Renders an entry's image again, off the main thread.
    private let renderStale: (LibraryEntry) -> Void

    private let grid = LibraryGrid()
    private let emptyNote = NSTextField(wrappingLabelWithString: "")
    private var days: [(title: String, entries: [LibraryEntry])] = []
    private var thumbnails: [URL: (modified: Date, image: NSImage, pixels: CGSize)] = [:]
    /// Pictures being made in the background, so an item shown twice asks once.
    private var loading: Set<URL> = []
    private var observer: NSObjectProtocol?
    private var pageNumber = 0
    /// Previous, the page and Next, under the grid while there is more than one page.
    private let pager = NSStackView()
    private let previousPage = NSButton(title: "Previous", target: nil, action: nil)
    private let nextPage = NSButton(title: "Next", target: nil, action: nil)
    private let pageTitle = NSTextField(labelWithString: "")

    init(library: LibraryStore, preferences: @escaping () -> Preferences, keeping: @escaping () -> Set<String>,
         onOpen: @escaping (LibraryEntry) -> Void, onPin: @escaping (LibraryEntry) -> Void,
         flush: @escaping (LibraryEntry) -> Void, renderStale: @escaping (LibraryEntry) -> Void) {
        self.library = library
        self.preferences = preferences
        self.keeping = keeping
        self.onOpen = onOpen
        self.onPin = onPin
        self.flush = flush
        self.renderStale = renderStale
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 860, height: 600),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "Library"
        // A tab of the window the captures share.
        window.tabbingMode = .preferred
        window.tabbingIdentifier = EditorWindowController.tabbingIdentifier
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 480, height: 320)
        super.init(window: window)
        window.delegate = self
        window.setFrameAutosaveName("TinysnapLibrary")
        let toolbar = NSToolbar(identifier: "TinysnapLibrary")
        toolbar.delegate = self
        toolbar.displayMode = .iconOnly
        toolbar.allowsUserCustomization = false
        window.toolbar = toolbar
        build(in: window)
        observer = NotificationCenter.default.addObserver(forName: .libraryChanged, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.reload() }
        }
    }

    required init?(coder: NSCoder) {
        fatalError("LibraryWindowController is created in code only")
    }

    private func build(in window: NSWindow) {
        let layout = LibraryLayout()
        layout.minimumInteritemSpacing = LibraryLayout.gap
        layout.minimumLineSpacing = LibraryLayout.gap
        layout.sectionInset = NSEdgeInsets(top: 0, left: LibraryLayout.edge, bottom: 16, right: LibraryLayout.edge)
        layout.headerReferenceSize = NSSize(width: 0, height: 52)

        grid.collectionViewLayout = layout
        grid.dataSource = self
        grid.delegate = self
        grid.isSelectable = true
        grid.allowsMultipleSelection = false
        grid.backgroundColors = [.windowBackgroundColor]
        grid.controller = self
        grid.register(LibraryItem.self, forItemWithIdentifier: LibraryItem.identifier)
        grid.register(LibraryHeader.self, forSupplementaryViewOfKind: NSCollectionView.elementKindSectionHeader,
                      withIdentifier: LibraryHeader.identifier)
        grid.setDraggingSourceOperationMask(.copy, forLocal: false)
        grid.setAccessibilityLabel("Captures")

        let scroll = NSScrollView()
        scroll.documentView = grid
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true

        emptyNote.alignment = .center
        emptyNote.textColor = .secondaryLabelColor
        emptyNote.font = .systemFont(ofSize: 15)

        previousPage.target = self
        previousPage.action = #selector(showPreviousPage(_:))
        nextPage.target = self
        nextPage.action = #selector(showNextPage(_:))
        pageTitle.font = .monospacedDigitSystemFont(ofSize: 13, weight: .regular)
        pageTitle.textColor = .secondaryLabelColor
        for button in [previousPage, nextPage] {
            button.widthAnchor.constraint(greaterThanOrEqualToConstant: 88).isActive = true
        }
        pager.setViews([previousPage, pageTitle, nextPage], in: .center)
        pager.spacing = 12
        pager.edgeInsets = NSEdgeInsets(top: 10, left: 0, bottom: 10, right: 0)
        pager.setContentHuggingPriority(.required, for: .vertical)

        // The pager detaches when hidden, so one page gives the grid the whole height.
        let column = NSStackView(views: [scroll, pager])
        column.orientation = .vertical
        column.spacing = 0
        let container = NSView()
        for view in [column, emptyNote] as [NSView] {
            view.translatesAutoresizingMaskIntoConstraints = false
            container.addSubview(view)
        }
        NSLayoutConstraint.activate([
            column.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            column.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            column.topAnchor.constraint(equalTo: container.topAnchor),
            column.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            scroll.widthAnchor.constraint(equalTo: column.widthAnchor),
            pager.widthAnchor.constraint(equalTo: column.widthAnchor),
            emptyNote.centerXAnchor.constraint(equalTo: container.centerXAnchor),
            emptyNote.centerYAnchor.constraint(equalTo: container.centerYAnchor),
            emptyNote.widthAnchor.constraint(lessThanOrEqualToConstant: 360),
        ])
        window.contentView = container
    }

    /// On the newest page when it was not open.
    func show() {
        if window?.isVisible != true { pageNumber = 0 }
        reload()
        // Centred only on its own: as a tab it keeps the shared window where it is.
        if window?.isVisible != true, (window?.tabbedWindows?.count ?? 0) <= 1 { window?.center() }
        showWindow(nil)
        window?.makeFirstResponder(grid)
        NSApp.activate(ignoringOtherApps: true)
    }

    // MARK: Contents

    /// Also has any image left older than its edits, by a crash or a quit mid render,
    /// rendered again, so what the grid and Quick Look show matches what reopening gives.
    /// The old image shows until the new one lands and the grid reloads.
    func reload() {
        let open = keeping()
        let entries = library.entries()
        for entry in entries where !open.contains(entry.name) && library.imageIsStale(entry) {
            renderStale(entry)
        }
        let page = LibraryPage(entries, number: pageNumber)
        pageNumber = page.number
        pager.isHidden = page.count == 1
        pageTitle.stringValue = page.title
        previousPage.isEnabled = page.hasPrevious
        nextPage.isEnabled = page.hasNext
        let calendar = Calendar.current
        var grouped: [(day: Date, entries: [LibraryEntry])] = []
        for entry in page.entries {
            let day = calendar.startOfDay(for: entry.captured)
            if grouped.last?.day == day {
                grouped[grouped.count - 1].entries.append(entry)
            } else {
                grouped.append((day, [entry]))
            }
        }
        let selected = selectedEntry
        days = grouped.map { (Self.title(for: $0.day), $0.entries) }
        grid.reloadData()
        // A reload, after an edit or a new capture, keeps the capture that was selected.
        if let selected, let section = days.firstIndex(where: { $0.entries.contains(selected) }),
           let item = days[section].entries.firstIndex(of: selected) {
            grid.selectionIndexPaths = [IndexPath(item: item, section: section)]
        }

        emptyNote.isHidden = !entries.isEmpty
        emptyNote.stringValue = preferences().keepLibrary
            ? "Captures appear here and stay for 30 days."
            : "The library is off. Turn it on in Settings to keep captures here for 30 days."
        if QLPreviewPanel.sharedPreviewPanelExists(), QLPreviewPanel.shared().isVisible {
            QLPreviewPanel.shared().reloadData()
        }
    }

    private static func title(for day: Date) -> String {
        let calendar = Calendar.current
        if calendar.isDateInToday(day) { return "Today" }
        if calendar.isDateInYesterday(day) { return "Yesterday" }
        return DateFormatter.localizedString(from: day, dateStyle: .full, timeStyle: .none)
    }

    private var allEntries: [LibraryEntry] { days.flatMap(\.entries) }

    private func entry(at indexPath: IndexPath) -> LibraryEntry {
        days[indexPath.section].entries[indexPath.item]
    }

    private var selectedEntry: LibraryEntry? {
        grid.selectionIndexPaths.first.map(entry(at:))
    }

    /// Another page, from its top, with nothing selected: the toolbar acts on the selection,
    /// which is never out of sight.
    @objc private func showPreviousPage(_ sender: Any?) { turn(by: -1) }
    @objc private func showNextPage(_ sender: Any?) { turn(by: 1) }

    private func turn(by pages: Int) {
        pageNumber += pages
        grid.selectionIndexPaths = []
        reload()
        grid.scrollToVisible(NSRect(x: 0, y: 0, width: 1, height: 1))
    }

    /// A small copy of the rendered image if one is kept, and the image's size in pixels.
    /// Without one it is made in the background and handed to the item showing the
    /// capture by then, so a page of big captures opens at once.
    private func thumbnail(for entry: LibraryEntry) -> (image: NSImage?, pixels: CGSize) {
        let url = FileManager.default.fileExists(atPath: entry.imageURL.path) ? entry.imageURL : entry.originalURL
        let modified = (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
        if let cached = thumbnails[url], cached.modified == modified { return (cached.image, cached.pixels) }
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return (nil, .zero) }
        // The header alone, which is quick enough to read as the item appears.
        let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
        let pixels = CGSize(width: (properties?[kCGImagePropertyPixelWidth] as? Int) ?? 0,
                            height: (properties?[kCGImagePropertyPixelHeight] as? Int) ?? 0)
        guard loading.insert(url).inserted else { return (nil, pixels) }
        Task { @MainActor [weak self] in
            let small = await Task.detached(priority: .userInitiated) { Self.smallCopy(of: url) }.value
            self?.arrived(small, for: entry, from: url, modified: modified, pixels: pixels)
        }
        return (nil, pixels)
    }

    /// No more than 640 pixels across.
    private nonisolated static func smallCopy(of url: URL) -> CGImage? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let options = [kCGImageSourceCreateThumbnailFromImageAlways: true,
                       kCGImageSourceThumbnailMaxPixelSize: 640] as CFDictionary
        return CGImageSourceCreateThumbnailAtIndex(source, 0, options)
    }

    /// Kept for the next time, and on the item showing the capture now, if one does: items
    /// are reused as the grid scrolls and pages turn.
    private func arrived(_ small: CGImage?, for entry: LibraryEntry, from url: URL, modified: Date, pixels: CGSize) {
        loading.remove(url)
        guard let small else { return }
        let image = NSImage(cgImage: small, size: .zero)
        thumbnails[url] = (modified, image, pixels)
        guard let section = days.firstIndex(where: { $0.entries.contains(entry) }),
              let index = days[section].entries.firstIndex(of: entry),
              let item = grid.item(at: IndexPath(item: index, section: section)) as? LibraryItem else { return }
        item.showPicture(image)
    }

    // MARK: Grid

    func numberOfSections(in collectionView: NSCollectionView) -> Int {
        days.count
    }

    func collectionView(_ collectionView: NSCollectionView, numberOfItemsInSection section: Int) -> Int {
        days[section].entries.count
    }

    func collectionView(_ collectionView: NSCollectionView, itemForRepresentedObjectAt indexPath: IndexPath) -> NSCollectionViewItem {
        let item = collectionView.makeItem(withIdentifier: LibraryItem.identifier, for: indexPath)
        guard let item = item as? LibraryItem else { return item }
        let entry = entry(at: indexPath)
        let (image, pixels) = thumbnail(for: entry)
        item.show(image: image, time: DateFormatter.localizedString(from: entry.captured, dateStyle: .none, timeStyle: .short),
                  width: Int(pixels.width), height: Int(pixels.height))
        item.onCopy = { [weak self] button in if self?.copy(entry: entry) == true { Output.showDone(on: button) } }
        item.onSave = { [weak self] button in if self?.save(entry: entry) == true { Output.showDone(on: button) } }
        item.onEdit = { [weak self] in self?.onOpen(entry) }
        return item
    }

    func collectionView(_ collectionView: NSCollectionView, viewForSupplementaryElementOfKind kind: NSCollectionView.SupplementaryElementKind,
                        at indexPath: IndexPath) -> NSView {
        let view = collectionView.makeSupplementaryView(ofKind: kind, withIdentifier: LibraryHeader.identifier, for: indexPath)
        (view as? LibraryHeader)?.label.stringValue = days[indexPath.section].title
        return view
    }

    func collectionView(_ collectionView: NSCollectionView, canDragItemsAt indexPaths: Set<IndexPath>, with event: NSEvent) -> Bool {
        true
    }

    /// The rendered PNG itself, which Finder, Mail, Slack and browsers all take.
    func collectionView(_ collectionView: NSCollectionView, pasteboardWriterForItemAt indexPath: IndexPath) -> NSPasteboardWriting? {
        let entry = entry(at: indexPath)
        flush(entry)
        return entry.imageURL as NSURL
    }

    func collectionView(_ collectionView: NSCollectionView, didSelectItemsAt indexPaths: Set<IndexPath>) {
        guard QLPreviewPanel.sharedPreviewPanelExists(), QLPreviewPanel.shared().isVisible,
              let selected = selectedEntry, let index = allEntries.firstIndex(of: selected) else { return }
        QLPreviewPanel.shared().currentPreviewItemIndex = index
    }

    // MARK: Actions

    func openSelected() {
        guard let entry = selectedEntry else { return }
        onOpen(entry)
    }

    @objc func openItem(_ sender: Any?) {
        openSelected()
    }

    @objc func copy(_ sender: Any?) {
        guard let entry = selectedEntry, copy(entry: entry) else { return }
        Output.showDone(on: sender)
    }

    @objc func saveImage(_ sender: Any?) {
        guard let entry = selectedEntry, save(entry: entry) else { return }
        Output.showDone(on: sender)
    }

    @objc func pinImage(_ sender: Any?) {
        guard let entry = selectedEntry else { return }
        onPin(entry)
    }

    /// The entry drawn from its edits, so a capture with a size of its own comes out at
    /// it and one without takes the Export setting. Nil, with a beep, when it cannot be.
    private func exported(_ entry: LibraryEntry) -> (ExportedImage, Data)? {
        flush(entry)
        guard let document = library.open(entry)?.document,
              let exported = Exporter.export(document, scale: preferences().exportScale),
              let png = Exporter.pngData(exported) else {
            NSSound.beep()
            return nil
        }
        return (exported, png)
    }

    private func copy(entry: LibraryEntry) -> Bool {
        guard let (exported, png) = exported(entry) else { return false }
        Output.copy(exported, png: png)
        return true
    }

    /// Into the save folder, as the editor's Save does.
    private func save(entry: LibraryEntry) -> Bool {
        guard let (_, png) = exported(entry) else { return false }
        do {
            try Output.save(png, in: preferences().saveFolderURL)
            return true
        } catch {
            Output.show(error, over: window)
            return false
        }
    }

    /// A tick on the button that asked, for a moment: copying and saving change nothing
    /// on screen, so without it a click looks as if it did nothing.
    @objc func showInFinder(_ sender: Any?) {
        guard let entry = selectedEntry else { return }
        flush(entry)
        NSWorkspace.shared.activateFileViewerSelecting([entry.imageURL])
    }

    /// To the Trash rather than gone, so a slip of the Delete key can be undone in Finder.
    @objc func moveToTrash(_ sender: Any?) {
        guard let entry = selectedEntry else { return }
        guard !keeping().contains(entry.name) else {
            let alert = NSAlert()
            alert.messageText = "This capture is open in an editor."
            alert.informativeText = "Close its window, then move it to the Trash."
            if let window { alert.beginSheetModal(for: window) }
            return
        }
        do {
            try FileManager.default.trashItem(at: entry.folder, resultingItemURL: nil)
            NotificationCenter.default.post(name: .libraryChanged, object: nil)
        } catch {
            Output.show(error, over: window)
        }
    }

    func contextMenu() -> NSMenu {
        let menu = NSMenu()
        for (title, action) in [("Edit", #selector(openItem(_:))), ("Copy", #selector(copy(_:))),
                                ("Save", #selector(saveImage(_:))), ("Pin", #selector(pinImage(_:))),
                                ("Show in Finder", #selector(showInFinder(_:))), ("Move to Trash", #selector(moveToTrash(_:)))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
            item.target = self
            menu.addItem(item)
        }
        return menu
    }

    // MARK: Toolbar

    /// Everything the right click menu offers for the selected capture, without a click
    /// on the capture first.
    private var toolbarActions: [(id: NSToolbarItem.Identifier, symbol: String, label: String, tooltip: String, action: Selector)] {
        [
            (NSToolbarItem.Identifier("copy"), "doc.on.doc", "Copy", "Copy (⌘C)", #selector(copy(_:))),
            (NSToolbarItem.Identifier("save"), "square.and.arrow.down", "Save", "Save to the save folder (⌘S)", #selector(saveImage(_:))),
            (NSToolbarItem.Identifier("edit"), "pencil", "Edit", "Edit (Return)", #selector(openItem(_:))),
            (NSToolbarItem.Identifier("pin"), "pin", "Pin", "Pin on top of every app (⌘P)", #selector(pinImage(_:))),
            (NSToolbarItem.Identifier("trash"), "trash", "Move to Trash", "Move to Trash (Delete)", #selector(moveToTrash(_:))),
        ]
    }

    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        [.flexibleSpace] + toolbarActions.map(\.id)
    }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        toolbarDefaultItemIdentifiers(toolbar)
    }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier identifier: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        guard let action = toolbarActions.first(where: { $0.id == identifier }) else { return nil }
        let item = NSToolbarItem(itemIdentifier: identifier)
        item.image = NSImage(systemSymbolName: action.symbol, accessibilityDescription: action.label)
        item.label = action.label
        item.toolTip = action.tooltip
        item.target = self
        item.action = action.action
        item.isBordered = true
        return item
    }

    /// Only with a capture selected, which is what every one of them acts on.
    func validateToolbarItem(_ item: NSToolbarItem) -> Bool {
        selectedEntry != nil
    }

    /// The same for the menus, so Save or Copy with nothing selected is dimmed rather
    /// than a key that does nothing.
    func validateMenuItem(_ item: NSMenuItem) -> Bool {
        guard let action = item.action, toolbarActions.contains(where: { $0.action == action })
                || action == #selector(showInFinder(_:)) else { return true }
        return selectedEntry != nil
    }

    // MARK: Quick Look

    func togglePreview() {
        if QLPreviewPanel.sharedPreviewPanelExists(), QLPreviewPanel.shared().isVisible {
            QLPreviewPanel.shared().orderOut(nil)
        } else if let entry = selectedEntry {
            flush(entry)
            QLPreviewPanel.shared().makeKeyAndOrderFront(nil)
        }
    }

    // Quick Look calls these on the main thread without declaring it, so they are
    // nonisolated and step onto the main actor themselves.
    override nonisolated func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool {
        true
    }

    override nonisolated func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated {
            panel.dataSource = self
            if let selected = selectedEntry, let index = allEntries.firstIndex(of: selected) {
                panel.currentPreviewItemIndex = index
            }
        }
    }

    override nonisolated func endPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated { panel.dataSource = nil }
    }

    nonisolated func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int {
        MainActor.assumeIsolated { allEntries.count }
    }

    /// Nil past the end: an entry trashed or swept while the panel is up shortens the list.
    nonisolated func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> (any QLPreviewItem)! {
        let url: URL? = MainActor.assumeIsolated {
            guard allEntries.indices.contains(index) else { return nil }
            flush(allEntries[index])
            return allEntries[index].imageURL
        }
        return url.map { $0 as NSURL }
    }

    func windowWillClose(_ notification: Notification) {
        if QLPreviewPanel.sharedPreviewPanelExists(), QLPreviewPanel.shared().isVisible {
            QLPreviewPanel.shared().orderOut(nil)
        }
    }
}

/// The grid's keys and clicks: Space, Return, Delete, double-click and right-click.
final class LibraryGrid: NSCollectionView {
    weak var controller: LibraryWindowController?

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 49: controller?.togglePreview()
        case 36, 76: controller?.openSelected()
        case 51, 117: controller?.moveToTrash(nil)
        default: super.keyDown(with: event)
        }
    }

    override func mouseDown(with event: NSEvent) {
        super.mouseDown(with: event)
        if event.clickCount == 2 { controller?.openSelected() }
    }

    /// Right-click selects what is under the pointer first, as Finder does.
    override func menu(for event: NSEvent) -> NSMenu? {
        guard let indexPath = indexPathForItem(at: convert(event.locationInWindow, from: nil)) else { return nil }
        selectionIndexPaths = [indexPath]
        return controller?.contextMenu()
    }
}

/// Tiles share each row: as many as sit nearest 230 points wide, stretched to fill it, so
/// the gaps and the edges stay the same at any window width. Leftover width went into
/// the gaps before, which grew uneven as the window widened.
final class LibraryLayout: NSCollectionViewFlowLayout {
    static let gap: CGFloat = 16
    static let edge: CGFloat = 24
    private static let idealTileWidth: CGFloat = 230

    override func prepare() {
        if let width = collectionView?.bounds.width, width > 0 {
            let usable = width - Self.edge * 2
            let columns = max(1, ((usable + Self.gap) / (Self.idealTileWidth + Self.gap)).rounded())
            let tile = ((usable - Self.gap * (columns - 1)) / columns).rounded(.down)
            let size = NSSize(width: tile, height: LibraryItem.height(forWidth: tile))
            if itemSize != size { itemSize = size }
        }
        super.prepare()
    }

    override func shouldInvalidateLayout(forBoundsChange newBounds: NSRect) -> Bool {
        newBounds.width != collectionView?.bounds.width || super.shouldInvalidateLayout(forBoundsChange: newBounds)
    }
}

/// One capture: its picture, the time it was taken and its size in pixels.
final class LibraryItem: NSCollectionViewItem {
    static let identifier = NSUserInterfaceItemIdentifier("LibraryItem")
    /// Copy, Save and Edit for this capture, from the buttons over its picture while the
    /// pointer is on the tile. Copy and Save hand over their button for a tick.
    var onCopy: ((NSButton) -> Void)?
    var onSave: ((NSButton) -> Void)?
    var onEdit: (() -> Void)?
    private lazy var hoverActions = TileHoverView(buttons: [
        TileActionButton(symbol: "doc.on.doc", label: "Copy", tooltip: "Copy", primary: true, target: self, action: #selector(copyTapped(_:))),
        TileActionButton(symbol: "square.and.arrow.down", label: "Save", tooltip: "Save to the save folder", primary: false, target: self,
                         action: #selector(saveTapped(_:))),
        TileActionButton(symbol: "pencil", label: "Edit", tooltip: "Edit", primary: false, target: self, action: #selector(editTapped(_:))),
    ])
    private let picture = NSImageView()
    private let time = NSTextField(labelWithString: "")
    private let pixels = NSTextField(labelWithString: "")

    private let tile = LibraryTile(frame: NSRect(x: 0, y: 0, width: 230, height: LibraryItem.height(forWidth: 230)))

    /// An 8 point margin round a 16 by 10 picture, then the labels' row.
    static func height(forWidth width: CGFloat) -> CGFloat {
        (8 + (width - 16) * 10 / 16 + 32).rounded()
    }

    override func loadView() {
        let root = tile
        picture.imageScaling = .scaleProportionallyUpOrDown
        picture.wantsLayer = true
        picture.layer?.cornerRadius = 6
        picture.layer?.masksToBounds = true
        // A well a shade under the tile, the same box on every tile, so a wide capture's
        // bands read as part of the frame rather than as uneven space.
        picture.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.12).cgColor
        for label in [time, pixels] {
            label.font = .monospacedDigitSystemFont(ofSize: 12, weight: .regular)
            label.textColor = .secondaryLabelColor
        }
        pixels.alignment = .right
        for view in [picture, time, pixels] as [NSView] {
            view.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(view)
        }
        NSLayoutConstraint.activate([
            picture.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 8),
            picture.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -8),
            picture.topAnchor.constraint(equalTo: root.topAnchor, constant: 8),
            picture.heightAnchor.constraint(equalTo: picture.widthAnchor, multiplier: 10.0 / 16),
            time.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 10),
            time.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -9),
            pixels.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -10),
            pixels.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -9),
        ])
        buildHoverActions(in: root)
        view = root
    }

    /// The fade and the buttons cover the picture exactly, clipped to its corners, so the
    /// row slides up from under the picture's own edge.
    private func buildHoverActions(in root: NSView) {
        hoverActions.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(hoverActions)
        NSLayoutConstraint.activate([
            hoverActions.leadingAnchor.constraint(equalTo: picture.leadingAnchor),
            hoverActions.trailingAnchor.constraint(equalTo: picture.trailingAnchor),
            hoverActions.topAnchor.constraint(equalTo: picture.topAnchor),
            hoverActions.bottomAnchor.constraint(equalTo: picture.bottomAnchor),
        ])
        tile.onHover = { [weak self] inside in self?.hoverActions.show(inside, animated: true) }
    }

    @objc private func copyTapped(_ sender: NSButton) { onCopy?(sender) }
    @objc private func saveTapped(_ sender: NSButton) { onSave?(sender) }
    @objc private func editTapped(_ sender: NSButton) { onEdit?() }

    /// A reused tile starts without its hover buttons, whatever the last one it showed.
    override func prepareForReuse() {
        super.prepareForReuse()
        hoverActions.show(false, animated: false)
    }

    override var isSelected: Bool {
        didSet { tile.isSelected = isSelected }
    }

    func showPicture(_ image: NSImage) {
        picture.image = image
    }

    func show(image: NSImage?, time: String, width: Int, height: Int) {
        picture.image = image
        self.time.stringValue = time
        pixels.stringValue = "\(width) × \(height)"
        picture.setAccessibilityLabel("Capture at \(time), \(width) by \(height) pixels")
    }
}

/// The ground one capture sits on, so a portrait capture and its labels line up on the
/// same tile instead of floating in an empty cell. Drawn in `updateLayer` so the fill
/// follows light and dark.
final class LibraryTile: NSView {
    var isSelected = false {
        didSet { needsDisplay = true }
    }
    /// The pointer came onto the tile, or left it.
    var onHover: ((Bool) -> Void)?
    private var area: NSTrackingArea?

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let area { removeTrackingArea(area) }
        let tracking = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
                                      owner: self, userInfo: nil)
        addTrackingArea(tracking)
        area = tracking
    }

    override func mouseEntered(with event: NSEvent) { onHover?(true) }
    override func mouseExited(with event: NSEvent) { onHover?(false) }

    override var wantsUpdateLayer: Bool { true }

    override func updateLayer() {
        layer?.cornerRadius = 10
        layer?.backgroundColor = NSColor.quaternarySystemFill.cgColor
        layer?.borderColor = NSColor.controlAccentColor.cgColor
        layer?.borderWidth = isSelected ? 3 : 0
    }
}

/// Copy, Save and Edit over a tile's picture: a dark fade rises from the foot and a row
/// of labelled buttons slides up on it while the pointer is on the tile. The fade keeps
/// white labels readable over a white capture as well as a dark one.
final class TileHoverView: NSView {
    private let scrim = TileScrim()
    private let row: NSStackView
    private var rowBottom: NSLayoutConstraint!
    /// Where the row sits when shown, and far enough under the picture to be out of sight.
    private static let shown: CGFloat = -8
    private static let away: CGFloat = 40

    init(buttons: [TileActionButton]) {
        row = NSStackView(views: buttons)
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 6
        layer?.masksToBounds = true
        row.distribution = .fillEqually
        row.spacing = 5
        for view in [scrim, row] as [NSView] {
            view.translatesAutoresizingMaskIntoConstraints = false
            addSubview(view)
        }
        rowBottom = row.bottomAnchor.constraint(equalTo: bottomAnchor, constant: Self.away)
        NSLayoutConstraint.activate([
            scrim.leadingAnchor.constraint(equalTo: leadingAnchor),
            scrim.trailingAnchor.constraint(equalTo: trailingAnchor),
            scrim.topAnchor.constraint(equalTo: topAnchor),
            scrim.bottomAnchor.constraint(equalTo: bottomAnchor),
            row.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 6),
            row.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -6),
            rowBottom,
        ])
        scrim.alphaValue = 0
    }

    required init?(coder: NSCoder) {
        fatalError("TileHoverView is created in code only")
    }

    /// Only the buttons take clicks. Everywhere else a click, a double-click or a drag
    /// reaches the grid under it, as on the picture itself.
    override func hitTest(_ point: NSPoint) -> NSView? {
        let hit = super.hitTest(point)
        return hit is NSButton ? hit : nil
    }

    func show(_ shown: Bool, animated: Bool) {
        let bottom = shown ? Self.shown : Self.away
        guard animated else {
            rowBottom.constant = bottom
            scrim.alphaValue = shown ? 1 : 0
            return
        }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = shown ? 0.24 : 0.16
            context.timingFunction = shown ? CAMediaTimingFunction(controlPoints: 0.2, 0.8, 0.2, 1) : CAMediaTimingFunction(name: .easeIn)
            context.allowsImplicitAnimation = true
            rowBottom.constant = bottom
            scrim.animator().alphaValue = shown ? 1 : 0
            layoutSubtreeIfNeeded()
        }
    }
}

/// Black rising from the foot of the picture: strongest under the buttons, gone by two
/// thirds of the way up.
final class TileScrim: NSView {
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
    }

    required init?(coder: NSCoder) {
        fatalError("TileScrim is created in code only")
    }

    override func makeBackingLayer() -> CALayer {
        let gradient = CAGradientLayer()
        gradient.colors = [NSColor.black.withAlphaComponent(0.78).cgColor, NSColor.black.withAlphaComponent(0.45).cgColor,
                           NSColor.black.withAlphaComponent(0).cgColor]
        gradient.locations = [0, 0.38, 0.7]
        gradient.startPoint = CGPoint(x: 0.5, y: 0)
        gradient.endPoint = CGPoint(x: 0.5, y: 1)
        return gradient
    }
}

/// One hover button: a white label on a translucent ground, or on the accent for Copy,
/// and dark on white under the pointer.
final class TileActionButton: NSButton {
    private let isPrimary: Bool
    private let label: String
    private var area: NSTrackingArea?
    private var hovering = false {
        didSet { restyle() }
    }

    init(symbol: String, label: String, tooltip: String, primary: Bool, target: AnyObject, action: Selector) {
        isPrimary = primary
        self.label = label
        super.init(frame: .zero)
        self.target = target
        self.action = action
        isBordered = false
        wantsLayer = true
        layer?.cornerRadius = 7
        image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?
            .withSymbolConfiguration(NSImage.SymbolConfiguration(pointSize: 11, weight: .semibold))
        imagePosition = .imageLeading
        imageHugsTitle = true
        toolTip = tooltip
        setAccessibilityLabel(tooltip)
        translatesAutoresizingMaskIntoConstraints = false
        heightAnchor.constraint(equalToConstant: 28).isActive = true
        restyle()
    }

    required init?(coder: NSCoder) {
        fatalError("TileActionButton is created in code only")
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let area { removeTrackingArea(area) }
        let tracking = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
                                      owner: self, userInfo: nil)
        addTrackingArea(tracking)
        area = tracking
    }

    override func mouseEntered(with event: NSEvent) { hovering = true }
    override func mouseExited(with event: NSEvent) { hovering = false }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        restyle()
    }

    private func restyle() {
        let accent = NSColor.controlAccentColor
        let ground: NSColor = isPrimary
            ? (hovering ? accent.blended(withFraction: 0.18, of: .white) ?? accent : accent)
            : (hovering ? .white : NSColor.white.withAlphaComponent(0.2))
        let ink: NSColor = hovering && !isPrimary ? NSColor(white: 0.07, alpha: 1) : .white
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = ground.cgColor
        }
        contentTintColor = ink
        attributedTitle = NSAttributedString(string: label, attributes: [.foregroundColor: ink, .font: NSFont.systemFont(ofSize: 12, weight: .semibold)])
    }
}

/// The day a group of captures was taken.
final class LibraryHeader: NSView, NSCollectionViewElement {
    static let identifier = NSUserInterfaceItemIdentifier("LibraryHeader")
    let label = NSTextField(labelWithString: "")

    override init(frame: NSRect) {
        super.init(frame: frame)
        label.font = .systemFont(ofSize: 15, weight: .semibold)
        label.translatesAutoresizingMaskIntoConstraints = false
        addSubview(label)
        // In line with the tiles below it, and sitting on them rather than on the title bar.
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: LibraryLayout.edge),
            label.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -12),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("LibraryHeader is created in code only")
    }
}


