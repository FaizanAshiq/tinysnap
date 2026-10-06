import AppKit
import TinysnapCore

/// Looks for a newer release at launch and every six hours. Homebrew installs and updates the Mac
/// copy, so a new version is told once, with the command that updates it, and stays in the menu
/// bar menu until it is installed.
@MainActor
final class ReleaseWatch {
    static let command = "brew upgrade faizanashiq/tap/tinysnap"
    private static let latest = URL(string: "https://api.github.com/repos/FaizanAshiq/tinysnap/releases/latest")!
    /// The version last told of, so each is told once rather than at every launch.
    private static let toldKey = "TinysnapToldRelease"

    /// The newer version, once one is found.
    private(set) var available: String?
    private var timer: Timer?

    func start() {
        Task { await check() }
        timer = Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.check() }
        }
    }

    private func check() async {
        let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        // Offline, or GitHub refusing: the next round tries again.
        guard let answer = try? await URLSession.shared.data(from: Self.latest),
              let version = NewRelease.version(in: answer.0, newerThan: current) else { return }
        available = version
        guard UserDefaults.standard.string(forKey: Self.toldKey) != version else { return }
        UserDefaults.standard.set(version, forKey: Self.toldKey)
        tell()
    }

    /// The new version, with the command and a button that copies it.
    func tell() {
        guard let available else { return }
        let alert = NSAlert()
        alert.messageText = "Tinysnap \(available) is out"
        alert.informativeText = "It is free, like every version. Homebrew updates it: run this in Terminal, then open Tinysnap again.\n\n\(Self.command)"
        alert.addButton(withTitle: "Copy Command")
        alert.addButton(withTitle: "Release Notes")
        // Escape answers the quiet option.
        alert.addButton(withTitle: "Later").keyEquivalent = "\u{1b}"
        NSApp.activate(ignoringOtherApps: true)
        switch alert.runModal() {
        case .alertFirstButtonReturn:
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(Self.command, forType: .string)
        case .alertSecondButtonReturn:
            NSWorkspace.shared.open(URL(string: "https://github.com/FaizanAshiq/tinysnap/releases/tag/v\(available)")!)
        default:
            break
        }
    }
}
