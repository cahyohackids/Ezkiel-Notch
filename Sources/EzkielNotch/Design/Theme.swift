import AppKit
import SwiftUI

/// Design tokens. The notch body is pure black (like the Dynamic Island); colour comes from content,
/// one accent per feature. Every view takes its sizes, radii, surfaces and springs from here.
enum Theme {
    // MARK: Colour

    /// Primary text, warm off-white.
    static let ink = Color(hex: 0xF5F1EA)
    /// Secondary text.
    static let mist = Color.white.opacity(0.56)
    /// Tertiary text, disabled glyphs.
    static let faint = Color.white.opacity(0.34)
    /// Text on a light accent fill.
    static let onAccent = Color(hex: 0x14110F)

    /// Ezkielyna gold, the app's own accent (selection, header lion, prominent buttons).
    static let gold = Color(hex: 0xFFCF87)
    static let orange = Color(hex: 0xF09A4A)
    static let teal = Color(hex: 0x6FD6B4)
    static let berry = Color(hex: 0xFF5A7A)
    static let water = Color(hex: 0x64C8F5)
    static let spotify = Color(hex: 0x1ED760)
    static let appleMusic = Color(hex: 0xFF4E6A)
    /// Lion details.
    static let rust = Color(hex: 0x7C3421)
    static let blush = Color(hex: 0xFF98AE)
    static let navy = Color(hex: 0x131A30)

    // MARK: Surfaces (white over black)

    static let surface = Color.white.opacity(0.05)
    static let surfaceRaised = Color.white.opacity(0.08)
    static let hairline = Color.white.opacity(0.08)
    /// Added on hover to whatever surface a control sits on.
    static let hoverLift = 0.04

    // MARK: Shape

    enum Radius {
        static let chip: CGFloat = 7
        static let control: CGFloat = 10
        static let card: CGFloat = 14
    }

    enum Space {
        static let xs: CGFloat = 4
        static let s: CGFloat = 8
        static let m: CGFloat = 12
        static let l: CGFloat = 16
    }

    // MARK: Type (five sizes)

    enum Size {
        static let caption: CGFloat = 10
        static let secondary: CGFloat = 11.5
        static let body: CGFloat = 12.5
        static let title: CGFloat = 15
        static let numeric: CGFloat = 24
    }

    /// The system face, like the rest of macOS.
    static func font(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight)
    }

    /// Tabular figures for anything that ticks.
    static func number(_ size: CGFloat, _ weight: Font.Weight = .semibold) -> Font {
        .system(size: size, weight: weight).monospacedDigit()
    }

    // MARK: Motion

    /// True when the user asked macOS to reduce motion: springs become short cross-fades.
    static var reduceMotion: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }

    /// Small state changes (selection, toggles, hover).
    static var snappy: Animation { reduceMotion ? .easeInOut(duration: 0.15) : .spring(response: 0.3, dampingFraction: 0.86) }
    /// Size and layout changes (open, close, tab height).
    static var smooth: Animation { reduceMotion ? .easeInOut(duration: 0.2) : .spring(response: 0.44, dampingFraction: 0.86) }
    /// Things that arrive (peeks, cards, the notch opening): one soft overshoot.
    static var bouncy: Animation { reduceMotion ? .easeInOut(duration: 0.2) : .spring(response: 0.48, dampingFraction: 0.74) }
    /// The settle of the notch's bottom edge: one visible bounce, no sign flip.
    static var wobble: Animation { reduceMotion ? .easeOut(duration: 0.15) : .spring(response: 0.4, dampingFraction: 0.55) }
    static let soft = Animation.easeInOut(duration: 0.2)
}

/// Apple-style two-stop gradient for one feature (light → deep, same hue).
struct Accent: Equatable {
    var light: Color
    var deep: Color

    var color: Color { deep }
    var linear: LinearGradient { LinearGradient(colors: [light, deep], startPoint: .top, endPoint: .bottom) }
    var diagonal: LinearGradient { LinearGradient(colors: [light, deep], startPoint: .topLeading, endPoint: .bottomTrailing) }
    /// Fitness-ring style: the stroke darkens along its length.
    var ring: AngularGradient { AngularGradient(colors: [deep, light, deep], center: .center, startAngle: .degrees(-90), endAngle: .degrees(270)) }

    static let den = Accent(light: Color(hex: 0xFFD27A), deep: Color(hex: 0xFF9500))
    static let hunt = Accent(light: Color(hex: 0xFF9A8A), deep: Color(hex: 0xFF453A))
    static let rally = Accent(light: Color(hex: 0xD9A2FF), deep: Color(hex: 0xBF5AF2))
    static let scratch = Accent(light: Color(hex: 0xFFE680), deep: Color(hex: 0xFFC400))
    static let purr = Accent(light: Color(hex: 0xFF8FB0), deep: Color(hex: 0xFF375F))
    static let stash = Accent(light: Color(hex: 0x9C9AFF), deep: Color(hex: 0x5E5CE6))
    static let oasis = Accent(light: Color(hex: 0x8AE3FF), deep: Color(hex: 0x0A84FF))
    static let whiskers = Accent(light: Color(hex: 0xF5F1EA), deep: Color(hex: 0xC7C2BA))
    static let neutral = Accent(light: Color(hex: 0xC7C7CC), deep: Color(hex: 0x8E8E93))

    /// An accent from one colour (e.g. the artwork tint): lighter on top, the colour below.
    static func from(_ color: NSColor) -> Accent {
        let c = color.usingColorSpace(.sRGB) ?? color
        var h: CGFloat = 0, s: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        c.getHue(&h, saturation: &s, brightness: &b, alpha: &a)
        // Keep it readable on black: enough brightness, not washed out.
        let deep = NSColor(hue: h, saturation: min(0.9, max(0.45, s)), brightness: max(0.72, b), alpha: 1)
        let light = NSColor(hue: h, saturation: min(0.6, max(0.25, s * 0.6)), brightness: 1, alpha: 1)
        return Accent(light: Color(nsColor: light), deep: Color(nsColor: deep))
    }
}

extension Color {
    init(hex: UInt32, opacity: Double = 1) {
        self.init(.sRGB,
                  red: Double((hex >> 16) & 0xFF) / 255,
                  green: Double((hex >> 8) & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255,
                  opacity: opacity)
    }
}
