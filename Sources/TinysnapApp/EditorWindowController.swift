import AppKit
import TinysnapCore
import UniformTypeIdentifiers

/// Keeps a smaller capture centred in the window instead of pinned to a corner.
final class CenteringClipView: NSClipView {
    /// Set while a shape is being drawn past the capture. The canvas then grows without
    /// being centred again under the pointer, and settles back to the centre after.
    var isHoldingPlace = false

    override func constrainBoundsRect(_ proposedBounds: NSRect) -> NSRect {
        guard !isHoldingPlace else { return proposedBounds }
        var rect = super.constrainBoundsRect(proposedBounds)
        guard let document = documentView else { return rect }
        if rect.width > document.frame.width { rect.origin.x = (document.frame.width - rect.width) / 2 }
        if rect.height > document.frame.height { rect.origin.y = (document.frame.height - rect.height) / 2 }
        return rect
    }
}

/// An upright rule between toolbar groups, fainter than the buttons either side.
final class ToolbarDivider: NSView {
    override var intrinsicContentSize: NSSize { NSSize(width: 13, height: 18) }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.separatorColor.setFill()
        NSRect(x: (bounds.width - 1) / 2, y: (bounds.height - 18) / 2, width: 1, height: 18).fill()
    }
}

/// One window per capture: the canvas, one toolbar row in the title bar, and the
/// copy, save and drag out actions.
@MainActor
final class EditorWindowController: NSWindowController, NSWindowDelegate, NSToolbarDelegate, NSMenuItemValidation {
    private let canvas: CanvasView
    private let scrollView = NSScrollView()
    private let preferences: () -> Preferences
    private let onStylesChange: ([Tool: Style], String) -> Void
    private let onClose: (EditorWindowController) -> Void
    /// The image, its pixels per point, its entry, and whether it keeps its size.
    private let onPin: (CGImage, CGFloat, LibraryEntry?, Bool) -> Void
    /// A backdrop setting was picked, so it can be remembered for the next capture.
    private let onBackdropChange: (Backdrop) -> Void
    /// The Measure tool's lines, edge contrast or guide changed, so they can be remembered.
    private let onMeasureChange: (MeasureSettings) -> Void
    /// The layers panel was opened or closed, so the next editor opens the same way.
    private let onShowsLayersChange: (Bool) -> Void
    /// The strip down the right edge: the library, and the layers panel's switch.
    private let rail = NSView()
    private lazy var libraryButton = RailButton(symbol: "photo.stack", label: "Library, every capture from the last 30 days",
                                                action: #selector(AppDelegate.openLibrary(_:)), target: nil)
    private lazy var layersButton = RailButton(symbol: "square.3.layers.3d", label: "Layers", tooltip: "Layers (⇧⌘L)",
                                               action: #selector(toggleLayers(_:)), target: self, isSwitch: true)
    private let layers = LayersPanel()
    private var showsLayers: Bool
    private static let railWidth: CGFloat = 40
    /// What every capture window and the library share, so they open as tabs of one window.
    static let tabbingIdentifier = "Tinysnap"
    /// The style bar's height before it has first shown, for the panel's place under it.
    private static let styleBarHeight: CGFloat = 46
    /// The Measure guide while it is open.
    private var measureGuide: NSPopover?
    /// The tool the toolbar last showed, so the guide opens as the Measure tool is picked.
    private var lastTool: Tool?
    /// The canvas's size when it last settled, so a change can be told from a redraw.
    private var settledCanvasSize = NSSize.zero
    /// Set once the window is resized by hand; from then on the editor leaves its size alone.
    private var sizedByHand = false
    /// The style panel's two other uses, each opened from its toolbar button.
    private enum Panel {
        case backdrop, size
    }

    /// Set while the style panel shows the backdrop or the export size, with the tool and
    /// selection it was opened over: picking another tool, or selecting something, puts
    /// the panel back.
    private var panel: (kind: Panel, tool: Tool, selection: Annotation.ID?)?
    private weak var backdropItem: NSToolbarItem?
    /// The library entry this editor keeps up to date. Nil for a file opened from disk,
    /// a damaged entry opened flat, or any capture while the library is off.
    let entry: LibraryEntry?
    private let library: LibraryStore
    /// What `edits.json` holds now, so an edit is written once and a no-op never.
    private var keptDocument: Document
    /// What `image.png` was last rendered from, so a close right after a save renders once.
    private var renderedDocument: Document?
    private var pendingKeep: Task<Void, Never>?

    private let styleBar = StyleBar()
    private let colorWell = NSView()
    private let colorLabel = NSTextField(labelWithString: "")
    /// The colour under the pointer, which Tab copies.
    private var pointerColor: String?
    private var magnifyObserver: NSObjectProtocol?
    private var isClosingForGood = false

    private static let copyItem = NSToolbarItem.Identifier("copy")
    private static let saveItem = NSToolbarItem.Identifier("save")
    private static let dragItem = NSToolbarItem.Identifier("drag")
    private static let pinItem = NSToolbarItem.Identifier("pin")
    private static let textItem = NSToolbarItem.Identifier("text")
    private static let qrItem = NSToolbarItem.Identifier("qr")
    private static let backdropItemIdentifier = NSToolbarItem.Identifier("backdrop")
    private static let sizeItem = NSToolbarItem.Identifier("size")
    /// One toolbar item per tool, not one group of them: the toolbar draws hover and
    /// selection per item, so a group lit up as one block under the pointer.
    nonisolated private static func toolItem(_ tool: Tool) -> NSToolbarItem.Identifier {
        NSToolbarItem.Identifier("tool.\(tool.rawValue)")
    }

    private static let toolItems = Tool.allCases.map(toolItem)
    private static let colorItem = NSToolbarItem.Identifier("colour")

    init(document: Document, entry: LibraryEntry?, library: LibraryStore, screen: NSScreen?, title: String,
         preferences: @escaping () -> Preferences, onStylesChange: @escaping ([Tool: Style], String) -> Void,
         onPin: @escaping (CGImage, CGFloat, LibraryEntry?, Bool) -> Void, onBackdropChange: @escaping (Backdrop) -> Void,
         onMeasureChange: @escaping (MeasureSettings) -> Void, onShowsLayersChange: @escaping (Bool) -> Void,
         onClose: @escaping (EditorWindowController) -> Void) {
        self.preferences = preferences
        self.onStylesChange = onStylesChange
        self.onPin = onPin
        self.onBackdropChange = onBackdropChange
        self.onMeasureChange = onMeasureChange
        self.onShowsLayersChange = onShowsLayersChange
        self.onClose = onClose
        showsLayers = preferences().showsLayers
        self.entry = entry
        self.library = library
        keptDocument = document
        canvas = CanvasView(session: EditorSession(document: document, styles: preferences().styles,
                                                   colorHex: preferences().colorHex))

        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 800, height: 500),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        // Hidden in the title bar, but it names the window in the Window menu, the Dock
        // and Mission Control.
        window.title = title
        window.titleVisibility = .hidden
        window.toolbarStyle = .unifiedCompact
        // Wide enough for every toolbar button: narrower, and the last ones go into an
        // overflow menu, the library button first.
        window.minSize = NSSize(width: 1180, height: 280)
        // Captures and the library share one window as tabs, whatever the system's own
        // preference for tabs says.
        window.tabbingMode = .preferred
        window.tabbingIdentifier = Self.tabbingIdentifier
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self

        buildCanvas()
        buildToolbar()
        place(on: screen ?? NSScreen.main)
        settledCanvasSize = canvas.frame.size
        refreshToolbar()
    }

    required init?(coder: NSCoder) {
        fatalError("EditorWindowController is created in code only")
    }

    private func buildCanvas() {
        scrollView.contentView = CenteringClipView()
        scrollView.documentView = canvas
        scrollView.hasHorizontalScroller = true
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.allowsMagnification = true
        scrollView.minMagnification = 0.1
        scrollView.maxMagnification = 16
        scrollView.drawsBackground = true

        // The style bar floats over the canvas's top right corner, outside the scroll
        // view, so it stays put while the capture scrolls and zooms under it.
        let container = NSView()
        scrollView.autoresizingMask = [.width, .height]
        container.addSubview(scrollView)
        styleBar.autoresizingMask = [.minXMargin, .minYMargin]
        styleBar.isHidden = true
        container.addSubview(styleBar)
        layers.isHidden = true
        container.addSubview(layers)
        textHint.isHidden = true
        container.addSubview(textHint)
        buildRail()
        container.addSubview(rail)
        window?.contentView = container
        let bounds = container.bounds
        scrollView.frame = NSRect(x: 0, y: 0, width: bounds.width - Self.railWidth, height: bounds.height)
        rail.frame = NSRect(x: bounds.width - Self.railWidth, y: 0, width: Self.railWidth, height: bounds.height)
        wireLayers()
        styleBar.onChange = { [weak self] merging, change in self?.canvas.session.restyle(merging: merging, change) }
        // One write, for whichever the panel was showing: every tool restyle used to
        // rewrite the remembered backdrop as well.
        styleBar.onCommit = { [weak self] in
            guard let self else { return }
            if self.panel?.kind == .backdrop, let backdrop = self.canvas.session.display.backdrop {
                self.onBackdropChange(backdrop)
            } else {
                self.rememberStyles()
            }
        }
        styleBar.onBackdrop = { [weak self] merging, backdrop in
            guard let self else { return }
            self.canvas.session.setBackdrop(backdrop, merging: merging)
            if !merging, let backdrop { self.onBackdropChange(backdrop) }
        }
        styleBar.readWallpaper = { [weak self] in WallpaperReader.softened(for: self?.window?.screen) }

        canvas.onChange = { [weak self] in
            self?.followCanvasSize()
            self?.refreshToolbar()
            self?.documentChanged()
        }
        canvas.onStylesCommitted = { [weak self] in self?.rememberStyles() }
        canvas.measure = preferences().measure
        canvas.onMeasureChange = { [weak self] settings in self?.measureChanged(settings) }
        styleBar.onMeasure = { [weak self] settings in
            self?.canvas.measure = settings
            self?.measureChanged(settings)
        }
        styleBar.onMeasureHelp = { [weak self] in self?.showMeasureGuide() }
        styleBar.onSize = { [weak self] request in
            guard let self else { return }
            let document = self.canvas.session.display
            switch request {
            case let .fraction(fraction): self.canvas.session.setResize(fraction)
            case let .width(pixels): self.canvas.session.setResize(document.resize(forWidth: pixels))
            case let .height(pixels): self.canvas.session.setResize(document.resize(forHeight: pixels))
            }
            self.window?.makeFirstResponder(self.canvas)
        }
        styleBar.recentColors = { [weak self] in self?.preferences().recentColors ?? [] }
        styleBar.onRedact = { [weak self] target in self?.redact(target) }
        styleBar.onStepStart = { [weak self] start in
            guard let self else { return }
            self.canvas.session.setStepStart(start)
            self.window?.makeFirstResponder(self.canvas)
        }
        canvas.onPointerColor = { [weak self] hex in self?.showPointerColor(hex) }
        canvas.onCopyColor = { [weak self] in self?.copyPointerColor() }
        canvas.onClose = { [weak self] in self?.window?.performClose(nil) }
        canvas.onPickImage = { [weak self] in self?.pickImage() }

        magnifyObserver = NotificationCenter.default.addObserver(forName: NSScrollView.didEndLiveMagnifyNotification,
                                                                 object: scrollView, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.magnificationChanged() }
        }
    }

