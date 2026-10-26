import SwiftUI

/// The seven places in the open notch, each with its own name, symbol, accent and height.
enum NotchTab: String, CaseIterable, Identifiable {
    case den, hunt, scratch, purr, stash, oasis, settings

    var id: String { rawValue }

    var title: String {
        switch self {
        case .den: return "Den"
        case .hunt: return "Hunt"
        case .scratch: return "Scratch"
        case .purr: return "Purr"
        case .stash: return "Stash"
        case .oasis: return "Oasis"
        case .settings: return "Settings"
        }
    }

    /// What it is, for tooltips, VoiceOver and the menu-bar menu.
    var subtitle: String {
        switch self {
        case .den: return "Focus timer"
        case .hunt: return "Tasks and meetings"
        case .scratch: return "Notes and clips"
        case .purr: return "Now playing"
        case .stash: return "File shelf"
        case .oasis: return "Water"
        case .settings: return "Settings"
        }
    }

    var icon: String {
        switch self {
        case .den: return "timer"
        case .hunt: return "checklist"
        case .scratch: return "note.text"
        case .purr: return "waveform"
        case .stash: return "tray.full"
        case .oasis: return "drop"
        case .settings: return "gearshape"
        }
    }

    /// Selected tabs use the filled variant where SF Symbols has one.
    var selectedIcon: String {
        switch self {
        case .den, .hunt, .purr, .scratch: return icon
        case .stash: return "tray.full.fill"
        case .oasis: return "drop.fill"
        case .settings: return "gearshape.fill"
        }
    }

    var accent: Accent {
        switch self {
        case .den: return .den
        case .hunt: return .hunt
        case .scratch: return .scratch
        case .purr: return .purr
        case .stash: return .stash
        case .oasis: return .oasis
        case .settings: return .neutral
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

/// A short, self-dismissing activity card (a "Whisker").
struct Peek: Identifiable, Equatable {
    var id = UUID()
    var icon: String
    var title: String
    var subtitle: String
    var tint: Color = Theme.gold
    var tab: NotchTab? = nil
    var mood: LionMood = .happy
    var sound: String? = nil
    /// 0...1 shows a progress ring (battery), nil hides it.
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
    /// Pounce: the new screenshot or download (thumbnail, drag, Copy · Stash · Show).
    var file: URL? = nil
    /// Rally: the meeting link behind the Join button.
    var link: URL? = nil

    var isHUD: Bool { hudKey != nil }
    /// Cards with a thumbnail or buttons need a taller second row.
    var isRich: Bool { file != nil || link != nil }
}

/// A message that floats at the bottom of the open notch, optionally with one action (Undo).
struct Toast: Identifiable, Equatable {
    let id = UUID()
    var text: String
    var actionTitle: String? = nil
    var action: (() -> Void)? = nil

    static func == (lhs: Toast, rhs: Toast) -> Bool { lhs.id == rhs.id }
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
    static let wing: CGFloat = 44
    static let expandedWidth: CGFloat = 440
    static let shadowMargin: CGFloat = 30
    /// Height of the sing-along line under the closed notch.
    static let lyricBand: CGFloat = 24

    // Open-notch chrome around the page.
    static let tabStripTop: CGFloat = 4
    static let tabStripHeight: CGFloat = 28
    static let pageTop: CGFloat = 10
    static let pageBottom: CGFloat = 12
    static let pageInset: CGFloat = 14

    static let compactShape = (radius: CGFloat(12), ear: CGFloat(8))
    static let peekShape = (radius: CGFloat(18), ear: CGFloat(11))
    static let expandedShape = (radius: CGFloat(22), ear: CGFloat(13))

    /// Each tab is only as tall as its content.
    static func paneHeight(_ tab: NotchTab, lyrics: Bool = false) -> CGFloat {
        switch tab {
        case .den: return 160
        case .hunt: return 188
        case .scratch: return 180
        case .purr: return lyrics ? 170 : 122
        case .stash: return 128
        case .oasis: return 136
        case .settings: return 196
        }
    }

    static var maxPaneHeight: CGFloat {
        NotchTab.allCases.map { paneHeight($0, lyrics: true) }.max() ?? 200
    }

    static func barHeight(_ g: NotchGeometry) -> CGFloat {
        g.hasNotch ? g.barHeight : max(22, g.barHeight)
    }

    static func expandedSize(_ g: NotchGeometry, pane: CGFloat) -> CGSize {
        CGSize(width: expandedWidth, height: barHeight(g) + tabStripTop + tabStripHeight + pageTop + pane + pageBottom)
    }

    /// The panel never resizes: it always fits the tallest tab.
    static func panelSize(_ g: NotchGeometry) -> CGSize {
        let e = expandedSize(g, pane: maxPaneHeight)
        return CGSize(width: e.width + expandedShape.ear * 2 + shadowMargin * 2,
                      height: e.height + shadowMargin + 10)
    }
}
