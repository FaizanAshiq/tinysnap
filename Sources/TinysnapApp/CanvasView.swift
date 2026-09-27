import AppKit
import TinysnapCore

/// Draws the document and turns pointer and key events into EditorSession calls. The
/// view is flipped and sized in points, one point per capture point, and the scroll
/// view around it does the zooming.
final class CanvasView: NSView, NSTextViewDelegate, NSMenuItemValidation {
    var session: EditorSession {
        didSet { sessionChanged(from: oldValue) }
    }

    /// Anything the toolbar shows may have changed: the tool, the selection, the style.
    var onChange: (() -> Void)?
    /// Escape with nothing left to finish or deselect.
    var onClose: (() -> Void)?
    /// The image tool was chosen, so an image needs picking.
    var onPickImage: (() -> Void)?
    /// A style change from the keyboard is finished, so it can be remembered.
    var onStylesCommitted: (() -> Void)?
    /// The colour under the pointer, as "#RRGGBB".
    var onPointerColor: ((String?) -> Void)?
    /// Tab: copy the colour under the pointer.
    var onCopyColor: (() -> Void)?
    /// The Measure tool's lines, edge contrast and guide, as the editor last set them.
    var measure = MeasureSettings.defaults {
        didSet { if measure != oldValue { needsDisplay = true } }
    }
    /// X, Y or an arrow changed the Measure tool's settings.
    var onMeasureChange: ((MeasureSettings) -> Void)?
    private var tracking: NSTrackingArea?

    private var rendered: CGImage?
    private var renderedDocument: Document?
    private var renderedHidden: Annotation.ID?
    private var renderedFramed = false
    /// The last render was the output drawn over an earlier frame, so it wants a fresh one.
    private var renderedOver = false
    private var textView: NSTextView?
    /// What the canvas shows, in capture pixels: the framed output with a backdrop on,
    /// otherwise the whole extent. Kept here because working it out measures text. The
    /// view's top left corner is its origin, which moves left of or above the capture
    /// once a shape is drawn past the edge, or a backdrop's padding goes round it.
    private var shown: CGRect

    init(session: EditorSession) {
        self.session = session
        shown = Self.shownRect(of: session)
        super.init(frame: NSRect(x: 0, y: 0, width: shown.width / session.scale, height: shown.height / session.scale))
        registerForDraggedTypes([.fileURL, .png, .tiff])
    }

