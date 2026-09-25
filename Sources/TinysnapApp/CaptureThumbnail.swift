import AppKit
import TinysnapCore

/// A capture floating in the bottom right corner of its screen instead of opening the
/// editor. Click edits it, dragging drops it into another app, the buttons copy, save
/// or pin it. Left alone, or swiped right, it slides away and lands on the clipboard.
@MainActor
final class CaptureThumbnail: NSObject {
    let document: Document
    let entry: LibraryEntry?
    private let preferences: () -> Preferences
    private let onOpen: (CaptureThumbnail) -> Void
    private let onPin: (CaptureThumbnail) -> Void
    private let onGone: (CaptureThumbnail) -> Void
    private let panel: NSPanel
    private let view: ThumbnailView
    private var timer: Timer?
    private var isGone = false
    /// The clipboard as it was when the thumbnail appeared. A time out copies only if it
    /// is still so, rather than replace text or an image copied since.
    private let pasteboardCount = NSPasteboard.general.changeCount

    init(document: Document, entry: LibraryEntry?, screen: NSScreen?, preferences: @escaping () -> Preferences,
         onOpen: @escaping (CaptureThumbnail) -> Void, onPin: @escaping (CaptureThumbnail) -> Void,
         onGone: @escaping (CaptureThumbnail) -> Void) {
        self.document = document
        self.entry = entry
        self.preferences = preferences
        self.onOpen = onOpen
        self.onPin = onPin
        self.onGone = onGone

        let points = document.capture.pointSize
        let fit = min(1, 240 / points.width, 240 / points.height)
        let size = NSSize(width: max(1, points.width * fit), height: max(1, points.height * fit))
        let image = Exporter.export(document, scale: .native).map { NSImage(cgImage: $0.image, size: size) }
        view = ThumbnailView(frame: NSRect(origin: .zero, size: size), image: image)
        panel = NSPanel(contentRect: NSRect(origin: .zero, size: size), styleMask: [.borderless, .nonactivatingPanel],
                        backing: .buffered, defer: false)
        super.init()

        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.isReleasedWhenClosed = false
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        view.owner = self
        panel.contentView = view

        let visible = (screen ?? NSScreen.main)?.visibleFrame ?? .zero
        let resting = NSPoint(x: visible.maxX - size.width - 20, y: visible.minY + 20)
        slide(from: NSPoint(x: visible.maxX + 20, y: resting.y), to: resting) {}
        startTimer()
    }

    // MARK: Leaving

    /// Every way out comes through here. `copying` is the time out, the swipe and a new
    /// capture, which copy only onto an unchanged clipboard; the close button, a click
    /// to edit and a drag out leave the clipboard alone.
    func dismiss(copying: Bool) {
        guard !isGone else { return }
        isGone = true
        timer?.invalidate()
        if copying, NSPasteboard.general.changeCount == pasteboardCount { copyNow() }
        let frame = panel.frame
        slide(from: frame.origin, to: NSPoint(x: frame.minX + frame.width + 40, y: frame.minY)) { [weak self] in
            guard let self else { return }
            self.panel.orderOut(nil)
            self.onGone(self)
        }
    }

    private func slide(from start: NSPoint, to end: NSPoint, then done: @escaping @MainActor () -> Void) {
        panel.setFrameOrigin(start)
        panel.orderFrontRegardless()
        guard !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else {
            panel.setFrameOrigin(end)
            done()
            return
        }
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.25
            panel.animator().setFrameOrigin(end)
        }, completionHandler: {
            MainActor.assumeIsolated { done() }
        })
    }

    private func startTimer() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.dismiss(copying: true) }
        }
    }

    // MARK: From the view

    func hover(_ inside: Bool) {
        if inside {
            timer?.invalidate()
        } else if !isGone {
            startTimer()
        }
    }

    func open() {
        onOpen(self)
        dismiss(copying: false)
    }

    private func copyNow() {
        guard let exported = Exporter.export(document, scale: preferences().exportScale),
              let png = Exporter.pngData(exported) else { return }
        Output.copy(exported, png: png)
    }

    /// Asked for, so it copies whatever the clipboard holds now.
    @objc func copyImage() {
        copyNow()
        dismiss(copying: false)
    }

    @objc func saveImage() {
        guard let exported = Exporter.export(document, scale: preferences().exportScale),
              let png = Exporter.pngData(exported) else { return }
        do {
            try Output.save(png, in: preferences().saveFolderURL)
            dismiss(copying: false)
        } catch {
            Output.show(error, over: nil)
        }
    }

    @objc func pinImage() {
        onPin(self)
        dismiss(copying: false)
    }

    @objc func close() {
        dismiss(copying: false)
    }

    /// The library's image when there is one, otherwise a temporary PNG, since other
    /// apps take a dragged file more readily than raw image data.
    func fileForDragging() -> URL? {
        if let entry { return entry.imageURL }
        guard let exported = Exporter.export(document, scale: preferences().exportScale),
              let png = Exporter.pngData(exported) else { return nil }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("Tinysnap", isDirectory: true)
        let url = folder.appendingPathComponent(FileNaming.fileName(for: Date()) { _ in false })
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return (try? png.write(to: url, options: .atomic)) == nil ? nil : url
    }
}

