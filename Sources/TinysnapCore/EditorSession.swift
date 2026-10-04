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

/// A line a dragged shape lines up on, in capture pixels: an edge or middle it shares with
/// another shape or the output, drawn from one across to the other while the drag lasts.
public struct Guide: Equatable, Sendable {
    public enum Axis: Sendable { case vertical, horizontal }
    public let axis: Axis
    /// x for a vertical line, y for a horizontal one.
    public let position: CGFloat
    /// Where the line starts and ends along its own direction.
    public let from: CGFloat
    public let to: CGFloat
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
        /// `snap` is how far the shape has been nudged to line up, kept so the next step of the
        /// drag starts from where the pointer alone would have put it.
        case moving(Annotation.ID, last: CGPoint, snap: CGVector = .zero)
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
    /// The lines the shape being dragged lines up on, empty when it lines up on nothing.
    public private(set) var guides: [Guide] = []
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
        // A locked shape keeps its style, and the tool keeps what it had.
        if selectedAnnotation?.isLocked == true { return }
        guard let id = selection, var annotation = display.annotation(id) else {
            var style = style(for: tool)
            let ratio = style.cropRatio
            change(&style)
            styles[tool] = style
            colorHex = style.colorHex
            // A picked ratio trims the crop to it, or the whole capture when nothing is
            // cropped yet, so the frame takes the ratio at once.
            if tool == .crop, style.cropRatio != ratio, let value = style.cropRatio.value {
                let trimmed = display.outputRect.trimmed(toRatio: value).wholePixels
                if trimmed.width >= 1, trimmed.height >= 1, trimmed != display.outputRect {
                    display.crop = trimmed
                    history.commit(display)
                }
            }
            return
        }
        let before = annotation.style.colorHex
        change(&annotation.style)
        styles[annotation.tool] = annotation.style
        // Only a colour that was picked becomes the shared one. Stepping an old red
        // annotation's size leaves a blue shared colour alone.
        if annotation.style.colorHex != before { colorHex = annotation.style.colorHex }
        display.replace(annotation)
        // Every spotlight lights one shared area, so all of them dim or all of them blur.
        if case .spotlight = annotation.kind {
            for index in display.annotations.indices {
                guard case .spotlight = display.annotations[index].kind else { continue }
                display.annotations[index].style.blurOutside = annotation.style.blurOutside
            }
        }
        // A text still being typed is committed when typing ends, as one step.
        if typingID == nil { history.commit(display, mergeKey: merging ? "style \(id)" : nil) }
    }

    // MARK: Measure

    /// The Measure tool's live reading, kept: one measurement a line, in one undo step,
    /// and none of them selected, so the next click measures again.
    public mutating func keep(_ lines: [MeasureLine]) {
        guard !lines.isEmpty else { return }
        let style = style(for: .measure)
        for line in MeasureShape.clearTags(lines, width: Tool.measure.points(for: style.size) ?? 2, scale: display.scale) {
            display.annotations.append(Annotation(kind: .measure(from: line.from, to: line.to), style: style, labelAt: line.labelAt))
        }
        history.commit(display)
        selection = nil
    }

    // MARK: Backdrop

    /// Sets or clears the backdrop as one undoable step. `merging` is for the colour
    /// panel's stream of changes, which undoes as one.
    public mutating func setBackdrop(_ backdrop: Backdrop?, merging: Bool = false) {
        display.backdrop = backdrop
        history.commit(display, mergeKey: merging ? "backdrop" : nil)
    }

    // MARK: Size

    /// Sets the export size as one undoable step, held to the limits. Nil follows the
    /// Export setting again.
    public mutating func setResize(_ resize: CGFloat?) {
        display.resize = resize.map(display.clampedResize)
        history.commit(display)
    }

    // MARK: Redact

    /// An erase box over each of `boxes`, a little past each so no edge of a letter shows, all as
    /// one undoable step. Each stays a box of its own, to delete if it covers too much. A box an
    /// erase already covers is skipped: the reader sees the text under the erases and finds it
    /// again, and a second box under the first would make deleting it look like it did nothing.
    /// Returns how many it added.
    @discardableResult
    public mutating func redact(_ boxes: [CGRect]) -> Int {
        let margin = 2 * scale
        let erased = display.annotations.compactMap { annotation -> CGRect? in
            guard case let .erase(rect) = annotation.kind else { return nil }
            return rect
        }
        let fresh = boxes.map { $0.insetBy(dx: -margin, dy: -margin).wholePixels }
            .filter { box in !erased.contains { $0.contains(box) } }
        guard !fresh.isEmpty else { return 0 }
        finishTyping()
        let style = style(for: .erase)
        for box in fresh { display.annotations.append(Annotation(kind: .erase(box), style: style)) }
        selection = nil
        history.commit(display)
        return fresh.count
    }

    // MARK: Steps

    /// Sets where the steps start counting, as one undoable step, held to the limits.
    public mutating func setStepStart(_ start: Int) {
        let limits = Document.stepStartLimits
        display.stepStart = min(max(start, limits.lowerBound), limits.upperBound)
        history.commit(display)
    }

    // MARK: Magnifier

    /// The topmost magnifier under `point` the scroll wheel may zoom: not a locked or
    /// hidden one.
    public func magnifier(at point: CGPoint) -> Annotation.ID? {
        display.annotations.last { annotation in
            guard case .magnifier = annotation.kind, !annotation.isLocked, !annotation.isHidden else { return false }
            return annotation.contains(point, scale: scale)
        }?.id
    }

    /// Zooms a magnifier half a step per scroll step, from 1.5x to 4x. A run of scroll
    /// steps on one lens undoes as one step.
    public mutating func zoomMagnifier(_ id: Annotation.ID, steps: Int) {
        guard phase == .idle, var lens = display.annotation(id), !lens.isLocked, !lens.isHidden,
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

        if let selected = selectedAnnotation, !selected.isLocked, !selected.isHidden,
           let handle = Self.handle(near: point, in: selected.handles(scale: scale), reach: reach) {
            phase = .resizing(original: selected, handle: handle)
            return
        }

        if clickCount >= 2, let id = display.topmost(at: point), let text = display.annotation(id),
           case .text = text.kind, !text.isLocked {
            selection = id
            phase = .typing(id)
            return
        }

        // The select tool, or Command held with any other, picks up what is applied. A
        // locked shape is selected, so it can be unlocked, but stays where it is.
        if tool == .select || tool == .image || modifiers.contains(.command) {
            selection = display.topmost(at: point)
            phase = selectedAnnotation.flatMap { $0.isLocked ? nil : .moving($0.id, last: point) } ?? .idle
            return
        }

        // With the text tool, clicking existing text edits it rather than starting a
        // new one on top. Locked text is drawn over instead.
        if tool == .text, let id = display.topmost(at: point), let text = display.annotation(id),
           case .text = text.kind, !text.isLocked {
            selection = id
            phase = .typing(id)
            return
        }

        // The selection is picked up anywhere on it, so a box just drawn moves at once.
        if let selected = selectedAnnotation, !selected.isLocked, !selected.isHidden,
           selected.contains(point, scale: scale) {
            phase = .moving(selected.id, last: point)
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
        case let .moving(id, last, snap):
            guard let annotation = display.annotation(id) else { return }
            // Moved from where the pointer alone would have it, so a snap lets go as soon as
            // the pointer carries the shape past it. Command drags freely.
            let free = annotation.moved(by: CGVector(dx: point.x - last.x - snap.dx, dy: point.y - last.y - snap.dy))
            let lined = modifiers.contains(.command) ? (offset: CGVector.zero, guides: []) : lining(up: free)
            display.replace(free.moved(by: lined.offset))
            guides = lined.guides
            phase = .moving(id, last: point, snap: lined.offset)
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
            // an existing crop's handle only takes Shift. Either keeps a chosen ratio;
            // Shift squares it whatever the ratio.
            let isNew = original.size == .zero
            let ratio = constrained ? 1 : style(for: .crop).cropRatio.value
            let rect = (isNew
                ? CGRect.dragged(from: original.origin, to: point, ratio: ratio, fromCentre: modifiers.contains(.option))
                : original.resized(dragging: handle, to: point, ratio: ratio))
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
            guides = []
            history.commit(display)
        case .idle, .typing:
            break
        }
    }

    /// How far to nudge `moving` so an edge or its middle lies on another shown shape's, or the
    /// output's, within five points on each axis; and the lines that show what it lines up on.
    private func lining(up moving: Annotation) -> (offset: CGVector, guides: [Guide]) {
        let box = moving.bounds(scale: scale)
        let reach = 5 * scale
        let others = display.annotations.filter { $0.id != moving.id && !$0.isHidden }.map { $0.bounds(scale: scale) }
            + [display.outputRect]
        func nudge(_ mine: [CGFloat], _ theirs: [CGFloat]) -> CGFloat? {
            var best: CGFloat?
            for a in mine {
                for b in theirs where abs(b - a) <= reach && abs(b - a) < abs(best ?? .infinity) { best = b - a }
            }
            return best
        }
        let dx = nudge([box.minX, box.midX, box.maxX], others.flatMap { [$0.minX, $0.midX, $0.maxX] })
        let dy = nudge([box.minY, box.midY, box.maxY], others.flatMap { [$0.minY, $0.midY, $0.maxY] })
        let lined = box.offsetBy(dx: dx ?? 0, dy: dy ?? 0)
        var guides: [Guide] = []
        if dx != nil {
            for x in [lined.minX, lined.midX, lined.maxX] {
                for other in others where [other.minX, other.midX, other.maxX].contains(where: { abs($0 - x) < 0.5 }) {
                    guides.append(Guide(axis: .vertical, position: x, from: min(lined.minY, other.minY), to: max(lined.maxY, other.maxY)))
                }
            }
        }
        if dy != nil {
            for y in [lined.minY, lined.midY, lined.maxY] {
                for other in others where [other.minY, other.midY, other.maxY].contains(where: { abs($0 - y) < 0.5 }) {
                    guides.append(Guide(axis: .horizontal, position: y, from: min(lined.minX, other.minX), to: max(lined.maxX, other.maxX)))
                }
            }
        }
        return (CGVector(dx: dx ?? 0, dy: dy ?? 0), guides)
    }

    private mutating func startDrawing(at point: CGPoint) {
        let zero = CGRect(origin: point, size: .zero)
        let kind: Annotation.Kind
        switch tool {
        case .arrow: kind = .arrow(from: point, to: point)
        case .line: kind = .line(from: point, to: point)
        case .highlighter:
            kind = style(for: .highlighter).freehand ? .highlighterPath([point]) : .highlighter(from: point, to: point)
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
        case .measure:
            // Nothing to drag out. A click that picks nothing up lets go of the selection,
            // and the canvas keeps its live reading.
            selection = nil
            return
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
        case .measure: return .measure(from: anchor, to: end)
        case .rectangle: return .rectangle(box)
        case .oval: return .oval(box)
        case .spotlight: return .spotlight(box)
        case .blur: return .blur(box)
        case .pixelate: return .pixelate(box)
        case .erase: return .erase(box)
        case let .freehand(points):
            guard let last = points.last, last.distance(to: point) >= 1 else { return kind }
            return .freehand(points + [point])
        case let .highlighterPath(points):
            guard let last = points.last, last.distance(to: point) >= 1 else { return kind }
            return .highlighterPath(points + [point])
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
        guard phase == .idle, let id = selection, selectedAnnotation?.isLocked != true else { return }
        display.remove(id)
        selection = nil
        history.commit(display)
    }

    public mutating func nudge(dx: CGFloat, dy: CGFloat) {
        guard phase == .idle, let annotation = selectedAnnotation, !annotation.isLocked, !annotation.isHidden else { return }
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

    // MARK: Layers

    public enum Arrangement: Sendable { case front, forward, backward, back }

    /// Points a duplicate sits right of and below its original.
    public static let duplicateOffset: CGFloat = 12

    /// Selects a shape from the layers panel, whatever the tool, or nothing.
    public mutating func select(_ id: Annotation.ID?) {
        finishTyping()
        guard phase == .idle else { return }
        selection = id.flatMap { display.annotation($0)?.id }
    }

    /// Moves the selection in the order. Front and back are the ends of the list.
    public mutating func arrange(_ arrangement: Arrangement) {
        finishTyping()
        guard let id = selection, let index = display.annotations.firstIndex(where: { $0.id == id }) else { return }
        let last = display.annotations.count - 1
        let target = switch arrangement {
        case .front: last
        case .forward: min(index + 1, last)
        case .backward: max(index - 1, 0)
        case .back: 0
        }
        moveLayer(id, to: target)
    }

    /// Puts a shape at `index` in the list, bottom first, in one go, as Arrange does. One
    /// undo step, and none when it lands where it was.
    public mutating func moveLayer(_ id: Annotation.ID, to index: Int) {
        dragLayer(id, to: index)
        dropLayer()
    }

    /// A row being dragged in the layers list: its shape moves as the pointer goes, kept by
    /// `dropLayer` as one step, or put back by `cancelLayerDrag`.
    public mutating func dragLayer(_ id: Annotation.ID, to index: Int) {
        finishTyping()
        guard phase == .idle, let from = display.annotations.firstIndex(where: { $0.id == id }) else { return }
        let target = min(max(index, 0), display.annotations.count - 1)
        guard target != from else { return }
        display.annotations.insert(display.annotations.remove(at: from), at: target)
    }

    public mutating func dropLayer() {
        guard phase == .idle else { return }
        history.commit(display)
    }

    public mutating func cancelLayerDrag() {
        display = history.document
    }

    public mutating func setLocked(_ id: Annotation.ID, _ locked: Bool) {
        edit(id) { $0.isLocked = locked }
    }

    public mutating func setHidden(_ id: Annotation.ID, _ hidden: Bool) {
        edit(id) { $0.isHidden = hidden }
    }

    public mutating func toggleLock() {
        guard let annotation = selectedAnnotation else { return }
        setLocked(annotation.id, !annotation.isLocked)
    }

    /// A copy right above the original, a little right and down, unlocked and shown, so
    /// it can be moved at once. It becomes the selection.
    public mutating func duplicateSelection() {
        finishTyping()
        guard phase == .idle, let original = selectedAnnotation,
              let index = display.annotations.firstIndex(where: { $0.id == original.id }) else { return }
        let offset = Self.duplicateOffset * scale
        let moved = original.moved(by: CGVector(dx: offset, dy: offset))
        let copy = Annotation(kind: moved.kind, style: moved.style, labelAt: moved.labelAt)
        display.annotations.insert(copy, at: index + 1)
        selection = copy.id
        history.commit(display)
    }

    private mutating func edit(_ id: Annotation.ID, _ change: (inout Annotation) -> Void) {
        finishTyping()
        guard phase == .idle, var annotation = display.annotation(id) else { return }
        change(&annotation)
        display.replace(annotation)
        history.commit(display)
    }

    // MARK: Images

    /// Adds a pasted or dropped image, centred at its own point size and scaled down to
    /// fit the capture if it is larger. It is then selected, and the tool in hand stays:
    /// a selection moves under any tool.
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
    }

    public mutating func markSaved() {
        history.markSaved()
    }
}
