import AppKit
import TinysnapCore

/// What the Measure tool does, shown from its toolbar button the first time it is picked
/// and from the ? in its panel after that: a moving example, then every key beside the
/// chip that does the same thing.
final class MeasureGuideController: NSViewController {
    private let onDone: () -> Void

    init(onDone: @escaping () -> Void) {
        self.onDone = onDone
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) {
        fatalError("MeasureGuideController is created in code only")
    }

    override func loadView() {
        let width: CGFloat = 288
        let title = NSTextField(labelWithString: "Measure the space under the pointer")
        title.font = .systemFont(ofSize: 13, weight: .semibold)

        let demo = MeasureGuideDemo()
        demo.translatesAutoresizingMaskIntoConstraints = false
        demo.widthAnchor.constraint(equalToConstant: width).isActive = true
        demo.heightAnchor.constraint(equalToConstant: 84).isActive = true

        let keys = NSGridView(views: [
            [KeyCaps(["X"]), note("Across the space, or the Across chip")],
            [KeyCaps(["Y"]), note("Down it, or the Down chip. Both at once show both")],
            [KeyCaps(["Click"]), note("Keeps the reading on the capture")],
            [KeyCaps(["↑", "↓"]), note("Finds more or fewer edges, when one is missed")],
        ])
        keys.rowSpacing = 10
        keys.columnSpacing = 12
        keys.column(at: 0).width = 58
        for row in 0..<keys.numberOfRows { keys.row(at: row).yPlacement = .center }

        let footnote = NSTextField(labelWithString: "The ? brings this back")
        footnote.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        footnote.textColor = .secondaryLabelColor
        let done = NSButton(title: "Got it", target: self, action: #selector(finish))
        done.bezelStyle = .rounded
        done.keyEquivalent = "\r"
        let footer = NSStackView(views: [footnote, done])
        footer.distribution = .equalSpacing
        footer.widthAnchor.constraint(equalToConstant: width).isActive = true

        let column = NSStackView(views: [title, demo, keys, footer])
        column.orientation = .vertical
        column.alignment = .leading
        column.spacing = 14
        column.edgeInsets = NSEdgeInsets(top: 16, left: 16, bottom: 16, right: 16)
        view = column
        // Told, or the popover picks a size of its own and squeezes the insets away.
        preferredContentSize = column.fittingSize
    }

    private func note(_ text: String) -> NSTextField {
        let label = NSTextField(wrappingLabelWithString: text)
        label.font = .systemFont(ofSize: 12)
        label.textColor = .secondaryLabelColor
        label.preferredMaxLayoutWidth = 218
        return label
    }

    @objc private func finish() {
        onDone()
    }
}

/// Keys drawn as caps, side by side.
private final class KeyCaps: NSView {
    private let keys: [String]
    private static let font = NSFont.systemFont(ofSize: 11, weight: .semibold)

    init(_ keys: [String]) {
        self.keys = keys
        super.init(frame: .zero)
        setAccessibilityElement(true)
        setAccessibilityRole(.staticText)
        setAccessibilityLabel(keys.joined(separator: " or "))
    }

    required init?(coder: NSCoder) {
        fatalError("KeyCaps is created in code only")
    }

    private func width(of key: String) -> CGFloat {
        max(22, NSAttributedString(string: key, attributes: [.font: Self.font]).size().width + 12)
    }

    override var intrinsicContentSize: NSSize {
        NSSize(width: keys.map(width).reduce(0, +) + CGFloat(keys.count - 1) * 4, height: 22)
    }

    override func draw(_ dirtyRect: NSRect) {
        var x: CGFloat = 0
        for key in keys {
            let cap = NSRect(x: x, y: 0, width: width(of: key), height: 22)
            NSColor.quaternaryLabelColor.setFill()
            NSBezierPath(roundedRect: cap, xRadius: 5, yRadius: 5).fill()
            let text = NSAttributedString(string: key, attributes: [.font: Self.font, .foregroundColor: NSColor.labelColor])
            let size = text.size()
            text.draw(at: NSPoint(x: cap.midX - size.width / 2, y: cap.midY - size.height / 2))
            x = cap.maxX + 4
        }
    }
}

/// Two cards and a measurement that moves from the gap between them to the width of one
/// every two seconds, the way a reading follows the pointer. Still with Reduce Motion.
private final class MeasureGuideDemo: NSView {
    private var showsCard = false {
        didSet { needsDisplay = true }
    }
    private var timer: Timer?

    override var isFlipped: Bool { true }

    override func viewDidMoveToWindow() {
        timer?.invalidate()
        timer = nil
        guard window != nil, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        timer = Timer.scheduledTimer(timeInterval: 2, target: self, selector: #selector(advance), userInfo: nil, repeats: true)
    }

    @objc private func advance() {
        showsCard.toggle()
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        NSColor(white: 0.95, alpha: 1).setFill()
        NSBezierPath(roundedRect: bounds, xRadius: 8, yRadius: 8).fill()
        let left = NSRect(x: 22, y: 18, width: 92, height: 50)
        let right = NSRect(x: bounds.maxX - 114, y: 18, width: 92, height: 50)
        NSColor.white.setFill()
        for card in [left, right] { NSBezierPath(roundedRect: card, xRadius: 7, yRadius: 7).fill() }
        let y = left.midY
        let line = showsCard
            ? (CGPoint(x: left.minX, y: y), CGPoint(x: left.maxX, y: y))
            : (CGPoint(x: left.maxX, y: y), CGPoint(x: right.minX, y: y))
        MeasureShape.draw(from: line.0, to: line.1, width: 1.5, color: Palette.color(hex: Palette.red), scale: 1, in: context)
    }
}
