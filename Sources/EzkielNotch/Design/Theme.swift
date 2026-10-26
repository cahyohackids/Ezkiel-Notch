import AppKit
import SwiftUI

/// Ezkielyna palette (navy night + lion gold + teal), shared by every view. Flat colors only:
/// gold is the one accent, teal marks rest/done, water blue belongs to the Water tab.
enum Theme {
    // Base
    static let void = Color(hex: 0x05070F)
    static let navy = Color(hex: 0x0B1020)
    static let navy2 = Color(hex: 0x131A30)
    static let navy3 = Color(hex: 0x1C2743)
    static let slate = Color(hex: 0x2A3A5C)
    static let ink = Color(hex: 0xF3E6C9)          // cream text
    static let mist = Color(hex: 0x9D8C70)          // muted text

    // Lion
    static let gold = Color(hex: 0xFFCF87)
    static let orange = Color(hex: 0xF09A4A)
    static let ember = Color(hex: 0xD0652E)
    static let rust = Color(hex: 0x7C3421)
    static let cream = Color(hex: 0xF3E6C9)
    static let blush = Color(hex: 0xFF98AE)

    // Accents
    static let teal = Color(hex: 0x86E3C6)
    static let teal2 = Color(hex: 0x38B39B)
    static let sky = Color(hex: 0x6786B0)
    static let berry = Color(hex: 0xE4497A)

    static let water = Color(hex: 0x7CC6F2)

    static let surface = Color.white.opacity(0.06)
    static let surfaceHi = Color.white.opacity(0.10)
    static let hairline = Color.white.opacity(0.08)

    /// Bouncy spring (cubic-bezier(.34,1.32,.5,1) ≈ a lightly bouncy spring).
    static let spring = Animation.spring(response: 0.46, dampingFraction: 0.74, blendDuration: 0.1)
    static let snappy = Animation.spring(response: 0.3, dampingFraction: 0.8)
    static let bouncy = Animation.spring(response: 0.55, dampingFraction: 0.58)
    static let soft = Animation.easeInOut(duration: 0.22)
    /// Opening: a touch more overshoot than `spring`, so the body pours out like liquid.
    static let liquid = Animation.spring(response: 0.5, dampingFraction: 0.68, blendDuration: 0.1)
    /// The settle of a water surface: low damping, a few visible ripples.
    static let wobble = Animation.spring(response: 0.42, dampingFraction: 0.32)
    /// Tab indicator and small selections: quick, with one soft overshoot.
    static let jelly = Animation.spring(response: 0.4, dampingFraction: 0.66)

    /// The system face, like the rest of macOS. Numbers use `.monospacedDigit()` where they tick.
    static func font(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight)
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
