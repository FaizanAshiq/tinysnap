import Foundation

/// A release newer than this copy, as GitHub's answer for the latest release names it. The Mac
/// copy is installed by Homebrew, which updates it, so Tinysnap only tells of one.
public enum NewRelease {
    /// The latest release's version without its `v`, when it is newer than `current`; nil when it
    /// is not, or the answer cannot be read, as when GitHub refuses or is down.
    public static func version(in latest: Data, newerThan current: String) -> String? {
        guard let answer = try? JSONSerialization.jsonObject(with: latest) as? [String: Any],
              let tag = answer["tag_name"] as? String else { return nil }
        let version = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
        guard var newest = numbers(version), var mine = numbers(current) else { return nil }
        // 1.5 is 1.5.0.
        let length = max(newest.count, mine.count)
        newest += Array(repeating: 0, count: length - newest.count)
        mine += Array(repeating: 0, count: length - mine.count)
        return mine.lexicographicallyPrecedes(newest) ? version : nil
    }

    /// 1.10.0 as [1, 10, 0], so it sorts after 1.9.2, as text it would not.
    private static func numbers(_ version: String) -> [Int]? {
        let parts = version.split(separator: ".").map { Int($0) }
        return parts.isEmpty || parts.contains(nil) ? nil : parts.compactMap { $0 }
    }
}
