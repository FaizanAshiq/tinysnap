import CoreGraphics
import Foundation

public struct Modifiers: OptionSet, Sendable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    /// Squares boxes and snaps lines to 45 degrees.
    public static let shift = Modifiers(rawValue: 1 << 0)
    /// Draws boxes out from where the drag began, as their centre.
    public static let option = Modifiers(rawValue: 1 << 1)
    /// Held while drawing: moves the shape being drawn instead of resizing it.
    public static let space = Modifiers(rawValue: 1 << 2)
    /// Held with any tool: picks up what is already applied, the way Photoshop's
    /// Command gives the move tool for as long as it is held.
    public static let command = Modifiers(rawValue: 1 << 3)
}

public enum EscapeResult: Equatable, Sendable {
    case finishedTyping, deselected, close
}

/// Everything the editor does with the pointer and the keyboard, with no AppKit in
/// it. The canvas view turns events into these calls and draws `display`.
public struct EditorSession {
    public enum Phase: Equatable {
        case idle
        case drawing(Annotation.ID, anchor: CGPoint, last: CGPoint)
        case moving(Annotation.ID, last: CGPoint)
        case resizing(original: Annotation, handle: Handle)
        case cropping(original: CGRect, handle: Handle, last: CGPoint)
        case typing(Annotation.ID)
    }

    public private(set) var history: EditHistory
    /// What the canvas draws: the committed document, or the one a gesture is changing.
    public private(set) var display: Document
    public private(set) var tool: Tool
    public private(set) var selection: Annotation.ID?
    public private(set) var phase: Phase = .idle
    /// The last style used with each tool, so each one remembers its own.
    public private(set) var styles: [Tool: Style]

    /// One colour for every tool: the last one picked, with whichever tool. Sizes and
    /// box shapes stay each tool's own.
    public private(set) var colorHex: String

    public init(document: Document, tool: Tool = .arrow, styles: [Tool: Style] = [:], colorHex: String = Palette.red) {
        history = EditHistory(document: document)
        display = document
        self.tool = tool
        self.styles = styles
        self.colorHex = colorHex
    }

    public var scale: CGFloat { display.scale }
    public var isUnsaved: Bool { history.isUnsaved }

    public var selectedAnnotation: Annotation? {
        selection.flatMap { display.annotation($0) }
    }

    /// The annotation whose text is being typed, which the canvas hides while its text
    /// field is showing.
    public var typingID: Annotation.ID? {
        if case let .typing(id) = phase { return id }
        return nil
    }

    public func style(for tool: Tool) -> Style {
        var style = styles[tool] ?? tool.defaultStyle
        style.colorHex = colorHex
        return style
    }

    // MARK: Tools and styles

    /// Picking another tool lets go of the selection, so the next drag draws.
    public mutating func choose(_ tool: Tool) {
        finishTyping()
        if tool != self.tool || tool == .crop { selection = nil }
        self.tool = tool
    }

    /// What is under `point`, for the hover border that shows what is applied where,
    /// with any tool: the annotation itself, or its border, which a filled box does not
    /// cover. Nothing while the crop tool is out, which only moves the crop.
    public func hovered(at point: CGPoint, reach: CGFloat = 0) -> Annotation.ID? {
        guard phase == .idle, tool != .crop else { return nil }
        return display.topmost(at: point) ?? display.borderHit(at: point, reach: reach)
    }

    /// Changes part of the style: the selection's, or the next annotation's when nothing
    /// is selected. Either way the tool remembers it.
    ///
    /// Only the part `change` touches moves. Applying a whole style copied when the
    /// popover opened turned a large filled box into a small outline when only its
    /// colour was picked. `merging` is for the colour panel, whose stream of changes
    /// to one annotation undoes as one step.
    public mutating func restyle(merging: Bool = false, _ change: (inout Style) -> Void) {
        guard let id = selection, var annotation = display.annotation(id) else {
            var style = style(for: tool)
            change(&style)
            styles[tool] = style
            colorHex = style.colorHex
            return
        }
        let before = annotation.style.colorHex
        change(&annotation.style)
        styles[annotation.tool] = annotation.style
        // Only a colour that was picked becomes the shared one. Stepping an old red
        // annotation's size leaves a blue shared colour alone.
        if annotation.style.colorHex != before { colorHex = annotation.style.colorHex }
        display.replace(annotation)
        // A text still being typed is committed when typing ends, as one step.
        if typingID == nil { history.commit(display, mergeKey: merging ? "style \(id)" : nil) }
    }

