import CoreGraphics
import CoreImage
import Foundation
import Testing
@testable import TinysnapCore

struct TextReaderTests {
    /// Black text on white at 2x, drawn by the same Core Text code the renderer uses.
    private func page(_ lines: [(String, CGPoint)], width: Int = 900, height: Int = 400,
                      paint: ((CGContext) -> Void)? = nil) -> Capture {
        Fixture.capture(width: width, height: height, scale: 2) { context in
            for (string, origin) in lines {
                TextLayout.draw(string, at: origin, points: 24, scale: 2, color: Fixture.black, in: context)
            }
            paint?(context)
        }
    }

    private func qrCode(_ message: String) -> CGImage {
        let filter = CIFilter(name: "CIQRCodeGenerator")!
        filter.setValue(Data(message.utf8), forKey: "inputMessage")
        let code = filter.outputImage!.transformed(by: CGAffineTransform(scaleX: 8, y: 8))
        return CIContext().createCGImage(code, from: code.extent)!
    }

    @Test func readsLinesTopToBottom() throws {
        let capture = page([("Order shipped today", CGPoint(x: 40, y: 60)), ("Tracking arrives soon", CGPoint(x: 40, y: 200))])
        let reading = try TextReader.read(capture.image)
        #expect(reading.lines == ["Order shipped today", "Tracking arrives soon"])
        #expect(reading.codes.isEmpty)
        #expect(reading.text == "Order shipped today\nTracking arrives soon")
    }

    @Test func sideBySideTextReadsAcrossEachRowBeforeGoingDown() throws {
        let capture = page([("Left top", CGPoint(x: 40, y: 60)), ("Right top", CGPoint(x: 560, y: 64)),
                            ("Left low", CGPoint(x: 40, y: 260)), ("Right low", CGPoint(x: 560, y: 256))])
        #expect(try TextReader.read(capture.image).lines == ["Left top", "Right top", "Left low", "Right low"])
    }

    @Test func aQRCodeWinsOverTextBesideIt() throws {
        let link = "https://tinysnap.example/qr"
        let code = qrCode(link)
        let capture = page([("Scan to open", CGPoint(x: 420, y: 150))], width: 900, height: 420) { context in
            // Flipped back, so the code is the right way up in the y down context.
            context.saveGState()
            context.translateBy(x: 40, y: 40 + CGFloat(code.height))
            context.scaleBy(x: 1, y: -1)
            context.draw(code, in: CGRect(x: 0, y: 0, width: code.width, height: code.height))
            context.restoreGState()
        }
        let reading = try TextReader.read(capture.image)
        #expect(reading.codes == [link])
        #expect(reading.text == link)
        #expect(reading.link == URL(string: link))
    }

    @Test func aBlankImageReadsAsNothing() throws {
        #expect(try TextReader.read(Fixture.capture(width: 200, height: 100).image).isEmpty)
    }

    @Test func joiningLinesLeavesSingleSpaces() {
        #expect(TextReader.join("one\ntwo  three\n four \n") == "one two three four")
    }

    @Test func aLinkIsOfferedOnlyForExactlyOneWebAddress() {
        #expect(TextReading(codes: [], lines: ["https://example.com/a"]).link == URL(string: "https://example.com/a"))
        #expect(TextReading(codes: [], lines: ["see https://example.com"]).link == nil)
        #expect(TextReading(codes: [], lines: ["https://a.example", "https://b.example"]).link == nil)
        #expect(TextReading(codes: [], lines: ["file:///etc/hosts"]).link == nil)
    }

    @Test func textUnderAnEraseIsNeverRead() throws {
        let capture = page([("Public note", CGPoint(x: 40, y: 60)), ("Secret code", CGPoint(x: 40, y: 200))])
        let erase = Fixture.annotation(.erase(CGRect(x: 20, y: 180, width: 600, height: 110)))
        let exported = try #require(Exporter.export(Document(capture: capture, annotations: [erase]), scale: .native))
        let reading = try TextReader.read(exported.image)
        #expect(reading.lines == ["Public note"])
    }
}
