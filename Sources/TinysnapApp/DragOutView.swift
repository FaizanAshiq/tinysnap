import AppKit

/// The toolbar handle a capture is dragged out by. It writes the PNG to a temporary
/// file and drags that file, which Finder, Slack, Mail and browsers all accept.
final class DragOutView: NSView, NSDraggingSource {
    /// Writes the PNG and returns where, or nil when it could not.
    var makeFile: (() -> URL?)?
    var onDropped: (() -> Void)?

    private let icon = NSImage(systemSymbolName: "hand.draw", accessibilityDescription: "Drag out") ?? NSImage()

    /// An image view tints the template symbol like every other toolbar icon. Drawn by
    /// hand it came out near black on the dark toolbar.
    private lazy var iconView: NSImageView = {
        let view = NSImageView(image: icon)
        view.contentTintColor = .secondaryLabelColor
        view.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 15, weight: .regular)
        view.translatesAutoresizingMaskIntoConstraints = false
        return view
    }()

    override init(frame: NSRect) {
        super.init(frame: frame)
        addSubview(iconView)
        NSLayoutConstraint.activate([
            iconView.centerXAnchor.constraint(equalTo: centerXAnchor),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("DragOutView is created in code only")
    }

    override var intrinsicContentSize: NSSize { NSSize(width: 28, height: 24) }

    /// In the title bar a view moves the window by default, so dragging the capture out
    /// dragged the window along with it.
    override var mouseDownCanMoveWindow: Bool { false }

    // A button to accessibility, named for what it does, so VoiceOver does not skip it.
    override func isAccessibilityElement() -> Bool { true }
    override func accessibilityRole() -> NSAccessibility.Role? { .button }
    override func accessibilityLabel() -> String? { "Drag out the capture" }
    override func accessibilityHelp() -> String? { "Drag into Finder, Mail or another app to drop the capture there as a PNG" }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .openHand)
    }

    override func mouseDown(with event: NSEvent) {
        guard let url = makeFile?() else {
            NSSound.beep()
            return
        }
        let item = NSDraggingItem(pasteboardWriter: url as NSURL)
        let preview = NSImage(contentsOf: url) ?? icon
        let side: CGFloat = 64
        let aspect = preview.size.width > 0 ? preview.size.height / preview.size.width : 1
        item.setDraggingFrame(NSRect(x: 0, y: 0, width: side, height: side * aspect), contents: preview)
        beginDraggingSession(with: [item], event: event, source: self)
    }

    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        .copy
    }

    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) {
        if !operation.isEmpty { onDropped?() }
    }
}