    required init?(coder: NSCoder) {
        fatalError("CanvasView is created in code only")
    }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: bounds, options: [.activeInKeyWindow, .mouseMoved, .mouseEnteredAndExited, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    /// Reads what is shown, annotations included, the way the readout should.
    private func reportColor(at point: CGPoint) {
        guard let rendered else { return }
        onPointerColor?(ColorProbe.hex(of: rendered, x: Int(point.x - shown.minX), y: Int(point.y - shown.minY)))
    }

    override func mouseMoved(with event: NSEvent) {
        // Command may have been let go in another window, so the flag is read again here.
        commandHeld = event.modifierFlags.contains(.command)
        let point = pixelPoint(event)
        reportColor(at: point)
        hover(at: point)
        measurePointer = session.tool == .measure ? point : nil
    }

    override func mouseExited(with event: NSEvent) {
        hovered = nil
        measurePointer = nil
    }

    // MARK: Measuring

    /// Where the pointer rests, in capture pixels, while the Measure tool is out.
    private var measurePointer: CGPoint? {
        didSet { if measurePointer != oldValue { needsDisplay = true } }
    }

    /// Read once per capture: the walks need each pixel's brightness, not its colour.
    private var luminance: (capture: ObjectIdentifier, buffer: LuminanceBuffer)?

    /// What a click would keep. Nothing over an annotation, where a click picks it up.
    private var liveReading: [MeasureLine] {
        guard session.tool == .measure, session.phase == .idle, !overPickUp, let point = measurePointer else { return [] }
        let capture = session.display.capture
        if luminance?.capture != ObjectIdentifier(capture) {
            luminance = LuminanceBuffer(image: capture.image).map { (ObjectIdentifier(capture), $0) }
        }
        guard let buffer = luminance?.buffer else { return [] }
        return MeasureReading.lines(at: point, in: buffer, scale: scale, settings: measure)
    }

    /// Drawn by the same code as a kept measurement, so the reading is what a click keeps.
    private func drawLiveReading(in context: CGContext) {
        let lines = liveReading
        guard !lines.isEmpty else { return }
        let style = session.style(for: .measure)
        context.saveGState()
        // Capture pixels, y down, as the renderer draws.
        context.scaleBy(x: 1 / scale, y: 1 / scale)
        context.translateBy(x: -shown.minX, y: -shown.minY)
        for line in lines {
            MeasureShape.draw(from: line.from, to: line.to, width: Tool.measure.points(for: style.size) ?? 2,
                              color: Palette.color(hex: style.colorHex), scale: scale, in: context)
        }
        context.restoreGState()
    }

    /// X and Y toggle the lines, and with nothing selected the up and down arrows change
    /// what counts as an edge. With something selected the arrows nudge it, as always.
    private func measureKey(_ event: NSEvent) -> Bool {
        guard session.tool == .measure, session.phase == .idle else { return false }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask).subtracting([.capsLock, .numericPad, .function])
        var next = measure
        let arrow = event.keyCode == 125 || event.keyCode == 126
        if arrow, session.selection == nil, flags.subtracting(.shift).isEmpty {
            next.stepContrast(up: event.keyCode == 126, coarse: flags.contains(.shift))
        } else {
            guard flags.isEmpty else { return false }
            switch event.charactersIgnoringModifiers?.lowercased() {
            case "x": next.across.toggle()
            case "y": next.down.toggle()
            default: return false
            }
        }
        measure = next
        onMeasureChange?(next)
        return true
    }

    // MARK: Showing what is applied

    /// Whatever is under the pointer gets a border, with any tool, so it is plain what is
    /// applied where, erase and spotlight boxes included.
    private var hovered: Annotation.ID? {
        didSet {
            guard hovered != oldValue else { return }
            needsDisplay = true
            updateCursor()
        }
    }

    /// Held, Command picks up what is applied with any tool, and every annotation shows
    /// its border meanwhile.
    private var commandHeld = false {
        didSet {
            guard commandHeld != oldValue else { return }
            needsDisplay = true
            updateCursor()
        }
    }

    /// Over something a click picks up with any tool: an annotation that changes its
    /// middle, a stroke, or a hover border.
    private var overPickUp = false {
        didSet { if overPickUp != oldValue { updateCursor() } }
    }

    /// An open hand over anything a click would pick up.
    private func updateCursor() {
        let picksUp = commandHeld || overPickUp || session.tool == .select || session.tool == .image
        (picksUp && hovered != nil ? NSCursor.openHand : NSCursor.arrow).set()
    }

    private func hover(at point: CGPoint) {
        hovered = session.hovered(at: point, reach: reach)
        overPickUp = session.phase == .idle && session.tool != .crop && session.display.pickUp(at: point, reach: reach) != nil
    }

    private func drawBorders(in context: CGContext) {
        let showAll = commandHeld || session.tool == .select
        context.saveGState()
        context.setStrokeColor(NSColor.controlAccentColor.cgColor)
        for annotation in session.display.annotations where annotation.id != session.selection {
            let isHovered = annotation.id == hovered
            guard showAll || isHovered else { continue }
            let box = viewRect(annotation.bounds(scale: scale)).insetBy(dx: -3 / magnification, dy: -3 / magnification)
            context.setLineWidth((isHovered ? 1.5 : 1) / magnification)
            context.setLineDash(phase: 0, lengths: isHovered ? [] : [3 / magnification, 3 / magnification])
            context.setAlpha(isHovered ? 1 : 0.7)
            context.stroke(box)
        }
        context.restoreGState()
    }