    // MARK: Backdrop

    /// Sets or clears the backdrop as one undoable step. `merging` is for the colour
    /// panel's stream of changes, which undoes as one.
    public mutating func setBackdrop(_ backdrop: Backdrop?, merging: Bool = false) {
        display.backdrop = backdrop
        history.commit(display, mergeKey: merging ? "backdrop" : nil)
    }

    // MARK: Magnifier

    /// The topmost magnifier under `point`, which the scroll wheel zooms.
    public func magnifier(at point: CGPoint) -> Annotation.ID? {
        display.annotations.last { annotation in
            guard case .magnifier = annotation.kind else { return false }
            return annotation.contains(point, scale: scale)
        }?.id
    }

    /// Zooms a magnifier half a step per scroll step, from 1.5x to 4x. A run of scroll
    /// steps on one lens undoes as one step.
    public mutating func zoomMagnifier(_ id: Annotation.ID, steps: Int) {
        guard phase == .idle, var lens = display.annotation(id),
              case let .magnifier(center, radius, zoom) = lens.kind else { return }
        let zoomed = min(max(zoom + 0.5 * CGFloat(steps), 1.5), 4)
        lens.kind = .magnifier(center: center, radius: radius, zoom: zoomed)
        display.replace(lens)
        history.commit(display, mergeKey: "zoom \(id)")
    }

    // MARK: Pointer

    /// `reach` is how close, in capture pixels, a click must land to grab a handle.
    public mutating func pointerDown(at point: CGPoint, modifiers: Modifiers = [], clickCount: Int = 1, reach: CGFloat) {
        // Clicking away ends typing, and that click does nothing else.
        if typingID != nil {
            finishTyping()
            return
        }

        if tool == .crop {
            let rect = display.outputRect
            if let handle = Self.handle(near: point, in: rect.handlePoints, reach: reach) {
                phase = .cropping(original: rect, handle: handle, last: point)
            } else {
                phase = .cropping(original: CGRect(origin: point, size: .zero), handle: .bottomRight, last: point)
            }
            return
        }

        if let selected = selectedAnnotation,
           let handle = Self.handle(near: point, in: selected.handles(scale: scale), reach: reach) {
            phase = .resizing(original: selected, handle: handle)
            return
        }

        if clickCount >= 2, let id = display.topmost(at: point),
           case .text = display.annotation(id)?.kind {
            selection = id
            phase = .typing(id)
            return
        }

        // The select tool, or Command held with any other, picks up what is applied.
        if tool == .select || tool == .image || modifiers.contains(.command) {
            selection = display.topmost(at: point)
            phase = selection.map { .moving($0, last: point) } ?? .idle
            return
        }

        // With the text tool, clicking existing text edits it rather than starting a
        // new one on top.
        if tool == .text, let id = display.topmost(at: point), case .text = display.annotation(id)?.kind {
            selection = id
            phase = .typing(id)
            return
        }

        // The selection is picked up anywhere on it, so a box just drawn moves at once.
        if let id = selection, display.annotation(id)?.contains(point, scale: scale) == true {
            phase = .moving(id, last: point)
            return
        }

        // A click on an annotation, or on its hover border, picks it up with any tool,
        // Command or not. The empty middle of an outline, or of a spotlight, still draws.
        if let id = display.pickUp(at: point, reach: reach) {
            selection = id
            phase = .moving(id, last: point)
            return
        }

        startDrawing(at: point)
    }

