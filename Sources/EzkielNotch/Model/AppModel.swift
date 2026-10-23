import AppKit
import NotchCore
import ServiceManagement
import SwiftUI
import UniformTypeIdentifiers

/// Single source of truth for the notch. Every visual change goes through here,
/// wrapped in one spring, so the shape and the content always move together.
@MainActor
final class AppModel: ObservableObject {
    static let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0.0"

    // Presentation
    @Published private(set) var presentation: Presentation = .compact
    @Published private(set) var tab: NotchTab = .focus
    @Published private(set) var tabForward = true
    @Published var pinned = false {
        didSet { if !pinned && oldValue && !pointerInside { scheduleAutoCollapse() } }
    }
    @Published private(set) var hovering = false
    @Published var dropTargeted = false
    @Published private(set) var fileDragNear = false
    @Published private(set) var peek: Peek?
    @Published private(set) var toast: String?
    @Published var geometry = NotchGeometry()
    @Published private(set) var lionMood: LionMood = .awake

    // Data
    @Published var data: AppData {
        didSet { if data != oldValue { scheduleSave() } }
    }
    @Published private(set) var focus: FocusClock
    @Published var selectedNoteID: UUID?
    @Published var loginItemError: String?

    let music = MusicController()
    let battery = BatteryMonitor()

    /// Reasons the notch must stay open (menu open, file picker, ...).
    private var blockers = Set<String>()
    private var pointerInside = false
    private var suppressHoverUntilExit = false
    private var hoverWork: DispatchWorkItem?
    private var collapseWork: DispatchWorkItem?
    private var peekWork: DispatchWorkItem?
    private var toastWork: DispatchWorkItem?
    private var saveWork: DispatchWorkItem?
    private var happyWork: DispatchWorkItem?
    private var ticker: Timer?
    private var tickCount = 0
    private var lastInteraction = Date()
    private var lastWaterReminder: Date?
    private var lastBatteryWarning = 100
    private let store: DataStore

    /// Provided by the window controller: is a text field currently being edited?
    var isEditingText: () -> Bool = { false }
    /// Provided by the window controller: give keyboard focus back after collapsing.
    var onCollapse: () -> Void = {}

    init(store: DataStore = DataStore(url: DataStore.defaultURL())) {
        self.store = store
        var loaded = store.load()
        if !loaded.migratedIslandDesk {
            IslandDeskMigration.importLegacyData(into: &loaded)
            loaded.migratedIslandDesk = true
        }
        if loaded.notes.isEmpty && !loaded.onboarded {
            loaded.notes = [NoteItem(title: "Welcome to Ezkiel Notch",
                                     body: "Your notes stay on this Mac.\n\nHover the notch to open it, try the Focus timer, add a task, or drop files on the Shelf. ⌥⌘I opens the notch from anywhere.")]
        }
        loaded.onboarded = true
        loaded.water = WaterLog.pruned(loaded.water, keepingDays: 35)
        data = loaded
        focus = FocusClock(minutes: loaded.settings.focusMinutes)
        selectedNoteID = loaded.notes.first?.id
        store.save(loaded)

        music.source = loaded.settings.musicSource
        music.onTrackChange = { [weak self] np in self?.trackChanged(np) }
        battery.onChange = { [weak self] old, new in self?.batteryChanged(from: old, to: new) }
        battery.start()

        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        timer.tolerance = 0.2
        RunLoop.main.add(timer, forMode: .common)
        ticker = timer
        music.start()
    }

    var settings: NotchSettings {
        get { data.settings }
        set { data.settings = newValue }
    }

    // MARK: - Geometry

    var shape: (radius: CGFloat, ear: CGFloat) {
        switch presentation {
        case .compact: return NotchMetrics.compactShape
        case .peek: return NotchMetrics.peekShape
        case .expanded: return NotchMetrics.expandedShape
        }
    }

    /// True when the closed notch has something live to show in its wings.
    var hasLiveActivity: Bool {
        focus.isRunning || (music.nowPlaying?.isPlaying ?? false) || battery.state?.isCharging == true
    }

    var showsWings: Bool {
        if !settings.minimalClosed { return true }
        return hovering || hasLiveActivity
    }

