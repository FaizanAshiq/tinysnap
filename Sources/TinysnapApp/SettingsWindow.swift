import AppKit
import ServiceManagement
import TinysnapCore

/// A short stack of standard controls. Anything not here is still reachable by editing
/// preferences.json, which is why this window stays small on purpose.
@MainActor
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    private var preferences: Preferences
    private let onChange: (Preferences) -> Set<HotKeyAction>
    private let onRecordingChange: (Bool) -> Void
    private var recorders: [HotKeyAction: HotKeyRecorderView] = [:]
    private let folderLabel = NSTextField(labelWithString: "")
    private let scalePopUp = NSPopUpButton()
    private let delayStepper = NSStepper()
    private let delayLabel = NSTextField(labelWithString: "")
    private let loginCheckbox = NSButton(checkboxWithTitle: "Open Tinysnap when you log in", target: nil, action: nil)
    private let menuBarIconCheckbox = NSButton(checkboxWithTitle: "Show the menu bar icon", target: nil, action: nil)
    private let dockIconCheckbox = NSButton(checkboxWithTitle: "Show in the Dock while a capture is open", target: nil, action: nil)
    private let screenAccessButton = NSButton(title: "", target: nil, action: nil)

    /// `onChange` applies the new preferences and returns the hotkeys another app holds.
    /// `onRecordingChange` pauses the hotkeys while a field is recording.
    init(preferences: Preferences, taken: Set<HotKeyAction>, onChange: @escaping (Preferences) -> Set<HotKeyAction>,
         onRecordingChange: @escaping (Bool) -> Void) {
        self.preferences = preferences
        self.onChange = onChange
        self.onRecordingChange = onRecordingChange
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 440, height: 360),
                              styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Tinysnap Settings"
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self
        buildLayout()
        loadValues(taken: taken)
    }

    required init?(coder: NSCoder) {
        fatalError("SettingsWindowController is created in code only")
    }

    private func buildLayout() {
        var rows: [(String, NSView)] = []
        for action in HotKeyAction.allCases {
            let recorder = HotKeyRecorderView(binding: preferences.hotkeys[action])
            recorder.onRecord = { [weak self] binding in self?.record(binding, for: action) ?? false }
            recorder.onRecordingChange = { [weak self] recording in self?.onRecordingChange(recording) }
            recorder.accessibilityName = "\(action.title) hotkey"
            recorders[action] = recorder
            rows.append((action.title, recorder))
        }

        let chooseFolder = NSButton(title: "Choose", target: self, action: #selector(chooseFolder))
        chooseFolder.bezelStyle = .rounded
        folderLabel.lineBreakMode = .byTruncatingMiddle
        folderLabel.widthAnchor.constraint(equalToConstant: 180).isActive = true
        rows.append(("Save folder", NSStackView(views: [folderLabel, chooseFolder])))

        scalePopUp.addItems(withTitles: ["Full resolution", "1x, one pixel per point"])
        scalePopUp.target = self
        scalePopUp.action = #selector(scaleChanged)
        rows.append(("Export", scalePopUp))

        delayStepper.minValue = Double(Preferences.delayRange.lowerBound)
        delayStepper.maxValue = 10
        delayStepper.increment = 1
        delayStepper.target = self
        delayStepper.action = #selector(delayChanged)
        rows.append(("Delay", NSStackView(views: [delayStepper, delayLabel])))

        loginCheckbox.target = self
        loginCheckbox.action = #selector(loginChanged)
        rows.append(("", loginCheckbox))

        menuBarIconCheckbox.target = self
        menuBarIconCheckbox.action = #selector(iconsChanged)
        menuBarIconCheckbox.toolTip = "Hidden, the hotkeys still work, and opening Tinysnap again from Finder or Spotlight shows Settings"
        rows.append(("", menuBarIconCheckbox))

        dockIconCheckbox.target = self
        dockIconCheckbox.action = #selector(iconsChanged)
        rows.append(("", dockIconCheckbox))

        screenAccessButton.bezelStyle = .rounded
        screenAccessButton.target = self
        screenAccessButton.action = #selector(requestScreenAccess)
        rows.append(("Screen Recording", screenAccessButton))

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        for (label, control) in rows {
            let row = NSStackView()
            row.spacing = 10
            let text = NSTextField(labelWithString: label)
            text.alignment = .right
            text.widthAnchor.constraint(equalToConstant: 130).isActive = true
            row.addArrangedSubview(text)
            row.addArrangedSubview(control)
            stack.addArrangedSubview(row)
        }
        window?.contentView = stack
    }

    private func loadValues(taken: Set<HotKeyAction>) {
        for (action, recorder) in recorders {
            recorder.binding = preferences.hotkeys[action]
            recorder.isTaken = taken.contains(action)
        }
        folderLabel.stringValue = (preferences.saveFolderURL.path as NSString).abbreviatingWithTildeInPath
        folderLabel.toolTip = preferences.saveFolderURL.path
        scalePopUp.selectItem(at: preferences.exportScale == .native ? 0 : 1)
        delayStepper.integerValue = preferences.delaySeconds
        delayLabel.stringValue = "\(preferences.delaySeconds) seconds"
        loginCheckbox.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menuBarIconCheckbox.state = preferences.showMenuBarIcon ? .on : .off
        dockIconCheckbox.state = preferences.showDockIconWhileCapturing ? .on : .off
        refreshScreenAccess()
    }

    /// Refuses a combination another Tinysnap action already uses.
    private func record(_ binding: HotKeyBinding?, for action: HotKeyAction) -> Bool {
        if let binding, let owner = preferences.hotkeys.action(using: binding), owner != action { return false }
        preferences.hotkeys[action] = binding
        persist()
        return true
    }

    @objc private func chooseFolder() {
        guard let window else { return }
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.directoryURL = preferences.saveFolderURL
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .OK, let url = panel.url else { return }
            self.preferences.saveFolder = (url.path as NSString).abbreviatingWithTildeInPath
            self.persist()
        }
    }

    @objc private func scaleChanged() {
        preferences.exportScale = scalePopUp.indexOfSelectedItem == 0 ? .native : .oneX
        persist()
    }

    @objc private func iconsChanged() {
        preferences.showMenuBarIcon = menuBarIconCheckbox.state == .on
        preferences.showDockIconWhileCapturing = dockIconCheckbox.state == .on
        persist()
    }

    @objc private func delayChanged() {
        preferences.delaySeconds = delayStepper.integerValue
        persist()
    }

    /// Asked of macOS every time rather than stored, so it can never disagree with
    /// Login Items in System Settings.
    @objc private func loginChanged() {
        do {
            if loginCheckbox.state == .on {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            let alert = NSAlert()
            alert.messageText = "macOS did not change the login item."
            alert.informativeText = error.localizedDescription
            alert.runModal()
        }
        loginCheckbox.state = SMAppService.mainApp.status == .enabled ? .on : .off
    }

    private func refreshScreenAccess() {
        let granted = ScreenAccess.isGranted
        screenAccessButton.title = granted ? "Granted" : "Enable in System Settings"
        screenAccessButton.isEnabled = !granted
    }

    @objc private func requestScreenAccess() {
        ScreenAccess.request()
        ScreenAccess.openSettings()
    }

    private func persist() {
        // Editors write the remembered tool styles, possibly while this window is open.
        // Saving the copy taken when it opened would undo them.
        if let onDisk = try? Preferences.load(from: Preferences.defaultFileURL) {
            preferences.toolStyles = onDisk.toolStyles
            preferences.colorHex = onDisk.colorHex
        }
        try? preferences.save(to: Preferences.defaultFileURL)
        loadValues(taken: onChange(preferences))
    }

    /// The controller outlives the window, so a recorder left listening would reopen
    /// still listening.
    func windowWillClose(_ notification: Notification) {
        recorders.values.forEach { $0.stopRecording() }
    }

    func show() {
        refreshScreenAccess()
        window?.center()
        showWindow(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
}