    /// Held keys work the way they do in Photoshop while a shape is drawn: Shift
    /// constrains, Option draws a box from its centre, and Space moves the whole shape,
    /// after which the drag carries on resizing from the new place.
    public mutating func pointerDragged(to point: CGPoint, modifiers: Modifiers = []) {
        let constrained = modifiers.contains(.shift)
        let delta = { (last: CGPoint) in CGVector(dx: point.x - last.x, dy: point.y - last.y) }
        switch phase {
        case let .drawing(id, anchor, last):
            guard var annotation = display.annotation(id) else { return }
            if modifiers.contains(.space) {
                let move = delta(last)
                display.replace(annotation.moved(by: move))
                phase = .drawing(id, anchor: anchor.offset(by: move), last: point)
                return
            }
            annotation.kind = Self.drawn(annotation.kind, anchor: anchor, to: point, constrained: constrained,
                                         fromCentre: modifiers.contains(.option))
            display.replace(annotation)
            phase = .drawing(id, anchor: anchor, last: point)
        case let .moving(id, last):
            guard let annotation = display.annotation(id) else { return }
            display.replace(annotation.moved(by: CGVector(dx: point.x - last.x, dy: point.y - last.y)))
            phase = .moving(id, last: point)
        case let .resizing(original, handle):
            display.replace(original.resized(dragging: handle, to: point, constrained: constrained))
        case let .cropping(original, handle, last):
            // The whole canvas, grown part included, can be cropped.
            let bounds = display.extent
            if modifiers.contains(.space) {
                // Moved as a whole, and stopped at the capture's edge rather than shrunk.
                let move = display.outputRect.allowedMove(by: delta(last), within: bounds)
                display.crop = display.outputRect.offsetBy(dx: move.dx, dy: move.dy)
                phase = .cropping(original: original.offsetBy(dx: move.dx, dy: move.dy), handle: handle,
                                  last: last.offset(by: move))
                return
            }
            // A new crop, drawn from nothing, follows the same keys as a box. Dragging
            // an existing crop's handle only takes Shift.
            let isNew = original.size == .zero
            let rect = (isNew
                ? CGRect.dragged(from: original.origin, to: point, square: constrained, fromCentre: modifiers.contains(.option))
                : original.resized(dragging: handle, to: point, square: constrained))
                .intersection(bounds).wholePixels
            if rect.width >= 1, rect.height >= 1 { display.crop = rect }
            phase = .cropping(original: original, handle: handle, last: point)
        case .idle, .typing:
            break
        }
    }

    public mutating func pointerUp() {
        switch phase {
        case let .drawing(id, _, _):
            phase = .idle
            guard let annotation = display.annotation(id) else { return }
            if annotation.isDegenerate(scale: scale) {
                display.remove(id)
                selection = nil
                return
            }
            history.commit(display)
            selection = id
        case .moving, .resizing, .cropping:
            phase = .idle
            history.commit(display)
        case .idle, .typing:
            break
        }
    }

    private mutating func startDrawing(at point: CGPoint) {
        let zero = CGRect(origin: point, size: .zero)
        let kind: Annotation.Kind
        switch tool {
        case .arrow: kind = .arrow(from: point, to: point)
        case .line: kind = .line(from: point, to: point)
        case .highlighter: kind = .highlighter(from: point, to: point)
        case .rectangle: kind = .rectangle(zero)
        case .oval: kind = .oval(zero)
        case .spotlight: kind = .spotlight(zero)
        case .blur: kind = .blur(zero)
        case .pixelate: kind = .pixelate(zero)
        case .erase: kind = .erase(zero)
        case .freehand: kind = .freehand([point])
        case .step: kind = .step(center: point)
        case .magnifier: kind = .magnifier(center: point, radius: Tool.magnifierRadiusPoints * scale, zoom: 2)
        case .text: kind = .text(origin: point, string: "")
        case .select, .image, .crop: return
        }

        let annotation = Annotation(kind: kind, style: style(for: tool))
        display.annotations.append(annotation)
        selection = annotation.id
        phase = tool == .text ? .typing(annotation.id) : .drawing(annotation.id, anchor: point, last: point)
    }

