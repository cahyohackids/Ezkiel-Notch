import Foundation

// MARK: - Persisted records

public struct TaskItem: Codable, Identifiable, Equatable {
    public var id: UUID
    public var title: String
    public var done: Bool
    public var due: Date?
    public var reminded: Bool
    public var created: Date

    public init(id: UUID = UUID(), title: String, done: Bool = false, due: Date? = nil, reminded: Bool = false, created: Date = Date()) {
        self.id = id
        self.title = title
        self.done = done
        self.due = due
        self.reminded = reminded
        self.created = created
    }
}

public struct NoteItem: Codable, Identifiable, Equatable {
    public var id: UUID
    public var title: String
    public var body: String
    public var updated: Date

    public init(id: UUID = UUID(), title: String = "", body: String = "", updated: Date = Date()) {
        self.id = id
        self.title = title
        self.body = body
        self.updated = updated
    }

    /// Title shown in lists: the explicit title, else the first line of the body.
    public var displayTitle: String {
        let explicit = title.trimmingCharacters(in: .whitespaces)
        if !explicit.isEmpty { return explicit }
        let line = body.split(whereSeparator: \.isNewline).first.map(String.init)?
            .trimmingCharacters(in: .whitespaces) ?? ""
        return line.isEmpty ? "Untitled note" : line
    }
}

public struct ClipItem: Codable, Identifiable, Equatable {
    public var id: UUID
    public var text: String
    public var captured: Date

    public init(id: UUID = UUID(), text: String, captured: Date = Date()) {
        self.id = id
        self.text = text
        self.captured = captured
    }
}

public struct ShelfRecord: Codable, Identifiable, Equatable {
    public var id: UUID
    public var path: String
    public var bookmark: Data?
    public var added: Date

    public init(id: UUID = UUID(), path: String, bookmark: Data? = nil, added: Date = Date()) {
        self.id = id
        self.path = path
        self.bookmark = bookmark
        self.added = added
    }

    public var name: String { (path as NSString).lastPathComponent }
}

public struct WaterEntry: Codable, Identifiable, Equatable {
    public var id: UUID
    public var date: Date
    public var ml: Int

    public init(id: UUID = UUID(), date: Date = Date(), ml: Int) {
        self.id = id
        self.date = date
        self.ml = ml
    }
}

public enum MusicSource: String, Codable, CaseIterable, Identifiable {
    case auto, spotify, appleMusic
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .auto: return "Auto"
        case .spotify: return "Spotify"
        case .appleMusic: return "Music"
        }
    }
}

/// Global shortcut that opens and closes the notch.
public enum HotkeyChoice: String, Codable, CaseIterable, Identifiable {
    /// ⌃⌥N. Default: nothing common uses it (⌥⌘I is the browsers' Web Inspector, ⌃⌥Space switches input sources).
    case controlOptionN
    case controlOptionSpace
    case optionCommandI
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .controlOptionN: return "⌃⌥N"
        case .controlOptionSpace: return "⌃⌥Space"
        case .optionCommandI: return "⌥⌘I"
        }
    }
}