    var bodySize: CGSize {
        let bar = NotchMetrics.barHeight(geometry)
        switch presentation {
        case .compact:
            var w = geometry.notchWidth + (showsWings ? NotchMetrics.wing * 2 : 0)
            var h = bar
            if hovering {
                w += 14
                h += 4
            }
            return CGSize(width: w, height: h)
        case .peek:
            let w = max(geometry.notchWidth + NotchMetrics.wing * 2, 380)
            return CGSize(width: w, height: bar + 56)
        case .expanded:
            return NotchMetrics.expandedSize(geometry)
        }
    }

    var panelSize: CGSize { NotchMetrics.panelSize(geometry) }

    /// Rect of the visible notch inside the panel (top-left origin).
    var shapeRect: CGRect {
        let size = bodySize
        let w = size.width + shape.ear * 2
        return CGRect(x: (panelSize.width - w) / 2, y: 0, width: w, height: size.height)
    }

    /// Rect that counts as "on the notch" for hover. A little forgiving around the closed notch.
    var hotRect: CGRect {
        let r = shapeRect
        switch presentation {
        case .compact: return r.insetBy(dx: -8, dy: 0).union(CGRect(x: r.minX - 8, y: 0, width: r.width + 16, height: r.height + 6))
        case .peek: return r.insetBy(dx: -4, dy: 0)
        case .expanded: return r.insetBy(dx: -10, dy: 0).union(CGRect(x: r.minX - 10, y: 0, width: r.width + 20, height: r.height + 12))
        }
    }

    /// While a file is being dragged, a larger zone around the notch attracts it (Nusa "near").
    var dragAttractRect: CGRect {
        let r = shapeRect
        return CGRect(x: r.minX - 70, y: 0, width: r.width + 140, height: r.height + 80)
    }

    // MARK: - Opening and closing

    func expand(_ newTab: NotchTab? = nil) {
        if let newTab { select(newTab) }
        cancelCollapse()
        noteInteraction()
        guard presentation != .expanded else { return }
        peekWork?.cancel()
        withAnimation(Theme.spring) {
            presentation = .expanded
            hovering = false
        }
        peek = nil
        if settings.haptics {
            NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
        }
        updateMusicPolling()
    }

    /// `explicit` = user asked (chevron, Esc, hotkey). Explicit collapses unpin and
    /// don't re-open from hover until the pointer has left once.
    func collapse(explicit: Bool = true) {
        hoverWork?.cancel()
        cancelCollapse()
        if explicit {
            pinned = false
            suppressHoverUntilExit = pointerInside
        }
        guard presentation != .compact else { return }
        withAnimation(Theme.spring) {
            presentation = .compact
            hovering = explicit ? false : pointerInside
            fileDragNear = false
        }
        peek = nil
        onCollapse()
        updateMusicPolling()
    }

    func toggle() {
        presentation == .expanded ? collapse() : expand()
    }

    func select(_ newTab: NotchTab) {
        guard newTab != tab else { return }
        tabForward = newTab.index > tab.index
        noteInteraction()
        withAnimation(Theme.snappy) { tab = newTab }
        updateMusicPolling()
    }

    func selectNeighbor(_ delta: Int) {
        let all = NotchTab.allCases
        let next = (tab.index + delta + all.count) % all.count
        select(all[next])
    }

    func block(_ reason: String) {
        blockers.insert(reason)
        cancelCollapse()
    }

    func unblock(_ reason: String) {
        blockers.remove(reason)
        if !pointerInside { scheduleAutoCollapse() }
    }

    // MARK: - Pointer (driven by NotchWindowController)

