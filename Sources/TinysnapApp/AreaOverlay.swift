import AppKit
import TinysnapCore

/// A borderless window refuses to become key, which would leave the overlay unable to
/// hear escape, space or the arrow keys.
final class OverlayWindow: NSWindow {
    override var canBecomeKey: Bool { true }
}

enum AreaResult {
    /// A box in points from the display's top left corner.
    case area(FrozenDisplay, CGRect)
    case window(PickableWindow)
    case cancelled
}

/// One window per display, each showing that display's frozen image. The user drags a
/// box, or presses space and clicks a window.
@MainActor
final class AreaOverlayController {
    private var windows: [OverlayWindow] = []
    private let onFinish: (AreaResult) -> Void
    private var focusObserver: NSObjectProtocol?
    private var isFinished = false

    init(displays: [FrozenDisplay], pickable: [PickableWindow], onFinish: @escaping (AreaResult) -> Void) {
        self.onFinish = onFinish

        for display in displays {
            let window = OverlayWindow(contentRect: display.frame, styleMask: .borderless, backing: .buffered, defer: false)
            window.isOpaque = true
            window.backgroundColor = .black
            window.hasShadow = false
            window.level = .screenSaver
            window.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
            // The frozen screen is there the moment the hotkey lands, and gone the moment the box
            // is done: AppKit would fade it in and out.
            window.animationBehavior = .none

            // The frozen image sits in a layer, so moving the pointer only redraws the
            // thin selection view above it, never the full resolution image.
            let base = NSView(frame: NSRect(origin: .zero, size: display.frame.size))
            base.wantsLayer = true
            base.layer?.contents = display.image
            base.layer?.contentsGravity = .resize

            let selection = AreaSelectionView(frame: base.bounds, display: display, pickable: pickable)
            selection.autoresizingMask = [.width, .height]
            selection.onFinish = { [weak self] result in self?.finish(result) }
            selection.onToggleWindowMode = { [weak self] in self?.toggleWindowMode() }
            base.addSubview(selection)

            window.contentView = base
            window.setFrame(display.frame, display: true)
            windows.append(window)
        }
    }

    func show() {
        windows.forEach { $0.orderFrontRegardless() }
        NSApp.activate(ignoringOtherApps: true)
        let mouse = NSEvent.mouseLocation
        let key = windows.first { $0.frame.contains(mouse) } ?? windows.first
        key?.makeKeyAndOrderFront(nil)
        if let key, let selection = key.contentView?.subviews.first { key.makeFirstResponder(selection) }
        watchForLostFocus()
    }

    private var selectionViews: [AreaSelectionView] {
        windows.compactMap { $0.contentView?.subviews.first as? AreaSelectionView }
    }

    /// For Capture Window: the overlay opens as if Space had been pressed.
    func startInWindowMode() {
        if selectionViews.first?.windowMode != true { toggleWindowMode() }
    }

    private func toggleWindowMode() {
        let on = !(selectionViews.first?.windowMode ?? false)
        selectionViews.forEach { $0.windowMode = on }
    }

    /// Up but unfocused, the overlay would cover every screen and hear nothing, so
    /// whatever takes the focus, take it straight back.
    private func watchForLostFocus() {
        focusObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didResignActiveNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, !self.isFinished else { return }
                NSApp.activate(ignoringOtherApps: true)
                let mouse = NSEvent.mouseLocation
                (self.windows.first { $0.frame.contains(mouse) } ?? self.windows.first)?.makeKeyAndOrderFront(nil)
            }
        }
    }

    private func finish(_ result: AreaResult) {
        guard !isFinished else { return }
        isFinished = true
        // First, or ordering the windows out reads as losing focus and pulls them back.
        if let focusObserver { NotificationCenter.default.removeObserver(focusObserver) }
        focusObserver = nil
        windows.forEach { $0.orderOut(nil) }
        windows.removeAll()
        onFinish(result)
    }
}

/// Draws the dimming, the selection box, its size in pixels, and in window mode the
/// highlighted window. Coordinates are points from the display's top left corner.
final class AreaSelectionView: NSView {
    var onFinish: ((AreaResult) -> Void)?
    var onToggleWindowMode: (() -> Void)?
    var windowMode = false {
        didSet {
            anchor = nil
            current = nil
            hovered = windowMode ? pickable.first { $0.frame.contains(NSEvent.mouseLocation) } : nil
            needsDisplay = true
        }
    }

    private let display: FrozenDisplay
    private let pickable: [PickableWindow]
    private var anchor: CGPoint?
    private var current: CGPoint?
    private var last: CGPoint?
    private var pointer: CGPoint?
    /// The Photoshop keys, the same as in the editor: Shift squares, Option draws from
    /// the centre, and Space held mid drag moves the box. Space before a drag still
    /// switches to picking a window, as in the macOS screenshot tool.
    private var square = false
    private var fromCentre = false
    private var spaceHeld = false
    private var hovered: PickableWindow?
    private var tracking: NSTrackingArea?

    init(frame: NSRect, display: FrozenDisplay, pickable: [PickableWindow]) {
        self.display = display
        self.pickable = pickable
        super.init(frame: frame)
    }

