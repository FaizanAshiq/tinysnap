import AppKit
import Security
import TinysnapCore

/// Keeps the Mac app up to date. Once a week it asks GitHub for the latest release. A release build
/// somewhere it can write downloads the update, checks it is signed exactly as itself, and installs
/// it the first minute nothing of Tinysnap's is open: it quits, the new version takes its place, and
/// it starts again saying so. A copy that cannot install it names the new version in its menu, never
/// in a message of its own: there, a Homebrew copy gives the command and a copy built on this Mac
/// says where to download one. A release build run from the disk image is offered a move to
/// Applications.
@MainActor
final class Updater {
    static let command = "brew upgrade faizanashiq/tap/tinysnap"
    private static let latest = URL(string: "https://api.github.com/repos/FaizanAshiq/tinysnap/releases/latest")!
    private static let disk = URL(string: "https://github.com/FaizanAshiq/tinysnap/releases/latest/download/Tinysnap-mac.dmg")!
    /// Where updates come from instead of GitHub: a folder holding `latest.json`, shaped as GitHub's
    /// answer, so the end-to-end test can update to a build of its own.
    private static let feedVariable = "TINYSNAP_UPDATE_FEED"
    private static let lastLookKey = "TinysnapLastUpdateCheck"
    /// Set just before quitting to install, so the new copy says what it was updated to.
    private static let updatedKey = "TinysnapUpdatedTo"

    /// The newer version, once one is found, for the menu: nil while this copy installs it itself.
    private(set) var available: NewRelease?
    private let isIdle: () -> Bool
    private let quit: () -> Void
    private var timer: Timer?
    private let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
    private let route = UpdateRoute.of(
        path: Bundle.main.bundlePath, releaseSigned: Updater.isReleaseSigned,
        writable: FileManager.default.isWritableFile(atPath: Bundle.main.bundleURL.deletingLastPathComponent().path)
            && FileManager.default.isWritableFile(atPath: Bundle.main.bundlePath))

    /// - Parameters:
    ///   - isIdle: True when a restart would close nothing and lose nothing.
    ///   - quit: Ends Tinysnap, as Quit does, for the update to install.
    init(isIdle: @escaping () -> Bool, quit: @escaping () -> Void) {
        self.isIdle = isIdle
        self.quit = quit
    }

    /// Where an update is unpacked and checked before it takes this copy's place.
    private static var staging: URL {
        FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("com.faizanashiq.tinysnap/Update", isDirectory: true)
    }

    func start() {
        // An update downloaded and never installed, as Tinysnap quit first: the next look comes at
        // once rather than a week on, and installs it.
        if FileManager.default.fileExists(atPath: Self.staging.appendingPathComponent("Tinysnap.app").path) {
            UserDefaults.standard.removeObject(forKey: Self.lastLookKey)
        }
        if UserDefaults.standard.string(forKey: Self.updatedKey) == current {
            UserDefaults.standard.removeObject(forKey: Self.updatedKey)
            TextCopy.say("Updated to Tinysnap \(current)", on: nil)
        }
        if route == .move { offerMove() }
        schedule(in: max(10, wait)) { await $0.lookIfDue() }
    }

    /// Until the next check whether a week has passed since the last look, at most an hour.
    private var wait: TimeInterval {
        UpdateSchedule.wait(lastLook: UserDefaults.standard.double(forKey: Self.lastLookKey), now: Date().timeIntervalSince1970)
    }

    private func lookIfDue() async {
        let wait = self.wait
        guard wait == 0 else {
            schedule(in: wait) { await $0.lookIfDue() }
            return
        }
        await look()
    }

