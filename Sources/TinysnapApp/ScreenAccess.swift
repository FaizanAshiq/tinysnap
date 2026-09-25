import AppKit

/// Screen Recording, the one permission Tinysnap needs.
@MainActor
enum ScreenAccess {
    /// Checked rather than requested, so nothing prompts before the user reaches for a
    /// capture. This reports the permission row, not whether a capture will succeed:
    /// macOS can list the app and still hand over nothing, which the capture itself
    /// finds out.
    static var isGranted: Bool {
        CGPreflightScreenCaptureAccess()
    }

    static func request() {
        CGRequestScreenCaptureAccess()
    }

    static func openSettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") else {
            return
        }
        NSWorkspace.shared.open(url)
    }
}
