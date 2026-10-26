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

public struct NotchSettings: Codable, Equatable {
    public var expandOnHover: Bool = true
    public var hoverDelay: Double = 0.12
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
    public var minimalClosed: Bool = false
    /// Lyrics view in the Music tab. Off until the user taps the lyrics button (LRCLIB is only asked then).
    public var showLyrics: Bool = false
    /// While lyrics are on, sing along under the closed notch too.
    public var closedLyrics: Bool = true
    /// Volume, Caps Lock and keyboard-language overlays.
    public var systemOverlays: Bool = true
    /// Wi-Fi and headphones peeks.
    public var connectionPeeks: Bool = true

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
    public var onboarded: Bool = false
    public var migratedIslandDesk: Bool = false

    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        tasks = (try? c.decode([TaskItem].self, forKey: .tasks)) ?? []
        notes = (try? c.decode([NoteItem].self, forKey: .notes)) ?? []
        clips = (try? c.decode([ClipItem].self, forKey: .clips)) ?? []
        shelf = (try? c.decode([ShelfRecord].self, forKey: .shelf)) ?? []
        water = (try? c.decode([WaterEntry].self, forKey: .water)) ?? []
        settings = (try? c.decode(NotchSettings.self, forKey: .settings)) ?? NotchSettings()
        onboarded = (try? c.decode(Bool.self, forKey: .onboarded)) ?? false
        migratedIslandDesk = (try? c.decode(Bool.self, forKey: .migratedIslandDesk)) ?? false
    }
}
