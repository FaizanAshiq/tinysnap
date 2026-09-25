import CoreGraphics
import Foundation
import Vision

/// What Vision found in an image: QR code contents, and lines of text top to bottom.
public struct TextReading: Equatable, Sendable {
    public var codes: [String]
    public var lines: [String]

    public init(codes: [String], lines: [String]) {
        self.codes = codes
        self.lines = lines
    }

    public var isEmpty: Bool { codes.isEmpty && lines.isEmpty }

    /// What is copied. A QR code wins over the text around it: pointing at a code means
    /// wanting what it holds, not the caption beside it.
    public var text: String { (codes.isEmpty ? lines : codes).joined(separator: "\n") }

    /// A web address to offer Open Link for, only when that is all the result is.
    public var link: URL? {
        let candidate = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !candidate.contains(where: \.isWhitespace), let url = URL(string: candidate),
              ["http", "https"].contains(url.scheme?.lowercased()), url.host != nil else { return nil }
        return url
    }
}

/// Text recognition and QR codes, on device. Vision picks the language itself and
/// corrects words with its language model, so there is no language setting.
public enum TextReader {
    public static func read(_ image: CGImage) throws -> TextReading {
        let text = VNRecognizeTextRequest()
        text.recognitionLevel = .accurate
        text.usesLanguageCorrection = true
        text.automaticallyDetectsLanguage = true
        let codes = VNDetectBarcodesRequest()
        codes.symbologies = [.qr]
        try VNImageRequestHandler(cgImage: image, options: [:]).perform([text, codes])

        let observations = text.results ?? []
        let lines = readingOrder(observations.map(\.boundingBox)).compactMap { observations[$0].topCandidates(1).first?.string }
        var seen = Set<String>()
        // One code can be reported more than once, so each payload is kept once, in order.
        let payloads = (codes.results ?? []).compactMap(\.payloadStringValue).filter { seen.insert($0).inserted }
        return TextReading(codes: payloads, lines: lines)
    }

    /// Vision reports text in blocks, which reads a dashboard column by column. Lines are
    /// put into rows instead: a row is every line whose middle sits within half a line of
    /// the row's first. Rows go top to bottom, and each row left to right. Boxes are
    /// Vision's, y growing upward.
    static func readingOrder(_ boxes: [CGRect]) -> [Int] {
        let topFirst = boxes.indices.sorted { boxes[$0].midY > boxes[$1].midY }
        var rows: [[Int]] = []
        for index in topFirst {
            if let first = rows.last?.first, abs(boxes[first].midY - boxes[index].midY) < boxes[first].height / 2 {
                rows[rows.count - 1].append(index)
            } else {
                rows.append([index])
            }
        }
        return rows.flatMap { row in row.sorted { boxes[$0].minX < boxes[$1].minX } }
    }

    /// Join Lines: every run of spaces and line breaks becomes one space.
    public static func join(_ text: String) -> String {
        text.split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }
}
