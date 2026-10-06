/// One page of the library, newest first: fifty captures, so the library opens as quickly with
/// a month of captures in it as with a day's.
public struct LibraryPage: Equatable, Sendable {
    public static let size = 50

    public let entries: [LibraryEntry]
    /// From 0.
    public let number: Int
    public let count: Int

    /// Page `number` of `all`, or the last page when there are fewer: trashing the last capture
    /// on a page, or the sweep, shows the page before instead of an empty one.
    public init(_ all: [LibraryEntry], number: Int) {
        count = max(1, (all.count + Self.size - 1) / Self.size)
        self.number = min(max(0, number), count - 1)
        entries = Array(all.dropFirst(self.number * Self.size).prefix(Self.size))
    }

    public var hasPrevious: Bool { number > 0 }
    public var hasNext: Bool { number < count - 1 }
    public var title: String { "Page \(number + 1) of \(count)" }
}