    /// Room round the canvas when it opens and on Zoom to Fit, so it never meets the
    /// window's edges.
    private static let fitMargin: CGFloat = 24

    /// At 100% when it fits, shrunk to fit the display it was captured on when it does not.
    /// Sized from the canvas, so a capture reopened with a backdrop fits with it. Opening
    /// centres the window on the display; a refit keeps it centred where it was, on screen.
    private func place(on screen: NSScreen?, around centre: NSPoint? = nil) {
        guard let window, let visible = screen?.visibleFrame else { return }
        let toolbarHeight: CGFloat = 40
        let margins = Self.fitMargin * 2
        let available = NSSize(width: visible.width * 0.9 - margins - Self.railWidth,
                               height: visible.height * 0.9 - toolbarHeight - margins)
        let size = canvas.frame.size
        let fit = min(1, available.width / size.width, available.height / size.height)
        let content = NSSize(width: max(size.width * fit + margins + Self.railWidth, window.minSize.width),
                             height: max(size.height * fit + margins, window.minSize.height - toolbarHeight))
        window.setContentSize(content)
        let middle = centre ?? NSPoint(x: visible.midX, y: visible.midY)
        let frame = window.frame
        window.setFrameOrigin(NSPoint(x: min(max(middle.x - frame.width / 2, visible.minX), visible.maxX - frame.width),
                                      y: min(max(middle.y - frame.height / 2, visible.minY), visible.maxY - frame.height)))
        scrollView.magnification = fit
        magnificationChanged()
    }

