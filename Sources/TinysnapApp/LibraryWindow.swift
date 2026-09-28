import AppKit
import Quartz
import TinysnapCore

/// Every kept capture, newest first, grouped by day. Return or double-click edits,
/// Space previews, Command C copies, dragging drops the image anywhere, Delete trashes.
@MainActor
final class LibraryWindowController: NSWindowController, NSWindowDelegate, NSCollectionViewDataSource,
    NSCollectionViewDelegate, QLPreviewPanelDataSource {
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
    private var observer: NSObjectProtocol?

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
        window.tabbingMode = .disallowed
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 480, height: 320)
        super.init(window: window)
        window.delegate = self
        window.setFrameAutosaveName("TinysnapLibrary")
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

        let container = NSView()
        for view in [scroll, emptyNote] as [NSView] {
            view.translatesAutoresizingMaskIntoConstraints = false
            container.addSubview(view)
        }
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: container.topAnchor),
            scroll.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            emptyNote.centerXAnchor.constraint(equalTo: container.centerXAnchor),
            emptyNote.centerYAnchor.constraint(equalTo: container.centerYAnchor),
            emptyNote.widthAnchor.constraint(lessThanOrEqualToConstant: 360),
        ])
        window.contentView = container
    }

    func show() {
        reload()
        if window?.isVisible != true { window?.center() }
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
        let calendar = Calendar.current
        var grouped: [(day: Date, entries: [LibraryEntry])] = []
        for entry in entries {
            let day = calendar.startOfDay(for: entry.captured)
            if grouped.last?.day == day {
                grouped[grouped.count - 1].entries.append(entry)
            } else {
                grouped.append((day, [entry]))
            }
        }
        days = grouped.map { (Self.title(for: $0.day), $0.entries) }
        grid.reloadData()

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

    /// A small copy of the rendered image, kept until the image changes.
    // ponytail: made on the main thread as items appear; move to a background queue if a big library scrolls slowly.
    private func thumbnail(for entry: LibraryEntry) -> (image: NSImage?, pixels: CGSize) {
        let url = FileManager.default.fileExists(atPath: entry.imageURL.path) ? entry.imageURL : entry.originalURL
        let modified = (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
        if let cached = thumbnails[url], cached.modified == modified { return (cached.image, cached.pixels) }
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return (nil, .zero) }
        let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
        let pixels = CGSize(width: (properties?[kCGImagePropertyPixelWidth] as? Int) ?? 0,
                            height: (properties?[kCGImagePropertyPixelHeight] as? Int) ?? 0)
        let options = [kCGImageSourceCreateThumbnailFromImageAlways: true,
                       kCGImageSourceThumbnailMaxPixelSize: 640] as CFDictionary
        guard let small = CGImageSourceCreateThumbnailAtIndex(source, 0, options) else { return (nil, pixels) }
        let image = NSImage(cgImage: small, size: .zero)
        thumbnails[url] = (modified, image, pixels)
        return (image, pixels)
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

    /// Drawn from the entry's edits, so a capture with a size of its own copies at it and
    /// one without takes the Export setting.
    @objc func copy(_ sender: Any?) {
        if let entry = selectedEntry { flush(entry) }
        guard let entry = selectedEntry, let document = library.open(entry)?.document,
              let exported = Exporter.export(document, scale: preferences().exportScale),
              let png = Exporter.pngData(exported) else {
            NSSound.beep()
            return
        }
        Output.copy(exported, png: png)
    }

    @objc func pinItem(_ sender: Any?) {
        guard let entry = selectedEntry else { return }
        onPin(entry)
    }

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
        for (title, action) in [("Open", #selector(openItem(_:))), ("Copy", #selector(copy(_:))),
                                ("Pin", #selector(pinItem(_:))), ("Show in Finder", #selector(showInFinder(_:))),
                                ("Move to Trash", #selector(moveToTrash(_:)))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
            item.target = self
            menu.addItem(item)
        }
        return menu
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
        view = root
    }

    override var isSelected: Bool {
        didSet { tile.isSelected = isSelected }
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

    override var wantsUpdateLayer: Bool { true }

    override func updateLayer() {
        layer?.cornerRadius = 10
        layer?.backgroundColor = NSColor.quaternarySystemFill.cgColor
        layer?.borderColor = NSColor.controlAccentColor.cgColor
        layer?.borderWidth = isSelected ? 3 : 0
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
