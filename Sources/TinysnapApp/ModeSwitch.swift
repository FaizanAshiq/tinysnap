import AppKit
import TinysnapCore

/// Essential or Pro side by side, the chosen one filled with the accent: the editor's two
/// toolbars, one click apart. A segmented control in a macOS 26 toolbar marks its choice in
/// grey glass, which reads as off as easily as on.
final class ModeSwitch: NSView {
    private let segments: [(mode: EditorMode, button: NSButton)]
    var mode: EditorMode { didSet { paint() } }
    /// A mode picked here, not one shown for a choice made elsewhere.
    var onPick: ((EditorMode) -> Void)?

    init(mode: EditorMode) {
        self.mode = mode
        segments = [(.essential, "Essential", "The everyday tools"), (.pro, "Pro", "Every tool, grouped by kind")].map { mode, title, tip in
            let button = NSButton(title: title, target: nil, action: nil)
            button.isBordered = false
            button.wantsLayer = true
            button.layer?.cornerRadius = 10
            button.toolTip = tip
            button.setAccessibilityRole(.radioButton)
            button.translatesAutoresizingMaskIntoConstraints = false
            button.heightAnchor.constraint(equalToConstant: 22).isActive = true
            button.widthAnchor.constraint(greaterThanOrEqualToConstant: title.count > 3 ? 74 : 40).isActive = true
            return (mode, button)
        }
        super.init(frame: .zero)
        let stack = NSStackView(views: segments.map(\.button))
        stack.spacing = 2
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: trailingAnchor),
            stack.topAnchor.constraint(equalTo: topAnchor),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
        for (mode, button) in segments {
            button.target = self
            button.action = #selector(picked(_:))
            button.tag = mode == .essential ? 0 : 1
        }
        paint()
    }

    required init?(coder: NSCoder) {
        fatalError("ModeSwitch is created in code only")
    }

    @objc private func picked(_ sender: NSButton) {
        let picked: EditorMode = sender.tag == 0 ? .essential : .pro
        guard picked != mode else { return }
        mode = picked
        onPick?(picked)
    }

    /// The accent resolves to a colour for the current appearance, so it is painted again on a change.
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        paint()
    }

    private func paint() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            for (mode, button) in segments {
                let chosen = mode == self.mode
                button.layer?.backgroundColor = chosen ? NSColor.controlAccentColor.cgColor : NSColor.clear.cgColor
                button.attributedTitle = NSAttributedString(string: button.title, attributes: [
                    .font: NSFont.systemFont(ofSize: 12, weight: .semibold),
                    .foregroundColor: chosen ? NSColor.white : NSColor.secondaryLabelColor,
                ])
                button.setAccessibilityValue(chosen ? 1 : 0)
            }
        }
    }
}
