import SwiftUI

enum NotchTab: String, CaseIterable, Identifiable {
    case focus, tasks, notes, capture, music, shelf, water, settings

    var id: String { rawValue }

    var title: String {
        switch self {
        case .focus: return "Focus"
        case .tasks: return "Tasks"
        case .notes: return "Notes"
        case .capture: return "Capture"
        case .music: return "Music"
        case .shelf: return "Shelf"
        case .water: return "Water"
        case .settings: return "Settings"
        }
    }

    var icon: String {
        switch self {
        case .focus: return "timer"
        case .tasks: return "checklist"
        case .notes: return "note.text"
        case .capture: return "doc.on.clipboard"
        case .music: return "waveform"
        case .shelf: return "tray.2"
        case .water: return "drop"
        case .settings: return "gearshape"
        }
    }

    var index: Int { NotchTab.allCases.firstIndex(of: self) ?? 0 }
}

enum Presentation: Equatable {
    case compact
    case peek
    case expanded
}

enum LionMood: Equatable {
    case awake, sleeping, happy, focused
}

/// A short, self-dismissing activity card.
struct Peek: Identifiable, Equatable {
    var id = UUID()
    var icon: String
    var title: String
    var subtitle: String
    var tint: Color = Theme.gold
    var tab: NotchTab? = nil
    var mood: LionMood = .happy
    var sound: String? = nil
    /// 0...1 shows a progress arc (battery), nil hides it.
    var progress: Double? = nil
    var showsLion: Bool = false
    /// Slim overlay (volume, Caps Lock, keyboard) instead of a card. Overlays with the same
    /// key update in place, so holding a volume key doesn't restart the animation.
    var hudKey: String? = nil
    /// 0...1 shows a level bar in an overlay.
    var level: Double? = nil
    /// Big value in the right wing, beside the camera ("70%").
    var value: String? = nil
    /// Show the now-playing artwork in the left wing and an equalizer in the right.
    var artwork: Bool = false

    var isHUD: Bool { hudKey != nil }

    static func == (lhs: Peek, rhs: Peek) -> Bool {
        lhs.id == rhs.id && lhs.level == rhs.level && lhs.title == rhs.title && lhs.icon == rhs.icon
    }
}

/// Where the notch lives on the current display.
struct NotchGeometry: Equatable {
    var hasNotch = false
    /// Width of the hardware notch (or a pleasant default on displays without one).
    var notchWidth: CGFloat = 190
    /// Height of the menu-bar strip the closed notch sits in.
    var barHeight: CGFloat = 32
}

/// All layout numbers in one place. Sizes are the notch *body*; ears are added on both sides.
enum NotchMetrics {
    static let wing: CGFloat = 46
    static let expandedWidth: CGFloat = 500
    static let expandedContentHeight: CGFloat = 214
    static let shadowMargin: CGFloat = 34

    /// Height of the sing-along line under the closed notch.
    static let lyricBand: CGFloat = 24

    static let compactShape = (radius: CGFloat(12), ear: CGFloat(8))
    static let peekShape = (radius: CGFloat(20), ear: CGFloat(12))
    static let expandedShape = (radius: CGFloat(24), ear: CGFloat(14))

    static func barHeight(_ g: NotchGeometry) -> CGFloat {
        g.hasNotch ? g.barHeight : max(30, g.barHeight)
    }

    static func expandedSize(_ g: NotchGeometry) -> CGSize {
        CGSize(width: expandedWidth, height: barHeight(g) + expandedContentHeight)
    }

    static func panelSize(_ g: NotchGeometry) -> CGSize {
        let e = expandedSize(g)
        return CGSize(width: e.width + expandedShape.ear * 2 + shadowMargin * 2,
                      height: e.height + shadowMargin + 10)
    }
}