/// The picture, with Copy, Save, Pin and a close button over it while the pointer is on it.
final class ThumbnailView: NSView, NSDraggingSource {
    /// The buttons act on the thumbnail, which is in no responder chain, so each targets
    /// it directly. Left without a target, the close button fell through to whichever
    /// Tinysnap window was key and closed that instead.
    weak var owner: CaptureThumbnail? {
        didSet {
            for case let button as NSButton in actions.arrangedSubviews + [closeButton] { button.target = owner }
        }
    }
    private let picture = NSImageView()
    private let actions = NSStackView()
    private let closeButton = FirstClickButton()
    private var pressed: NSPoint?
    private var swipe: CGFloat = 0

    init(frame: NSRect, image: NSImage?) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.cornerRadius = 8
        layer?.masksToBounds = true
        layer?.borderWidth = 0.5
        layer?.borderColor = NSColor.white.withAlphaComponent(0.4).cgColor

        picture.image = image
        picture.imageScaling = .scaleAxesIndependently
        picture.frame = bounds
        picture.autoresizingMask = [.width, .height]
        picture.unregisterDraggedTypes()
        addSubview(picture)

        for (title, action) in [("Copy", #selector(CaptureThumbnail.copyImage)), ("Save", #selector(CaptureThumbnail.saveImage)),
                                ("Pin", #selector(CaptureThumbnail.pinImage))] {
            let button = FirstClickButton(title: title, target: nil, action: action)
            button.bezelStyle = .rounded
            button.controlSize = .small
            actions.addArrangedSubview(button)
        }
        actions.spacing = 6
        actions.isHidden = true
        actions.translatesAutoresizingMaskIntoConstraints = false
        addSubview(actions)

        closeButton.image = NSImage(systemSymbolName: "xmark.circle.fill", accessibilityDescription: "Close without copying")
        closeButton.isBordered = false
        closeButton.action = #selector(CaptureThumbnail.close)
        closeButton.isHidden = true
        closeButton.translatesAutoresizingMaskIntoConstraints = false
        addSubview(closeButton)

        NSLayoutConstraint.activate([
            actions.centerXAnchor.constraint(equalTo: centerXAnchor),
            actions.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -8),
            closeButton.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 6),
            closeButton.topAnchor.constraint(equalTo: topAnchor, constant: 6),
        ])
        setAccessibilityElement(true)
        setAccessibilityRole(.button)
        setAccessibilityLabel("Capture thumbnail")
        setAccessibilityHelp("Click to edit, drag into another app, or wait to copy it")
    }

    required init?(coder: NSCoder) {
        fatalError("ThumbnailView is created in code only")
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                                       owner: self, userInfo: nil))
    }

    override func mouseEntered(with event: NSEvent) {
        actions.isHidden = false
        closeButton.isHidden = false
        owner?.hover(true)
    }

    override func mouseExited(with event: NSEvent) {
        actions.isHidden = true
        closeButton.isHidden = true
        owner?.hover(false)
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        pressed = event.locationInWindow
    }

    /// A few points of movement makes it a drag out rather than a click.
    override func mouseDragged(with event: NSEvent) {
        guard let start = pressed, hypot(event.locationInWindow.x - start.x, event.locationInWindow.y - start.y) > 4 else { return }
        pressed = nil
        guard let url = owner?.fileForDragging() else { return }
        let item = NSDraggingItem(pasteboardWriter: url as NSURL)
        item.setDraggingFrame(bounds, contents: picture.image)
        beginDraggingSession(with: [item], event: event, source: self)
    }

    override func mouseUp(with event: NSEvent) {
        guard pressed != nil else { return }
        pressed = nil
        owner?.open()
    }

    /// Two fingers to the right sends it away, as with the macOS thumbnail.
    override func scrollWheel(with event: NSEvent) {
        let rightward = event.isDirectionInvertedFromDevice ? event.scrollingDeltaX : -event.scrollingDeltaX
        swipe = max(0, swipe + rightward)
        if swipe > 30 { owner?.dismiss(copying: true) }
    }

    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        .copy
    }

    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) {
        if !operation.isEmpty { owner?.dismiss(copying: false) }
    }
}
