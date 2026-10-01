import AppKit
import TinysnapCore

extension Notification.Name {
    /// An entry was added, rendered again, trashed or swept, so the library window reloads.
    static let libraryChanged = Notification.Name("TinysnapLibraryChanged")
}

/// The menu bar item, the hotkeys, and the way from a capture request to an editor.
/// Everything here touches AppKit, which is main thread only. Swift 6 does not infer
/// that from the delegate conformance, so it is stated once on the class.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem?
    private var hotKeys: HotKeyCenter?
    private var takenHotKeys: Set<HotKeyAction> = []
    /// Read here rather than on finishing launching: opening a file launches Tinysnap and
    /// delivers the file first, and an editor built before the read took the defaults,
    /// then wrote them over the remembered styles when it closed.
    private var preferences: Preferences = (try? Preferences.load(from: Preferences.defaultFileURL)) ?? .defaults
    private var editors: [EditorWindowController] = []
    private let library = LibraryStore()
    /// Why the last capture could not be kept, shown in Settings until one can.
    private(set) var libraryError: String?
    private var sweepTimer: Timer?
    private var pins: [PinWindowController] = []
    private var libraryWindow: LibraryWindowController?
    private var thumbnail: CaptureThumbnail?
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
        MainMenu.install()
        installStatusItem()
        statusItem?.isVisible = preferences.showMenuBarIcon

        let center = HotKeyCenter { [weak self] action in self?.perform(action) }
        takenHotKeys = center.register(preferences.hotkeys)
        hotKeys = center

        Task { await ScreenReader.warmUp() }

        sweepLibrary()
        sweepTimer = Timer.scheduledTimer(withTimeInterval: 86_400, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.sweepLibrary() }
        }
    }

    /// `open -a Tinysnap picture.png`, or a file dropped on the app, opens in an editor.
    /// This is also how the editor is tried out on a synthetic image, with no screen
    /// content involved.
    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            guard let image = NSImage(contentsOf: url),
                  let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
                  image.size.width > 0 else { continue }
            let capture = Capture(image: cgImage, scale: CGFloat(cgImage.width) / image.size.width)
            openEditor(Document(capture: capture), entry: nil, on: nil, title: url.lastPathComponent)
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
        case .area: captureArea(for: .image)
        case .window: captureArea(for: .image, windowMode: true)
        case .fullscreen: captureFullscreen()
        case .repeatArea: repeatLastArea()
        case .delayed: startDelayedCapture()
        case .text: captureArea(for: .text)
        case .qr: captureArea(for: .codes)
        case .library: showLibrary()
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

    /// An area or window picked on the frozen screen becomes an image to edit, its text
    /// on the clipboard, or what a QR code in it holds.
    private enum Purpose {
        case image, text, codes
    }

    /// Freezes the screen, then puts the area overlay on the frozen image, ready to pick a
    /// window when `windowMode` is set, as Space would make it.
    private func captureArea(for purpose: Purpose, windowMode: Bool = false) {
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
                self?.finishArea(result, for: purpose)
            }
            self.overlay = controller
            controller.show()
            if windowMode { controller.startInWindowMode() }
        }
    }

    private func finishArea(_ result: AreaResult, for purpose: Purpose) {
        overlay = nil
        switch result {
        case .cancelled:
            return
        case let .area(display, rect):
            // Repeat Last Area repeats pictures, not text grabs.
            if purpose == .image { lastArea = (display.displayID, rect) }
            guard let capture = display.capture(of: rect) else {
                showCaptureFailed()
                return
            }
            take(capture, on: display.displayID, for: purpose)
        case let .window(picked):
            Task {
                guard let capture = await ScreenReader.capture(picked) else {
                    showCaptureFailed()
                    return
                }
                let screen = NSScreen.screens.first { $0.frame.intersects(picked.frame) }
                take(capture, on: screen?.displayID, for: purpose)
            }
        }
    }

    private func take(_ capture: Capture, on displayID: CGDirectDisplayID?, for purpose: Purpose) {
        switch purpose {
        case .image: deliver(capture, on: displayID)
        case .text: TextCopy.read(capture.image, for: .text, on: NSScreen.screens.first { $0.displayID == displayID })
        case .codes: TextCopy.read(capture.image, for: .codes, on: NSScreen.screens.first { $0.displayID == displayID })
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
            deliver(Capture(image: display.image, scale: display.scale), on: displayID)
        }
    }

    /// The last box again, from a fresh freeze of the same display. If that display is
    /// gone, the overlay opens instead.
    private func repeatLastArea() {
        guard let lastArea else {
            captureArea(for: .image)
            return
        }
        guard !isFreezing, ensureScreenAccess() else { return }
        isFreezing = true
        Task {
            let frozen = await ScreenReader.freeze(onlyDisplay: lastArea.displayID)
            isFreezing = false
            guard let display = frozen.displays.first, let capture = display.capture(of: lastArea.rect) else {
                captureArea(for: .image)
                return
            }
            deliver(capture, on: display.displayID)
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
            captureArea(for: .image)
        }
    }

    /// Where every capture goes once it is taken: into the library, then to an editor or
    /// the thumbnail, as Settings says.
    private func deliver(_ capture: Capture, on displayID: CGDirectDisplayID?) {
        var entry: LibraryEntry?
        if preferences.keepLibrary {
            do {
                entry = try library.add(capture, captured: Date())
                libraryError = nil
                NotificationCenter.default.post(name: .libraryChanged, object: nil)
            } catch {
                // The capture still opens; Settings says why it was not kept.
                libraryError = error.localizedDescription
            }
        }
        switch preferences.afterCapture {
        case .editor: openEditor(Document(capture: capture), entry: entry, on: displayID)
        case .thumbnail: showThumbnail(Document(capture: capture), entry: entry, on: displayID)
        }
    }

    /// One thumbnail at a time: a new capture sends the last one away, as a time out would.
    private func showThumbnail(_ document: Document, entry: LibraryEntry?, on displayID: CGDirectDisplayID?) {
        thumbnail?.dismiss(copying: true)
        thumbnail = CaptureThumbnail(
            document: document, entry: entry,
            screen: NSScreen.screens.first { $0.displayID == displayID } ?? NSScreen.main,
            preferences: { [weak self] in self?.preferences ?? .defaults },
            onOpen: { [weak self] shown in self?.openEditor(shown.document, entry: shown.entry, on: displayID) },
            onPin: { [weak self] shown in
                guard let exported = Exporter.export(shown.document, scale: .native) else { return }
                self?.pin(exported.image, scale: shown.document.scale, entry: shown.entry)
            },
            onGone: { [weak self] gone in
                if self?.thumbnail === gone { self?.thumbnail = nil }
            }
        )
    }

    /// Opens a library entry, or brings its editor forward when it is already open.
    func open(_ entry: LibraryEntry) {
        if let editor = editors.first(where: { $0.entry == entry }) {
            editor.showWindow(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        guard let opened = library.open(entry) else {
            NSSound.beep()
            return
        }
        // A damaged entry opens flat and on its own, so nothing is written over it.
        openEditor(opened.document, entry: opened.isEditable ? entry : nil, on: nil, title: Self.title(for: entry.captured))
    }

    private static func title(for date: Date) -> String {
        let day: DateFormatter.Style = Calendar.current.isDateInToday(date) ? .none : .medium
        return "Capture at \(DateFormatter.localizedString(from: date, dateStyle: day, timeStyle: .medium))"
    }

    /// For the app menu, the Dock menu and Settings, so the library never depends on a
    /// hotkey, which is unset until chosen, or on the menu bar icon, which can be hidden.
    @objc func openLibrary(_ sender: Any?) {
        showLibrary()
    }

    func showLibrary() {
        if libraryWindow == nil {
            libraryWindow = LibraryWindowController(
                library: library,
                preferences: { [weak self] in self?.preferences ?? .defaults },
                keeping: { [weak self] in self?.openEntryNames ?? [] },
                onOpen: { [weak self] entry in self?.open(entry) },
                onPin: { [weak self] entry in self?.pin(entry) },
                flush: { [weak self] entry in self?.flush(entry) },
                renderStale: { [weak self] entry in
                    self?.renderInBackground(entry) { library in
                        let asOf = library.editsDate(entry)
                        if let opened = library.open(entry), opened.isEditable {
                            try? library.saveImage(opened.document, to: entry, editsAsOf: asOf)
                        }
                    }
                }
            )
        }
        libraryWindow?.show()
    }

    // MARK: Pins

    /// `keepsSize` is for an image drawn at a size the capture was given.
    func pin(_ image: CGImage, scale: CGFloat, entry: LibraryEntry?, keepsSize: Bool = false) {
        let pin = PinWindowController(
            image: image, scale: scale, entry: entry, keepsSize: keepsSize,
            preferences: { [weak self] in self?.preferences ?? .defaults },
            onOpen: { [weak self] pin in self?.openPinned(pin) },
            onClose: { [weak self] pin in self?.pins.removeAll { $0 === pin } }
        )
        pins.append(pin)
        pin.showWindow(nil)
    }

    /// An entry still open in an editor has that editor render it first.
    private func flush(_ entry: LibraryEntry) {
        editors.first { $0.entry == entry }?.keep(renderingImage: true)
    }

    /// The entry drawn from its edits, brought up to date with any editor still open on
    /// it, at its size when it has one.
    func pin(_ entry: LibraryEntry) {
        flush(entry)
        guard let document = library.open(entry)?.document, let exported = Exporter.export(document, scale: .native) else {
            NSSound.beep()
            return
        }
        pin(exported.image, scale: exported.dpi / 72, entry: entry, keepsSize: document.resize != nil)
    }

    /// Back into the editor: the library entry, editable, when there is one.
    private func openPinned(_ pin: PinWindowController) {
        if let entry = pin.entry {
            open(entry)
        } else {
            openEditor(Document(capture: Capture(image: pin.image, scale: pin.scale)), entry: nil, on: nil)
        }
    }

    /// Entries open in an editor are never swept or cleared out from under it.
    var openEntryNames: Set<String> { Set(editors.compactMap { $0.entry?.name }).union(rendering) }

    /// Entries whose image is being rendered after their editor closed.
    private var rendering: Set<String> = []

    /// An entry's image, rendered off the main thread: at 400% of a 5K capture it is
    /// 16,384 pixels across, and closing the editor stalled on it. Until it lands the entry
    /// counts as open, so the library neither renders it a second time nor sweeps it.
    /// Quitting first loses nothing: the edits are written, and a stale image is rendered
    /// again the next time the library looks.
    private func renderInBackground(_ entry: LibraryEntry, _ render: @escaping @Sendable (LibraryStore) -> Void) {
        guard !rendering.contains(entry.name) else { return }
        rendering.insert(entry.name)
        let library = self.library
        Task.detached(priority: .utility) {
            render(library)
            await MainActor.run { [weak self] in
                self?.rendering.remove(entry.name)
                NotificationCenter.default.post(name: .libraryChanged, object: nil)
            }
        }
    }

    private func sweepLibrary() {
        guard !library.sweep(keeping: openEntryNames).isEmpty else { return }
        NotificationCenter.default.post(name: .libraryChanged, object: nil)
    }

    /// Every capture gets its own window, named for the time it was taken, which is how
    /// it is told apart in the Window menu, the Dock and Mission Control.
    private func openEditor(_ document: Document, entry: LibraryEntry?, on displayID: CGDirectDisplayID?,
                            title: String = AppDelegate.title(for: Date())) {
        let screen = NSScreen.screens.first { $0.displayID == displayID } ?? NSScreen.main
        let previous = editors.last { $0.window?.isVisible == true }?.window
        let editor = EditorWindowController(
            document: document,
            entry: entry,
            library: library,
            screen: screen,
            title: title,
            preferences: { [weak self] in self?.preferences ?? .defaults },
            onStylesChange: { [weak self] styles, colorHex in self?.remember(styles, colorHex: colorHex) },
            onPin: { [weak self] image, scale, entry, keepsSize in
                self?.pin(image, scale: scale, entry: entry, keepsSize: keepsSize)
            },
            onBackdropChange: { [weak self] backdrop in self?.remember(backdrop) },
            onMeasureChange: { [weak self] measure in self?.remember(measure) },
            onClose: { [weak self] closed in
                self?.editors.removeAll { $0 === closed }
                self?.updateDockIcon()
                if let (document, entry) = closed.pendingRender(), let self {
                    let asOf = self.library.editsDate(entry)
                    self.renderInBackground(entry) { try? $0.saveImage(document, to: entry, editsAsOf: asOf) }
                }
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

    /// Merged into what is on disk, as the styles are, so Settings is not written over.
    private func remember(_ backdrop: Backdrop) {
        var current = (try? Preferences.load(from: Preferences.defaultFileURL)) ?? preferences
        current.backdrop = backdrop
        preferences = current
        try? current.save(to: Preferences.defaultFileURL)
    }

    /// Merged into what is on disk, as the styles are, so Settings is not written over.
    /// Every open editor follows, so X, Y or a guide closed in one holds in all of them.
    private func remember(_ measure: MeasureSettings) {
        var current = (try? Preferences.load(from: Preferences.defaultFileURL)) ?? preferences
        current.measure = measure
        preferences = current
        try? current.save(to: Preferences.defaultFileURL)
        editors.forEach { $0.preferencesChanged() }
    }

    // MARK: Settings and lifecycle

    @objc func showSettings(_ sender: Any?) {
        if settings == nil {
            settings = SettingsWindowController(preferences: preferences, taken: takenHotKeys, library: SettingsWindowController.Library(
                size: { [weak self] in self?.library.size() ?? 0 },
                problem: { [weak self] in self?.libraryError },
                clear: { [weak self] in
                    guard let self else { return }
                    self.library.clear(keeping: self.openEntryNames)
                    NotificationCenter.default.post(name: .libraryChanged, object: nil)
                },
                open: { [weak self] in self?.showLibrary() }
            ), onChange: { [weak self] updated in
                guard let self else { return [] }
                self.preferences = updated
                self.editors.forEach { $0.preferencesChanged() }
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

extension AppDelegate: NSMenuItemValidation {
    /// While a capture window is open Tinysnap has a Dock icon, and with it a Dock menu:
    /// the captures and the library, as on the menu bar icon.
    func applicationDockMenu(_ sender: NSApplication) -> NSMenu? {
        let menu = NSMenu()
        for action in HotKeyAction.allCases where action != .library {
            let item = NSMenuItem(title: action.title, action: #selector(captureFromMenu(_:)), keyEquivalent: "")
            item.representedObject = action.rawValue
            item.target = self
            menu.addItem(item)
        }
        menu.addItem(.separator())
        let library = NSMenuItem(title: "Open Library", action: #selector(openLibrary(_:)), keyEquivalent: "")
        library.target = self
        menu.addItem(library)
        return menu
    }

    /// The app menu and the Dock menu enable their own items, so a capture that can not
    /// run now shows as unavailable there too, as it does on the menu bar icon.
    func validateMenuItem(_ item: NSMenuItem) -> Bool {
        guard item.action == #selector(captureFromMenu(_:)), let raw = item.representedObject as? String,
              let action = HotKeyAction(rawValue: raw) else { return true }
        return !(action == .repeatArea && lastArea == nil) && secondsLeft == nil
    }
}
