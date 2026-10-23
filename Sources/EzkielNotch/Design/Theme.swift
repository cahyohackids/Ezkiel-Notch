import AppKit
import SwiftUI

/// Ezkielyna palette (navy night + lion gold/orange + teal), shared by every view.
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

    static let lionGradient = LinearGradient(colors: [gold, orange, ember], startPoint: .topLeading, endPoint: .bottomTrailing)
    static let glowGradient = LinearGradient(colors: [gold, orange, ember, orange, gold], startPoint: .leading, endPoint: .trailing)
    static let tealGradient = LinearGradient(colors: [teal, teal2], startPoint: .leading, endPoint: .trailing)
    static let waterGradient = LinearGradient(colors: [Color(hex: 0x9FE7FF), Color(hex: 0x4FA3E8)], startPoint: .top, endPoint: .bottom)

    static let surface = Color.white.opacity(0.06)
    static let surfaceHi = Color.white.opacity(0.10)
    static let hairline = Color.white.opacity(0.08)

    /// Nusa-style spring (cubic-bezier(.34,1.32,.5,1) ≈ a lightly bouncy spring).
    static let spring = Animation.spring(response: 0.46, dampingFraction: 0.74, blendDuration: 0.1)
    static let snappy = Animation.spring(response: 0.3, dampingFraction: 0.8)
    static let bouncy = Animation.spring(response: 0.55, dampingFraction: 0.58)
    static let soft = Animation.easeInOut(duration: 0.22)

    static func rounded(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .rounded)
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