    /// `fromCentre` applies to boxes only: Photoshop's line tool does not centre either.
    private static func drawn(_ kind: Annotation.Kind, anchor: CGPoint, to point: CGPoint, constrained: Bool,
                              fromCentre: Bool) -> Annotation.Kind {
        let end = constrained ? point.snapped45(from: anchor) : point
        let box = CGRect.dragged(from: anchor, to: point, square: constrained, fromCentre: fromCentre)
        switch kind {
        case .arrow: return .arrow(from: anchor, to: end)
        case .line: return .line(from: anchor, to: end)
        case .highlighter: return .highlighter(from: anchor, to: end)
        case .rectangle: return .rectangle(box)
        case .oval: return .oval(box)
        case .spotlight: return .spotlight(box)
        case .blur: return .blur(box)
        case .pixelate: return .pixelate(box)
        case .erase: return .erase(box)
        case let .freehand(points):
            guard let last = points.last, last.distance(to: point) >= 1 else { return kind }
            return .freehand(points + [point])
        case .step: return .step(center: point)
        case let .magnifier(_, radius, zoom): return .magnifier(center: point, radius: radius, zoom: zoom)
        case .text, .image: return kind
        }
    }

    private static func handle(near point: CGPoint, in handles: [(Handle, CGPoint)], reach: CGFloat) -> Handle? {
        handles
            .map { (handle: $0.0, distance: $0.1.distance(to: point)) }
            .filter { $0.distance <= reach }
            .min { $0.distance < $1.distance }?
            .handle
    }

    // MARK: Text

    public mutating func updateTyping(_ string: String) {
        guard let id = typingID, var annotation = display.annotation(id),
              case let .text(origin, _) = annotation.kind else { return }
        annotation.kind = .text(origin: origin, string: string)
        display.replace(annotation)
    }

    /// Ends typing. Text left empty is removed: a new one leaves no trace in undo, an
    /// existing one emptied out is an ordinary, undoable delete.
    public mutating func finishTyping() {
        guard let id = typingID else { return }
        phase = .idle
        if display.annotation(id)?.isDegenerate(scale: scale) == true {
            display.remove(id)
            selection = nil
        }
        history.commit(display)
    }

    // MARK: Keyboard

    public mutating func deleteSelection() {
        guard phase == .idle, let id = selection else { return }
        display.remove(id)
        selection = nil
        history.commit(display)
    }

    public mutating func nudge(dx: CGFloat, dy: CGFloat) {
        guard phase == .idle, let annotation = selectedAnnotation else { return }
        display.replace(annotation.moved(by: CGVector(dx: dx, dy: dy)))
        history.commit(display)
    }

    public mutating func escape() -> EscapeResult {
        if typingID != nil {
            finishTyping()
            return .finishedTyping
        }
        if selection != nil {
            selection = nil
            return .deselected
        }
        return .close
    }

    public mutating func undo() {
        guard phase == .idle else { return }
        history.undo()
        display = history.document
        if let id = selection, display.annotation(id) == nil { selection = nil }
    }

    public mutating func redo() {
        guard phase == .idle else { return }
        history.redo()
        display = history.document
        if let id = selection, display.annotation(id) == nil { selection = nil }
    }

    // MARK: Images

    /// Adds a pasted or dropped image, centred at its own point size and scaled down to
    /// fit the capture if it is larger. It is then selected with the select tool.
    public mutating func insert(_ image: PastedImage, pointSize: CGSize) {
        guard pointSize.width > 0, pointSize.height > 0 else { return }
        finishTyping()
        let bounds = display.capture.bounds
        let natural = CGSize(width: pointSize.width * scale, height: pointSize.height * scale)
        let fit = min(1, bounds.width / natural.width, bounds.height / natural.height)
        let size = CGSize(width: natural.width * fit, height: natural.height * fit)
        let rect = CGRect(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2,
                          width: size.width, height: size.height)

        // Corners carry over from the last image; see-through and difference do not, so
        // a new paste is never a faint or inverted surprise.
        var style = style(for: .image)
        style.opacity = 1
        style.difference = false
        let annotation = Annotation(kind: .image(rect, image), style: style)
        display.annotations.append(annotation)
        history.commit(display)
        selection = annotation.id
        tool = .select
    }

    public mutating func markSaved() {
        history.markSaved()
    }
}
