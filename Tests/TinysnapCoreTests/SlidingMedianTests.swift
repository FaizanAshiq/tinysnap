import Testing
@testable import TinysnapCore

/// The erase fill smooths each edge with the median of the pixels within 16 of each one. It
/// slides one sorted window along the edge rather than sorting a fresh copy at every pixel.
struct SlidingMedianTests {
    @Test func everyMedianMatchesSortingItsWindow() {
        var seed: UInt64 = 7
        func next() -> UInt8 {
            seed = seed &* 6364136223846793005 &+ 1442695040888963407
            return UInt8(truncatingIfNeeded: seed >> 56)
        }
        for count in [1, 2, 5, 16, 33, 34, 40, 200] {
            let values = (0..<count).map { _ in next() }
            let expected = (0..<count).map { i in
                let window = values[max(0, i - 16)...min(count - 1, i + 16)].sorted()
                return window[window.count / 2]
            }
            #expect(PixelBuffer.slidingMedians(values, reach: 16) == expected, "count \(count)")
        }
    }
}
