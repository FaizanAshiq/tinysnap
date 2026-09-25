import AppKit
import TinysnapCore

/// A small square button that draws its own glyph, with a solid accent fill when it is
/// the chosen one.
final class ChipButton: NSButton {
    var isChosen = false { didSet { needsDisplay = true } }
    private let glyph: (NSRect, NSColor) -> Void

    init(label: String, glyph: @escaping (NSRect, NSColor) -> Void) {
        self.glyph = glyph
        super.init(frame: NSRect(x: 0, y: 0, width: 30, height: 26))
        title = ""
        isBordered = false
        setButtonType(.momentaryChange)
        toolTip = label
        setAccessibilityLabel(label)
    }

    required init?(coder: NSCoder) {
        fatalError("ChipButton is created in code only")
    }

    override var intrinsicContentSize: NSSize { NSSize(width: 30, height: 26) }

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

    private let row = NSStackView()
    private var style = Style(colorHex: Palette.red)
    private var tool = Tool.arrow
    private var palette: NSPopover?

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
        NSLayoutConstraint.activate([
            row.leadingAnchor.constraint(equalTo: leadingAnchor),
            row.trailingAnchor.constraint(equalTo: trailingAnchor),
            row.topAnchor.constraint(equalTo: topAnchor),
            row.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("StyleBar is created in code only")
    }

    /// Whether this tool has anything to set, and so whether the bar shows at all.
    static func shows(_ tool: Tool) -> Bool { tool.hasColor || tool.hasSize || tool.hasFill || tool.hasCorners }

    func show(tool: Tool, style: Style) {
        guard tool != self.tool || style != self.style || row.arrangedSubviews.isEmpty else { return }
        self.tool = tool
        self.style = style
        rebuild()
    }

    private func rebuild() {
        row.arrangedSubviews.forEach { $0.removeFromSuperview() }
        if tool.hasColor { row.addArrangedSubview(colorButton()) }
        if tool.hasSize { row.addArrangedSubview(group(sizeChips())) }
        if tool.hasFill { row.addArrangedSubview(group(fillChips())) }
        if tool.hasCorners { row.addArrangedSubview(group(cornerChips())) }
        setFrameSize(fittingSize)
    }

    private func group(_ chips: [NSView]) -> NSStackView {
        let stack = NSStackView(views: chips)
        stack.spacing = 2
        return stack
    }

    private func change(merging: Bool = false, _ change: (inout Style) -> Void) {
        change(&style)
        onChange?(merging, change)
        rebuild()
        if !merging { onCommit?() }
    }

    // MARK: Colour

    private func colorButton() -> NSView {
        let hex = style.colorHex
        let button = ChipButton(label: "Colour \(hex)") { box, _ in
            let swatch = NSBezierPath(roundedRect: box.insetBy(dx: 3, dy: 2), xRadius: 6, yRadius: 6)
            NSColor(cgColor: Palette.color(hex: hex))?.setFill()
            swatch.fill()
            NSColor.separatorColor.setStroke()
            swatch.lineWidth = 1
            swatch.stroke()
        }
        button.target = self
        button.action = #selector(showPalette(_:))
        return button
    }

    @objc private func showPalette(_ sender: NSButton) {
        palette?.close()
        let controller = ColorPaletteController(chosen: style.colorHex) { [weak self] hex, merging in
            self?.change(merging: merging) { $0.colorHex = hex }
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
            case .arrow, .line, .rectangle, .oval, .freehand, .highlighter:
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
        let isOval = tool == .oval
        return [false, true].map { filled in
            let chip = ChipButton(label: filled ? "Filled" : "Outline") { box, color in
                let frame = box.insetBy(dx: 7, dy: 6)
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
        Self.colorPanelOwner = self
        panel.setTarget(self)
        panel.setAction(#selector(colorPanelChanged(_:)))
        panel.color = NSColor(cgColor: Palette.color(hex: chosen)) ?? .systemRed
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
