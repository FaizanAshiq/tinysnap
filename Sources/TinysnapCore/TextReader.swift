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

    /// What is copied: a reading for text holds only lines, and a scan only codes.
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
    /// Copy Text reads the words and Scan QR Code reads what codes hold. Asked for apart,
    /// the caption beside a code is never taken for it, and each pays only for its own pass.
    public enum Target: Sendable {
        case text, codes
    }

    /// Every line Vision reads, split into words at the spaces, each with its box in pixels, y
    /// down, for Redact. Read as accurately as Copy Text reads.
    public static func lines(in image: CGImage) throws -> [TextLine] {
        let handler = VNImageRequestHandler(cgImage: image, options: [:])
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.automaticallyDetectsLanguage = true
        try handler.perform([request])
        let width = CGFloat(image.width), height = CGFloat(image.height)
        return (request.results ?? []).compactMap { observation in
            guard let candidate = observation.topCandidates(1).first else { return nil }
            let string = candidate.string
            let words = string.split(whereSeparator: \.isWhitespace).compactMap { part -> TextWord? in
                // Vision boxes in 0 to 1, y up.
                guard let box = (try? candidate.boundingBox(for: part.startIndex..<part.endIndex))??.boundingBox else { return nil }
                return TextWord(text: String(part), box: CGRect(x: box.minX * width, y: (1 - box.maxY) * height,
                                                                 width: box.width * width, height: box.height * height))
            }
            return words.isEmpty ? nil : TextLine(words: words)
        }
    }

    public static func read(_ image: CGImage, for target: Target) throws -> TextReading {
        let handler = VNImageRequestHandler(cgImage: image, options: [:])
        switch target {
        case .text:
            let request = VNRecognizeTextRequest()
            request.recognitionLevel = .accurate
            request.usesLanguageCorrection = true
            request.automaticallyDetectsLanguage = true
            try handler.perform([request])
            let observations = request.results ?? []
            let lines = readingOrder(observations.map(\.boundingBox)).compactMap { observations[$0].topCandidates(1).first?.string }
            return TextReading(codes: [], lines: lines)
        case .codes:
            let request = VNDetectBarcodesRequest()
            request.symbologies = [.qr]
            try handler.perform([request])
            var seen = Set<String>()
            // One code can be reported more than once, so each payload is kept once, in order.
            let payloads = (request.results ?? []).compactMap(\.payloadStringValue).filter { seen.insert($0).inserted }
            return TextReading(codes: payloads, lines: [])
        }
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

    /// Reads a few words once, at launch, so the first Copy Text does not wait while macOS
    /// gets its text model ready, which took 26 seconds the first time on this machine and
    /// a sixth of a second after.
    public static func warmUp() {
        guard let sample = warmUpSample() else { return }
        _ = try? read(sample, for: .text)
    }

    /// Black words on white, as a capture would have them: a blank image never wakes the
    /// text model, since there is nothing in it to read.
    static func warmUpSample() -> CGImage? {
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: 480, height: 80, bitsPerComponent: 8, bytesPerRow: 0, space: space,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        context.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 480, height: 80))
        // The text layout draws with y growing downward, as captures are.
        context.translateBy(x: 0, y: 80)
        context.scaleBy(x: 1, y: -1)
        TextLayout.draw("Tinysnap reads text", at: CGPoint(x: 20, y: 20), points: 18, scale: 2,
                        color: CGColor(srgbRed: 0, green: 0, blue: 0, alpha: 1), in: context)
        return context.makeImage()
    }

    /// Join Lines: every run of spaces and line breaks becomes one space.
    public static func join(_ text: String) -> String {
        text.split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }
}