    /// A canvas that was wholly in view stays so when it changes size, once no drag or
    /// typing is under way: the window is fitted to it as on opening, or, once sized by
    /// hand, keeps its size while the zoom drops as far as it must. A canvas zoomed past
    /// the window is left as it is.
    private func followCanvasSize() {
        let size = canvas.frame.size
        guard canvas.session.phase == .idle, size != settledCanvasSize, let window else { return }
        let old = settledCanvasSize
        settledCanvasSize = size
        let view = scrollView.contentSize
        let magnification = scrollView.magnification
        guard old.width * magnification <= view.width + 0.5, old.height * magnification <= view.height + 0.5 else { return }
        // A window shared as tabs keeps its size: another capture's tab is showing in it too.
        guard sizedByHand || (window.tabbedWindows?.count ?? 0) > 1 else {
            place(on: window.screen, around: NSPoint(x: window.frame.midX, y: window.frame.midY))
            return
        }
        let margins = Self.fitMargin * 2
        let fit = min((view.width - margins) / size.width, (view.height - margins) / size.height)
        if fit < magnification { zoom(to: fit) }
    }

    override func showWindow(_ sender: Any?) {
        super.showWindow(sender)
        window?.makeFirstResponder(canvas)
    }

    // MARK: Toolbar

    private func buildToolbar() {
        let toolbar = NSToolbar(identifier: "TinysnapEditor")
        toolbar.delegate = self
        toolbar.displayMode = .iconOnly
        toolbar.allowsUserCustomization = false
        window?.toolbar = toolbar
    }

