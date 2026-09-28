import AppKit
import TinysnapCore

/// Reads the text, or what QR codes hold, in an image off the main thread, copies it,
/// and shows what it copied in a toast at the top of `screen`.
@MainActor
enum TextCopy {
    private static var toast: TextToast?

    static func read(_ image: CGImage, for target: TextReader.Target, on screen: NSScreen?) {
        Task {
            let reading = await Task.detached(priority: .userInitiated) { try? TextReader.read(image, for: target) }.value
            show(reading, for: target, on: screen)
        }
    }

    private static func show(_ reading: TextReading?, for target: TextReader.Target, on screen: NSScreen?) {
        toast?.dismiss()
        let scanning = target == .codes
        guard let reading else {
            toast = TextToast(title: scanning ? "Could not scan for a QR code" : "Could not read text", preview: "",
                              reading: nil, on: screen)
            return
        }
        guard !reading.isEmpty else {
            toast = TextToast(title: scanning ? "No QR code found" : "No text found", preview: "", reading: nil, on: screen)
            return
        }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(reading.text, forType: .string)
        let preview = reading.text.components(separatedBy: "\n").prefix(4).joined(separator: "\n")
        let title = !scanning ? "Text copied" : reading.codes.count == 1 ? "QR code copied" : "\(reading.codes.count) QR codes copied"
        toast = TextToast(title: title, preview: preview, reading: reading, on: screen)
    }
}

/// A button that works on the first click in a panel that is not key, as the toast and
/// the thumbnail always are.
final class FirstClickButton: NSButton {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// A small panel at the top of the screen: what was copied, Join Lines, Open Link. It
/// stays five seconds after the pointer leaves it, and never takes focus.
@MainActor
final class TextToast: NSObject {
    private let panel: NSPanel
    private let titleLabel: NSTextField
    private let previewLabel: NSTextField
    private let reading: TextReading?
    private var timer: Timer?

    init(title: String, preview: String, reading: TextReading?, on screen: NSScreen?) {
        self.reading = reading
        panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 380, height: 60),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        titleLabel = NSTextField(labelWithString: title)
        previewLabel = NSTextField(wrappingLabelWithString: preview)
        super.init()

        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        panel.isReleasedWhenClosed = false
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true

        let background = HoverView()
        background.material = .hudWindow
        background.state = .active
        background.wantsLayer = true
        background.layer?.cornerRadius = 12
        background.onHover = { [weak self] inside in self?.hover(inside) }

        titleLabel.font = .systemFont(ofSize: 13, weight: .semibold)
        previewLabel.font = .systemFont(ofSize: 12)
        previewLabel.textColor = .secondaryLabelColor
        previewLabel.maximumNumberOfLines = 4
        previewLabel.lineBreakMode = .byTruncatingTail
        previewLabel.preferredMaxLayoutWidth = 348
        previewLabel.isHidden = preview.isEmpty

        var buttons: [NSView] = []
        if let reading, reading.codes.isEmpty, reading.lines.count > 1 {
            buttons.append(button("Join Lines", #selector(joinLines)))
        }
        if reading?.link != nil {
            buttons.append(button("Open Link", #selector(openLink)))
        }
        let row = NSStackView(views: buttons)
        row.isHidden = buttons.isEmpty

        let stack = NSStackView(views: [titleLabel, previewLabel, row])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 6
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 16, bottom: 12, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false
        background.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: background.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: background.trailingAnchor),
            stack.topAnchor.constraint(equalTo: background.topAnchor),
            stack.bottomAnchor.constraint(equalTo: background.bottomAnchor),
            background.widthAnchor.constraint(equalToConstant: 380),
        ])
        panel.contentView = background
        panel.setContentSize(background.fittingSize)

        let visible = (screen ?? NSScreen.main)?.visibleFrame ?? .zero
        panel.setFrameOrigin(NSPoint(x: visible.midX - panel.frame.width / 2, y: visible.maxY - panel.frame.height - 12))
        panel.orderFrontRegardless()
        NSAccessibility.post(element: NSApp as Any, notification: .announcementRequested, userInfo: [
            .announcement: preview.isEmpty ? title : "\(title). \(preview)",
            .priority: NSAccessibilityPriorityLevel.high.rawValue,
        ])
        startTimer()
    }

    private func button(_ title: String, _ action: Selector) -> NSButton {
        let button = FirstClickButton(title: title, target: self, action: action)
        button.bezelStyle = .rounded
        button.controlSize = .small
        return button
    }

    private func startTimer() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.dismiss() }
        }
    }

    private func hover(_ inside: Bool) {
        if inside {
            timer?.invalidate()
        } else {
            startTimer()
        }
    }

    @objc private func joinLines() {
        guard let reading else { return }
        let joined = TextReader.join(reading.text)
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(joined, forType: .string)
        titleLabel.stringValue = "Joined and copied"
        previewLabel.stringValue = joined
        panel.setContentSize(panel.contentView?.fittingSize ?? panel.frame.size)
    }

    @objc private func openLink() {
        guard let link = reading?.link else { return }
        NSWorkspace.shared.open(link)
        dismiss()
    }

    func dismiss() {
        timer?.invalidate()
        panel.orderOut(nil)
    }
}

/// Tells its owner when the pointer comes and goes, so a toast or a thumbnail stays up
/// while it is being looked at.
final class HoverView: NSVisualEffectView {
    var onHover: ((Bool) -> Void)?
    private var area: NSTrackingArea?

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let area { removeTrackingArea(area) }
        let tracking = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                                      owner: self, userInfo: nil)
        addTrackingArea(tracking)
        area = tracking
    }

    override func mouseEntered(with event: NSEvent) { onHover?(true) }
    override func mouseExited(with event: NSEvent) { onHover?(false) }
}
