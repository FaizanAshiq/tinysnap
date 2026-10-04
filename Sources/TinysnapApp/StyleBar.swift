import AppKit
import TinysnapCore

/// A small square button that draws its own glyph, with a solid accent fill when it is
/// the chosen one.
final class ChipButton: NSButton {
    var isChosen = false { didSet { needsDisplay = true } }
    private let glyph: (NSRect, NSColor) -> Void
    private let width: CGFloat

    /// 30 points wide for a glyph; a chip with a word on it passes more.
    init(label: String, width: CGFloat = 30, glyph: @escaping (NSRect, NSColor) -> Void) {
        self.glyph = glyph
        self.width = width
        super.init(frame: NSRect(x: 0, y: 0, width: width, height: 26))
        title = ""
        isBordered = false
        setButtonType(.momentaryChange)
        toolTip = label
        setAccessibilityLabel(label)
    }

    required init?(coder: NSCoder) {
        fatalError("ChipButton is created in code only")
    }

    override var intrinsicContentSize: NSSize { NSSize(width: width, height: 26) }

    override func accessibilityValue() -> Any? { isChosen ? "selected" : nil }

    override func draw(_ dirtyRect: NSRect) {
        let box = bounds.insetBy(dx: 1, dy: 1)
        if isChosen {
            NSColor.controlAccentColor.setFill()
            NSBezierPath(roundedRect: box, xRadius: 7, yRadius: 7).fill()
        }
        glyph(box, isChosen ? .white : .labelColor)
    }
}

/// The controls for the tool in use, floating over the top right of the canvas: colour,
/// five thicknesses, fill, and a box's corners. Only the parts the tool uses are shown,
/// and none at all for a tool with nothing to set.
final class StyleBar: NSVisualEffectView {
    /// Hands over only the part that changed. `merging` is true for the colour panel's
    /// stream of changes, which undo as one step.
    var onChange: ((_ merging: Bool, _ change: (inout Style) -> Void) -> Void)?
    /// A change is finished, so the remembered styles can be written once.
    var onCommit: (() -> Void)?
    /// The backdrop as it now is, nil for none. `merging` is true for the colour panel's
    /// stream of changes.
    var onBackdrop: ((_ merging: Bool, _ backdrop: Backdrop?) -> Void)?
    /// A Measure chip changed its lines or its edge contrast.
    var onMeasure: ((MeasureSettings) -> Void)?
    /// The ? in the Measure panel.
    var onMeasureHelp: (() -> Void)?
    /// A size chip, or a width or height typed into the Size panel and entered.
    enum SizeRequest: Equatable {
        case fraction(CGFloat), width(Int), height(Int)
    }
    var onSize: ((SizeRequest) -> Void)?
    /// Reads the desktop picture when the wallpaper fill is picked.
    var readWallpaper: (() -> Backdrop.Wallpaper?)?

    private let row = NSStackView()
    private var style = Style(colorHex: Palette.red)
    private var tool = Tool.arrow
    private var palette: NSPopover?
    private weak var colorChip: NSView?

    /// The bar shows the tool's style, the capture's backdrop, or its export size.
    private enum Mode {
        case tool, backdrop, size
    }

    private var mode = Mode.tool
    private var backdrop: Backdrop?
    private var measure = MeasureSettings.defaults
    /// A locked shape picked up: its style shows, but nothing in the bar changes it.
    private var locked = false
    /// The settings a backdrop starts from when it is turned on.
    private var remembered = Backdrop.defaults
    /// The export size in use, as a fraction of full resolution, and the pixels it makes.
    private var sizeFraction: CGFloat = 1
    private var sizePixels = CGSize.zero

