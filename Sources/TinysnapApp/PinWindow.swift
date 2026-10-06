import AppKit
import TinysnapCore

/// Borderless panels refuse to become key unless told otherwise, and a pin needs the
/// keys: Escape, the opacity digits, Command C, Command S and Command W.
final class PinPanel: NSPanel {
    override var canBecomeKey: Bool { true }
}

/// One capture floating above every app. It never activates Tinysnap, so pinning a
/// reference leaves the app being worked in at the front.
@MainActor
final class PinWindowController: NSWindowController, NSWindowDelegate {
    /// The library entry it came from, so double-click can reopen it editable.
    let entry: LibraryEntry?
    let image: CGImage
    let scale: CGFloat
    /// Drawn at a size the capture was given, which copy and save keep rather than
    /// taking the Export setting's.
    private let keepsSize: Bool
    private let preferences: () -> Preferences
    private let onOpen: (PinWindowController) -> Void
    private let onClose: (PinWindowController) -> Void

    init(image: CGImage, scale: CGFloat, entry: LibraryEntry?, keepsSize: Bool, preferences: @escaping () -> Preferences,
         onOpen: @escaping (PinWindowController) -> Void, onClose: @escaping (PinWindowController) -> Void) {
        self.image = image
        self.scale = scale
        self.keepsSize = keepsSize
        self.entry = entry
        self.preferences = preferences
        self.onOpen = onOpen
        self.onClose = onClose

        let panel = PinPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.hasShadow = true
        panel.backgroundColor = .clear
        panel.isOpaque = false
        super.init(window: panel)
        panel.delegate = self

        let view = PinView(image: NSImage(cgImage: image, size: NSSize(width: CGFloat(image.width) / scale,
                                                                          height: CGFloat(image.height) / scale)))
        view.controller = self
        panel.contentView = view
        place(panel)
    }

    required init?(coder: NSCoder) {
        fatalError("PinWindowController is created in code only")
    }

    /// At the capture's point size, no larger than 80% of the screen with the pointer,
    /// centred on it.
    private func place(_ panel: NSPanel) {
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        guard let visible = screen?.visibleFrame else { return }
        let size = CGSize(width: CGFloat(image.width) / scale, height: CGFloat(image.height) / scale)
        let fit = min(1, visible.width * 0.8 / size.width, visible.height * 0.8 / size.height)
        let frame = NSRect(x: visible.midX - size.width * fit / 2, y: visible.midY - size.height * fit / 2,
                           width: size.width * fit, height: size.height * fit)
        panel.setFrame(frame, display: false)
    }

    override func showWindow(_ sender: Any?) {
        window?.orderFrontRegardless()
        window?.makeKey()
    }

    // MARK: Actions

    /// 1 to 9 for 10% to 90%, 0 for solid. A see-through pin has no shadow, so it lines up
    /// cleanly with whatever it is compared against.
    func setOpacity(tenths: Int) {
        let alpha = tenths == 0 ? 1 : CGFloat(tenths) / 10
        window?.alphaValue = alpha
        window?.hasShadow = alpha == 1
    }

    private var exportScale: ExportScale { keepsSize ? .native : preferences().exportScale }

    @objc func copyImage(_ sender: Any?) {
        guard let (exported, png) = Output.exported(image, scale: scale, as: exportScale) else { return }
        Output.copy(exported, png: png)
    }

    @objc func saveImage(_ sender: Any?) {
        guard let (_, png) = Output.exported(image, scale: scale, as: exportScale) else { return }
        do {
            try Output.save(png, in: preferences().saveFolderURL)
        } catch {
            Output.show(error, over: nil)
        }
    }

    @objc func openInEditor(_ sender: Any?) {
        onOpen(self)
    }

    @objc func closePin(_ sender: Any?) {
        close()
    }

    func windowWillClose(_ notification: Notification) {
        onClose(self)
    }
}

/// The image, and every way of handling it: drag to move, scroll to resize around the
/// pointer, digits for opacity, double-click to edit, right-click for the rest.
final class PinView: NSImageView {
    weak var controller: PinWindowController?

    init(image: NSImage) {
        super.init(frame: .zero)
        self.image = image
        imageScaling = .scaleAxesIndependently
        setAccessibilityLabel("Pinned capture")
        setAccessibilityHelp("Drag to move, scroll to resize, 1 to 9 for opacity, Escape to close")
    }

    required init?(coder: NSCoder) {
        fatalError("PinView is created in code only")
    }

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 {
            controller?.openInEditor(nil)
            return
        }
        window?.performDrag(with: event)
    }

    /// Resizes around the pointer, keeping the shape, between 64 points on the short
    /// side and the screen. A wheel moves 10% a notch whatever size of delta it reports,
    /// which varies with its acceleration; a trackpad follows the fingers.
    override func scrollWheel(with event: NSEvent) {
        guard let window, let screen = window.screen?.visibleFrame, event.scrollingDeltaY != 0 else { return }
        let factor = event.hasPreciseScrollingDeltas ? pow(1.005, event.scrollingDeltaY) : event.scrollingDeltaY > 0 ? 1.1 : 1 / 1.1
        let frame = window.frame
        let aspect = frame.height / frame.width
        var width = frame.width * factor
        width = min(width, screen.width, screen.height / aspect)
        width = max(width, 64 / min(1, aspect))
        let height = width * aspect
        let pointer = NSEvent.mouseLocation
        let fx = (pointer.x - frame.minX) / frame.width
        let fy = (pointer.y - frame.minY) / frame.height
        window.setFrame(NSRect(x: pointer.x - fx * width, y: pointer.y - fy * height, width: width, height: height), display: true)
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 {
            controller?.closePin(nil)
            return
        }
        if let digit = event.charactersIgnoringModifiers.flatMap(Int.init), (0...9).contains(digit),
           event.modifierFlags.intersection(.deviceIndependentFlagsMask).isEmpty {
            controller?.setOpacity(tenths: digit)
            return
        }
        super.keyDown(with: event)
    }

    /// Handled here rather than by the main menu, which only sees keys while Tinysnap is
    /// the active app, and a pin never makes it so.
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard event.modifierFlags.intersection(.deviceIndependentFlagsMask) == .command else {
            return super.performKeyEquivalent(with: event)
        }
        switch event.charactersIgnoringModifiers {
        case "c": controller?.copyImage(nil)
        case "w": controller?.closePin(nil)
        case "s": controller?.saveImage(nil)
        default: return super.performKeyEquivalent(with: event)
        }
        return true
    }

    override func menu(for event: NSEvent) -> NSMenu? {
        let menu = NSMenu()
        for (title, action) in [("Copy", #selector(PinWindowController.copyImage(_:))),
                                ("Save", #selector(PinWindowController.saveImage(_:))),
                                ("Open in Editor", #selector(PinWindowController.openInEditor(_:))),
                                ("Close", #selector(PinWindowController.closePin(_:)))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
            item.target = controller
            menu.addItem(item)
        }
        return menu
    }
}
