import CoreGraphics
import CoreText
import Foundation

/// Measures and draws text with Core Text, so the renderer needs no AppKit.
///
/// Fonts are made at their size in points and the drawing is scaled up to capture
/// pixels, never made at the pixel size. San Francisco changes its letter shapes and
/// spacing with size, so a 20 point label made at 40 on a Retina capture came out in
/// the tighter display cut, about 5% narrower than the same text typed in the editor.
public enum TextLayout {
    /// Public so the editor's text field types in exactly the font the renderer draws.
    public static func font(points: CGFloat, bold: Bool = false) -> CTFont {
        CTFontCreateUIFontForLanguage(bold ? .emphasizedSystem : .system, points, nil)
            ?? CTFontCreateWithName("Helvetica" as CFString, points, nil)
    }

    static func lineHeight(of font: CTFont) -> CGFloat {
        CTFontGetAscent(font) + CTFontGetDescent(font) + CTFontGetLeading(font)
    }

    static func lines(of string: String, font: CTFont, color: CGColor) -> [CTLine] {
        string.components(separatedBy: "\n").map { text in
            let attributed = NSAttributedString(string: text, attributes: [
                NSAttributedString.Key(kCTFontAttributeName as String): font,
                NSAttributedString.Key(kCTForegroundColorAttributeName as String): color,
            ])
            return CTLineCreateWithAttributedString(attributed)
        }
    }

    /// How far a filled text's box reaches past its letters, in points: a third of an em
    /// at the sides, an eighth above and below, where the line's own ascent and descent
    /// already leave room.
    static func boxPadding(points: CGFloat) -> CGSize { CGSize(width: points * 0.3, height: points * 0.12) }

    /// A filled text's box corner radius, in points.
    static func boxCorner(points: CGFloat) -> CGFloat { points * 0.25 }

    /// The colour a text's letters are drawn in: its own, or black or white on a filled
    /// text's box so they read on it. Public so the editor's text field types in it too.
    public static func letterColor(_ style: Style) -> CGColor {
        let color = Palette.color(hex: style.colorHex)
        return style.filled ? MeasureShape.textColor(on: color) : color
    }

    /// The box the text fills in capture pixels, measured from its top left corner. An
    /// empty string still gets a narrow box, so text being typed can be clicked and found.
    static func size(of string: String, points: CGFloat, scale: CGFloat, bold: Bool = false) -> CGSize {
        let font = font(points: points, bold: bold)
        let lines = lines(of: string, font: font, color: CGColor(gray: 0, alpha: 1))
        let widest = lines.map { CGFloat(CTLineGetTypographicBounds($0, nil, nil, nil)) }.max() ?? 0
        return CGSize(width: max(widest, points / 2) * scale, height: lineHeight(of: font) * CGFloat(lines.count) * scale)
    }

    /// How far in from the box's left edge each line starts, in points: none when set left,
    /// half the room the line leaves when centred, all of it when set right.
    static func lineOffsets(of string: String, points: CGFloat, align: TextAlign, bold: Bool = false) -> [CGFloat] {
        let font = font(points: points, bold: bold)
        let widths = lines(of: string, font: font, color: CGColor(gray: 0, alpha: 1))
            .map { CGFloat(CTLineGetTypographicBounds($0, nil, nil, nil)) }
        let box = max(widths.max() ?? 0, points / 2)
        let share: CGFloat = switch align {
        case .left: 0
        case .center: 0.5
        case .right: 1
        }
        return widths.map { (box - $0) * share }
    }

    /// Draws into a context whose user space is capture pixels with y growing downward,
    /// with `origin` as the top left corner of the first line.
    static func draw(_ string: String, at origin: CGPoint, points: CGFloat, scale: CGFloat, color: CGColor,
                     bold: Bool = false, align: TextAlign = .left, in context: CGContext) {
        let font = font(points: points, bold: bold)
        let ascent = CTFontGetAscent(font)
        let height = lineHeight(of: font)

        context.saveGState()
        context.translateBy(x: origin.x, y: origin.y)
        context.scaleBy(x: scale, y: scale)
        // Glyphs are drawn y up. Flipping the text matrix turns them the right way up
        // inside the y down user space.
        context.textMatrix = CGAffineTransform(scaleX: 1, y: -1)
        let offsets = align == .left ? [] : lineOffsets(of: string, points: points, align: align, bold: bold)
        for (index, line) in lines(of: string, font: font, color: color).enumerated() {
            context.textPosition = CGPoint(x: offsets.indices.contains(index) ? offsets[index] : 0, y: ascent + CGFloat(index) * height)
            CTLineDraw(line, context)
        }
        context.restoreGState()
    }

    /// The width, ascent and descent of one line in capture pixels, for centring a step number.
    static func metrics(of string: String, points: CGFloat, scale: CGFloat, bold: Bool) -> (width: CGFloat, ascent: CGFloat, descent: CGFloat) {
        let font = font(points: points, bold: bold)
        let line = lines(of: string, font: font, color: CGColor(gray: 0, alpha: 1))[0]
        return (CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil)) * scale,
                CTFontGetAscent(font) * scale, CTFontGetDescent(font) * scale)
    }
}