public struct NotchSettings: Codable, Equatable {
    public var expandOnHover: Bool = true
    public var hoverDelay: Double = 0.25
    public var showMascot: Bool = true
    public var mascotSleeps: Bool = true
    public var haptics: Bool = true
    public var sounds: Bool = true
    public var waterReminders: Bool = true
    public var waterIntervalMinutes: Int = 60
    public var displayName: String = ""
    public var launchAtLogin: Bool = false
    public var waterGoal: Int = 2000
    public var activeHourStart: Int = 8
    public var activeHourEnd: Int = 22
    public var focusMinutes: Int = 25
    public var breakMinutes: Int = 5
    public var musicSource: MusicSource = .auto
    /// Closed notch shows its wings only while something is live (calmer, keeps the menu bar clear).
    public var minimalClosed: Bool = true
    /// Lyrics view in the Music tab. Off until the user taps the lyrics button (LRCLIB is only asked then).
    public var showLyrics: Bool = false
    /// While lyrics are on, sing along under the closed notch too.
    public var closedLyrics: Bool = true
    /// Volume, Caps Lock and keyboard-language overlays.
    public var systemOverlays: Bool = true
    /// Wi-Fi and headphones peeks.
    public var connectionPeeks: Bool = true
    /// Pounce: peek when a new screenshot appears.
    public var pounceScreenshots: Bool = false
    /// Pounce: peek when a download finishes in ~/Downloads.
    public var pounceDownloads: Bool = false
    /// Rally: show today's next meetings in Hunt and peek 5 minutes before.
    public var rally: Bool = false
    /// Rally: "in 5m" countdown in the closed notch.
    public var rallyCountdown: Bool = true
    public var hotkey: HotkeyChoice = .controlOptionN
    /// CGDirectDisplayID of the chosen display; 0 = automatic. `displayName` is the older fallback.
    public var displayID: UInt32 = 0
    /// Roar: unread counts from WhatsApp, Telegram, Teams and other apps' Dock badges (no permission needed).
    public var roar: Bool = true
    /// Roar: peek when an app's unread count goes up.
    public var roarPeeks: Bool = true
    /// Roar: bundle IDs the user muted.
    public var roarMuted: [String] = []
    /// Roar: also read the Dock's badges, so apps that aren't open count too (needs Accessibility).
    public var roarFromDock: Bool = false
    /// Scout: notifications from AI agents and scripts (Claude Code, Codex, ezkiel-notify).
    public var scout: Bool = true
    /// While an app is fullscreen on the notch's display: no wings, no pill on displays without a notch,
    /// and only urgent peeks (low battery, a meeting, an agent that needs you).
    public var hideInFullscreen: Bool = true
    /// During a Den focus session, Roar and finished-agent peeks wait (the lists still update).
    public var quietDuringFocus: Bool = true
    /// Keep the notch out of screen sharing and screen recordings.
    public var hideFromScreenShare: Bool = false
    /// Ask GitHub once a day whether a newer version exists.
    public var checkUpdates: Bool = true
    /// Purr may ask Spotify and Music what is playing (macOS asks for Automation access the first time).
    /// Off until the user opens Purr, so the permission prompt never comes out of nowhere.
    public var musicAllowed: Bool = false
    /// Prowl: dots in the closed notch while a microphone or camera is on.
    public var prowl: Bool = true
    /// Weather glance: the city picked in Settings (no Location permission). Empty = off.
    public var weatherCity: String = ""
    public var weatherLatitude: Double = 0
    public var weatherLongitude: Double = 0
    public var fahrenheit: Bool = false
    /// UI language: follow macOS, English or Indonesian.
    public var language: AppLanguage = .auto
    /// Notch (hangs from the top edge) or Island (a floating pill, like the Dynamic Island).
    public var style: NotchStyle = .notch
    public var islandSize: IslandSize = .medium
    public var islandGap: IslandGap = .medium
    /// Island in Liquid Glass (macOS 26; a blur on older macOS).
    public var islandGlass: Bool = false
    public var islandShadow: Bool = true
    /// What the closed notch shows of Ezkiel: blinking eyes, the lion, or nothing.
    public var closedFace: ClosedFace = .eyes
    /// Bumped when defaults change in a way existing users should get too (see the decoder).
    public var defaultsRevision: Int = NotchSettings.currentRevision
    public static let currentRevision = 2

    public init() {}

