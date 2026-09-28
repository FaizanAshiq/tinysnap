import CoreGraphics

/// The colours a capture's surroundings are painted with, read from a 64 pixel copy, so
/// even a 5K capture costs one quick downsample. Pixels are grouped by colour and the
/// biggest group wins, which ignores a toolbar or a photo in one corner. Transparent
/// pixels, such as a window's rounded corners, are left out.
struct ColorSample {
    let edge: CGColor
    let gradient: [CGColor]

    private static let side = 64
    /// How far apart, in 0 to 255 per channel, two colours are to read as different.
    private static let different = 80.0

    init(_ image: CGImage) {
        let white = CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1)
        let pixels = Self.pixels(of: image)
        edge = Self.groups(pixels.filter(\.onBorder)).first?.color ?? white
        let groups = Self.groups(pixels)
        guard let first = groups.first else {
            gradient = [white, white]
            return
        }
        let second = groups.dropFirst().first { $0.distance(to: first) >= Self.different }
        gradient = [first.color, second?.color ?? first.shade]
    }

    private struct Pixel {
        let red, green, blue: Int
        let onBorder: Bool
    }

    private struct Group {
        var count = 0
        var red = 0, green = 0, blue = 0

        var mean: (Double, Double, Double) {
            let count = Double(max(1, self.count))
            return (Double(red) / count, Double(green) / count, Double(blue) / count)
        }

        var color: CGColor {
            let (red, green, blue) = mean
            return CGColor(srgbRed: red / 255, green: green / 255, blue: blue / 255, alpha: 1)
        }

        func distance(to other: Group) -> Double {
            let (a, b) = (mean, other.mean)
            return ((a.0 - b.0) * (a.0 - b.0) + (a.1 - b.1) * (a.1 - b.1) + (a.2 - b.2) * (a.2 - b.2)).squareRoot()
        }

        /// A light colour gets a darker shade and anything else a lighter one, so the
        /// gradient is always visible.
        var shade: CGColor {
            let (red, green, blue) = mean
            let light = (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255 > 0.8
            func mix(_ channel: Double) -> CGFloat {
                CGFloat(light ? channel * 0.7 / 255 : (channel + (255 - channel) * 0.4) / 255)
            }
            return CGColor(srgbRed: mix(red), green: mix(green), blue: mix(blue), alpha: 1)
        }
    }

    private static func pixels(of image: CGImage) -> [Pixel] {
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: side, height: side, bitsPerComponent: 8, bytesPerRow: side * 4,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let data = context.data else { return [] }
        context.interpolationQuality = .medium
        context.draw(image, in: CGRect(x: 0, y: 0, width: side, height: side))
        let bytes = data.bindMemory(to: UInt8.self, capacity: side * side * 4)
        var pixels: [Pixel] = []
        pixels.reserveCapacity(side * side)
        for y in 0..<side {
            for x in 0..<side {
                let at = (y * side + x) * 4
                let alpha = Int(bytes[at + 3])
                guard alpha >= 128 else { continue }
                // Premultiplied, so each channel is divided back out.
                pixels.append(Pixel(red: Int(bytes[at]) * 255 / alpha, green: Int(bytes[at + 1]) * 255 / alpha,
                                    blue: Int(bytes[at + 2]) * 255 / alpha,
                                    onBorder: y == 0 || y == side - 1 || x == 0 || x == side - 1))
            }
        }
        return pixels
    }

    /// Biggest first, and equal sizes by colour: a dictionary's order changes every
    /// launch, which could flip a capture's gradient on reopening.
    private static func groups(_ pixels: [Pixel]) -> [Group] {
        var groups: [Int: Group] = [:]
        for pixel in pixels {
            let key = (pixel.red >> 4) << 8 | (pixel.green >> 4) << 4 | pixel.blue >> 4
            var group = groups[key, default: Group()]
            group.count += 1
            group.red += pixel.red
            group.green += pixel.green
            group.blue += pixel.blue
            groups[key] = group
        }
        return groups.sorted { $0.value.count != $1.value.count ? $0.value.count > $1.value.count : $0.key < $1.key }
            .map(\.value)
    }
}
