import Testing
@testable import TinysnapCore

/// A colour typed into the palette's field, as Windows and Linux take it: six hex digits,
/// with or without the #, in either case.
struct TypedHexTests {
    @Test func sixDigitsWithOrWithoutTheHashAreTaken() {
        #expect(Palette.hex(typed: "123abc") == "#123ABC")
        #expect(Palette.hex(typed: " #FF3B30 ") == "#FF3B30")
    }

    @Test func anythingElseIsNot() {
        #expect(Palette.hex(typed: "green") == nil)
        #expect(Palette.hex(typed: "#12345") == nil)
        #expect(Palette.hex(typed: "") == nil)
    }
}