    func pointerMoved(inside: Bool) {
        guard inside != pointerInside else { return }
        pointerInside = inside
        hoverWork?.cancel()
        if inside {
            cancelCollapse()
            wakeLion()
            if presentation != .expanded {
                withAnimation(Theme.snappy) { hovering = true }
            }
            guard settings.expandOnHover, !suppressHoverUntilExit, presentation != .expanded else { return }
            let work = DispatchWorkItem { [weak self] in
                guard let self, self.pointerInside, !self.suppressHoverUntilExit else { return }
                self.expand()
            }
            hoverWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + max(0.05, settings.hoverDelay), execute: work)
        } else {
            suppressHoverUntilExit = false
            if hovering { withAnimation(Theme.snappy) { hovering = false } }
            scheduleAutoCollapse()
        }
    }

    /// A click outside the notch (in another app) closes it unless pinned.
    func clickedOutside() {
        guard presentation == .expanded, !pinned, blockers.isEmpty else { return }
        collapse(explicit: false)
    }

    func fileDrag(near: Bool) {
        guard near != fileDragNear else { return }
        withAnimation(Theme.spring) { fileDragNear = near }
        if near {
            expand(.shelf)
        } else if !pointerInside {
            scheduleAutoCollapse()
        }
    }

    private func scheduleAutoCollapse(after delay: TimeInterval = 0.5) {
        guard presentation == .expanded else { return }
        cancelCollapse()
        let work = DispatchWorkItem { [weak self] in self?.autoCollapseIfAllowed() }
        collapseWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
    }

    private func autoCollapseIfAllowed() {
        guard presentation == .expanded, !pointerInside, !pinned, !fileDragNear else { return }
        // Never close under a held mouse button (dragging a shelf file out, resizing, selecting text).
        if NSEvent.pressedMouseButtons != 0 || !blockers.isEmpty || isEditingText() {
            scheduleAutoCollapse(after: 0.6)
            return
        }
        collapse(explicit: false)
    }

    private func cancelCollapse() {
        collapseWork?.cancel()
        collapseWork = nil
    }

    // MARK: - Peeks and toasts

    func showPeek(_ p: Peek) {
        if settings.sounds, let sound = p.sound { NSSound(named: sound)?.play() }
        makeLionHappy()
        if presentation == .expanded {
            showToast("\(p.title) · \(p.subtitle)")
            return
        }
        peekWork?.cancel()
        withAnimation(Theme.bouncy) {
            peek = p
            presentation = .peek
            hovering = false
        }
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.presentation == .peek, self.peek?.id == p.id else { return }
            if self.pointerInside { return } // hovering keeps it; hover-open takes over
            withAnimation(Theme.spring) { self.presentation = .compact }
            self.peek = nil
        }
        peekWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.4, execute: work)
    }

    func openPeek() {
        expand(peek?.tab)
    }

    func showToast(_ text: String) {
        toastWork?.cancel()
        withAnimation(Theme.soft) { toast = text }
        let work = DispatchWorkItem { [weak self] in
            withAnimation(Theme.soft) { self?.toast = nil }
        }
        toastWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.5, execute: work)
    }

    // MARK: - Lion

    var displayedMood: LionMood {
        if let peek, presentation == .peek { return peek.mood }
        if lionMood == .happy { return .happy }
        if focus.isRunning && focus.mode == .focus { return .focused }
        return lionMood
    }

    func noteInteraction() {
        lastInteraction = Date()
        if lionMood == .sleeping { wakeLion() }
    }

    private func wakeLion() {
        lastInteraction = Date()
        if lionMood == .sleeping { makeLionHappy() }
    }

    private func makeLionHappy() {
        happyWork?.cancel()
        withAnimation(Theme.bouncy) { lionMood = .happy }
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.lionMood == .happy else { return }
            withAnimation(Theme.spring) { self.lionMood = .awake }
        }
        happyWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6, execute: work)
    }

    // MARK: - Ticker

    private func tick() {
        tickCount += 1
        let now = Date()
        if focus.isFinished(at: now) { completeFocus() }

        guard tickCount % 5 == 0 else { return }
        checkTasks(now)
        checkWater(now)
        if settings.mascotSleeps, lionMood == .awake, presentation == .compact,
           !hasLiveActivity, now.timeIntervalSince(lastInteraction) > 120 {
            withAnimation(Theme.spring) { lionMood = .sleeping }
        }
    }

    // MARK: - Focus

    func focusStartPause() {
        noteInteraction()
        withAnimation(Theme.snappy) { focus.toggle() }
    }

    func focusReset() {
        withAnimation(Theme.spring) { focus.reset() }
    }

    func setFocusMinutes(_ minutes: Int) {
        settings.focusMinutes = minutes
        withAnimation(Theme.spring) { focus.setMinutes(minutes, mode: .focus) }
    }

    func toggleBreak() {
        withAnimation(Theme.spring) {
            if focus.mode == .focus {
                focus.setMinutes(settings.breakMinutes, mode: .rest)
            } else {
                focus.setMinutes(settings.focusMinutes, mode: .focus)
            }
        }
    }

    private func completeFocus() {
        let finished = withAnimation(Theme.spring) {
            focus.complete(focusMinutes: settings.focusMinutes, breakMinutes: settings.breakMinutes)
        }
        if finished == .focus {
            showPeek(Peek(icon: "cup.and.saucer.fill", title: "Focus complete",
                          subtitle: "Take a \(settings.breakMinutes)-minute break", tint: Theme.teal,
                          tab: .focus, mood: .happy, sound: "Glass", showsLion: true))
        } else {
            showPeek(Peek(icon: "bolt.fill", title: "Break complete",
                          subtitle: "Ready to focus again", tint: Theme.gold,
                          tab: .focus, mood: .happy, sound: "Glass", showsLion: true))
        }
    }

    // MARK: - Tasks

    func addTask(_ title: String, due: Date?) {
        let clean = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { return }
        withAnimation(Theme.spring) {
            data.tasks.insert(TaskItem(title: clean, due: due), at: 0)
        }
    }

    func toggleTask(_ id: UUID) {
        guard let i = data.tasks.firstIndex(where: { $0.id == id }) else { return }
        withAnimation(Theme.spring) { data.tasks[i].done.toggle() }
        if data.tasks[i].done {
            if settings.sounds { NSSound(named: "Tink")?.play() }
            makeLionHappy()
        }
    }

    func deleteTask(_ id: UUID) {
        withAnimation(Theme.spring) { data.tasks.removeAll { $0.id == id } }
    }

    func clearDoneTasks() {
        withAnimation(Theme.spring) { data.tasks.removeAll(where: \.done) }
    }

    private func checkTasks(_ now: Date) {
        for i in data.tasks.indices {
            let t = data.tasks[i]
            guard !t.done, !t.reminded, let due = t.due, due <= now else { continue }
            data.tasks[i].reminded = true
            showPeek(Peek(icon: "checklist", title: "Task due", subtitle: t.title, tint: Theme.orange,
                          tab: .tasks, mood: .happy, sound: "Ping", showsLion: true))
            break
        }
    }

    // MARK: - Notes

    func addNote() {
        let note = NoteItem(title: "", body: "")
        withAnimation(Theme.spring) {
            data.notes.insert(note, at: 0)
            selectedNoteID = note.id
        }
    }

    func deleteNote(_ id: UUID) {
        withAnimation(Theme.spring) {
            data.notes.removeAll { $0.id == id }
            selectedNoteID = data.notes.first?.id
        }
    }

    func noteBinding(_ id: UUID, _ keyPath: WritableKeyPath<NoteItem, String>) -> Binding<String> {
        Binding(
            get: { [weak self] in self?.data.notes.first(where: { $0.id == id })?[keyPath: keyPath] ?? "" },
            set: { [weak self] value in
                guard let self, let i = self.data.notes.firstIndex(where: { $0.id == id }) else { return }
                self.data.notes[i][keyPath: keyPath] = value
                self.data.notes[i].updated = Date()
            }
        )
    }

    // MARK: - Capture

    func captureClipboard() {
        guard let text = NSPasteboard.general.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines),
              !text.isEmpty else {
            showToast("Clipboard has no text to capture")
            return
        }
        withAnimation(Theme.spring) {
            data.clips.removeAll { $0.text == text }
            data.clips.insert(ClipItem(text: text), at: 0)
            if data.clips.count > 30 { data.clips = Array(data.clips.prefix(30)) }
        }
        makeLionHappy()
    }

    func copyClip(_ clip: ClipItem) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(clip.text, forType: .string)
        showToast("Copied to clipboard")
    }

    func removeClip(_ id: UUID) {
        withAnimation(Theme.spring) { data.clips.removeAll { $0.id == id } }
    }

    // MARK: - Shelf

    func url(for record: ShelfRecord) -> URL {
        if let bookmark = record.bookmark {
            var stale = false
            if let resolved = try? URL(resolvingBookmarkData: bookmark, options: [.withoutUI, .withoutMounting],
                                       relativeTo: nil, bookmarkDataIsStale: &stale) {
                return resolved
            }
        }
        return URL(fileURLWithPath: record.path)
    }

    func addToShelf(_ urls: [URL]) {
        var added = 0
        withAnimation(.spring(response: 0.55, dampingFraction: 0.6)) {
            for url in urls where url.isFileURL {
                let standard = url.standardizedFileURL
                guard !data.shelf.contains(where: { self.url(for: $0).standardizedFileURL == standard }) else { continue }
                let bookmark = try? url.bookmarkData(options: .minimalBookmark, includingResourceValuesForKeys: nil, relativeTo: nil)
                data.shelf.insert(ShelfRecord(path: url.path, bookmark: bookmark), at: 0)
                added += 1
            }
            if data.shelf.count > 40 { data.shelf = Array(data.shelf.prefix(40)) }
        }
        if added > 0 {
            if settings.sounds { NSSound(named: "Pop")?.play() }
            makeLionHappy()
        }
    }

    func receiveDrop(_ providers: [NSItemProvider]) -> Bool {
        let files = providers.filter { $0.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) }
        guard !files.isEmpty else { return false }
        expand(.shelf)
        for provider in files {
            provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { [weak self] value, _ in
                var url: URL?
                if let data = value as? Data { url = URL(dataRepresentation: data, relativeTo: nil) }
                else if let u = value as? URL { url = u }
                guard let url else { return }
                DispatchQueue.main.async { self?.addToShelf([url]) }
            }
        }
        fileDragNear = false
        return true
    }

    func chooseShelfFiles() {
        block("openPanel")
        let picker = NSOpenPanel()
        picker.title = "Add files to Ezkiel Notch Shelf"
        picker.prompt = "Add to Shelf"
        picker.allowsMultipleSelection = true
        picker.canChooseDirectories = true
        picker.canChooseFiles = true
        NSApp.activate(ignoringOtherApps: true)
        picker.begin { [weak self] response in
            DispatchQueue.main.async {
                guard let self else { return }
                if response == .OK { self.addToShelf(picker.urls) }
                self.unblock("openPanel")
            }
        }
    }

    func removeFromShelf(_ id: UUID) {
        withAnimation(Theme.spring) { data.shelf.removeAll { $0.id == id } }
    }

    func clearShelf() {
        withAnimation(Theme.spring) { data.shelf.removeAll() }
    }

    // MARK: - Water

    var waterToday: Int { WaterLog.total(data.water, on: Date()) }
    var waterProgress: Double { min(1, Double(waterToday) / Double(max(1, settings.waterGoal))) }

    func addWater(_ ml: Int) {
        let before = waterToday
        withAnimation(.spring(response: 0.6, dampingFraction: 0.62)) {
            data.water.append(WaterEntry(ml: ml))
        }
        lastWaterReminder = nil
        if settings.sounds { NSSound(named: "Bottle")?.play() }
        makeLionHappy()
        if before < settings.waterGoal && waterToday >= settings.waterGoal {
            showToast("Goal reached! \(waterToday) ml today")
        } else {
            showToast("Logged \(ml) ml · \(waterToday) ml today")
        }
    }

    func undoWater() {
        guard let i = data.water.lastIndex(where: { Calendar.current.isDateInToday($0.date) }) else { return }
        withAnimation(Theme.spring) { _ = data.water.remove(at: i) }
    }

    private func checkWater(_ now: Date) {
        guard settings.waterReminders else { return }
        let lastDrink = data.water.map(\.date).max()
        guard WaterLog.reminderDue(lastDrink: lastDrink ?? now.addingTimeInterval(-86_400),
                                   lastReminder: lastWaterReminder ?? appLaunch,
                                   intervalMinutes: settings.waterIntervalMinutes,
                                   startHour: settings.activeHourStart, endHour: settings.activeHourEnd, now: now)
        else { return }
        lastWaterReminder = now
        showPeek(Peek(icon: "drop.fill", title: "Time for a sip",
                      subtitle: "\(waterToday) of \(settings.waterGoal) ml today", tint: Color(hex: 0x7FD3FF),
                      tab: .water, mood: .happy, sound: "Purr", showsLion: true))
    }

    private let appLaunch = Date()

    // MARK: - Music

    private func trackChanged(_ np: NowPlaying?) {
        guard let np, np.isPlaying, presentation == .compact else { return }
        showPeek(Peek(icon: "music.note", title: np.title, subtitle: np.artist,
                      tint: np.player == .spotify ? Color(hex: 0x1ED760) : Theme.berry,
                      tab: .music, mood: .happy))
    }

    func updateMusicPolling() {
        music.fastPolling = presentation == .expanded && tab == .music
    }

    func setMusicSource(_ source: MusicSource) {
        settings.musicSource = source
        music.source = source
        music.refresh()
    }

    // MARK: - Battery

    private func batteryChanged(from old: BatteryState?, to new: BatteryState?) {
        guard let new else { return }
        let fraction = Double(new.percent) / 100
        if let old, old.onAC != new.onAC {
            if new.onAC {
                showPeek(Peek(icon: "bolt.fill", title: new.isCharging ? "Charging" : "Connected to power",
                              subtitle: "\(new.percent)% battery", tint: Theme.teal, mood: .happy,
                              progress: fraction))
            } else {
                showPeek(Peek(icon: "battery.75", title: "On battery", subtitle: "\(new.percent)% remaining",
                              tint: Theme.gold, mood: .awake, progress: fraction))
            }
            lastBatteryWarning = 100
        } else if !new.onAC, new.percent <= 20, new.percent < lastBatteryWarning,
                  [20, 10, 5].contains(new.percent) {
            lastBatteryWarning = new.percent
            showPeek(Peek(icon: "battery.25", title: "Battery low", subtitle: "\(new.percent)% left · plug in soon",
                          tint: Theme.berry, mood: .awake, sound: "Funk", progress: fraction))
        }
    }

    // MARK: - Settings helpers

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            settings.launchAtLogin = enabled
            loginItemError = nil
        } catch {
            settings.launchAtLogin = SMAppService.mainApp.status == .enabled
            loginItemError = "Move Ezkiel Notch to Applications first"
            showToast("Login item: move the app to Applications first")
        }
    }

    func refreshLoginItemState() {
        settings.launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    func open(_ target: QuickLaunch) {
        noteInteraction()
        switch target {
        case .store: NSWorkspace.shared.open(URL(string: "https://ezkielyna.store")!)
        case .claude:
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString("Help me plan my day. Ask what matters most, then suggest a realistic schedule with breaks.", forType: .string)
            NSWorkspace.shared.open(URL(string: "https://claude.ai/new")!)
            showToast("Starter prompt copied · paste it in Claude")
        case .finder: NSWorkspace.shared.open(URL(fileURLWithPath: NSHomeDirectory()))
        case .terminal: NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/Utilities/Terminal.app"))
        case .downloads:
            if let url = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first {
                NSWorkspace.shared.open(url)
            }
        case .systemSettings: NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:")!)
        }
    }

    // MARK: - Persistence

    private func scheduleSave() {
        saveWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            self.store.save(self.data)
        }
        saveWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6, execute: work)
    }

    func saveNow() {
        saveWork?.cancel()
        store.save(data, synchronously: true)
    }
}

enum QuickLaunch: String, CaseIterable, Identifiable {
    case store, claude, finder, terminal, downloads, systemSettings
    var id: String { rawValue }
    var title: String {
        switch self {
        case .store: return "Ezkielyna"
        case .claude: return "Claude"
        case .finder: return "Finder"
        case .terminal: return "Terminal"
        case .downloads: return "Downloads"
        case .systemSettings: return "Settings"
        }
    }
    var icon: String {
        switch self {
        case .store: return "storefront.fill"
        case .claude: return "sparkles"
        case .finder: return "folder.fill"
        case .terminal: return "terminal.fill"
        case .downloads: return "arrow.down.circle.fill"
        case .systemSettings: return "gearshape.fill"
        }
    }
    var tint: Color {
        switch self {
        case .store: return Theme.gold
        case .claude: return Theme.orange
        case .finder: return Theme.sky
        case .terminal: return Theme.teal
        case .downloads: return Color(hex: 0x7FD3FF)
        case .systemSettings: return Theme.mist
        }
    }
}