    private var magnification: CGFloat { enclosingScrollView?.magnification ?? 1 }
    private var scale: CGFloat { session.scale }

    /// Six screen points of reach for a handle, whatever the zoom.
    private var reach: CGFloat { 6 / magnification * scale }

    private func pixelPoint(_ event: NSEvent) -> CGPoint {
        let point = convert(event.locationInWindow, from: nil)
        return CGPoint(x: point.x * scale + shown.minX, y: point.y * scale + shown.minY)
    }

    private func viewRect(_ pixels: CGRect) -> CGRect {
        CGRect(x: (pixels.minX - shown.minX) / scale, y: (pixels.minY - shown.minY) / scale,
               width: pixels.width / scale, height: pixels.height / scale)
    }

    private func sessionChanged(from old: EditorSession) {
        fitExtent()
        needsDisplay = true
        syncTextView()
        onChange?()
    }

    /// A backdrop shows round the output, except while the crop tool is out, when the
    /// whole capture shows so the crop can be changed.
    private var framed: Bool { Self.isFramed(session) }

    private static func isFramed(_ session: EditorSession) -> Bool {
        session.display.backdrop != nil && session.tool != .crop
    }

    private static func shownRect(of session: EditorSession) -> CGRect {
        (isFramed(session) ? session.display.framedRect : nil) ?? session.display.extent
    }

