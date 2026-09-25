import AppKit
import TinysnapCore
import UniformTypeIdentifiers

/// Keeps a smaller capture centred in the window instead of pinned to a corner.
final class CenteringClipView: NSClipView {
    override func constrainBoundsRect(_ proposedBounds: NSRect) -> NSRect {
        var rect = super.constrainBoundsRect(proposedBounds)
        guard let document = documentView else { return rect }
        if rect.width > document.frame.width { rect.origin.x = (document.frame.width - rect.width) / 2 }
        if rect.height > document.frame.height { rect.origin.y = (document.frame.height - rect.height) / 2 }
        return rect
    }
}

/// One window per capture: the canvas, one toolbar row in the title bar, and the
/// copy, save and drag out actions.
@MainActor
final class EditorWindowController: NSWindowController, NSWindowDelegate, NSToolbarDelegate {
    private let canvas: CanvasView
    private let scrollView = NSScrollView()
    private let preferences: () -> Preferences
    private let onStylesChange: ([Tool: Style], String) -> Void
    private let onClose: (EditorWindowController) -> Void

    private let styleBar = StyleBar()
    private let colorWell = NSView()
    private let colorLabel = NSTextField(labelWithString: "")
    private let sizeLabel = NSTextField(labelWithString: "")
    private let zoomLabel = NSTextField(labelWithString: "")
    /// The colour under the pointer, which Tab copies.
    private var pointerColor: String?
    private var magnifyObserver: NSObjectProtocol?
    private var isClosingForGood = false

    private static let copyItem = NSToolbarItem.Identifier("copy")
    private static let saveItem = NSToolbarItem.Identifier("save")
    private static let dragItem = NSToolbarItem.Identifier("drag")
    /// One toolbar item per tool, not one group of them: the toolbar draws hover and
    /// selection per item, so a group lit up as one block under the pointer.
    nonisolated private static func toolItem(_ tool: Tool) -> NSToolbarItem.Identifier {
        NSToolbarItem.Identifier("tool.\(tool.rawValue)")
    }

    private static let toolItems = Tool.allCases.map(toolItem)
    private static let colorItem = NSToolbarItem.Identifier("colour")
    private static let sizeItem = NSToolbarItem.Identifier("size")
    private static let zoomItem = NSToolbarItem.Identifier("zoom")

    init(capture: Capture, screen: NSScreen?, title: String, preferences: @escaping () -> Preferences,
         onStylesChange: @escaping ([Tool: Style], String) -> Void, onClose: @escaping (EditorWindowController) -> Void) {
        self.preferences = preferences
        self.onStylesChange = onStylesChange
        self.onClose = onClose
        canvas = CanvasView(session: EditorSession(document: Document(capture: capture), styles: preferences().styles,
                                                   colorHex: preferences().colorHex))

        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 800, height: 500),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        // Hidden in the title bar, but it names the window in the Window menu, the Dock
        // and Mission Control.
        window.title = title
        window.titleVisibility = .hidden
        window.toolbarStyle = .unifiedCompact
        window.minSize = NSSize(width: 1100, height: 280)
        // Every capture its own window. With tabbing left to macOS, a second capture
        // could land as a tab inside the first.
        window.tabbingMode = .disallowed
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self

        buildCanvas()
        buildToolbar()
        place(on: screen ?? NSScreen.main, capture: capture)
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
        scrollView.backgroundColor = .underPageBackgroundColor
        scrollView.drawsBackground = true

        // The style bar floats over the canvas's top right corner, outside the scroll
        // view, so it stays put while the capture scrolls and zooms under it.
        let container = NSView()
        scrollView.autoresizingMask = [.width, .height]
        container.addSubview(scrollView)
        styleBar.autoresizingMask = [.minXMargin, .minYMargin]
        styleBar.isHidden = true
        container.addSubview(styleBar)
        window?.contentView = container
        scrollView.frame = container.bounds
        styleBar.onChange = { [weak self] merging, change in self?.canvas.session.restyle(merging: merging, change) }
        styleBar.onCommit = { [weak self] in self?.rememberStyles() }

        canvas.onChange = { [weak self] in self?.refreshToolbar() }
        canvas.onStylesCommitted = { [weak self] in self?.rememberStyles() }
        canvas.onPointerColor = { [weak self] hex in self?.showPointerColor(hex) }
        canvas.onCopyColor = { [weak self] in self?.copyPointerColor() }
        canvas.onClose = { [weak self] in self?.window?.performClose(nil) }
        canvas.onPickImage = { [weak self] in self?.pickImage() }

