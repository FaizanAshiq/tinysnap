import CoreGraphics
import Foundation

/// A word the text reader found, with its box in capture pixels, y growing downward.
public struct TextWord: Equatable, Sendable {
    public var text: String
    public var box: CGRect

    public init(text: String, box: CGRect) {
        self.text = text
        self.box = box
    }
}

/// One line of words, left to right.
public struct TextLine: Equatable, Sendable {
    public var words: [TextWord]

    public init(words: [TextWord]) {
        self.words = words
    }
}

/// What Redact covers.
public enum RedactTarget: CaseIterable, Sendable {
    case emails, phones, numbers, allText
}

/// Finds what to redact among the words read from a capture. A match can run over several
/// words, as a phone number written +1 (555) 014-2297 does, and is covered whole.
public enum TextRedaction {
    public static func boxes(in lines: [TextLine], for target: RedactTarget) -> [CGRect] {
        lines.flatMap { line -> [CGRect] in
            guard let first = line.words.first else { return [] }
            if target == .allText { return [line.words.dropFirst().reduce(first.box) { $0.union($1.box) }] }
            // The line as one string, words a space apart, and where each word sits in it.
            var text = "", ranges: [NSRange] = []
            for word in line.words {
                if !text.isEmpty { text += " " }
                ranges.append(NSRange(location: (text as NSString).length, length: (word.text as NSString).length))
                text += word.text
            }
            return matches(in: text, for: target).compactMap { match in
                let covered = line.words.indices.filter { NSIntersectionRange(ranges[$0], match).length > 0 }
                guard let start = covered.first else { return nil }
                return covered.dropFirst().reduce(line.words[start].box) { $0.union(line.words[$1].box) }
            }
        }
    }

    /// What in `text` is the target. Phone numbers take seven digits or more, numbers four or
    /// more, so a lone 5 in "after 5 pm" stays.
    static func matches(in text: String, for target: RedactTarget) -> [NSRange] {
        let pattern: String
        let digits: Int
        switch target {
        case .emails: (pattern, digits) = (#"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}"#, 0)
        case .phones: (pattern, digits) = (#"[+(]?\d[\d ().-]{5,}\d"#, 7)
        case .numbers: (pattern, digits) = (#"[+(]?\d(?:[\d ().,/-]*\d)?"#, 4)
        case .allText: return [NSRange(location: 0, length: (text as NSString).length)]
        }
        guard let expression = try? NSRegularExpression(pattern: pattern) else { return [] }
        return expression.matches(in: text, range: NSRange(location: 0, length: (text as NSString).length))
            .map(\.range)
            .filter { (text as NSString).substring(with: $0).filter(\.isNumber).count >= digits }
    }
}