    // Tolerant decoding so new settings never wipe a user's saved data.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = NotchSettings.defaults
        expandOnHover = (try? c.decode(Bool.self, forKey: .expandOnHover)) ?? d.expandOnHover
        hoverDelay = (try? c.decode(Double.self, forKey: .hoverDelay)) ?? d.hoverDelay
        showMascot = (try? c.decode(Bool.self, forKey: .showMascot)) ?? d.showMascot
        mascotSleeps = (try? c.decode(Bool.self, forKey: .mascotSleeps)) ?? d.mascotSleeps
        haptics = (try? c.decode(Bool.self, forKey: .haptics)) ?? d.haptics
        sounds = (try? c.decode(Bool.self, forKey: .sounds)) ?? d.sounds
        waterReminders = (try? c.decode(Bool.self, forKey: .waterReminders)) ?? d.waterReminders
        waterIntervalMinutes = (try? c.decode(Int.self, forKey: .waterIntervalMinutes)) ?? d.waterIntervalMinutes
        displayName = (try? c.decode(String.self, forKey: .displayName)) ?? d.displayName
        launchAtLogin = (try? c.decode(Bool.self, forKey: .launchAtLogin)) ?? d.launchAtLogin
        waterGoal = (try? c.decode(Int.self, forKey: .waterGoal)) ?? d.waterGoal
        activeHourStart = (try? c.decode(Int.self, forKey: .activeHourStart)) ?? d.activeHourStart
        activeHourEnd = (try? c.decode(Int.self, forKey: .activeHourEnd)) ?? d.activeHourEnd
        focusMinutes = (try? c.decode(Int.self, forKey: .focusMinutes)) ?? d.focusMinutes
        breakMinutes = (try? c.decode(Int.self, forKey: .breakMinutes)) ?? d.breakMinutes
        musicSource = (try? c.decode(MusicSource.self, forKey: .musicSource)) ?? d.musicSource
        minimalClosed = (try? c.decode(Bool.self, forKey: .minimalClosed)) ?? d.minimalClosed
        showLyrics = (try? c.decode(Bool.self, forKey: .showLyrics)) ?? d.showLyrics
        closedLyrics = (try? c.decode(Bool.self, forKey: .closedLyrics)) ?? d.closedLyrics
        systemOverlays = (try? c.decode(Bool.self, forKey: .systemOverlays)) ?? d.systemOverlays
        connectionPeeks = (try? c.decode(Bool.self, forKey: .connectionPeeks)) ?? d.connectionPeeks
        pounceScreenshots = (try? c.decode(Bool.self, forKey: .pounceScreenshots)) ?? d.pounceScreenshots
        pounceDownloads = (try? c.decode(Bool.self, forKey: .pounceDownloads)) ?? d.pounceDownloads
        rally = (try? c.decode(Bool.self, forKey: .rally)) ?? d.rally
        rallyCountdown = (try? c.decode(Bool.self, forKey: .rallyCountdown)) ?? d.rallyCountdown
        hotkey = (try? c.decode(HotkeyChoice.self, forKey: .hotkey)) ?? d.hotkey
        displayID = (try? c.decode(UInt32.self, forKey: .displayID)) ?? d.displayID
        roar = (try? c.decode(Bool.self, forKey: .roar)) ?? d.roar
        roarPeeks = (try? c.decode(Bool.self, forKey: .roarPeeks)) ?? d.roarPeeks
        roarMuted = (try? c.decode([String].self, forKey: .roarMuted)) ?? d.roarMuted
        roarFromDock = (try? c.decode(Bool.self, forKey: .roarFromDock)) ?? d.roarFromDock
        scout = (try? c.decode(Bool.self, forKey: .scout)) ?? d.scout
        hideInFullscreen = (try? c.decode(Bool.self, forKey: .hideInFullscreen)) ?? d.hideInFullscreen
        quietDuringFocus = (try? c.decode(Bool.self, forKey: .quietDuringFocus)) ?? d.quietDuringFocus
        hideFromScreenShare = (try? c.decode(Bool.self, forKey: .hideFromScreenShare)) ?? d.hideFromScreenShare
        checkUpdates = (try? c.decode(Bool.self, forKey: .checkUpdates)) ?? d.checkUpdates
        musicAllowed = (try? c.decode(Bool.self, forKey: .musicAllowed)) ?? d.musicAllowed
        prowl = (try? c.decode(Bool.self, forKey: .prowl)) ?? d.prowl
        weatherCity = (try? c.decode(String.self, forKey: .weatherCity)) ?? d.weatherCity
        weatherLatitude = (try? c.decode(Double.self, forKey: .weatherLatitude)) ?? d.weatherLatitude
        weatherLongitude = (try? c.decode(Double.self, forKey: .weatherLongitude)) ?? d.weatherLongitude
        fahrenheit = (try? c.decode(Bool.self, forKey: .fahrenheit)) ?? d.fahrenheit
        language = (try? c.decode(AppLanguage.self, forKey: .language)) ?? d.language
        style = (try? c.decode(NotchStyle.self, forKey: .style)) ?? d.style
        islandSize = (try? c.decode(IslandSize.self, forKey: .islandSize)) ?? d.islandSize
        islandGap = (try? c.decode(IslandGap.self, forKey: .islandGap)) ?? d.islandGap
        islandGlass = (try? c.decode(Bool.self, forKey: .islandGlass)) ?? d.islandGlass
        islandShadow = (try? c.decode(Bool.self, forKey: .islandShadow)) ?? d.islandShadow
        // Before 1.6 there was only "show the lion": hidden stays hidden, shown becomes the eyes.
        closedFace = (try? c.decode(ClosedFace.self, forKey: .closedFace)) ?? (showMascot ? .eyes : .off)
        let revision = (try? c.decode(Int.self, forKey: .defaultsRevision)) ?? 1
        if revision < 2 {
            // 1.3.1: calmer defaults. Only values still at the old default move; choices the user made stay.
            if hoverDelay == 0.12 { hoverDelay = d.hoverDelay }
            minimalClosed = true
            // Settings from before 1.3.1 means Purr already ran (and macOS already asked).
            musicAllowed = true
        }
        defaultsRevision = NotchSettings.currentRevision
    }

    public static let defaults = NotchSettings()
}