        magnifyObserver = NotificationCenter.default.addObserver(forName: NSScrollView.didEndLiveMagnifyNotification,
                                                                 object: scrollView, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.magnificationChanged() }
        }
    }

    /// At 100% when it fits, shrunk to fit the display it was captured on when it does not.
    private func place(on screen: NSScreen?, capture: Capture) {
        guard let window, let visible = screen?.visibleFrame else { return }
        let toolbarHeight: CGFloat = 40
        let available = NSSize(width: visible.width * 0.9, height: visible.height * 0.9 - toolbarHeight)
        let size = capture.pointSize
        let fit = min(1, available.width / size.width, available.height / size.height)
        let content = NSSize(width: max(size.width * fit, window.minSize.width), height: max(size.height * fit, window.minSize.height - toolbarHeight))
        window.setContentSize(content)
        window.setFrameOrigin(NSPoint(x: visible.midX - window.frame.width / 2, y: visible.midY - window.frame.height / 2))
        scrollView.magnification = fit
        magnificationChanged()
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

    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        let tools = Tool.toolbarGroups.map { $0.map(Self.toolItem) }.joined(separator: [.space])
        return [Self.copyItem, Self.saveItem, Self.dragItem, .space] + tools
            + [.flexibleSpace, Self.colorItem, Self.sizeItem, Self.zoomItem]
    }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        toolbarDefaultItemIdentifiers(toolbar)
    }

    /// The toolbar itself marks the current tool, the way it marks a settings tab.
    func toolbarSelectableItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        Self.toolItems
    }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier identifier: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
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
            return button(identifier, symbol: "doc.on.doc", tooltip: "Copy and close (⌘C)", action: #selector(copy(_:)))
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
        case Self.sizeItem:
            return infoItem(identifier, view: readout(sizeLabel, caption: "Image size"), label: "Image size")
        case Self.zoomItem:
            return infoItem(identifier, view: readout(zoomLabel, caption: "Zoom"), label: "Zoom")
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
        window?.toolbar?.selectedItemIdentifier = Self.toolItem(canvas.session.tool)
        let target = styleTarget
        // Shown only for a tool with something to set, and placed 12 points in from the
        // canvas's top right corner.
        styleBar.isHidden = !StyleBar.shows(target.tool) || canvas.session.phase != .idle && canvas.session.typingID == nil
        if !styleBar.isHidden {
            styleBar.show(tool: target.tool, style: target.style)
            if let bounds = window?.contentView?.bounds {
                styleBar.setFrameOrigin(NSPoint(x: bounds.maxX - styleBar.frame.width - 12,
                                                y: bounds.maxY - styleBar.frame.height - 12))
            }
        }
        let pixels = canvas.session.display.outputRect.size
        sizeLabel.stringValue = "\(Int(pixels.width))×\(Int(pixels.height))px"
    }

    /// Written when a change is finished, not on every tick of the colour panel, each of
    /// which would rewrite preferences.json.
    private func rememberStyles() {
        onStylesChange(canvas.session.styles, canvas.session.colorHex)
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

    private func magnificationChanged() {
        zoomLabel.stringValue = "\(Int((scrollView.magnification * 100).rounded()))%"
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

    @objc func zoomToFit(_ sender: Any?) {
        let visible = scrollView.contentSize
        let size = canvas.bounds.size
        zoom(to: min(visible.width / size.width, visible.height / size.height))
    }

    // MARK: Output

    private func exportedPNG() -> (ExportedImage, Data)? {
        canvas.finishTyping()
        guard let exported = Exporter.export(canvas.session.display, scale: preferences().exportScale),
              let png = Exporter.pngData(exported) else { return nil }
        return (exported, png)
    }

    /// Copy is the one-keystroke way out: the image goes on the clipboard and the window closes.
    @objc func copy(_ sender: Any?) {
        guard let (exported, png) = exportedPNG() else {
            showError("Tinysnap could not make an image from this capture.")
            return
        }
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setData(png, forType: .png)
        // TIFF too, sized in points, for apps that read only that.
        let representation = NSBitmapImageRep(cgImage: exported.image)
        representation.size = exported.pointSize
        if let tiff = representation.tiffRepresentation { pasteboard.setData(tiff, forType: .tiff) }

        canvas.session.markSaved()
        isClosingForGood = true
        window?.close()
    }

    @objc func paste(_ sender: Any?) {
        guard let image = NSImage(pasteboard: NSPasteboard.general) else {
            NSSound.beep()
            return
        }
        canvas.insert(image)
    }

    @objc func saveImage(_ sender: Any?) {
        _ = saveToFolder()
    }

    /// For quitting: ends any typing, then says whether there are edits to lose.
    func hasUnsavedEdits() -> Bool {
        canvas.finishTyping()
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
            guard response == .OK, let url = panel.url else { return }
            self?.write(to: url)
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
            if response == .OK, let url = panel.url, let image = NSImage(contentsOf: url) {
                self.canvas.insert(image)
            } else {
                self.canvas.choose(.select)
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

    /// Asks once when there are edits not yet copied, saved or dragged out.
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        canvas.finishTyping()
        guard canvas.session.isUnsaved, !isClosingForGood else { return true }

        let alert = NSAlert()
        alert.messageText = "Save this capture before closing?"
        alert.informativeText = "Its annotations and crop are lost if you do not."
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

    func windowWillClose(_ notification: Notification) {
        styleBar.closePalette()
        rememberStyles()
        if let magnifyObserver { NotificationCenter.default.removeObserver(magnifyObserver) }
        onClose(self)
    }
}
