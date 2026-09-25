import AppKit
import TinysnapCore

/// The menu bar item, the hotkeys, and the way from a capture request to an editor.
/// Everything here touches AppKit, which is main thread only. Swift 6 does not infer
/// that from the delegate conformance, so it is stated once on the class.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem?
    private var hotKeys: HotKeyCenter?
    private var takenHotKeys: Set<HotKeyAction> = []
    private var preferences: Preferences = .defaults
    private var editors: [EditorWindowController] = []
    private var overlay: AreaOverlayController?
    private var settings: SettingsWindowController?
    private var lastArea: (displayID: CGDirectDisplayID, rect: CGRect)?
    private var secondsLeft: Int?
    private var isFreezing = false

    /// Whether the screen could be read when the app started. Granting the permission
    /// flips the check straight away, but captures keep failing until the app restarts,
    /// so this tells "not granted" apart from "granted, restart to finish".
    private var couldReadScreenAtLaunch = false
    private var hasOfferedScreenAccess = false
    private var suppressRelaunchOnQuit = false
    private var hasConfirmedQuit = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        couldReadScreenAtLaunch = ScreenAccess.isGranted
        preferences = (try? Preferences.load(from: Preferences.defaultFileURL)) ?? .defaults
        MainMenu.install()
        installStatusItem()
        statusItem?.isVisible = preferences.showMenuBarIcon

        let center = HotKeyCenter { [weak self] action in self?.perform(action) }
        takenHotKeys = center.register(preferences.hotkeys)
        hotKeys = center

        Task { await ScreenReader.warmUp() }
    }

    /// `open -a Tinysnap picture.png`, or a file dropped on the app, opens in an editor.
    /// This is also how the editor is tried out on a synthetic image, with no screen
    /// content involved.
    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            guard let image = NSImage(contentsOf: url),
                  let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
                  image.size.width > 0 else { continue }
            openEditor(Capture(image: cgImage, scale: CGFloat(cgImage.width) / image.size.width), on: nil,
                       title: url.lastPathComponent)
        }
    }

    // MARK: Menu bar

    private func installStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.image = NSImage(systemSymbolName: "camera.viewfinder", accessibilityDescription: "Tinysnap")
        item.button?.image?.isTemplate = true
        item.button?.imagePosition = .imageLeading
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item
    }

    /// Rebuilt every time it opens, so the permission rows come and go as the grant does
    /// and the hotkeys shown always match the preferences.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()

        if !ScreenAccess.isGranted {
            menu.addItem(NSMenuItem(title: "Enable Screen Recording", action: #selector(enableScreenAccess), keyEquivalent: ""))
            menu.addItem(note("Every capture needs it. Nothing else does"))
            menu.addItem(.separator())
        } else if !couldReadScreenAtLaunch {
            menu.addItem(NSMenuItem(title: "Restart Tinysnap to Finish Enabling", action: #selector(restart), keyEquivalent: ""))
            menu.addItem(note("macOS only lets an app read the screen after a restart"))
            menu.addItem(.separator())
        }

        for action in HotKeyAction.allCases {
            var title = action.title
            if action == .delayed, let secondsLeft { title = "Capturing in \(secondsLeft)" }
            if let binding = preferences.hotkeys[action] {
                title += "  \(binding.displayString)" + (takenHotKeys.contains(action) ? " (taken)" : "")
            }
            let item = NSMenuItem(title: title, action: #selector(captureFromMenu(_:)), keyEquivalent: "")
            item.representedObject = action.rawValue
            item.isEnabled = !(action == .repeatArea && lastArea == nil) && secondsLeft == nil
            menu.addItem(item)
        }

        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Settings...", action: #selector(showSettings(_:)), keyEquivalent: ","))
        menu.addItem(NSMenuItem(title: "Quit Tinysnap", action: #selector(quit), keyEquivalent: "q"))
        menu.autoenablesItems = false
        menu.items.forEach { $0.target = self }
    }

    private func note(_ text: String) -> NSMenuItem {
        let item = NSMenuItem(title: text, action: nil, keyEquivalent: "")
        item.attributedTitle = NSAttributedString(string: text, attributes: [
            .font: NSFont.menuFont(ofSize: NSFont.smallSystemFontSize),
            .foregroundColor: NSColor.secondaryLabelColor,
        ])
        item.isEnabled = false
        return item
    }

    /// The menu fades out after the click, so the freeze waits for it to be gone rather
    /// than photographing it.
    @objc private func captureFromMenu(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String, let action = HotKeyAction(rawValue: raw) else { return }
        Task {
            try? await Task.sleep(for: .milliseconds(250))
            perform(action)
        }
    }

    // MARK: Capturing

    private func perform(_ action: HotKeyAction) {
        switch action {
        case .area: captureArea()
        case .fullscreen: captureFullscreen()
        case .repeatArea: repeatLastArea()
        case .delayed: startDelayedCapture()
        }
    }

    /// The way into every capture. Asked at the point of use, and once per launch, so
    /// saying no is not punished with the same box every time.
    private func ensureScreenAccess() -> Bool {
        if ScreenAccess.isGranted {
            guard couldReadScreenAtLaunch else {
                offerRestart()
                return false
            }
            return true
        }
        guard !hasOfferedScreenAccess else {
            NSSound.beep()
            return false
        }
        hasOfferedScreenAccess = true

        let alert = NSAlert()
        alert.messageText = "Tinysnap needs Screen Recording to capture"
        alert.informativeText = "Every capture reads the screen, so macOS asks for this once. Turn Tinysnap on under Screen Recording, then restart it from the menu bar."
        alert.addButton(withTitle: "Open System Settings")
        // Escape answers the quiet option, as Cancel would.
        alert.addButton(withTitle: "Not Now").keyEquivalent = "\u{1b}"
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn {
            // Asking puts Tinysnap in the list, but after an earlier denial macOS shows
            // nothing at all, and a button that did nothing left the person stuck. The
            // pane opens either way.
            ScreenAccess.request()
            ScreenAccess.openSettings()
        }
        return false
    }

    private func offerRestart() {
        let alert = NSAlert()
        alert.messageText = "Restart Tinysnap to finish enabling Screen Recording"
        alert.informativeText = "macOS only lets an app read the screen after it restarts."
        alert.addButton(withTitle: "Restart")
        // Escape answers the quiet option, as Cancel would.
        alert.addButton(withTitle: "Later").keyEquivalent = "\u{1b}"
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn { restart() }
    }

    private func showCaptureFailed() {
        let alert = NSAlert()
        alert.messageText = "Tinysnap could not read the screen"
        alert.informativeText = "macOS lists Tinysnap under Screen Recording but handed over nothing. Turning it off and on again in System Settings usually fixes this."
        alert.addButton(withTitle: "Open System Settings")
        alert.addButton(withTitle: "OK")
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn { ScreenAccess.openSettings() }
    }

    /// Freezes the screen, then puts the area overlay on the frozen image.
    private func captureArea() {
        guard overlay == nil, !isFreezing, ensureScreenAccess() else { return }
        isFreezing = true
        Task { [weak self] in
            let frozen = await ScreenReader.freeze()
            guard let self else { return }
            self.isFreezing = false
            guard !frozen.displays.isEmpty else {
                self.showCaptureFailed()
                return
            }
            let controller = AreaOverlayController(displays: frozen.displays, pickable: frozen.windows) { [weak self] result in
                self?.finishArea(result)
            }
            self.overlay = controller
            controller.show()
        }
    }

    private func finishArea(_ result: AreaResult) {
        overlay = nil
        switch result {
        case .cancelled:
            return
        case let .area(display, rect):
            lastArea = (display.displayID, rect)
            guard let capture = display.capture(of: rect) else {
                showCaptureFailed()
                return
            }
            openEditor(capture, on: display.displayID)
        case let .window(picked):
            Task {
                guard let capture = await ScreenReader.capture(picked) else {
                    showCaptureFailed()
                    return
                }
                let screen = NSScreen.screens.first { $0.frame.intersects(picked.frame) }
                openEditor(capture, on: screen?.displayID)
            }
        }
    }

    private func captureFullscreen() {
        guard !isFreezing, ensureScreenAccess() else { return }
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        guard let displayID = screen?.displayID else { return }
        isFreezing = true
        Task {
            let frozen = await ScreenReader.freeze(onlyDisplay: displayID)
            isFreezing = false
            guard let display = frozen.displays.first else {
                showCaptureFailed()
                return
            }
            openEditor(Capture(image: display.image, scale: display.scale), on: displayID)
        }
    }

    /// The last box again, from a fresh freeze of the same display. If that display is
    /// gone, the overlay opens instead.
    private func repeatLastArea() {
        guard let lastArea else {
            captureArea()
            return
        }
        guard !isFreezing, ensureScreenAccess() else { return }
        isFreezing = true
        Task {
            let frozen = await ScreenReader.freeze(onlyDisplay: lastArea.displayID)
            isFreezing = false
            guard let display = frozen.displays.first, let capture = display.capture(of: lastArea.rect) else {
                captureArea()
                return
            }
            openEditor(capture, on: display.displayID)
        }
    }

    /// Counts down in the menu bar, then opens the area overlay, so an open menu or a
    /// hover state can be set up first and caught by the freeze.
    private func startDelayedCapture() {
        guard secondsLeft == nil, ensureScreenAccess() else { return }
        Task {
            for remaining in stride(from: preferences.delaySeconds, to: 0, by: -1) {
                secondsLeft = remaining
                statusItem?.button?.title = " \(remaining)"
                try? await Task.sleep(for: .seconds(1))
            }
            secondsLeft = nil
            statusItem?.button?.title = ""
            captureArea()
        }
    }

    /// Every capture gets its own window, named for the time it was taken, which is how
    /// it is told apart in the Window menu, the Dock and Mission Control.
    private func openEditor(_ capture: Capture, on displayID: CGDirectDisplayID?,
                            title: String = "Capture at \(DateFormatter.localizedString(from: Date(), dateStyle: .none, timeStyle: .medium))") {
        let screen = NSScreen.screens.first { $0.displayID == displayID } ?? NSScreen.main
        let previous = editors.last { $0.window?.isVisible == true }?.window
        let editor = EditorWindowController(
            capture: capture,
            screen: screen,
            title: title,
            preferences: { [weak self] in self?.preferences ?? .defaults },
            onStylesChange: { [weak self] styles, colorHex in self?.remember(styles, colorHex: colorHex) },
            onClose: { [weak self] closed in
                self?.editors.removeAll { $0 === closed }
                self?.updateDockIcon()
            }
        )
        if let previous, let window = editor.window { cascade(window, after: previous) }
        editors.append(editor)
        updateDockIcon()
        editor.showWindow(nil)
        // Not the newer activate(): that one only asks, and the app in front has to
        // yield, so the editor opened behind whatever was active.
        NSApp.activate(ignoringOtherApps: true)
    }

    /// Each new capture sits a step down and to the right of the last one on the same
    /// display, so no window lands exactly on top of another and hides it.
    private func cascade(_ window: NSWindow, after previous: NSWindow) {
        guard let visible = previous.screen?.visibleFrame, previous.screen == window.screen else { return }
        var origin = NSPoint(x: previous.frame.minX + 28, y: previous.frame.maxY - 28 - window.frame.height)
        if origin.x + window.frame.width > visible.maxX || origin.y < visible.minY {
            origin = NSPoint(x: visible.minX + 28, y: visible.maxY - 28 - window.frame.height)
        }
        window.setFrameOrigin(origin)
    }

    /// A Dock icon, and a place in Command Tab, only while a capture is open, so a
    /// capture never gets lost behind other apps. Off in Settings, Tinysnap stays a
    /// menu bar app throughout.
    private func updateDockIcon() {
        let wanted: NSApplication.ActivationPolicy =
            preferences.showDockIconWhileCapturing && !editors.isEmpty ? .regular : .accessory
        guard NSApp.activationPolicy() != wanted else { return }
        NSApp.setActivationPolicy(wanted)
    }

    /// Clicking the Dock icon, or opening Tinysnap again from Finder or Spotlight, brings
    /// the captures back, or Settings when there are none. With the menu bar icon hidden,
    /// this is the way into Settings.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if editors.isEmpty {
            showSettings(nil)
        } else {
            editors.forEach { $0.showWindow(nil) }
        }
        NSApp.activate(ignoringOtherApps: true)
        return false
    }

    /// Settings may have written the file since it was read, so the styles are merged
    /// into what is on disk rather than written over it.
    private func remember(_ styles: [Tool: Style], colorHex: String) {
        var current = (try? Preferences.load(from: Preferences.defaultFileURL)) ?? preferences
        for (tool, style) in styles { current.toolStyles[tool.rawValue] = style }
        current.colorHex = colorHex
        preferences = current
        try? current.save(to: Preferences.defaultFileURL)
    }

    // MARK: Settings and lifecycle

    @objc func showSettings(_ sender: Any?) {
        if settings == nil {
            settings = SettingsWindowController(preferences: preferences, taken: takenHotKeys, onChange: { [weak self] updated in
                guard let self else { return [] }
                self.preferences = updated
                self.statusItem?.isVisible = updated.showMenuBarIcon
                self.updateDockIcon()
                self.takenHotKeys = self.hotKeys?.register(updated.hotkeys) ?? []
                return self.takenHotKeys
            }, onRecordingChange: { [weak self] recording in
                guard let self else { return }
                if recording {
                    self.hotKeys?.unregisterAll()
                } else {
                    self.takenHotKeys = self.hotKeys?.register(self.preferences.hotkeys) ?? []
                }
            })
        }
        settings?.show()
    }

    @objc private func enableScreenAccess() {
        ScreenAccess.request()
        ScreenAccess.openSettings()
    }

    @objc private func quit() {
        suppressRelaunchOnQuit = true
        NSApp.terminate(nil)
    }

    /// System Settings' Quit and Reopen does not reliably bring a menu bar only app
    /// back, so this starts a fresh copy first, then stands down.
    @objc private func restart() {
        // Asked before the fresh copy starts, or cancelling would leave two running.
        guard confirmQuit() else { return }
        suppressRelaunchOnQuit = true
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(at: Bundle.main.bundleURL, configuration: configuration) { _, _ in
            DispatchQueue.main.async { NSApp.terminate(nil) }
        }
    }

    /// True when it is fine to quit: nothing unsaved, or the person saved or discarded
    /// it. Every way out comes through here, Quit, Command Q, logout and Restart,
    /// because captures with edits nobody copied, saved or dragged out would otherwise
    /// go with the app without a word.
    private func confirmQuit() -> Bool {
        if hasConfirmedQuit { return true }
        let unsaved = editors.filter { $0.hasUnsavedEdits() }
        guard !unsaved.isEmpty else { return true }

        let alert = NSAlert()
        alert.messageText = unsaved.count == 1
            ? "Save the capture before quitting?"
            : "Save \(unsaved.count) captures before quitting?"
        alert.informativeText = (unsaved.count == 1 ? "Its annotations and crop are" : "Their annotations and crops are")
            + " lost if you quit without saving. Saving puts them in the save folder."
        alert.addButton(withTitle: unsaved.count == 1 ? "Save" : "Save All")
        alert.addButton(withTitle: "Discard")
        alert.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        switch alert.runModal() {
        case .alertFirstButtonReturn:
            // A save that fails has its own alert up, and the quit stops there.
            guard unsaved.allSatisfy({ $0.saveBeforeQuitting() }) else { return false }
        case .alertSecondButtonReturn:
            break
        default:
            return false
        }
        hasConfirmedQuit = true
        return true
    }

    /// Something other than our own Quit is ending this run, and the screen became
    /// readable while it ran: that is System Settings' Quit and Reopen, whose reopen
    /// half often leaves a menu bar app gone. Bring a fresh copy up first.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard confirmQuit() else {
            suppressRelaunchOnQuit = false
            return .terminateCancel
        }
        guard !suppressRelaunchOnQuit, ScreenAccess.isGranted, !couldReadScreenAtLaunch else { return .terminateNow }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(at: Bundle.main.bundleURL, configuration: configuration) { _, _ in
            DispatchQueue.main.async { NSApp.reply(toApplicationShouldTerminate: true) }
        }
        // Quitting still has to happen if the launch never reports back.
        DispatchQueue.main.asyncAfter(deadline: .now() + 5) { NSApp.reply(toApplicationShouldTerminate: true) }
        return .terminateLater
    }
}