/// Everything Ezkiel Notch keeps on disk. Stored as one JSON file.
public struct AppData: Codable, Equatable {
    public var tasks: [TaskItem] = []
    public var notes: [NoteItem] = []
    public var clips: [ClipItem] = []
    public var shelf: [ShelfRecord] = []
    public var water: [WaterEntry] = []
    public var settings: NotchSettings = NotchSettings()
    public var focusLog: [FocusEntry] = []
    /// The last Scout events (agents and scripts), newest first.
    public var scoutLog: [ScoutEvent] = []
    /// The Den timer, so a running session survives a restart.
    public var focusState: FocusSnapshot?
    public var onboarded: Bool = false
    /// Update check: when GitHub was last asked, and the newest version already announced with a peek.
    public var lastUpdateCheck: Date?
    public var announcedVersion: String?

    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        // Item by item: one damaged entry costs that entry, never the whole list.
        tasks = c.lossy([TaskItem].self, forKey: .tasks)
        notes = c.lossy([NoteItem].self, forKey: .notes)
        clips = c.lossy([ClipItem].self, forKey: .clips)
        shelf = c.lossy([ShelfRecord].self, forKey: .shelf)
        water = c.lossy([WaterEntry].self, forKey: .water)
        settings = (try? c.decode(NotchSettings.self, forKey: .settings)) ?? NotchSettings()
        focusLog = c.lossy([FocusEntry].self, forKey: .focusLog)
        scoutLog = c.lossy([ScoutEvent].self, forKey: .scoutLog)
        focusState = try? c.decode(FocusSnapshot.self, forKey: .focusState)
        onboarded = (try? c.decode(Bool.self, forKey: .onboarded)) ?? false
        lastUpdateCheck = try? c.decode(Date.self, forKey: .lastUpdateCheck)
        announcedVersion = try? c.decode(String.self, forKey: .announcedVersion)
    }
}

/// Decodes whatever elements of an array it can and skips the rest.
private struct LossyElement<T: Decodable>: Decodable {
    let value: T?
    init(from decoder: Decoder) throws { value = try? T(from: decoder) }
}

extension KeyedDecodingContainer {
    func lossy<T: Decodable>(_ type: [T].Type, forKey key: Key) -> [T] {
        ((try? decode([LossyElement<T>].self, forKey: key)) ?? []).compactMap(\.value)
    }
}

/// "1.3.1" vs "v1.4.0": numeric, missing parts count as 0.
public enum AppVersion {
    public static func parts(_ version: String) -> [Int] {
        version.trimmingCharacters(in: CharacterSet(charactersIn: "vV ")).split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 }
    }

    public static func isNewer(_ candidate: String, than current: String) -> Bool {
        let a = parts(candidate), b = parts(current)
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0, y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }
}

/// The app's language: follow macOS, or pick one.
public enum AppLanguage: String, Codable, CaseIterable, Identifiable {
    case auto, en, id
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .auto: return "Auto"
        case .en: return "English"
        case .id: return "Indonesia"
        }
    }
}

/// The shape of the notch: hanging from the top edge, or a floating island.
public enum NotchStyle: String, Codable, CaseIterable, Identifiable {
    case notch, island
    public var id: String { rawValue }
}

public enum IslandSize: String, Codable, CaseIterable, Identifiable {
    case small, medium, large
    public var id: String { rawValue }
}

/// Space between the island and the top of the screen (or the camera housing).
public enum IslandGap: String, Codable, CaseIterable, Identifiable {
    case tight, medium, roomy
    public var id: String { rawValue }
    public var points: Double {
        switch self {
        case .tight: return 3
        case .medium: return 6
        case .roomy: return 10
        }
    }
}

public enum ClosedFace: String, Codable, CaseIterable, Identifiable {
    case eyes, lion, off
    public var id: String { rawValue }
}
