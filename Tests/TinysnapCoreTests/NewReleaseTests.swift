import Foundation
import Testing
@testable import TinysnapCore

/// A newer release, from GitHub's answer for the latest one: its version when newer than this
/// copy, and the Mac app zipped, which a copy that updates itself downloads.
struct NewReleaseTests {
    private func latest(_ tag: String, assets: [String] = []) -> Data {
        let list = assets.map { #"{"name": "\#($0)", "browser_download_url": "https://example.com/\#($0)"}"# }
        return Data(#"{"tag_name": "\#(tag)", "name": "Tinysnap", "assets": [\#(list.joined(separator: ","))]}"#.utf8)
    }

    @Test func aNewerReleaseIsToldWithoutItsVAndWithTheMacZip() {
        let release = NewRelease.newer(in: latest("v1.5.0", assets: ["Tinysnap-win-x64-Setup.exe", "Tinysnap-mac.zip"]), than: "1.4.5")
        #expect(release == NewRelease(version: "1.5.0", download: URL(string: "https://example.com/Tinysnap-mac.zip")))
    }

    @Test func aReleaseWithNoMacZipIsStillTold() {
        #expect(NewRelease.newer(in: latest("v1.5.0"), than: "1.4.5")?.download == nil)
        #expect(NewRelease.newer(in: latest("v1.5.0"), than: "1.4.5")?.version == "1.5.0")
    }

    @Test func theSameOrAnOlderReleaseIsNot() {
        #expect(NewRelease.newer(in: latest("v1.4.5"), than: "1.4.5") == nil)
        #expect(NewRelease.newer(in: latest("v1.4.4"), than: "1.4.5") == nil)
        #expect(NewRelease.newer(in: latest("v1.5"), than: "1.5.0") == nil)
    }

    /// Compared number by number, so 1.10 comes after 1.9, as text it would not.
    @Test func versionsCompareAsNumbers() {
        #expect(NewRelease.newer(in: latest("v1.10.0"), than: "1.9.2")?.version == "1.10.0")
        #expect(NewRelease.newer(in: latest("v2.0.0"), than: "1.99.99")?.version == "2.0.0")
    }

    @Test func anAnswerThatCannotBeReadTellsNothing() {
        #expect(NewRelease.newer(in: Data("rate limited".utf8), than: "1.4.5") == nil)
        #expect(NewRelease.newer(in: Data(#"{"message": "Not Found"}"#.utf8), than: "1.4.5") == nil)
        #expect(NewRelease.newer(in: latest("nightly"), than: "1.4.5") == nil)
    }
}

/// How a copy of the Mac app gets a newer version depends on where it runs and how it was signed.
struct UpdateRouteTests {
    @Test func aReleaseBuildInApplicationsUpdatesItself() {
        #expect(UpdateRoute.of(path: "/Applications/Tinysnap.app", releaseSigned: true, writable: true) == .itself)
        #expect(UpdateRoute.of(path: "/Users/someone/Applications/Tinysnap.app", releaseSigned: true, writable: true) == .itself)
    }

    /// Homebrew keeps track of what it installed, so it does the updating, whatever the signature.
    @Test func aHomebrewCopyIsToldTheCommand() {
        #expect(UpdateRoute.of(path: "/opt/homebrew/Cellar/tinysnap/1.5.0/Tinysnap.app", releaseSigned: false, writable: true) == .homebrew)
        #expect(UpdateRoute.of(path: "/opt/homebrew/opt/tinysnap/Tinysnap.app", releaseSigned: false, writable: true) == .homebrew)
        #expect(UpdateRoute.of(path: "/usr/local/Cellar/tinysnap/1.5.0/Tinysnap.app", releaseSigned: true, writable: true) == .homebrew)
    }

    /// Run from the disk image, or straight from Downloads, which macOS runs from a hidden copy no
    /// app may write to.
    @Test func aReleaseBuildThatCannotWriteWhereItIsIsOfferedTheMove() {
        #expect(UpdateRoute.of(path: "/Volumes/Tinysnap/Tinysnap.app", releaseSigned: true, writable: false) == .move)
        #expect(UpdateRoute.of(path: "/private/var/folders/x/AppTranslocation/1/d/Tinysnap.app", releaseSigned: true, writable: false) == .move)
    }

    /// Built on this Mac, it cannot tell a release build from anyone else's, so it is told where to get one.
    @Test func aCopyBuiltHereIsToldWhereToDownload() {
        #expect(UpdateRoute.of(path: "/Applications/Tinysnap.app", releaseSigned: false, writable: true) == .download)
    }
}