    /// The output buttons, then each group of tools, a divider before every group after
    /// the first.
    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        let groups = [[Self.copyItem, Self.saveItem, Self.dragItem, Self.textItem, Self.qrItem, Self.pinItem, Self.backdropItemIdentifier,
                       Self.sizeItem]]
            + Tool.toolbarGroups.map { $0.map(Self.toolItem) }
        let divided = groups.enumerated().flatMap { index, group in index == 0 ? group : [Self.divider(index)] + group }
        return divided + [.flexibleSpace, Self.colorItem]
    }

    /// One identifier each, so no two items in the toolbar share one.
    private static func divider(_ index: Int) -> NSToolbarItem.Identifier { NSToolbarItem.Identifier("divider \(index)") }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        toolbarDefaultItemIdentifiers(toolbar)
    }

    /// The toolbar itself marks the current tool, the way it marks a settings tab.
    /// Copy Text shows as selected while it waits, as a tool does.
    func toolbarSelectableItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        Self.toolItems + [Self.textItem]
    }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier identifier: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        if identifier.rawValue.hasPrefix("divider ") {
            let item = NSToolbarItem(itemIdentifier: identifier)
            item.view = ToolbarDivider()
            return item
        }
        if let tool = Tool.allCases.first(where: { Self.toolItem($0) == identifier }) {
            let item = NSToolbarItem(itemIdentifier: identifier)
            item.image = tool.symbol
            item.label = tool.title
            item.toolTip = tool.tooltip
            item.target = self
            item.action = #selector(toolPicked(_:))
            item.isBordered = true
            return item
        }
        switch identifier {
        case Self.copyItem:
            return button(identifier, symbol: "doc.on.doc", tooltip: "Copy (⌘C)", action: #selector(copy(_:)))
        case Self.saveItem:
            return button(identifier, symbol: "square.and.arrow.down", tooltip: "Save to the save folder (⌘S)", action: #selector(saveImage(_:)))
        case Self.dragItem:
            let item = NSToolbarItem(itemIdentifier: identifier)
            let handle = DragOutView()
            handle.toolTip = "Drag the capture into another app"
            handle.makeFile = { [weak self] in self?.writeTemporaryFile() }
            handle.onDropped = { [weak self] in self?.canvas.session.markSaved() }
            item.view = handle
            // The toolbar reads this label to accessibility, not the view's own.
            item.label = "Drag out the capture"
            return item
        case Self.textItem:
            return button(identifier, symbol: "text.viewfinder", tooltip: "Copy the text in an area (⌘⇧C)", action: #selector(copyText(_:)))
        case Self.qrItem:
            return button(identifier, symbol: "qrcode.viewfinder", tooltip: "Scan a QR code (⌘⇧R)", action: #selector(scanQRCode(_:)))
        case Self.backdropItemIdentifier:
            let item = button(identifier, symbol: "rectangle.dashed", tooltip: "Backdrop", action: #selector(showBackdropPanel(_:)))
            backdropItem = item
            return item
        case Self.sizeItem:
            return button(identifier, symbol: "square.resize", tooltip: "Export size", action: #selector(showSizePanel(_:)))
        case Self.pinItem:
            return button(identifier, symbol: "pin", tooltip: "Pin on top of every app and close (⌘P)", action: #selector(pinImage(_:)))
        case Self.colorItem:
            colorWell.wantsLayer = true
            colorWell.layer?.cornerRadius = 6
            colorWell.layer?.borderWidth = 0.5
            colorWell.layer?.borderColor = NSColor.separatorColor.cgColor
            colorWell.widthAnchor.constraint(equalToConstant: 20).isActive = true
            colorWell.heightAnchor.constraint(equalToConstant: 20).isActive = true
            let stack = NSStackView(views: [colorWell, readout(colorLabel, caption: "Tab to copy")])
            stack.spacing = 8
            return infoItem(identifier, view: stack, label: "Colour under the pointer")
        default:
            return nil
        }
    }

    /// A value over its caption.
    private func readout(_ value: NSTextField, caption: String) -> NSStackView {
        // Sized to fit two lines inside the compact toolbar; at 12 over 10 the top was cut.
        value.font = .monospacedDigitSystemFont(ofSize: 11, weight: .semibold)
        value.textColor = .labelColor
        let note = NSTextField(labelWithString: caption)
        note.font = .systemFont(ofSize: 9)
        note.textColor = .secondaryLabelColor
        let stack = NSStackView(views: [value, note])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 0
        return stack
    }

    private func infoItem(_ identifier: NSToolbarItem.Identifier, view: NSView, label: String) -> NSToolbarItem {
        let item = NSToolbarItem(itemIdentifier: identifier)
        item.view = view
        item.label = label
        return item
    }

    private func button(_ identifier: NSToolbarItem.Identifier, symbol: String, tooltip: String, action: Selector) -> NSToolbarItem {
        let item = NSToolbarItem(itemIdentifier: identifier)
        item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: tooltip)
        item.toolTip = tooltip
        item.label = tooltip
        item.target = self
        item.action = action
        item.isBordered = true
        return item
    }

    @objc private func toolPicked(_ sender: NSToolbarItem) {
        guard let tool = Tool.allCases.first(where: { Self.toolItem($0) == sender.itemIdentifier }) else { return }
        canvas.choose(tool)
        window?.makeFirstResponder(canvas)
    }

    /// The tool, and the style of whatever the style bar would change: the selection, or
    /// the tool's own for the next annotation.
    private var styleTarget: (tool: Tool, style: Style) {
        if let selected = canvas.session.selectedAnnotation { return (selected.tool, selected.style) }
        let tool = canvas.session.tool
        return (tool, canvas.session.style(for: tool))
    }

    private func refreshToolbar() {
        // Last, once the style bar is placed, since the panel sits under it.
        defer { refreshLayers() }
        let session = canvas.session
        window?.toolbar?.selectedItemIdentifier = canvas.isPickingText ? Self.textItem : Self.toolItem(session.tool)
        // Filled while a backdrop is on, dashed while there is none.
        backdropItem?.image = NSImage(systemSymbolName: session.display.backdrop == nil ? "rectangle.dashed" : "rectangle.inset.filled",
                                      accessibilityDescription: "Backdrop")
        if let panel, panel.tool != session.tool || panel.selection != session.selection { self.panel = nil }
        if let panel {
            styleBar.isHidden = false
            switch panel.kind {
            case .backdrop:
                styleBar.showBackdrop(session.display.backdrop, remembered: preferences().backdrop)
            case .size:
                let fraction = Exporter.outputScale(of: session.display, setting: preferences().exportScale)
                styleBar.showSize(fraction: fraction, pixels: session.display.exportPixelSize(at: fraction))
            }
            placeStyleBar()
            return
        }
        let target = styleTarget
        // Shown only for a tool with something to set. Deleting is the layers panel's bin.
        styleBar.isHidden = !StyleBar.shows(target.tool) || session.phase != .idle && session.typingID == nil
        if !styleBar.isHidden {
            styleBar.show(tool: target.tool, style: target.style, measure: canvas.measure,
                          stepStart: session.display.stepStart, locked: session.selectedAnnotation?.isLocked == true)
            placeStyleBar()
        }
        // The first time the Measure tool is picked, its guide opens from its button.
        if session.tool == .measure, lastTool != .measure, !canvas.measure.guideSeen {
            DispatchQueue.main.async { [weak self] in self?.showMeasureGuide() }
        }
        lastTool = session.tool
    }

    /// Settings, or another editor, changed what this one shows: the Size panel's starting
    /// size, and the Measure tool's lines, edge contrast and whether its guide was seen.
    func preferencesChanged() {
        canvas.measure = preferences().measure
        refreshToolbar()
    }

    private func measureChanged(_ settings: MeasureSettings) {
        refreshToolbar()
        onMeasureChange(settings)
    }

    /// From the Measure button, where the eye already is.
    private func showMeasureGuide() {
        guard measureGuide == nil,
              let item = window?.toolbar?.items.first(where: { $0.itemIdentifier == Self.toolItem(.measure) }) else { return }
        let popover = NSPopover()
        popover.behavior = .transient
        popover.contentViewController = MeasureGuideController { [weak popover] in popover?.close() }
        NotificationCenter.default.addObserver(forName: NSPopover.didCloseNotification, object: popover, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.measureGuideClosed() }
        }
        measureGuide = popover
        popover.show(relativeTo: item)
    }

    /// Closed any way at all, it is not shown on its own again; the ? still opens it.
    private func measureGuideClosed() {
        measureGuide = nil
        guard !canvas.measure.guideSeen else { return }
        canvas.measure.guideSeen = true
        onMeasureChange(canvas.measure)
    }

    /// 12 points in from the canvas's top right corner, left of the rail.
    private func placeStyleBar() {
        guard let bounds = window?.contentView?.bounds else { return }
        styleBar.setFrameOrigin(NSPoint(x: bounds.maxX - Self.railWidth - styleBar.frame.width - 12,
                                        y: bounds.maxY - styleBar.frame.height - 12))
    }

    // MARK: Layers

    /// Library on top, Layers under it, below a divider down the rail's left edge.
    private func buildRail() {
        rail.autoresizingMask = [.minXMargin, .height]
        let divider = NSBox()
        divider.boxType = .separator
        divider.frame = NSRect(x: 0, y: 0, width: 1, height: 10)
        divider.autoresizingMask = [.height]
        rail.addSubview(divider)
        for button in [libraryButton, layersButton] {
            button.autoresizingMask = [.minYMargin]
            rail.addSubview(button)
        }
    }

    /// Once, as the window is built; the buttons keep to the rail's top as it resizes.
    private func placeRailButtons() {
        let height = rail.bounds.height
        rail.subviews.first?.frame = NSRect(x: 0, y: 0, width: 1, height: height)
        libraryButton.frame = NSRect(x: 4, y: height - 12 - 32, width: 32, height: 32)
        layersButton.frame = NSRect(x: 4, y: height - 12 - 32 - 8 - 32, width: 32, height: 32)
    }

    private func wireLayers() {
        placeRailButtons()
        layers.onSelect = { [weak self] id in self?.canvas.session.select(id) }
        layers.onDragTo = { [weak self] id, index in self?.canvas.session.dragLayer(id, to: index) }
        layers.onDrop = { [weak self] in self?.canvas.session.dropLayer() }
        layers.onDragCancel = { [weak self] in self?.canvas.session.cancelLayerDrag() }
        // Space in the list keeps the keys there; a click on a row button hands them back.
        layers.onHide = { [weak self] id, hidden in self?.canvas.session.setHidden(id, hidden) }
        layers.onLock = { [weak self] id, locked in self?.canvas.session.setLocked(id, locked) }
        layers.onDuplicate = { [weak self] id in
            self?.canvas.session.select(id)
            self?.canvas.session.duplicateSelection()
        }
        layers.onDelete = { [weak self] in self?.canvas.session.deleteSelection() }
        layers.onDeleteRow = { [weak self] id in
            self?.canvas.session.select(id)
            self?.canvas.session.deleteSelection()
        }
        layers.onUndo = { [weak self] in self?.canvas.undo(nil) }
        layers.onRedo = { [weak self] in self?.canvas.redo(nil) }
        layers.onHover = { [weak self] id in self?.canvas.highlight(id) }
        layers.onClicked = { [weak self] in self?.handKeysToCanvas() }
        layers.onClose = { [weak self] in self?.toggleLayers(nil) }
    }

    private func handKeysToCanvas() {
        window?.makeFirstResponder(canvas)
    }

    private func refreshLayers() {
        layersButton.isOn = showsLayers
        layers.isHidden = !showsLayers
        guard showsLayers, let bounds = window?.contentView?.bounds else { return }
        layers.show(canvas.session.display, selection: canvas.session.selection)
        // Always under the style bar's place, shown or not: following it made the panel jump
        // down under the pointer whenever a click on a row brought the style bar up.
        let top = bounds.maxY - 12 - max(styleBar.frame.height, Self.styleBarHeight) - 8
        let height = max(min(layers.fittingHeight, top - 12), 80)
        layers.frame = NSRect(x: bounds.maxX - Self.railWidth - 12 - LayersPanel.width, y: top - height,
                              width: LayersPanel.width, height: height)
    }

    /// Opens or closes the layers panel, and remembers which for the next editor. Opened
    /// from the menu or its shortcut, the list takes the keys, for the arrows, Space and Esc;
    /// from the rail, they stay with the canvas.
    @objc func toggleLayers(_ sender: Any?) {
        showsLayers.toggle()
        onShowsLayersChange(showsLayers)
        refreshLayers()
        if showsLayers, sender is NSMenuItem { layers.focusList() } else { handKeysToCanvas() }
    }

    /// The Layers item is ticked while the panel shows.
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        if menuItem.action == #selector(toggleLayers(_:)) { menuItem.state = showsLayers ? .on : .off }
        return true
    }

    /// While the layers list has the keys, the canvas still answers the editing items:
    /// Delete, the arrange items, Lock and Duplicate.
    override func supplementalTarget(forAction action: Selector, sender: Any?) -> Any? {
        canvas.responds(to: action) ? canvas : super.supplementalTarget(forAction: action, sender: sender)
    }

    /// The panel shows the backdrop until another tool is picked or something selected.
    /// The tool stays as it is, the crop tool too, where the backdrop shows once another
    /// tool is picked.
    @objc func showBackdropPanel(_ sender: Any?) {
        panel = (.backdrop, canvas.session.tool, canvas.session.selection)
        refreshToolbar()
    }

    /// The panel shows the export size until another tool is picked or something selected.
    @objc func showSizePanel(_ sender: Any?) {
        panel = (.size, canvas.session.tool, canvas.session.selection)
        refreshToolbar()
    }

    // MARK: Redact

    /// Reads the capture's words off the main thread, then covers each match with an erase
    /// box, all as one undo step, and says how many.
    private func redact(_ target: RedactTarget) {
        let image = canvas.session.display.capture.image
        let screen = window?.screen
        let (one, many) = switch target {
        case .emails: ("email", "emails")
        case .phones: ("phone number", "phone numbers")
        case .numbers: ("number", "numbers")
        case .allText: ("line of text", "lines of text")
        }
        TextCopy.say("Finding \(many)…", on: screen, working: true)
        Task { @MainActor [weak self] in
            let lines = await Task.detached(priority: .userInitiated) { try? TextReader.lines(in: image) }.value
            guard let self else { return }
            // A read that failed says so: "No emails found" would vouch for a capture nobody read.
            guard let lines else {
                TextCopy.say("Could not read text", on: screen)
                return
            }
            let boxes = TextRedaction.boxes(in: lines, for: target)
            let added = self.canvas.session.redact(boxes)
            TextCopy.say(boxes.isEmpty ? "No \(many) found"
                         : added == 0 ? "Already erased"
                         : "Erased \(added) \(added == 1 ? one : many)", on: screen)
        }
    }

    /// Written when a change is finished, not on every tick of the colour panel, each of
    /// which would rewrite preferences.json.
    private func rememberStyles() {
        onStylesChange(canvas.session.styles, canvas.session.colorHex)
    }

    // MARK: Library

    /// An edit is written one second after the last one, so a burst of nudges or a colour
    /// drag writes once, and a crash loses at most that second.
    private func documentChanged() {
        guard entry != nil, canvas.session.history.document != keptDocument else { return }
        pendingKeep?.cancel()
        pendingKeep = Task { [weak self] in
            try? await Task.sleep(for: .seconds(1))
            guard !Task.isCancelled else { return }
            self?.keep(renderingImage: false)
        }
    }

    /// Writes the edits now, and with `renderingImage` the image too, which Quick Look
    /// and a drag from the library show. The image is rendered on save, quit and when the
    /// library asks, not on every edit, because a Retina capture takes a moment to encode;
    /// on close the app renders it off the main thread instead (`pendingRender`).
    ///
    /// It never ends typing: the timer fires mid-word, and every caller that needs the
    /// text committed ends it first. False when the library could not be written, so
    /// closing and quitting ask instead of trusting a copy that is not there.
    @discardableResult
    func keep(renderingImage: Bool) -> Bool {
        pendingKeep?.cancel()
        pendingKeep = nil
        guard let entry else { return true }
        let document = canvas.session.history.document
        do {
            if document != keptDocument {
                try library.saveEdits(document, to: entry)
                keptDocument = document
            }
            if renderingImage, document != renderedDocument {
                try library.saveImage(document, to: entry)
                renderedDocument = document
                // Posted after this turn, so a library drag that asked for the render
                // is under way before the grid reloads.
                DispatchQueue.main.async { NotificationCenter.default.post(name: .libraryChanged, object: nil) }
            }
            return true
        } catch {
            return false
        }
    }

    // MARK: Colour readout

    private func showPointerColor(_ hex: String?) {
        guard let hex else { return }
        pointerColor = hex
        colorLabel.stringValue = String(hex.dropFirst())
        colorWell.layer?.backgroundColor = Palette.color(hex: hex)
    }

    /// Tab copies the hex under the pointer, from any tool.
    private func copyPointerColor() {
        guard let hex = pointerColor else {
            NSSound.beep()
            return
        }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(hex, forType: .string)
        colorLabel.stringValue = "Copied"
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in
            guard let self, let hex = self.pointerColor else { return }
            self.colorLabel.stringValue = String(hex.dropFirst())
        }
    }

    // MARK: Zoom

    /// Handles and borders are drawn a fixed number of screen points, so a new zoom
    /// draws them again.
    private func magnificationChanged() {
        canvas.needsDisplay = true
    }

    private func zoom(to magnification: CGFloat) {
        let clamped = min(max(magnification, scrollView.minMagnification), scrollView.maxMagnification)
        scrollView.setMagnification(clamped, centeredAt: NSPoint(x: canvas.bounds.midX, y: canvas.bounds.midY))
        magnificationChanged()
    }

    @objc func zoomIn(_ sender: Any?) { zoom(to: scrollView.magnification * 1.25) }
    @objc func zoomOut(_ sender: Any?) { zoom(to: scrollView.magnification / 1.25) }
    @objc func zoomToActualSize(_ sender: Any?) { zoom(to: 1) }

    /// Joined as a tab: the window keeps the size it has, at least as wide as the toolbar
    /// needs, and the capture shrinks to fit it. A smaller one stays at 100%, as in a window
    /// of its own: enlarged, its pixels would show as squares and its shapes as steps.
    func joinedTabs() {
        sizedByHand = true
        guard let window else { return }
        if window.frame.width < window.minSize.width {
            var frame = window.frame
            frame.size.width = window.minSize.width
            window.setFrame(frame, display: true)
        }
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            zoom(to: min(1, fit))
        }
    }

    @objc func zoomToFit(_ sender: Any?) { zoom(to: fit) }

    /// The zoom that shows the whole canvas in the window, with its margin.
    private var fit: CGFloat {
        let margins = Self.fitMargin * 2
        let visible = scrollView.contentSize
        let size = canvas.bounds.size
        return min((visible.width - margins) / size.width, (visible.height - margins) / size.height)
    }

    // MARK: Output

    private func exportedPNG() -> (ExportedImage, Data)? {
        canvas.finishTyping()
        guard let exported = Exporter.export(canvas.session.display, scale: preferences().exportScale),
              let png = Exporter.pngData(exported) else { return nil }
        return (exported, png)
    }

    /// The image goes on the clipboard and the editor closes.
    @objc func copy(_ sender: Any?) {
        guard let (exported, png) = exportedPNG() else {
            showError("Tinysnap could not make an image from this capture.")
            return
        }
        Output.copy(exported, png: png)
        closeDone()
    }

    /// Pins the result and closes, as Copy does: at full resolution, or at the size the
    /// capture was given. The pin is where the capture lives now, and the library still
    /// has it editable.
    @objc func pinImage(_ sender: Any?) {
        canvas.finishTyping()
        let document = canvas.session.display
        guard let exported = Exporter.export(document, scale: .native) else {
            showError("Tinysnap could not make an image from this capture.")
            return
        }
        onPin(exported.image, exported.dpi / 72, entry, document.resize != nil)
        closeDone()
    }

    /// The capture went where it was sent, so the editor closes without asking; the library
    /// still keeps its edits as the window closes.
    private func closeDone() {
        canvas.session.markSaved()
        isClosingForGood = true
        window?.close()
    }

    /// Copy Text stays on, as a tool does: each drag over text copies that text and each
    /// click all of it, until Esc, another tool or Copy Text again. What is read is what
    /// an export holds, so text under a blur or an erase never is.
    @objc func copyText(_ sender: Any?) {
        guard !canvas.isPickingText else {
            canvas.stopPickingText()
            return
        }
        showTextHint(true)
        canvas.pickText { [weak self] pick in
            guard let self else { return }
            let area: CGRect?
            switch pick {
            case .stopped:
                self.showTextHint(false)
                self.refreshToolbar()
                return
            case .whole: area = nil
            case let .area(box): area = box
            }
            guard let image = Exporter.readingImage(self.canvas.session.display, in: area) else {
                NSSound.beep()
                return
            }
            TextCopy.read(image, for: .text, on: self.window?.screen)
        }
        refreshToolbar()
    }

    /// Reads every QR code in the capture and copies what they hold.
    @objc func scanQRCode(_ sender: Any?) {
        canvas.finishTyping()
        guard let image = Exporter.readingImage(canvas.session.display) else {
            NSSound.beep()
            return
        }
        TextCopy.read(image, for: .codes, on: window?.screen)
    }

    /// Says what Copy Text is waiting for, along the bottom of the canvas where the style
    /// panel never is.
    private let textHint: NSView = {
        let label = NSTextField(labelWithString: "Drag over text to copy it, or click to copy all of it. Esc to stop.")
        label.font = .systemFont(ofSize: 13, weight: .medium)
        let back = NSVisualEffectView()
        back.material = .popover
        back.blendingMode = .withinWindow
        back.state = .active
        back.wantsLayer = true
        back.layer?.cornerRadius = 10
        label.translatesAutoresizingMaskIntoConstraints = false
        back.addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: back.leadingAnchor, constant: 14),
            label.trailingAnchor.constraint(equalTo: back.trailingAnchor, constant: -14),
            label.topAnchor.constraint(equalTo: back.topAnchor, constant: 8),
            label.bottomAnchor.constraint(equalTo: back.bottomAnchor, constant: -8),
        ])
        back.setFrameSize(back.fittingSize)
        back.autoresizingMask = [.minXMargin, .maxXMargin, .maxYMargin]
        return back
    }()

    private func showTextHint(_ shown: Bool) {
        textHint.isHidden = !shown
        guard shown, let bounds = window?.contentView?.bounds else { return }
        // Centred on the canvas, not across the rail.
        textHint.setFrameOrigin(NSPoint(x: (bounds.width - Self.railWidth - textHint.frame.width) / 2, y: 16))
    }

    @objc func paste(_ sender: Any?) {
        guard let image = NSImage(pasteboard: NSPasteboard.general) else {
            NSSound.beep()
            return
        }
        canvas.insert(image)
    }

    @objc func saveImage(_ sender: Any?) {
        if saveToFolder() { closeDone() }
    }

    /// For quitting: ends any typing, then says whether there are edits to lose. An
    /// editor whose library entry took them has nothing to lose.
    func hasUnsavedEdits() -> Bool {
        canvas.finishTyping()
        if entry != nil, keep(renderingImage: true) { return false }
        return canvas.session.isUnsaved
    }

    /// For quitting: saves into the save folder. False, with an alert already up, when
    /// it could not.
    func saveBeforeQuitting() -> Bool {
        saveToFolder()
    }

    /// Saves into the save folder under a fresh name. False, with an alert already up,
    /// when it could not, and the capture stays open with nothing lost.
    private func saveToFolder() -> Bool {
        let folder = preferences().saveFolderURL
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            showError("The save folder \(folder.path) does not exist. Choose another in Settings.")
            return false
        }
        let name = FileNaming.fileName(for: Date()) { FileManager.default.fileExists(atPath: folder.appendingPathComponent($0).path) }
        return write(to: folder.appendingPathComponent(name))
    }

    @discardableResult
    private func write(to url: URL) -> Bool {
        guard let (_, png) = exportedPNG() else {
            showError("Tinysnap could not make an image from this capture.")
            return false
        }
        do {
            try png.write(to: url, options: .atomic)
            canvas.session.markSaved()
            keep(renderingImage: true)
            return true
        } catch {
            showError("Tinysnap could not save to \(url.deletingLastPathComponent().path). \(error.localizedDescription)")
            return false
        }
    }

    @objc func saveImageAs(_ sender: Any?) {
        guard let window else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        panel.nameFieldStringValue = FileNaming.fileName(for: Date()) { _ in false }
        panel.directoryURL = preferences().saveFolderURL
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url, let self, self.write(to: url) else { return }
            self.closeDone()
        }
    }

    private func writeTemporaryFile() -> URL? {
        guard let (_, png) = exportedPNG() else { return nil }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("Tinysnap", isDirectory: true)
        let url = folder.appendingPathComponent(FileNaming.fileName(for: Date()) { _ in false })
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try png.write(to: url, options: .atomic)
            return url
        } catch {
            return nil
        }
    }

    private func pickImage() {
        guard let window else { return }
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image]
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self else { return }
            // Cancelled, the image tool stays out: the tool changes only when picked.
            if response == .OK, let url = panel.url, let image = NSImage(contentsOf: url) {
                self.canvas.insert(image)
            }
        }
    }

    private func showError(_ message: String) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = message
        if let window { alert.beginSheetModal(for: window) } else { alert.runModal() }
    }

    // MARK: Closing

    /// Asks once when there are edits not yet copied, saved or dragged out, unless the
    /// library took them. When writing the library fails, it asks as it would with no
    /// library, so a full disk never loses the edits.
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        canvas.finishTyping()
        if isClosingForGood { return true }
        if entry != nil, keep(renderingImage: false) { return true }
        guard canvas.session.isUnsaved else { return true }

        let alert = NSAlert()
        alert.messageText = "Save this capture before closing?"
        alert.informativeText = entry == nil
            ? "Its annotations and crop are lost if you do not."
            : "Tinysnap could not keep it in the library, so its annotations and crop are lost if you do not."
        alert.addButton(withTitle: "Save")
        alert.addButton(withTitle: "Discard")
        alert.addButton(withTitle: "Cancel")
        alert.beginSheetModal(for: sender) { [weak self] response in
            guard let self else { return }
            switch response {
            case .alertFirstButtonReturn:
                guard self.saveToFolder() else { return }
            case .alertSecondButtonReturn:
                break
            default:
                return
            }
            self.isClosingForGood = true
            sender.close()
        }
        return false
    }

    func windowDidEndLiveResize(_ notification: Notification) {
        // The panel's height follows the window's, so no row is cut off below it.
        refreshLayers()
        sizedByHand = true
    }

    /// The entry's edits and document when its image is behind them, for the app to
    /// render once this editor has closed.
    func pendingRender() -> (document: Document, entry: LibraryEntry)? {
        let document = canvas.session.history.document
        guard let entry, document != renderedDocument else { return nil }
        return (document, entry)
    }

    func windowWillClose(_ notification: Notification) {
        keep(renderingImage: false)
        styleBar.closePalette()
        rememberStyles()
        if let magnifyObserver { NotificationCenter.default.removeObserver(magnifyObserver) }
        onClose(self)
    }
}