    init() {
        super.init(frame: .zero)
        material = .popover
        blendingMode = .withinWindow
        state = .active
        wantsLayer = true
        layer?.cornerRadius = 10
        layer?.borderWidth = 0.5
        layer?.borderColor = NSColor.separatorColor.cgColor
        row.orientation = .horizontal
        row.spacing = 14
        row.edgeInsets = NSEdgeInsets(top: 7, left: 12, bottom: 7, right: 12)
        row.translatesAutoresizingMaskIntoConstraints = false
        addSubview(row)
        // The bar is sized by hand to fit the row once its chips are in, and a window lays it
        // out before that at its first size, zero. Just below required, the far edges give way
        // until then rather than AppKit breaking the row's own constraints and logging it.
        let trailing = row.trailingAnchor.constraint(equalTo: trailingAnchor)
        let bottom = row.bottomAnchor.constraint(equalTo: bottomAnchor)
        trailing.priority = .required - 1
        bottom.priority = .required - 1
        NSLayoutConstraint.activate([
            row.leadingAnchor.constraint(equalTo: leadingAnchor),
            trailing,
            row.topAnchor.constraint(equalTo: topAnchor),
            bottom,
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("StyleBar is created in code only")
    }

    /// Whether this tool has anything to set, and so whether the bar shows at all.
    static func shows(_ tool: Tool) -> Bool { tool.hasColor || tool.hasSize || tool.hasFill || tool.hasCorners || tool.hasOverlay }

    /// `locked` shows a locked shape's style dimmed, with nothing to press. Deleting is the
    /// layers panel's bin, or the Delete key.
    func show(tool: Tool, style: Style, measure: MeasureSettings = .defaults, locked: Bool = false) {
        guard mode != .tool || tool != self.tool || style != self.style || measure != self.measure
                || locked != self.locked || row.arrangedSubviews.isEmpty else { return }
        mode = .tool
        self.tool = tool
        self.style = style
        self.measure = measure
        self.locked = locked
        rebuild()
    }

    func showBackdrop(_ backdrop: Backdrop?, remembered: Backdrop) {
        guard mode != .backdrop || backdrop != self.backdrop || remembered != self.remembered || row.arrangedSubviews.isEmpty else { return }
        mode = .backdrop
        self.backdrop = backdrop
        self.remembered = remembered
        rebuild()
    }

    func showSize(fraction: CGFloat, pixels: CGSize) {
        guard mode != .size || fraction != sizeFraction || pixels != sizePixels || row.arrangedSubviews.isEmpty else { return }
        mode = .size
        sizeFraction = fraction
        sizePixels = pixels
        rebuild()
    }

    private func rebuild() {
        row.arrangedSubviews.forEach { $0.removeFromSuperview() }
        // A locked shape dims the tool's chips only; Backdrop and Size are the capture's own.
        row.alphaValue = 1
        guard mode == .tool else {
            if mode == .backdrop { buildBackdrop() } else { buildSize() }
            setFrameSize(fittingSize)
            return
        }
        if tool.hasColor { row.addArrangedSubview(colorButton()) }
        if tool.hasSize { row.addArrangedSubview(group(sizeChips())) }
        if tool.hasFill { row.addArrangedSubview(group(fillChips())) }
        if tool.hasCorners { row.addArrangedSubview(group(cornerChips())) }
        if tool.hasAlign { row.addArrangedSubview(group(alignChips())) }
        if tool.hasOverlay {
            row.addArrangedSubview(group(opacityChips()))
            row.addArrangedSubview(group([differenceChip()]))
        }
        if tool == .measure {
            row.addArrangedSubview(group(measureLineChips()))
            row.addArrangedSubview(contrastControl())
            row.addArrangedSubview(group([measureHelpChip()]))
        }
        row.alphaValue = locked ? 0.45 : 1
        if locked { disable(row) }
        setFrameSize(fittingSize)
    }

    private func disable(_ view: NSView) {
        (view as? NSControl)?.isEnabled = false
        view.subviews.forEach(disable)
    }

    private func group(_ chips: [NSView]) -> NSStackView {
        let stack = NSStackView(views: chips)
        stack.spacing = 2
        return stack
    }

    private func change(_ change: (inout Style) -> Void) {
        change(&style)
        onChange?(false, change)
        rebuild()
        onCommit?()
    }

    // MARK: Colour

    private var colorHex: String { mode == .backdrop ? (backdrop ?? remembered).colorHex : style.colorHex }

    private func colorButton() -> NSView {
        let button = ChipButton(label: "Colour \(colorHex)") { [weak self] box, _ in
            guard let self else { return }
            let swatch = NSBezierPath(roundedRect: box.insetBy(dx: 3, dy: 2), xRadius: 6, yRadius: 6)
            NSColor(cgColor: Palette.color(hex: self.colorHex))?.setFill()
            swatch.fill()
            NSColor.separatorColor.setStroke()
            swatch.lineWidth = 1
            swatch.stroke()
        }
        button.target = self
        button.action = #selector(showPalette(_:))
        colorChip = button
        return button
    }

    @objc private func showPalette(_ sender: NSButton) {
        palette?.close()
        let controller = ColorPaletteController(chosen: colorHex) { [weak self] hex, merging in
            self?.recolor(hex, merging: merging)
        }
        let popover = NSPopover()
        popover.contentViewController = controller
        // Semitransient, so working the colour panel does not close it and orphan it.
        popover.behavior = .semitransient
        popover.show(relativeTo: sender.bounds, of: sender, preferredEdge: .minY)
        NotificationCenter.default.addObserver(forName: NSPopover.didCloseNotification, object: popover, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                controller.detachColorPanel()
                self?.onCommit?()
            }
        }
        palette = popover
    }

    func closePalette() {
        palette?.close()
    }

    /// Only the colour button shows the colour, so a new one redraws it where it stands.
    /// Rebuilding the bar replaced the button the palette hangs from, which closed the
    /// palette and cut the colour panel off before a colour from it could land. A swatch
    /// is a finished choice, so it closes the palette, which commits.
    private func recolor(_ hex: String, merging: Bool) {
        if mode == .backdrop {
            var next = backdrop ?? remembered
            next.colorHex = hex
            backdrop = next
            onBackdrop?(merging, next)
        } else {
            style.colorHex = hex
            onChange?(merging) { $0.colorHex = hex }
        }
        colorChip?.toolTip = "Colour \(hex)"
        colorChip?.setAccessibilityLabel("Colour \(hex)")
        colorChip?.needsDisplay = true
        if !merging { palette?.close() }
    }

    // MARK: Size

    private func sizeChips() -> [NSView] {
        let sizes = StyleSize.allCases
        return sizes.enumerated().map { index, size in
            let step = CGFloat(index)
            let glyph: (NSRect, NSColor) -> Void
            switch tool {
            case .text:
                glyph = { box, color in
                    let text = NSAttributedString(string: "A", attributes: [
                        .font: NSFont.systemFont(ofSize: 9 + step * 2.5, weight: .semibold), .foregroundColor: color,
                    ])
                    let size = text.size()
                    text.draw(at: NSPoint(x: box.midX - size.width / 2, y: box.midY - size.height / 2))
                }
            case .arrow, .line, .rectangle, .oval, .freehand, .highlighter, .measure:
                glyph = { box, color in
                    let line = NSBezierPath()
                    line.move(to: NSPoint(x: box.minX + 8, y: box.midY))
                    line.line(to: NSPoint(x: box.maxX - 8, y: box.midY))
                    line.lineWidth = 1 + step * 1.3
                    line.lineCapStyle = .round
                    color.setStroke()
                    line.stroke()
                }
            default:
                glyph = { box, color in
                    let diameter = 4 + step * 2.2
                    color.setFill()
                    NSBezierPath(ovalIn: NSRect(x: box.midX - diameter / 2, y: box.midY - diameter / 2,
                                                width: diameter, height: diameter)).fill()
                }
            }
            let chip = ChipButton(label: "Size \(index + 1) of \(sizes.count), [ and ] to step", glyph: glyph)
            chip.isChosen = style.size == size
            chip.target = self
            chip.action = #selector(pickSize(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickSize(_ sender: NSButton) {
        let sizes = StyleSize.allCases
        guard sizes.indices.contains(sender.tag) else { return }
        change { $0.size = sizes[sender.tag] }
    }

    // MARK: Fill and corners

    private func fillChips() -> [NSView] {
        let isOval = tool == .oval, isText = tool == .text
        return [false, true].map { filled in
            let label = isText ? (filled ? "Text on a box" : "Plain text") : (filled ? "Filled" : "Outline")
            let chip = ChipButton(label: label) { box, color in
                let frame = box.insetBy(dx: 7, dy: 6)
                if isText { return Self.drawText(in: frame, onABox: filled, color: color) }
                let path = isOval ? NSBezierPath(ovalIn: frame) : NSBezierPath(roundedRect: frame, xRadius: 3, yRadius: 3)
                if filled {
                    color.setFill()
                    path.fill()
                } else {
                    path.lineWidth = 1.6
                    color.setStroke()
                    path.stroke()
                }
            }
            chip.isChosen = style.filled == filled
            chip.target = self
            chip.action = #selector(pickFill(_:))
            chip.tag = filled ? 1 : 0
            return chip
        }
    }

    @objc private func pickFill(_ sender: NSButton) {
        change { $0.filled = sender.tag == 1 }
    }

    /// Two lines of text, alone or cut out of a box.
    private static func drawText(in frame: NSRect, onABox: Bool, color: NSColor) {
        let lines = onABox ? frame.insetBy(dx: 3, dy: 3) : frame.insetBy(dx: 1, dy: 3)
        let path = onABox ? NSBezierPath(roundedRect: frame, xRadius: 3, yRadius: 3) : NSBezierPath()
        for (row, share) in [1.0, 0.6].enumerated() {
            path.append(NSBezierPath(rect: NSRect(x: lines.minX, y: lines.maxY - 1.6 - CGFloat(row) * (lines.height - 1.6),
                                                  width: lines.width * share, height: 1.6)))
        }
        path.windingRule = .evenOdd
        color.setFill()
        path.fill()
    }

    /// Five corner radii, square to fully round, each drawn as the corner it gives.
    private func cornerChips() -> [NSView] {
        let corners = CornerSize.allCases
        return corners.enumerated().map { index, corner in
            let label = ["Square corners", "Slightly rounded", "Rounded", "Very rounded", "Fully round"][index]
            let chip = ChipButton(label: label) { box, color in
                let frame = box.insetBy(dx: 7, dy: 6)
                let radius = corner == .full ? frame.height / 2 : [0, 1.5, 3, 5, 0][index]
                let path = NSBezierPath(roundedRect: frame, xRadius: radius, yRadius: radius)
                path.lineWidth = 1.6
                color.setStroke()
                path.stroke()
            }
            chip.isChosen = style.corners == corner
            chip.target = self
            chip.action = #selector(pickCorners(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickCorners(_ sender: NSButton) {
        let corners = CornerSize.allCases
        guard corners.indices.contains(sender.tag) else { return }
        change { $0.corners = corners[sender.tag] }
    }

    // MARK: Text alignment

    /// Left, centre and right, each drawn as three lines set that way.
    private func alignChips() -> [NSView] {
        TextAlign.allCases.enumerated().map { index, align in
            let label = ["Align left", "Centre", "Align right"][index]
            let chip = ChipButton(label: label) { box, color in
                let frame = box.insetBy(dx: 8, dy: 8)
                color.setFill()
                for (row, share) in [1.0, 0.6, 0.85].enumerated() {
                    let width = frame.width * share
                    let x: CGFloat = switch align {
                    case .left: frame.minX
                    case .center: frame.midX - width / 2
                    case .right: frame.maxX - width
                    }
                    NSBezierPath(roundedRect: NSRect(x: x, y: frame.maxY - 1.6 - CGFloat(row) * (frame.height - 1.6) / 2,
                                                     width: width, height: 1.6), xRadius: 0.8, yRadius: 0.8).fill()
                }
            }
            chip.isChosen = style.align == align
            chip.target = self
            chip.action = #selector(pickAlign(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickAlign(_ sender: NSButton) {
        let aligns = TextAlign.allCases
        guard aligns.indices.contains(sender.tag) else { return }
        change { $0.align = aligns[sender.tag] }
    }

    // MARK: Backdrop

    /// Fill first, None among them; padding, corners and shadow once there is a backdrop.
    private func buildBackdrop() {
        row.addArrangedSubview(group(backdropFillChips()))
        guard let backdrop else { return }
        if backdrop.fill == .solid { row.addArrangedSubview(colorButton()) }
        if backdrop.fill == .wallpaper, backdrop.wallpaper == nil {
            let note = NSTextField(labelWithString: "Using the gradient")
            note.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
            note.textColor = .secondaryLabelColor
            note.toolTip = "The desktop picture could not be read, so the gradient stands in"
            row.addArrangedSubview(note)
        }
        row.addArrangedSubview(group(paddingChips(backdrop)))
        row.addArrangedSubview(group(backdropCornerChips(backdrop)))
        row.addArrangedSubview(group(shadowChips(backdrop)))
    }

    /// Rebuilt before the editor hears of it, so the bar is placed at its new width.
    private func changeBackdrop(_ change: (inout Backdrop) -> Void) {
        var next = backdrop ?? remembered
        change(&next)
        backdrop = next
        rebuild()
        onBackdrop?(false, next)
    }

    private static let fillLabels = ["No backdrop", "Gradient from the capture", "Solid colour", "Desktop wallpaper", "Clear, see-through"]

    private func backdropFillChips() -> [NSView] {
        let fills: [Backdrop.Fill?] = [nil] + Backdrop.Fill.allCases
        return fills.enumerated().map { index, fill in
            let chip = ChipButton(label: Self.fillLabels[index]) { box, color in
                let frame = box.insetBy(dx: 7, dy: 6)
                let square = NSBezierPath(roundedRect: frame, xRadius: 2, yRadius: 2)
                square.lineWidth = 1.4
                color.setStroke()
                color.setFill()
                switch fill {
                case nil:
                    square.stroke()
                    let slash = NSBezierPath()
                    slash.move(to: NSPoint(x: frame.minX, y: frame.minY))
                    slash.line(to: NSPoint(x: frame.maxX, y: frame.maxY))
                    slash.lineWidth = 1.4
                    slash.stroke()
                case .gradient:
                    NSGradient(starting: color, ending: color.withAlphaComponent(0.15))?.draw(in: square, angle: -45)
                case .solid:
                    square.fill()
                case .wallpaper:
                    square.stroke()
                    let hill = NSBezierPath()
                    hill.move(to: NSPoint(x: frame.minX + 1, y: frame.minY + 1))
                    hill.line(to: NSPoint(x: frame.midX - 1, y: frame.maxY - 5))
                    hill.line(to: NSPoint(x: frame.maxX - 1, y: frame.minY + 1))
                    hill.close()
                    hill.fill()
                case .clear:
                    let cell = frame.width / 3
                    for column in 0..<3 {
                        for line in 0..<3 where (column + line) % 2 == 0 {
                            NSRect(x: frame.minX + CGFloat(column) * cell, y: frame.minY + CGFloat(line) * frame.height / 3,
                                   width: cell, height: frame.height / 3).fill()
                        }
                    }
                }
            }
            chip.isChosen = backdrop?.fill == fill
            chip.target = self
            chip.action = #selector(pickBackdropFill(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickBackdropFill(_ sender: NSButton) {
        guard sender.tag > 0 else {
            backdrop = nil
            rebuild()
            onBackdrop?(false, nil)
            return
        }
        let fill = Backdrop.Fill.allCases[sender.tag - 1]
        changeBackdrop { backdrop in
            backdrop.fill = fill
            if fill == .wallpaper, backdrop.wallpaper == nil { backdrop.wallpaper = readWallpaper?() }
        }
    }

    /// Each drawn as a frame round a smaller and smaller middle.
    private func paddingChips(_ backdrop: Backdrop) -> [NSView] {
        Backdrop.Padding.allCases.enumerated().map { index, padding in
            let chip = ChipButton(label: ["Small padding", "Medium padding", "Large padding"][index]) { box, color in
                let frame = box.insetBy(dx: 7, dy: 5)
                let outline = NSBezierPath(roundedRect: frame, xRadius: 2, yRadius: 2)
                outline.lineWidth = 1
                color.setStroke()
                outline.stroke()
                color.setFill()
                let inset = 2 + CGFloat(index) * 1.6
                NSBezierPath(rect: frame.insetBy(dx: inset, dy: inset)).fill()
            }
            chip.isChosen = backdrop.padding == padding
            chip.target = self
            chip.action = #selector(pickPadding(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickPadding(_ sender: NSButton) {
        let paddings = Backdrop.Padding.allCases
        guard paddings.indices.contains(sender.tag) else { return }
        changeBackdrop { $0.padding = paddings[sender.tag] }
    }

    private func backdropCornerChips(_ backdrop: Backdrop) -> [NSView] {
        Backdrop.cornerChoices.enumerated().map { index, corner in
            let chip = ChipButton(label: ["Square capture corners", "Round capture corners", "Rounder capture corners"][index]) { box, color in
                let frame = box.insetBy(dx: 7, dy: 6)
                let radius: CGFloat = [0, 3, 5.5][index]
                let path = NSBezierPath(roundedRect: frame, xRadius: radius, yRadius: radius)
                path.lineWidth = 1.6
                color.setStroke()
                path.stroke()
            }
            chip.isChosen = backdrop.corners == corner
            chip.target = self
            chip.action = #selector(pickBackdropCorners(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickBackdropCorners(_ sender: NSButton) {
        guard Backdrop.cornerChoices.indices.contains(sender.tag) else { return }
        changeBackdrop { $0.corners = Backdrop.cornerChoices[sender.tag] }
    }

    /// A square with no shadow, a faint one, and a dark one.
    private func shadowChips(_ backdrop: Backdrop) -> [NSView] {
        Backdrop.Shadow.allCases.enumerated().map { index, shadow in
            let chip = ChipButton(label: ["No shadow", "Soft shadow", "Strong shadow"][index]) { box, color in
                let frame = box.insetBy(dx: 8, dy: 7).offsetBy(dx: -1, dy: 1)
                if index > 0 {
                    color.withAlphaComponent(index == 1 ? 0.3 : 0.6).setFill()
                    NSBezierPath(roundedRect: frame.offsetBy(dx: 2, dy: -2), xRadius: 2, yRadius: 2).fill()
                }
                let square = NSBezierPath(roundedRect: frame, xRadius: 2, yRadius: 2)
                square.lineWidth = 1.4
                color.setStroke()
                square.stroke()
            }
            chip.isChosen = backdrop.shadow == shadow
            chip.target = self
            chip.action = #selector(pickShadow(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickShadow(_ sender: NSButton) {
        let shadows = Backdrop.Shadow.allCases
        guard shadows.indices.contains(sender.tag) else { return }
        changeBackdrop { $0.shadow = shadows[sender.tag] }
    }

    // MARK: Export size

    private static let sizeChoices: [CGFloat] = [0.25, 0.5, 1, 2]

    /// The chips, the one for the size in use lit whether it was picked or comes from
    /// Settings, then the pixels that size makes, which can be typed over.
    private func buildSize() {
        let chips = Self.sizeChoices.enumerated().map { index, fraction in
            let title = "\(Int(fraction * 100))%"
            let chip = ChipButton(label: "Export at \(title)", width: 46) { box, color in
                let text = NSAttributedString(string: title, attributes: [
                    .font: NSFont.systemFont(ofSize: 11, weight: .semibold), .foregroundColor: color,
                ])
                let size = text.size()
                text.draw(at: NSPoint(x: box.midX - size.width / 2, y: box.midY - size.height / 2))
            }
            chip.isChosen = abs(sizeFraction - fraction) < 0.0005
            chip.target = self
            chip.action = #selector(pickExportSize(_:))
            chip.tag = index
            return chip
        }
        row.addArrangedSubview(group(chips))
        let times = NSTextField(labelWithString: "×")
        times.textColor = .secondaryLabelColor
        let unit = NSTextField(labelWithString: "px")
        unit.textColor = .secondaryLabelColor
        let fields = NSStackView(views: [
            pixelField(sizePixels.width, label: "Export width in pixels, Return to apply", action: #selector(enterWidth(_:))),
            times,
            pixelField(sizePixels.height, label: "Export height in pixels, Return to apply", action: #selector(enterHeight(_:))),
            unit,
        ])
        fields.spacing = 4
        row.addArrangedSubview(fields)
    }

    private func pixelField(_ value: CGFloat, label: String, action: Selector) -> NSTextField {
        let field = NSTextField(string: String(Int(value)))
        field.font = .monospacedDigitSystemFont(ofSize: 12, weight: .regular)
        field.alignment = .right
        field.bezelStyle = .roundedBezel
        field.toolTip = label
        field.setAccessibilityLabel(label)
        field.target = self
        field.action = action
        field.widthAnchor.constraint(equalToConstant: 58).isActive = true
        return field
    }

    @objc private func pickExportSize(_ sender: NSButton) {
        guard Self.sizeChoices.indices.contains(sender.tag) else { return }
        onSize?(.fraction(Self.sizeChoices[sender.tag]))
    }

    @objc private func enterWidth(_ sender: NSTextField) {
        enter(sender, current: sizePixels.width) { .width($0) }
    }

    @objc private func enterHeight(_ sender: NSTextField) {
        enter(sender, current: sizePixels.height) { .height($0) }
    }

    /// A whole number of pixels is sent on, and the fields then show what it gave, held
    /// to the limits. Anything else puts the size in use back, selected to type over.
    private func enter(_ field: NSTextField, current: CGFloat, _ request: (Int) -> SizeRequest) {
        guard let pixels = Int(field.stringValue.trimmingCharacters(in: .whitespaces)), pixels > 0 else {
            field.stringValue = String(Int(current))
            field.selectText(nil)
            return
        }
        onSize?(request(pixels))
        rebuild()
    }

    // MARK: Measure

    /// Across and Down, the same as X and Y: a line with a tick across each end.
    private func measureLineChips() -> [NSView] {
        [(true, "Across, or X"), (false, "Down, or Y")].map { across, label in
            let chip = ChipButton(label: label) { box, color in
                let frame = box.insetBy(dx: 8, dy: 7)
                let line = NSBezierPath()
                if across {
                    line.move(to: NSPoint(x: frame.minX, y: frame.midY))
                    line.line(to: NSPoint(x: frame.maxX, y: frame.midY))
                    for x in [frame.minX, frame.maxX] {
                        line.move(to: NSPoint(x: x, y: frame.minY + 2))
                        line.line(to: NSPoint(x: x, y: frame.maxY - 2))
                    }
                } else {
                    line.move(to: NSPoint(x: frame.midX, y: frame.minY))
                    line.line(to: NSPoint(x: frame.midX, y: frame.maxY))
                    for y in [frame.minY, frame.maxY] {
                        line.move(to: NSPoint(x: frame.midX - 5, y: y))
                        line.line(to: NSPoint(x: frame.midX + 5, y: y))
                    }
                }
                line.lineWidth = 1.6
                color.setStroke()
                line.stroke()
            }
            chip.isChosen = across ? measure.across : measure.down
            chip.target = self
            chip.action = across ? #selector(toggleAcross) : #selector(toggleDown)
            return chip
        }
    }

    @objc private func toggleAcross() {
        changeMeasure { $0.across.toggle() }
    }

    @objc private func toggleDown() {
        changeMeasure { $0.down.toggle() }
    }

    /// Minus, the value, plus: the same as the down and up arrows.
    private func contrastControl() -> NSView {
        func sign(_ plus: Bool, _ label: String) -> ChipButton {
            let chip = ChipButton(label: label) { box, color in
                let sign = NSBezierPath()
                sign.move(to: NSPoint(x: box.midX - 5, y: box.midY))
                sign.line(to: NSPoint(x: box.midX + 5, y: box.midY))
                if plus {
                    sign.move(to: NSPoint(x: box.midX, y: box.midY - 5))
                    sign.line(to: NSPoint(x: box.midX, y: box.midY + 5))
                }
                sign.lineWidth = 1.6
                color.setStroke()
                sign.stroke()
            }
            chip.target = self
            chip.action = plus ? #selector(raiseContrast) : #selector(lowerContrast)
            return chip
        }
        let value = NSTextField(labelWithString: measure.contrastLabel)
        value.font = .monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        value.textColor = .secondaryLabelColor
        value.alignment = .center
        value.widthAnchor.constraint(equalToConstant: 34).isActive = true
        value.toolTip = "Edge contrast: how big a change in brightness counts as an edge"
        value.setAccessibilityLabel("Edge contrast \(measure.contrastLabel)")
        return group([sign(false, "Lower edge contrast, finds fainter edges, or the down arrow"), value,
                      sign(true, "Higher edge contrast, finds fewer edges, or the up arrow")])
    }

    @objc private func lowerContrast() {
        changeMeasure { $0.stepContrast(up: false, coarse: false) }
    }

    @objc private func raiseContrast() {
        changeMeasure { $0.stepContrast(up: true, coarse: false) }
    }

    private func measureHelpChip() -> NSView {
        let chip = ChipButton(label: "Show the Measure guide") { box, color in
            let circle = NSBezierPath(ovalIn: box.insetBy(dx: 7, dy: 5))
            circle.lineWidth = 1.4
            color.setStroke()
            circle.stroke()
            let mark = NSAttributedString(string: "?", attributes: [.font: NSFont.systemFont(ofSize: 11, weight: .bold),
                                                                    .foregroundColor: color])
            let size = mark.size()
            mark.draw(at: NSPoint(x: box.midX - size.width / 2, y: box.midY - size.height / 2))
        }
        chip.target = self
        chip.action = #selector(showMeasureHelp)
        return chip
    }

    @objc private func showMeasureHelp() {
        onMeasureHelp?()
    }

    private func changeMeasure(_ change: (inout MeasureSettings) -> Void) {
        change(&measure)
        rebuild()
        onMeasure?(measure)
    }

    // MARK: Overlay

    private static let opacities: [CGFloat] = [0.25, 0.5, 0.75, 1]

    /// How see-through a pasted image is, each chip drawn at its own opacity. Keys 1 to
    /// 9 and 0 set the steps between.
    private func opacityChips() -> [NSView] {
        Self.opacities.enumerated().map { index, opacity in
            let chip = ChipButton(label: "Opacity \(Int(opacity * 100))%, or keys 1 to 9 and 0") { box, color in
                color.withAlphaComponent(opacity).setFill()
                NSBezierPath(roundedRect: box.insetBy(dx: 8, dy: 6), xRadius: 2, yRadius: 2).fill()
            }
            chip.isChosen = abs(style.opacity - opacity) < 0.01
            chip.target = self
            chip.action = #selector(pickOpacity(_:))
            chip.tag = index
            return chip
        }
    }

    @objc private func pickOpacity(_ sender: NSButton) {
        guard Self.opacities.indices.contains(sender.tag) else { return }
        change { $0.opacity = Self.opacities[sender.tag] }
    }

    /// Two overlapping squares whose overlap is cut out, the difference blend's look.
    private func differenceChip() -> NSView {
        let chip = ChipButton(label: "Difference: what matches goes black") { box, color in
            let frame = box.insetBy(dx: 7, dy: 6)
            let back = NSRect(x: frame.minX, y: frame.minY + 3, width: frame.width - 5, height: frame.height - 3)
            let front = NSRect(x: frame.minX + 5, y: frame.minY, width: frame.width - 5, height: frame.height - 3)
            let shape = NSBezierPath(rect: back)
            shape.append(NSBezierPath(rect: front))
            shape.windingRule = .evenOdd
            color.setFill()
            shape.fill()
        }
        chip.isChosen = style.difference
        chip.target = self
        chip.action = #selector(toggleDifference)
        return chip
    }

    @objc private func toggleDifference() {
        change { $0.difference.toggle() }
    }
}

/// The colour choices: eight swatches with room around them, and the system colour
/// panel for anything else.
final class ColorPaletteController: NSViewController {
    private var chosen: String
    private let onPick: (_ hex: String, _ merging: Bool) -> Void
    private var swatches: [ChipButton] = []

    init(chosen: String, onPick: @escaping (_ hex: String, _ merging: Bool) -> Void) {
        self.chosen = chosen
        self.onPick = onPick
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) {
        fatalError("ColorPaletteController is created in code only")
    }

    override func loadView() {
        let rows = stride(from: 0, to: Palette.swatches.count, by: 4).map { start in
            let stack = NSStackView(views: Palette.swatches[start..<min(start + 4, Palette.swatches.count)].map(swatch))
            stack.spacing = 10
            return stack
        }
        let custom = NSButton(title: "Custom colour...", target: self, action: #selector(showColorPanel))
        custom.bezelStyle = .rounded
        let column = NSStackView(views: rows + [custom])
        column.orientation = .vertical
        column.spacing = 10
        column.edgeInsets = NSEdgeInsets(top: 14, left: 14, bottom: 14, right: 14)
        view = column
        // Told, or the popover picks a size of its own and squeezes the insets away.
        preferredContentSize = column.fittingSize
    }

    private func swatch(_ hex: String) -> ChipButton {
        let button = ChipButton(label: hex) { [weak self] box, _ in
            let circle = NSBezierPath(ovalIn: box.insetBy(dx: 3, dy: 1))
            NSColor(cgColor: Palette.color(hex: hex))?.setFill()
            circle.fill()
            NSColor.separatorColor.setStroke()
            circle.lineWidth = 1
            circle.stroke()
            if self?.chosen == hex {
                let ring = NSBezierPath(ovalIn: box.insetBy(dx: 0.5, dy: -1.5))
                NSColor.controlAccentColor.setStroke()
                ring.lineWidth = 2
                ring.stroke()
            }
        }
        button.target = self
        button.action = #selector(pick(_:))
        button.identifier = NSUserInterfaceItemIdentifier(hex)
        swatches.append(button)
        return button
    }

    @objc private func pick(_ sender: NSButton) {
        guard let hex = sender.identifier?.rawValue else { return }
        chosen = hex
        swatches.forEach { $0.needsDisplay = true }
        onPick(hex, false)
    }

    @objc private func showColorPanel() {
        let panel = NSColorPanel.shared
        // The colour first, while the panel points nowhere: setting it in code sends the
        // action as if it had been picked.
        panel.setTarget(nil)
        panel.color = NSColor(cgColor: Palette.color(hex: chosen)) ?? .systemRed
        Self.colorPanelOwner = self
        panel.setTarget(self)
        panel.setAction(#selector(colorPanelChanged(_:)))
        panel.orderFront(nil)
    }

    @objc private func colorPanelChanged(_ sender: NSColorPanel) {
        guard let color = sender.color.usingColorSpace(.sRGB) else { return }
        chosen = Palette.hex(red: color.redComponent, green: color.greenComponent, blue: color.blueComponent)
        swatches.forEach { $0.needsDisplay = true }
        onPick(chosen, true)
    }

    /// The colour panel outlives the palette. Left pointing here, it would keep
    /// restyling whatever became selected after the palette closed.
    func detachColorPanel() {
        guard Self.colorPanelOwner === self else { return }
        Self.colorPanelOwner = nil
        NSColorPanel.shared.setTarget(nil)
        NSColorPanel.shared.setAction(nil)
    }

    /// NSColorPanel does not say who its target is, so the palette that took it last is
    /// remembered, and only that one lets it go.
    private static weak var colorPanelOwner: ColorPaletteController?
}