    private func schedule(in seconds: TimeInterval, _ work: @escaping @MainActor (Updater) async -> Void) {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: seconds, repeats: false) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                await work(self)
            }
        }
    }

    private func look() async {
        UserDefaults.standard.set(Date().timeIntervalSince1970, forKey: Self.lastLookKey)
        schedule(in: UpdateSchedule.hour) { await $0.lookIfDue() }
        let source = ProcessInfo.processInfo.environment[Self.feedVariable].map {
            URL(fileURLWithPath: $0).appendingPathComponent("latest.json")
        } ?? Self.latest
        // Offline, or GitHub refusing: the next look tries again.
        guard let answer = try? await URLSession.shared.data(from: source),
              let release = NewRelease.newer(in: answer.0, than: current) else { return }
        if route == .itself, let zip = release.download, let app = await prepare(zip) {
            install(app, version: release.version)
            return
        }
        available = release
    }

    /// The update downloaded and unpacked, when it is signed exactly as this copy; nil otherwise.
    private func prepare(_ zip: URL) async -> URL? {
        // A test feed's zip is a file already, used where it is and left there.
        var downloaded: URL?
        if !zip.isFileURL {
            guard let (file, _) = try? await URLSession.shared.download(from: zip) else { return nil }
            downloaded = file
        }
        let file = downloaded ?? zip
        defer { if let downloaded { try? FileManager.default.removeItem(at: downloaded) } }
        let staging = Self.staging
        try? FileManager.default.removeItem(at: staging)
        try? FileManager.default.createDirectory(at: staging, withIntermediateDirectories: true)
        let unpacked = await Task.detached { Self.run("/usr/bin/ditto", ["-x", "-k", file.path, staging.path]) }.value
        let app = staging.appendingPathComponent("Tinysnap.app")
        guard unpacked, Self.signedAsThisCopy(app) else {
            try? FileManager.default.removeItem(at: staging)
            return nil
        }
        return app
    }

    /// Waits, a minute at a time, for nothing to be open, then quits for the new version to take
    /// this one's place and start.
    private func install(_ app: URL, version: String) {
        guard isIdle() else {
            schedule(in: 60) { $0.install(app, version: version) }
            return
        }
        UserDefaults.standard.set(version, forKey: Self.updatedKey)
        let swap = """
            while kill -0 "$1" 2>/dev/null; do sleep 0.2; done
            rm -rf "$2.replaced"
            if mv "$2" "$2.replaced"; then
                if mv "$3" "$2"; then rm -rf "$2.replaced"; else mv "$2.replaced" "$2"; fi
            fi
            open "$2"
            """
        guard Self.launch(swap, [Bundle.main.bundlePath, app.path]) else {
            UserDefaults.standard.removeObject(forKey: Self.updatedKey)
            schedule(in: UpdateSchedule.hour) { await $0.lookIfDue() }
            return
        }
        quit()
    }

    /// The new version, and how to get it from where this copy runs.
    func tell() {
        guard let available else { return }
        let alert = NSAlert()
        alert.messageText = "Tinysnap \(available.version) is out"
        if route == .homebrew {
            alert.informativeText = "It is free, like every version. Homebrew updates it: run this in Terminal, then open Tinysnap again.\n\n\(Self.command)"
            alert.addButton(withTitle: "Copy Command")
        } else {
            alert.informativeText = "It is free, like every version. Download it and drag it into Applications in place of this copy. From there it keeps itself up to date."
            alert.addButton(withTitle: "Download")
        }
        alert.addButton(withTitle: "Release Notes")
        // Escape answers the quiet option.
        alert.addButton(withTitle: "Later").keyEquivalent = "\u{1b}"
        NSApp.activate(ignoringOtherApps: true)
        switch alert.runModal() {
        case .alertFirstButtonReturn where route == .homebrew:
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(Self.command, forType: .string)
        case .alertFirstButtonReturn:
            NSWorkspace.shared.open(Self.disk)
        case .alertSecondButtonReturn:
            NSWorkspace.shared.open(URL(string: "https://github.com/FaizanAshiq/tinysnap/releases/tag/v\(available.version)")!)
        default:
            break
        }
    }

    /// Run from the disk image, or from Downloads before it was ever moved, macOS lets the app change
    /// nothing where it is, so it could never update. Copied to Applications, it can.
    private func offerMove() {
        let alert = NSAlert()
        alert.messageText = "Move Tinysnap to Applications?"
        alert.informativeText = "It is running from the disk image or Downloads, where it cannot keep itself up to date. From Applications it can."
        alert.addButton(withTitle: "Move to Applications")
        alert.addButton(withTitle: "Not Now").keyEquivalent = "\u{1b}"
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        let destination = URL(fileURLWithPath: UpdateRoute.applications)
        do {
            // An earlier copy goes to the Bin, where it can still be had back.
            if FileManager.default.fileExists(atPath: destination.path) {
                try FileManager.default.trashItem(at: destination, resultingItemURL: nil)
            }
            try FileManager.default.copyItem(at: Bundle.main.bundleURL, to: destination)
            // Already opened once, as the person allowed in System Settings; without the mark the
            // copy runs where it is rather than from another hidden copy.
            _ = Self.run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", destination.path])
        } catch {
            let failed = NSAlert()
            failed.messageText = "Tinysnap could not move itself"
            failed.informativeText = "Drag it from the disk image into Applications, then open it from there."
            failed.runModal()
            return
        }
        if Self.launch(#"while kill -0 "$1" 2>/dev/null; do sleep 0.2; done; open "$2""#, [destination.path]) { quit() }
    }

    // Signatures

    /// Signed with the release certificate, as every build CI makes for download is.
    private static var isReleaseSigned: Bool {
        guard let code = staticCode(Bundle.main.bundleURL) else { return false }
        var information: CFDictionary?
        guard SecCodeCopySigningInformation(code, SecCSFlags(rawValue: kSecCSSigningInformation), &information) == errSecSuccess,
              let certificates = (information as? [String: Any])?[kSecCodeInfoCertificates as String] as? [SecCertificate],
              let leaf = certificates.first else { return false }
        return SecCertificateCopySubjectSummary(leaf) as String? == "Tinysnap Release"
    }

    /// True when `app` meets this copy's own designated requirement: signed exactly as this copy
    /// is, so nothing but a release build replaces a release build.
    private static func signedAsThisCopy(_ app: URL) -> Bool {
        guard let own = staticCode(Bundle.main.bundleURL), let other = staticCode(app) else { return false }
        var requirement: SecRequirement?
        guard SecCodeCopyDesignatedRequirement(own, [], &requirement) == errSecSuccess, let requirement else { return false }
        let flags = SecCSFlags(rawValue: kSecCSCheckAllArchitectures | kSecCSStrictValidate | kSecCSCheckNestedCode)
        return SecStaticCodeCheckValidity(other, flags, requirement) == errSecSuccess
    }

    private static func staticCode(_ url: URL) -> SecStaticCode? {
        var code: SecStaticCode?
        return SecStaticCodeCreateWithPath(url as CFURL, [], &code) == errSecSuccess ? code : nil
    }

    // Processes

    private nonisolated static func run(_ tool: String, _ arguments: [String]) -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        guard (try? process.run()) != nil else { return false }
        process.waitUntilExit()
        return process.terminationStatus == 0
    }

    /// Starts `script` with this process's id as `$1` and `arguments` after it, to carry on once
    /// Tinysnap has quit.
    private static func launch(_ script: String, _ arguments: [String]) -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = ["-c", script, "sh", "\(ProcessInfo.processInfo.processIdentifier)"] + arguments
        return (try? process.run()) != nil
    }
}