    /// Grows and shrinks with what is shown. Mid gesture the capture holds its
    /// place on screen: growing left or up moves it within the view, so the scroll
    /// position moves by as much, and centring waits. Once the gesture ends, the canvas
    /// glides back to the centre.
    private func fitExtent() {
        let clip = enclosingScrollView?.contentView as? CenteringClipView
        let gesture = session.phase != .idle
        let new = Self.shownRect(of: session)
        if new != shown {
            if gesture { clip?.isHoldingPlace = true }
            let shift = NSPoint(x: (shown.minX - new.minX) / scale, y: (shown.minY - new.minY) / scale)
            shown = new
            setFrameSize(NSSize(width: new.width / scale, height: new.height / scale))
            if let clip, shift != .zero {
                clip.scroll(to: NSPoint(x: clip.bounds.origin.x + shift.x, y: clip.bounds.origin.y + shift.y))
                enclosingScrollView?.reflectScrolledClipView(clip)
            }
        }
        // Outside a gesture the canvas settles: centred when it is smaller than the
        // window, which a switch to or from the backdrop also needs. After a gesture it
        // glides there; otherwise it goes at once.
        guard !gesture, let clip else { return }
        let glides = clip.isHoldingPlace
        clip.isHoldingPlace = false
        let settled = clip.constrainBoundsRect(clip.bounds).origin
        guard settled != clip.bounds.origin else { return }
        if glides {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : 0.2
                clip.animator().setBoundsOrigin(settled)
            }
        } else {
            clip.setBoundsOrigin(settled)
        }
        enclosingScrollView?.reflectScrolledClipView(clip)
    }

    // MARK: Ground

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        paintGround()
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        paintGround()
    }

    /// Outside the canvas the window shows a checkerboard, as image editors do, so where
    /// the image ends is plain whatever colour the capture is. A solid ground next to a
    /// dark capture read as part of it.
    private func paintGround() {
        let dark = effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        enclosingScrollView?.backgroundColor = Self.checkerboard(dark: dark)
        enclosingScrollView?.drawsBackground = true
    }

    private static func checkerboard(dark: Bool) -> NSColor {
        let side: CGFloat = 8
        let base = NSColor(white: dark ? 0.16 : 0.90, alpha: 1)
        let square = NSColor(white: dark ? 0.22 : 0.97, alpha: 1)
        let tile = NSImage(size: NSSize(width: side * 2, height: side * 2), flipped: false) { rect in
            base.setFill()
            rect.fill()
            square.setFill()
            NSRect(x: 0, y: 0, width: side, height: side).fill()
            NSRect(x: side, y: side, width: side, height: side).fill()
            return true
        }
        return NSColor(patternImage: tile)
    }

    // MARK: Drawing

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        let hidden = session.typingID
        // ponytail: renders the whole document on every change. Fine at Retina laptop
        // sizes; cache the annotations below the one being dragged if 5K captures lag.
        let framed = self.framed
        let gesture = session.phase != .idle
        if rendered == nil || renderedDocument != session.display || renderedHidden != hidden || renderedFramed != framed
            || renderedOver && !gesture {
            let hiding: Set<Annotation.ID> = hidden.map { [$0] } ?? []
            // Mid gesture only the output changes, so it goes over the last frame, and the
            // frame is drawn afresh once the gesture ends.
            let over = framed && renderedFramed && gesture
                ? rendered.flatMap { Renderer.reframe(session.display, over: $0, hiding: hiding) } : nil
            // With a backdrop, the canvas shows exactly what an export gives.
            rendered = over ?? (framed ? Renderer.renderFramed(session.display, hiding: hiding) : Renderer.render(session.display, hiding: hiding))
            renderedDocument = session.display
            renderedHidden = hidden
            renderedFramed = framed
            renderedOver = over != nil
        }

        if let rendered {
            context.saveGState()
            // Past 100% every pixel stays a sharp square.
            context.interpolationQuality = magnification > 1 ? .none : .high
            context.translateBy(x: 0, y: bounds.height)
            context.scaleBy(x: 1, y: -1)
            context.draw(rendered, in: bounds)
            context.restoreGState()
        }

        drawCrop(in: context)
        drawBorders(in: context)
        drawSelection(in: context)
        drawLiveReading(in: context)
    }

    private func drawCrop(in context: CGContext) {
        // Framed, only the crop shows, so there is nothing outside it to dim.
        guard session.tool == .crop || session.display.crop != nil && !framed else { return }
        let crop = viewRect(session.display.outputRect)
        context.saveGState()
        context.setFillColor(NSColor.black.withAlphaComponent(0.55).cgColor)
        context.addRect(bounds)
        context.addRect(crop)
        context.fillPath(using: .evenOdd)
        context.restoreGState()
        if session.tool == .crop {
            drawHandles(session.display.outputRect.handlePoints.map(\.1), in: context)
        }
    }

    private func drawSelection(in context: CGContext) {
        guard let annotation = session.selectedAnnotation, session.typingID == nil else { return }
        let outline = viewRect(annotation.bounds(scale: scale)).insetBy(dx: -2 / magnification, dy: -2 / magnification)
        context.saveGState()
        context.setStrokeColor(NSColor.controlAccentColor.cgColor)
        context.setLineWidth(1 / magnification)
        context.setLineDash(phase: 0, lengths: [4 / magnification, 3 / magnification])
        context.stroke(outline)
        context.restoreGState()
        drawHandles(annotation.handles(scale: scale).map(\.1), in: context)
    }

    private func drawHandles(_ points: [CGPoint], in context: CGContext) {
        let size = 8 / magnification
        context.saveGState()
        context.setFillColor(NSColor.white.cgColor)
        context.setStrokeColor(NSColor.controlAccentColor.cgColor)
        context.setLineWidth(1 / magnification)
        for point in points {
            let box = CGRect(x: (point.x - shown.minX) / scale - size / 2, y: (point.y - shown.minY) / scale - size / 2,
                             width: size, height: size)
            context.fill(box)
            context.stroke(box)
        }
        context.restoreGState()
    }

    // MARK: Pointer

    /// Space is a key, not a modifier, so whether it is held is tracked here.
    private var spaceHeld = false
    private var lastPoint: CGPoint?

    private func modifiers(_ flags: NSEvent.ModifierFlags) -> Modifiers {
        var modifiers: Modifiers = []
        if flags.contains(.shift) { modifiers.insert(.shift) }
        if flags.contains(.option) { modifiers.insert(.option) }
        if flags.contains(.command) { modifiers.insert(.command) }
        if spaceHeld { modifiers.insert(.space) }
        return modifiers
    }

    private var isDrawing: Bool {
        switch session.phase {
        case .drawing, .cropping: true
        default: false
        }
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        let point = pixelPoint(event)
        lastPoint = point
        let reading = liveReading
        session.pointerDown(at: point, modifiers: modifiers(event.modifierFlags), clickCount: event.clickCount, reach: reach)
        // A Measure click that picked nothing up keeps what was showing.
        if session.tool == .measure, session.phase == .idle, session.selection == nil { session.keep(reading) }
        hover(at: point)
    }

    override func mouseDragged(with event: NSEvent) {
        autoscroll(with: event)
        let point = pixelPoint(event)
        lastPoint = point
        session.pointerDragged(to: point, modifiers: modifiers(event.modifierFlags))
        reportColor(at: point)
    }

    override func mouseUp(with event: NSEvent) {
        lastPoint = nil
        session.pointerUp()
        hover(at: pixelPoint(event))
    }

    /// Pressing or letting go of Shift or Option mid drag reshapes at once, without
    /// waiting for the pointer to move. Command shows every border while it is held.
    override func flagsChanged(with event: NSEvent) {
        commandHeld = event.modifierFlags.contains(.command)
        guard isDrawing, let lastPoint else {
            super.flagsChanged(with: event)
            return
        }
        session.pointerDragged(to: lastPoint, modifiers: modifiers(event.modifierFlags))
    }

    override func keyUp(with event: NSEvent) {
        guard event.keyCode == 49 else {
            super.keyUp(with: event)
            return
        }
        spaceHeld = false
    }

    // MARK: Scrolling

    private var scrollCarry: CGFloat = 0

    /// Over a magnifier the wheel zooms it; anywhere else it scrolls the canvas.
    override func scrollWheel(with event: NSEvent) {
        let point = pixelPoint(event)
        guard session.phase == .idle, let lens = session.magnifier(at: point) else {
            scrollCarry = 0
            super.scrollWheel(with: event)
            return
        }
        // A trackpad sends many small deltas and a wheel whole lines, so both come out
        // at about half a step of zoom per notch.
        scrollCarry += event.hasPreciseScrollingDeltas ? event.scrollingDeltaY / 12 : event.scrollingDeltaY
        let steps = Int(scrollCarry)
        guard steps != 0 else { return }
        scrollCarry -= CGFloat(steps)
        session.zoomMagnifier(lens, steps: steps)
    }

    // MARK: Keys

    override func keyDown(with event: NSEvent) {
        if measureKey(event) { return }
        let step: CGFloat = event.modifierFlags.contains(.shift) ? 10 : 1
        switch event.keyCode {
        case 49 where isDrawing:
            // Held Space moves the shape being drawn, as in Photoshop.
            spaceHeld = true
            return
        case 53:
            if session.escape() == .close { onClose?() }
            return
        case 48:
            onCopyColor?()
            return
        case 33, 30:
            // [ and ] step the size, as in Photoshop. A run of them undoes as one step.
            let thicker = event.keyCode == 30
            let target = session.selectedAnnotation?.tool ?? session.tool
            guard target.hasSize else {
                NSSound.beep()
                return
            }
            session.restyle(merging: true) { $0.size = thicker ? $0.size.thicker : $0.size.thinner }
            onStylesCommitted?()
            return
        case 51, 117:
            session.deleteSelection()
            return
        case 123: session.nudge(dx: -step, dy: 0); return
        case 124: session.nudge(dx: step, dy: 0); return
        case 125: session.nudge(dx: 0, dy: step); return
        case 126: session.nudge(dx: 0, dy: -step); return
        default: break
        }

        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask).subtracting([.shift, .capsLock])
        // Digits set a selected pasted image's opacity, 1 to 9 for 10% to 90% and 0 for
        // solid, as on pins.
        if flags.isEmpty, session.selectedAnnotation?.tool == .image,
           let digit = event.charactersIgnoringModifiers.flatMap(Int.init), (0...9).contains(digit) {
            session.restyle { $0.opacity = digit == 0 ? 1 : CGFloat(digit) / 10 }
            onStylesCommitted?()
            return
        }
        if flags.isEmpty, let character = event.charactersIgnoringModifiers?.first, let tool = Tool.forKey(character) {
            choose(tool)
            return
        }
        super.keyDown(with: event)
    }

    func choose(_ tool: Tool) {
        session.choose(tool)
        if tool == .image { onPickImage?() }
    }

    /// Undo and redo live here rather than on the window controller, because the window
    /// itself answers undo: for its own undo manager and would take it first.
    @objc func undo(_ sender: Any?) {
        session.undo()
    }

    @objc func redo(_ sender: Any?) {
        session.redo()
    }

    @objc func delete(_ sender: Any?) {
        session.deleteSelection()
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(undo(_:)): return session.history.canUndo && session.phase == .idle
        case #selector(redo(_:)): return session.history.canRedo && session.phase == .idle
        case #selector(delete(_:)): return session.selection != nil
        default: return true
        }
    }

    // MARK: Text

    /// Ends any typing, for copy, save and close, which all want the text committed.
    func finishTyping() {
        session.finishTyping()
    }

    /// A text field sits over the annotation while it is typed, and the renderer leaves
    /// that annotation out so it is not drawn twice.
    private func syncTextView() {
        guard let id = session.typingID, let annotation = session.display.annotation(id),
              case let .text(origin, string) = annotation.kind else {
            if let textView {
                textView.removeFromSuperview()
                self.textView = nil
                window?.makeFirstResponder(self)
            }
            return
        }

        let fontSize = annotation.pixelSize(scale: scale) / scale
        let field = textView ?? makeTextView()
        field.font = TextLayout.font(points: fontSize) as NSFont
        field.textColor = NSColor(cgColor: Palette.color(hex: annotation.style.colorHex))
        field.insertionPointColor = field.textColor ?? .labelColor
        if field.string != string { field.string = string }
        field.setFrameOrigin(CGPoint(x: (origin.x - shown.minX) / scale, y: (origin.y - shown.minY) / scale))
        field.sizeToFit()
        if field.frame.width < fontSize { field.setFrameSize(NSSize(width: fontSize, height: field.frame.height)) }
        if window?.firstResponder !== field { window?.makeFirstResponder(field) }
    }

    private func makeTextView() -> NSTextView {
        let field = NSTextView(frame: NSRect(x: 0, y: 0, width: 20, height: 20))
        field.drawsBackground = false
        field.isRichText = false
        field.allowsUndo = true
        field.isHorizontallyResizable = true
        field.isVerticallyResizable = true
        // maxSize starts out as the first frame, which stops sizeToFit growing the
        // field and clips everything typed past its first few letters.
        field.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        field.textContainerInset = .zero
        field.textContainer?.lineFragmentPadding = 0
        field.textContainer?.widthTracksTextView = false
        field.textContainer?.containerSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        field.delegate = self
        addSubview(field)
        textView = field
        return field
    }

    func textDidChange(_ notification: Notification) {
        guard let field = notification.object as? NSTextView else { return }
        session.updateTyping(field.string)
    }

    func textView(_ textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        guard selector == #selector(cancelOperation(_:)) else { return false }
        _ = session.escape()
        return true
    }

    // MARK: Images

    func insert(_ image: NSImage) {
        guard let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return }
        session.insert(PastedImage(cgImage), pointSize: image.size)
    }

    override func draggingEntered(_ sender: any NSDraggingInfo) -> NSDragOperation {
        NSImage.canInit(with: sender.draggingPasteboard) ? .copy : []
    }

    override func performDragOperation(_ sender: any NSDraggingInfo) -> Bool {
        guard let image = NSImage(pasteboard: sender.draggingPasteboard) else { return false }
        insert(image)
        return true
    }
}
