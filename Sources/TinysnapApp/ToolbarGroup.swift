import AppKit

/// One kind of toolbar button as a single toolbar item, so macOS 26 draws one glass capsule behind
/// the whole kind, with the chosen tool filled with the accent, as the Essential and Pro switch's
/// choice is. Tools as separate items had no capsule at all: macOS draws selectable items bare.
/// Before macOS 26 there is no glass, so the group paints a rounded ground of its own.
final class ToolbarGroup: NSView {
    enum Entry {
        case button(id: String, symbol: String, tip: String, action: () -> Void)
        case view(NSView)
    }

    private var buttons: [(id: String, button: NSButton)] = []
    private var actions: [() -> Void] = []
    /// The button shown as chosen, by id; nil for none.
    var chosen: String? { didSet { paint() } }

    init(_ entries: [Entry]) {
        super.init(frame: .zero)
        var views: [NSView] = []
        for entry in entries {
            switch entry {
            case let .button(id, symbol, tip, action):
                let button = NSButton(image: Self.image(symbol, tip), target: self, action: #selector(pressed(_:)))
                button.isBordered = false
                button.wantsLayer = true
                button.layer?.cornerRadius = 11
                button.toolTip = tip
                button.setAccessibilityLabel(tip)
                button.tag = actions.count
                button.translatesAutoresizingMaskIntoConstraints = false
                button.widthAnchor.constraint(equalToConstant: 28).isActive = true
                button.heightAnchor.constraint(equalToConstant: 24).isActive = true
                actions.append(action)
                buttons.append((id, button))
                views.append(button)
            case let .view(view):
                views.append(view)
            }
        }
        let stack = NSStackView(views: views)
        stack.spacing = 2
        stack.edgeInsets = NSEdgeInsets(top: 2, left: 3, bottom: 2, right: 3)
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: trailingAnchor),
            stack.topAnchor.constraint(equalTo: topAnchor),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
        wantsLayer = true
        layer?.cornerRadius = 14
        paint()
    }

    required init?(coder: NSCoder) {
        fatalError("ToolbarGroup is created in code only")
    }

    /// The button with this id, for a popover to point at.
    func button(_ id: String) -> NSButton? {
        buttons.first { $0.id == id }?.button
    }

    func setSymbol(_ symbol: String, for id: String) {
        guard let button = button(id) else { return }
        button.image = Self.image(symbol, button.toolTip ?? "")
    }

    @objc private func pressed(_ sender: NSButton) {
        actions[sender.tag]()
    }

    /// The accent resolves to a colour for the current appearance, so it is painted again on a change.
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        paint()
    }

    private func paint() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            if #unavailable(macOS 26) {
                self.layer?.backgroundColor = NSColor.quaternaryLabelColor.cgColor
            }
            for (id, button) in self.buttons {
                let chosen = id == self.chosen
                button.layer?.backgroundColor = chosen ? NSColor.controlAccentColor.cgColor : NSColor.clear.cgColor
                button.contentTintColor = chosen ? .white : .labelColor
            }
        }
    }

    private static func image(_ symbol: String, _ tip: String) -> NSImage {
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: tip) ?? NSImage()
        return image.withSymbolConfiguration(NSImage.SymbolConfiguration(pointSize: 14, weight: .regular)) ?? image
    }
}