    required init?(coder: NSCoder) {
        fatalError("AreaSelectionView is created in code only")
    }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        // Always active, so the display without key focus still follows the pointer.
        let area = NSTrackingArea(rect: bounds, options: [.activeAlways, .mouseMoved, .mouseEnteredAndExited, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    private var selection: CGRect? {
        guard let anchor, let current else { return nil }
        let box = CGRect.dragged(from: anchor, to: current, square: square, fromCentre: fromCentre).intersection(bounds)
        return box.isNull ? nil : box
    }

    private func readKeys(_ flags: NSEvent.ModifierFlags) {
        square = flags.contains(.shift)
        fromCentre = flags.contains(.option)
    }

    private func location(_ event: NSEvent) -> CGPoint {
        let point = convert(event.locationInWindow, from: nil)
        return CGPoint(x: min(max(point.x, 0), bounds.width), y: min(max(point.y, 0), bounds.height))
    }

    override func mouseMoved(with event: NSEvent) {
        pointer = location(event)
        if windowMode { hovered = pickable.first { $0.frame.contains(NSEvent.mouseLocation) } }
        needsDisplay = true
    }

    override func mouseExited(with event: NSEvent) {
        pointer = nil
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        if windowMode {
            if let picked = pickable.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) {
                onFinish?(.window(picked))
            }
            return
        }
        let point = location(event)
        anchor = point
        current = point
        last = point
        readKeys(event.modifierFlags)
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard let anchor else { return }
        let point = location(event)
        readKeys(event.modifierFlags)
        if spaceHeld, let current, let last, let box = selection {
            // Moved as a whole, and stopped at the display's edge.
            let move = box.allowedMove(by: CGVector(dx: point.x - last.x, dy: point.y - last.y), within: bounds)
            self.anchor = anchor.offset(by: move)
            self.current = current.offset(by: move)
            self.last = last.offset(by: move)
        } else {
            current = point
            last = point
        }
        pointer = point
        needsDisplay = true
    }

    override func flagsChanged(with event: NSEvent) {
        readKeys(event.modifierFlags)
        needsDisplay = true
    }

    override func keyUp(with event: NSEvent) {
        guard event.keyCode == 49 else {
            super.keyUp(with: event)
            return
        }
        spaceHeld = false
    }

    override func mouseUp(with event: NSEvent) {
        defer {
            anchor = nil
            current = nil
            last = nil
            needsDisplay = true
        }
        guard let rect = selection, rect.width >= 2, rect.height >= 2 else { return }
        onFinish?(.area(display, rect))
    }

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 53: onFinish?(.cancelled)
        case 49:
            if anchor == nil {
                onToggleWindowMode?()
            } else {
                spaceHeld = true
            }
        case 123: nudge(dx: -1, dy: 0)
        case 124: nudge(dx: 1, dy: 0)
        case 125: nudge(dx: 0, dy: 1)
        case 126: nudge(dx: 0, dy: -1)
        default: super.keyDown(with: event)
        }
    }

    /// Arrow keys move the corner being dragged by one point.
    private func nudge(dx: CGFloat, dy: CGFloat) {
        guard let point = current else { return }
        current = clamp(CGPoint(x: point.x + dx, y: point.y + dy))
        needsDisplay = true
    }

    private func clamp(_ point: CGPoint) -> CGPoint {
        CGPoint(x: min(max(point.x, 0), bounds.width), y: min(max(point.y, 0), bounds.height))
    }

    /// A global AppKit rectangle in this view's flipped coordinates.
    private func local(_ global: CGRect) -> CGRect {
        CGRect(x: global.minX - display.frame.minX, y: display.frame.maxY - global.maxY,
               width: global.width, height: global.height)
    }

    override func draw(_ dirtyRect: NSRect) {
        let dim = NSColor.black.withAlphaComponent(0.35)
        let lit: CGRect? = selection ?? (windowMode ? hovered.map { local($0.frame) } : nil)

        let shade = NSBezierPath(rect: bounds)
        if let lit { shade.append(NSBezierPath(rect: lit)) }
        shade.windingRule = .evenOdd
        dim.setFill()
        shade.fill()

        if let lit {
            if windowMode {
                NSColor.controlAccentColor.withAlphaComponent(0.25).setFill()
                lit.fill()
            }
            NSColor.white.setStroke()
            NSBezierPath(rect: lit.insetBy(dx: 0.5, dy: 0.5)).stroke()
        }

        if let rect = selection {
            drawReadout("\(Int((rect.width * display.scale).rounded())) × \(Int((rect.height * display.scale).rounded()))",
                        near: CGPoint(x: rect.maxX, y: rect.maxY))
        } else if !windowMode, let pointer {
            NSColor.white.withAlphaComponent(0.6).setStroke()
            let cross = NSBezierPath()
            cross.move(to: CGPoint(x: pointer.x, y: 0))
            cross.line(to: CGPoint(x: pointer.x, y: bounds.height))
            cross.move(to: CGPoint(x: 0, y: pointer.y))
            cross.line(to: CGPoint(x: bounds.width, y: pointer.y))
            cross.lineWidth = 1
            cross.stroke()
        }
    }

    private func drawReadout(_ text: String, near corner: CGPoint) {
        let string = NSAttributedString(string: text, attributes: [
            .font: NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium),
            .foregroundColor: NSColor.white,
        ])
        let size = string.size()
        var box = CGRect(x: corner.x + 8, y: corner.y + 8, width: size.width + 12, height: size.height + 6)
        if box.maxX > bounds.maxX { box.origin.x = corner.x - box.width - 8 }
        if box.maxY > bounds.maxY { box.origin.y = corner.y - box.height - 8 }
        NSColor.black.withAlphaComponent(0.75).setFill()
        NSBezierPath(roundedRect: box, xRadius: 5, yRadius: 5).fill()
        string.draw(at: CGPoint(x: box.minX + 6, y: box.minY + 3))
    }
}
