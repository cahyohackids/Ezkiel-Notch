import AppKit
import Combine
import NotchCore
import ServiceManagement
import SwiftUI
import UniformTypeIdentifiers

/// State that changes on every hover and every wobble frame. Kept apart from `AppModel`
/// so only the notch shell and the header observe it; the panes don't re-render.
@MainActor
final class NotchChrome: ObservableObject {
    /// How far the notch's bottom edge bows (+ down, − up); springs back to 0.
    @Published var ripple: CGFloat = 0
    @Published var hovering = false
    @Published var toast: Toast?
}

/// One Stash card with everything the view needs, resolved once (no file I/O while rendering).
struct StashItem: Identifiable, Equatable {
    let id: UUID
    let url: URL
    let name: String
    let detail: String
    let exists: Bool
    let icon: NSImage

    static func == (lhs: StashItem, rhs: StashItem) -> Bool {
        lhs.id == rhs.id && lhs.url == rhs.url && lhs.detail == rhs.detail && lhs.exists == rhs.exists
    }
}

enum ScratchMode: String, CaseIterable, Identifiable {
    case notes, catch_
    var id: String { rawValue }
    var title: String { self == .notes ? "Notes" : "Catch" }
}

enum SettingsSection: String, CaseIterable, Identifiable {
    case general, whiskers, roar, features, about
    var id: String { rawValue }
    var title: String {
        switch self {
        case .general: return "General"
        case .whiskers: return "Whiskers"
        case .roar: return "Roar"
        case .features: return "Features"
        case .about: return "About"
        }
    }
}

/// Single source of truth for the notch. Every visual change goes through here,
/// wrapped in one spring, so the shape and the content always move together.
@MainActor
final class AppModel: ObservableObject {
    static let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.6.0"

    // Presentation
    @Published private(set) var presentation: Presentation = .compact
    @Published private(set) var tab: NotchTab = .den
    @Published private(set) var tabForward = true
    @Published var pinned = false {
        didSet {
            if !pinned, oldValue, !pointerInside, presentation == .expanded, !collapsing { scheduleAutoCollapse() }
        }
    }
    @Published var dropTargeted = false
    @Published private(set) var fileDragNear = false
    @Published private(set) var peek: Peek?
    @Published var geometry = NotchGeometry()
    /// An app is fullscreen on the notch's display (set by the window controller).
    @Published var fullscreenActive = false
    @Published private(set) var lionMood: LionMood = .awake
    @Published var settingsSection: SettingsSection = .general
    @Published var scratchMode: ScratchMode = .notes
    @Published var roarMode: RoarMode = .roar
    /// Scout: agents that are working or waiting for you, by session.
    @Published var agents: [String: ScoutEvent] = [:]
    @Published var agentLinks: [AgentLinks.Agent: ScoutSetup.LinkState] = [:]

    // Data
    @Published var data: AppData {
        didSet {
            scheduleSave()
            if data.settings != oldValue.settings { applySettings(old: oldValue.settings) }
        }
    }
    @Published private(set) var focus: FocusClock {
        didSet { if focus != oldValue { data.focusState = focus.snapshot } }
    }
    @Published var selectedNoteID: UUID?
    /// A note that was just created: the editor focuses its title once.
    @Published var noteToFocus: UUID?
    @Published var loginItemError: String?
    @Published private(set) var stashItems: [StashItem] = []

    let chrome = NotchChrome()
    let music = MusicController()
    let battery = BatteryMonitor()
    let lyrics = LyricsService()
    let calendar = CalendarService()
    let badges = BadgeWatcher()
    let updates = UpdateChecker()
    let updater = AppUpdater()
    let prowl = PrivacyWatch()
    let outputs = AudioOutputs()
    let weather = WeatherService()
    let mirror = CameraMirror()
    /// Mirror covers the page of the open notch.
    @Published var mirrorOn = false
    let scout = ScoutService()
    /// Roar: when each app last peeked, so a busy chat doesn't peek on every message.
    var lastRoarPeek: [String: Date] = [:]
    /// Screens off or the session switched away: background work pauses.
    private var screensAsleep = false
    private let system = SystemMonitor()
    private let pounce = PounceWatcher()
    private var subscriptions = Set<AnyCancellable>()

    /// Reasons the notch must stay open (menu open, file picker, ...).
    private var blockers = Set<String>()
    private var pointerInside = false
    private var pointerLeftAt = Date.distantPast
    private var suppressHoverUntilExit = false
    private var collapsing = false
    private var hoverWork: DispatchWorkItem?
    private var collapseWork: DispatchWorkItem?
    private var peekWork: DispatchWorkItem?
    private var toastWork: DispatchWorkItem?
    private var saveWork: DispatchWorkItem?
    private var happyWork: DispatchWorkItem?
    private var rippleWork: DispatchWorkItem?
    /// A volume/keyboard overlay that arrived while a card was showing; shown after it.
    private var queuedHUD: (peek: Peek, at: Date)?
    private var ticker: Timer?
    private var tickCount = 0
    private var lastInteraction = Date()
    private var lastWaterReminder: Date?
    private var notifiedMeetings = Set<String>()
    private let store: DataStore

    /// Self-test: ignore the real pointer so a cursor resting near the notch can't change the outcome.
    var ignoresPointer = false
    /// Provided by the window controller: is a text field currently being edited?
    var isEditingText: () -> Bool = { false }
    /// Provided by the window controller: give keyboard focus back after collapsing.
    var onCollapse: () -> Void = {}
    /// Provided by the app delegate: register the chosen global shortcut.
    var onHotkeyChange: (HotkeyChoice) -> Void = { _ in }

    init(store: DataStore = DataStore(url: DataStore.defaultURL())) {
        self.store = store
        var loaded = store.load()
        if loaded.notes.isEmpty && !loaded.onboarded {
            loaded.notes = [NoteItem(title: "Welcome to Ezkiel Notch",
                                     body: "Your notes stay on this Mac.\n\nHover the notch to open it. Den keeps your focus, Hunt your tasks, Stash your files. ⌃⌥N opens the notch from anywhere.")]
        }
        loaded.onboarded = true
        loaded.water = WaterLog.pruned(loaded.water, keepingDays: 35)
        loaded.focusLog = FocusStats.pruned(loaded.focusLog)
        var clock = loaded.focusState.map(FocusClock.init(snapshot:)) ?? FocusClock(minutes: loaded.settings.focusMinutes)
        // A session that ended while the app was closed counts once it's noticed (tick below).
        if case .ready = clock.phase, clock.mode == .focus { clock.setMinutes(loaded.settings.focusMinutes, mode: .focus) }
        data = loaded
        focus = clock
        selectedNoteID = loaded.notes.first?.id
        store.save(loaded)

        music.source = loaded.settings.musicSource
        music.onTrackChange = { [weak self] np in self?.trackChanged(np) }
        battery.onChange = { [weak self] old, new in self?.batteryChanged(from: old, to: new) }
        battery.start()
        system.onEvent = { [weak self] event in self?.systemEvent(event) }
        system.start()
        pounce.onScreenshot = { [weak self] url in self?.pounced(url, screenshot: true) }
        pounce.onDownload = { [weak self] url in self?.pounced(url, screenshot: false) }
        badges.onIncrease = { [weak self] badge, before in self?.badgeIncreased(badge, before: before) }
        scout.onEvent = { [weak self] event in self?.scoutReceived(event) }
        applySettings(old: nil)
        rebuildStash()

        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in self?.reportRecovery() }
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) { [weak self] in self?.checkForUpdates() }
        updater.beforeQuit = { [weak self] in self?.saveNow() }
        if loaded.settings.scout, !ProcessInfo.processInfo.arguments.contains("--self-test") { repairAgentLinks() }
        let ws = NSWorkspace.shared.notificationCenter
        for (name, asleep) in [(NSWorkspace.screensDidSleepNotification, true), (NSWorkspace.sessionDidResignActiveNotification, true),
                               (NSWorkspace.screensDidWakeNotification, false), (NSWorkspace.sessionDidBecomeActiveNotification, false)] {
            ws.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.setScreensAsleep(asleep) }
            }
        }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] note in
            let id = (note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication)?.bundleIdentifier
            MainActor.assumeIsolated { self?.agentAppActivated(id) }
        }

        // Lyrics follow the song.
        music.$nowPlaying
            .map { $0?.trackKey }
            .removeDuplicates()
            .sink { [weak self] _ in
                DispatchQueue.main.async { self?.refreshLyrics() }
            }
            .store(in: &subscriptions)
        // Roar's counts, Prowl's dots and the weather show in the closed notch and the header.
        for publisher in [badges.objectWillChange.eraseToAnyPublisher(), prowl.objectWillChange.eraseToAnyPublisher(),
                          weather.objectWillChange.eraseToAnyPublisher(), updates.objectWillChange.eraseToAnyPublisher()] {
            publisher
                .sink { [weak self] _ in self?.objectWillChange.send() }
                .store(in: &subscriptions)
        }
        // The open notch's height depends on whether Purr shows lyrics.
        lyrics.$state
            .map { if case .synced = $0 { return true } else { return false } }
            .removeDuplicates()
            .sink { [weak self] _ in self?.objectWillChange.send() }
            .store(in: &subscriptions)

        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        timer.tolerance = 0.25
        RunLoop.main.add(timer, forMode: .common)
        ticker = timer
        music.start()
    }

    /// The data file in use (the default one, or `--data`).
    var dataFileURL: URL { store.url }

    var settings: NotchSettings {
        get { data.settings }
        set { data.settings = newValue }
    }

    /// Watchers and services follow their switches.
    private func applySettings(old: NotchSettings?) {
        let s = data.settings
        system.setCapsLockWatching(s.systemOverlays && !screensAsleep)
        music.allowed = s.musicAllowed
        pounce.setScreenshots(s.pounceScreenshots)
        pounce.setDownloads(s.pounceDownloads)
        calendar.setEnabled(s.rally)
        badges.readDock = s.roarFromDock
        badges.setEnabled(s.roar)
        if s.roarFromDock && old?.roarFromDock == false && !AXIsProcessTrusted() { DockBadges.requestAccess() }
        prowl.setEnabled(s.prowl)
        Lang.choice = s.language
        weather.configure(latitude: s.weatherCity.isEmpty ? nil : s.weatherLatitude,
                          longitude: s.weatherCity.isEmpty ? nil : s.weatherLongitude)
        scout.setEnabled(s.scout)
        if !s.scout { agents = [:] }
        if old?.hotkey != s.hotkey { onHotkeyChange(s.hotkey) }
        if old?.musicSource != s.musicSource {
            music.source = s.musicSource
            if old != nil { music.refresh() }
        }
    }

    // MARK: - Geometry

    // MARK: Style (Notch or Island)

    var isIsland: Bool { settings.style == .island }

    /// Height of the top row: the menu-bar strip for a notch, the user's size for an island.
    var rowHeight: CGFloat { isIsland ? NotchMetrics.islandHeight(settings.islandSize) : NotchMetrics.barHeight(geometry) }

    /// The middle of the top row: the camera housing for a notch, a small centre for an island.
    var centerWidth: CGFloat { isIsland ? NotchMetrics.islandCenter(settings.islandSize) : geometry.notchWidth }

    /// An island floats: just under the top edge, or under the camera housing on a notched display.
    var topInset: CGFloat {
        guard isIsland else { return 0 }
        return (geometry.hasNotch ? geometry.barHeight : 0) + CGFloat(settings.islandGap.points)
    }

    var expandedWidth: CGFloat { isIsland ? NotchMetrics.islandExpandedWidth(settings.islandSize) : NotchMetrics.expandedWidth }

    var expandedSize: CGSize {
        CGSize(width: expandedWidth, height: rowHeight + NotchMetrics.tabStripTop + NotchMetrics.tabStripHeight
                   + NotchMetrics.pageTop + paneHeight + NotchMetrics.pageBottom)
    }

    var shape: (radius: CGFloat, ear: CGFloat, top: CGFloat) {
        if isIsland {
            switch presentation {
            case .compact:
                let round = min(bodySize.height / 2, rowHeight / 2 + 4)
                return (round, 0, round)
            case .peek: return (20, 0, 20)
            case .expanded: return (26, 0, 26)
            }
        }
        switch presentation {
        case .compact: return (NotchMetrics.compactShape.radius, NotchMetrics.compactShape.ear, 0)
        case .peek: return (NotchMetrics.peekShape.radius, NotchMetrics.peekShape.ear, 0)
        case .expanded: return (NotchMetrics.expandedShape.radius, NotchMetrics.expandedShape.ear, 0)
        }
    }

    /// The meeting Rally counts down to in the closed notch (within 15 minutes).
    var meetingSoon: CalendarService.Meeting? {
        guard settings.rally, settings.rallyCountdown else { return nil }
        let now = Date()
        return calendar.upcoming.first { $0.start > now && $0.start.timeIntervalSince(now) <= 900 }
    }

    /// True when the closed notch has something live to show in its wings.
    var hasLiveActivity: Bool {
        focus.isRunning || (music.nowPlaying?.isPlaying ?? false) || battery.state?.isCharging == true || meetingSoon != nil
            || !agents.isEmpty || prowl.micInUse || prowl.cameraInUse
    }

    /// Fullscreen calm: the setting is on and something is fullscreen on our display.
    var inFullscreenCalm: Bool { settings.hideInFullscreen && fullscreenActive }

    /// On a display without a notch, a fullscreen app gets the whole top edge: the closed pill steps away.
    /// The pill has no camera housing to hide behind on a display without a notch, nor as a floating island.
    var hiddenForFullscreen: Bool { inFullscreenCalm && (!geometry.hasNotch || isIsland) && presentation == .compact }

    /// Den is running a focus block and the user wants quiet.
    var focusQuiet: Bool { settings.quietDuringFocus && focus.isRunning && focus.mode == .focus }

    var showsWings: Bool {
        if inFullscreenCalm { return chrome.hovering }
        if !settings.minimalClosed { return true }
        // Unread messages are what Roar is for: the count stays visible in the calm closed notch.
        return chrome.hovering || hasLiveActivity || showsClosedLyrics || (settings.roar && unreadTotal > 0)
    }

    /// The closed notch grows a line underneath and sings along (Chorus).
    var showsClosedLyrics: Bool {
        settings.showLyrics && settings.closedLyrics && (music.nowPlaying?.isPlaying ?? false) && lyrics.syncedLines != nil
    }

    /// Purr is taller while Chorus has lyrics to show.
    var purrShowsLyrics: Bool { settings.showLyrics && music.nowPlaying != nil }

    var paneHeight: CGFloat { NotchMetrics.paneHeight(tab, lyrics: tab == .purr && purrShowsLyrics) }

    var bodySize: CGSize {
        let bar = rowHeight
        switch presentation {
        case .compact:
            var w = centerWidth + (showsWings ? NotchMetrics.wing * 2 : 0)
            var h = bar
            if showsClosedLyrics {
                w = max(w, 340)
                h += NotchMetrics.lyricBand
            }
            if chrome.hovering {
                w += 12
                h += 3
            }
            return CGSize(width: w, height: h)
        case .peek:
            return peekSize(peek)
        case .expanded:
            return expandedSize
        }
    }

    /// Ezkiel's eyes follow what's going on: an agent waiting or working, a focus block, a nap, a happy moment.
    var eyesState: EzkielEyes.State {
        if let agent = liveAgents.first { return agent.kind == .needsYou ? .waiting : .working }
        switch displayedMood {
        case .sleeping: return .sleeping
        case .happy: return .happy
        case .focused: return .focused
        case .awake: return .awake
        }
    }

    /// The closed notch has a free middle (no camera there): an island, or a display without a notch.
    var centerIsFree: Bool { isIsland || !geometry.hasNotch }

    /// A one-row peek is as wide as what it says: icon, text, then the value or ring, with even margins.
    static func inlineCardWidth(_ p: Peek) -> CGFloat {
        func width(_ text: String, _ size: CGFloat, _ weight: NSFont.Weight) -> CGFloat {
            (L(text) as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: size, weight: weight)]).width
        }
        let text = max(width(p.title, Theme.Size.body, .semibold), p.subtitle.isEmpty ? 0 : width(p.subtitle, Theme.Size.secondary, .regular))
        let trailing: CGFloat
        if p.progress != nil { trailing = 32 }
        else if let value = p.value { trailing = width(value, 15, .semibold) + 4 }
        else if p.artwork { trailing = 26 }
        else if p.showsLion { trailing = 18 }
        else { trailing = 0 }
        // Padding 14 + icon 28 + gap 10 + text + (gap 10 + spacer 6 + gap 10 + trailing) + padding 14, plus 4 to breathe.
        let content = 28 + 28 + 10 + ceil(text) + (trailing > 0 ? 26 + trailing : 6) + 4
        return min(380, max(180, content))
    }

    /// Peeks split around the camera only when there is one; an island or a plain display gets one tidy row.
    var peeksInline: Bool { isIsland || !geometry.hasNotch }

    func peekSize(_ p: Peek?) -> CGSize {
        let bar = rowHeight
        let middle = centerWidth
        if peeksInline, let p {
            if p.isHUD { return CGSize(width: 280, height: bar + 8) }
            if p.isRich { return CGSize(width: 372, height: 64) }
            return CGSize(width: Self.inlineCardWidth(p), height: max(bar + 14, 46))
        }
        guard let p else { return CGSize(width: middle, height: bar) }
        if p.isHUD {
            return CGSize(width: max(middle + 104, 260), height: bar + (p.level != nil ? 18 : 24))
        }
        if p.isRich {
            return CGSize(width: max(middle + 150, 340), height: bar + 56)
        }
        return CGSize(width: max(middle + 128, 300), height: bar + 26)
    }

    var panelSize: CGSize { NotchMetrics.panelSize(geometry) }

    /// Rect of the visible notch inside the panel (top-left origin).
    var shapeRect: CGRect {
        let size = bodySize
        let w = size.width + shape.ear * 2
        return CGRect(x: (panelSize.width - w) / 2, y: topInset, width: w, height: size.height)
    }

    /// Rect that counts as "on the notch" for hover. A little forgiving around the closed notch.
    var hotRect: CGRect {
        let r = shapeRect
        switch presentation {
        // From the very top edge down: pushing the pointer to the top always reaches it, even an island.
        case .compact: return CGRect(x: r.minX - 8, y: 0, width: r.width + 16, height: r.maxY + 6)
        case .peek: return CGRect(x: r.minX - 4, y: 0, width: r.width + 8, height: r.maxY)
        case .expanded: return CGRect(x: r.minX - 10, y: 0, width: r.width + 20, height: r.maxY + 12)
        }
    }

    /// While a file is being dragged, a larger zone around the notch attracts it.
    var dragAttractRect: CGRect {
        let r = shapeRect
        return CGRect(x: r.minX - 70, y: 0, width: r.width + 140, height: r.maxY + 80)
    }

    // MARK: - Opening and closing

    func expand(_ newTab: NotchTab? = nil) {
        if let newTab { select(newTab) }
        if tab == .purr { allowMusic() }
        cancelCollapse()
        noteInteraction()
        guard presentation != .expanded else { return }
        peekWork?.cancel()
        if tab == .stash { rebuildStash() }
        withAnimation(Theme.bouncy) {
            presentation = .expanded
            chrome.hovering = false
        }
        splash(10)
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
        collapsing = true
        if explicit {
            pinned = false
            suppressHoverUntilExit = pointerInside
        }
        collapsing = false
        cancelCollapse()
        guard presentation != .compact else { return }
        withAnimation(Theme.smooth) {
            presentation = .compact
            // Keep the hover look while the pointer is still on the notch.
            chrome.hovering = pointerInside
            fileDragNear = false
        }
        splash(-4)
        peek = nil
        clearToast()
        if mirrorOn { setMirror(false) }
        onCollapse()
        updateMusicPolling()
    }

    func toggle() {
        presentation == .expanded ? collapse() : expand()
    }

    func select(_ newTab: NotchTab) {
        if newTab == .purr { allowMusic() }
        guard newTab != tab else { return }
        tabForward = newTab.index > tab.index
        noteInteraction()
        // The height changes with the tab, so one spring drives both.
        withAnimation(Theme.smooth) { tab = newTab }
        if newTab == .stash { rebuildStash() }
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

    func pointerMoved(inside rawInside: Bool) {
        let inside = rawInside && !hiddenForFullscreen
        guard !ignoresPointer, inside != pointerInside else { return }
        pointerInside = inside
        hoverWork?.cancel()
        if inside {
            cancelCollapse()
            wakeLion()
            if presentation == .compact {
                withAnimation(Theme.snappy) { chrome.hovering = true }
                splash(3)
            }
            guard settings.expandOnHover, !suppressHoverUntilExit, presentation != .expanded else { return }
            let work = DispatchWorkItem { [weak self] in
                guard let self, self.pointerInside, !self.suppressHoverUntilExit else { return }
                self.expand(self.presentation == .peek ? self.peek?.tab : nil)
            }
            hoverWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + max(0.05, settings.hoverDelay), execute: work)
        } else {
            pointerLeftAt = Date()
            suppressHoverUntilExit = false
            if chrome.hovering { withAnimation(Theme.snappy) { chrome.hovering = false } }
            scheduleAutoCollapse()
            // A peek that waited for the pointer to leave goes now.
            if presentation == .peek { schedulePeekDismiss(after: 1.2) }
        }
    }

    /// A click outside the notch (in another app) closes it unless pinned.
    func clickedOutside() {
        guard !ignoresPointer, presentation == .expanded, !pinned, blockers.isEmpty else { return }
        collapse(explicit: false)
    }

    func fileDrag(near: Bool) {
        // Self-test: a real drag elsewhere on screen mustn't open or close anything.
        guard !ignoresPointer, near != fileDragNear else { return }
        withAnimation(Theme.smooth) { fileDragNear = near }
        if near {
            expand(.stash)
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
        // Never close under a held mouse button (dragging a Stash file out, selecting text).
        // A focused text field holds the notch open, but not forever once the pointer has left.
        let editingHold = isEditingText() && Date().timeIntervalSince(pointerLeftAt) < 20
        if NSEvent.pressedMouseButtons != 0 || !blockers.isEmpty || editingHold {
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

    func showPeek(_ incoming: Peek) {
        var p = incoming
        // Over a fullscreen video, game or presentation only what can't wait gets through.
        if inFullscreenCalm, !p.urgent, !p.isHUD { return }
        announce(p)
        if settings.sounds, let sound = p.sound { NSSound(named: sound)?.play() }
        if !p.isHUD { makeLionHappy() }
        if presentation == .expanded {
            if !p.isHUD { showToast(p.subtitle.isEmpty ? L(p.title) : "\(L(p.title)) · \(L(p.subtitle))") }
            return
        }
        // An overlay never pushes a card away; it waits until the card is gone.
        if p.isHUD, presentation == .peek, let current = peek, !current.isHUD {
            queuedHUD = (p, Date())
            return
        }
        peekWork?.cancel()
        if presentation == .peek, let current = peek, p.hudKey != nil, current.hudKey == p.hudKey {
            // Same overlay again (volume key held): update in place, no re-entrance.
            p.id = current.id
            withAnimation(Theme.snappy) { peek = p }
        } else {
            withAnimation(Theme.bouncy) {
                peek = p
                presentation = .peek
                chrome.hovering = pointerInside
            }
            splash(p.isHUD ? 4 : 7)
        }
        schedulePeekDismiss(after: p.isHUD ? 1.6 : (p.isRich ? 6 : 3.4))
    }

    private func schedulePeekDismiss(after delay: TimeInterval) {
        guard let id = peek?.id else { return }
        peekWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.presentation == .peek, self.peek?.id == id else { return }
            // Hovering keeps it; leaving the notch reschedules (pointerMoved).
            if self.pointerInside { return }
            self.dismissPeek()
        }
        peekWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
    }

    /// VoiceOver reads peeks out, since they appear without focus.
    private func announce(_ p: Peek) {
        guard !p.isHUD, NSWorkspace.shared.isVoiceOverEnabled else { return }
        let text = p.subtitle.isEmpty ? L(p.title) : "\(L(p.title)). \(L(p.subtitle))"
        NSAccessibility.post(element: NSApp as Any, notification: .announcementRequested,
                             userInfo: [.announcement: text, .priority: NSAccessibilityPriorityLevel.high.rawValue])
    }

    func dismissPeek() {
        guard presentation == .peek else { return }
        withAnimation(Theme.smooth) { presentation = .compact }
        splash(-3)
        peek = nil
        if let queued = queuedHUD, Date().timeIntervalSince(queued.at) < 1.5 {
            queuedHUD = nil
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in self?.showPeek(queued.peek) }
        }
        queuedHUD = nil
    }

    func openPeek() {
        if let app = peek?.app {
            openApp(app)
            dismissPeek()
            return
        }
        if peek?.tab == .roar { roarMode = .scout }
        expand(peek?.tab)
    }

    func showToast(_ text: String, action: String? = nil, perform: (() -> Void)? = nil) {
        toastWork?.cancel()
        withAnimation(Theme.snappy) { chrome.toast = Toast(text: text, actionTitle: action, action: perform) }
        let work = DispatchWorkItem { [weak self] in self?.clearToast() }
        toastWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + (action == nil ? 2.5 : 5), execute: work)
    }

    func clearToast() {
        toastWork?.cancel()
        guard chrome.toast != nil else { return }
        withAnimation(Theme.soft) { chrome.toast = nil }
    }

    /// Water-drop settle of the bottom edge: bow out fast, then spring back with one bounce.
    func splash(_ amount: CGFloat) {
        guard !Theme.reduceMotion else { return }
        rippleWork?.cancel()
        withAnimation(.easeOut(duration: 0.12)) { chrome.ripple = amount }
        let work = DispatchWorkItem { [weak self] in
            withAnimation(Theme.wobble) { self?.chrome.ripple = 0 }
        }
        rippleWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.12, execute: work)
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
            withAnimation(Theme.smooth) { self.lionMood = .awake }
        }
        happyWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6, execute: work)
    }

    // MARK: - Ticker

    private func setScreensAsleep(_ asleep: Bool) {
        guard asleep != screensAsleep else { return }
        screensAsleep = asleep
        music.paused = asleep
        prowl.isPaused = asleep
        system.setCapsLockWatching(settings.systemOverlays && !asleep)
    }

    private func tick() {
        tickCount += 1
        let now = Date()
        if focus.isFinished(at: now) { completeFocus() }
        // Nobody is looking: only the timer above (and due reminders, once back) matter.
        if screensAsleep { return }

        guard tickCount % 5 == 0 else { return }
        checkTasks(now)
        checkWater(now)
        checkMeetings(now)
        expireAgents(now)
        if tickCount % 60 == 0 { calendar.refresh(now: now) }
        if tickCount % 3600 == 30 { checkForUpdates() }
        if tickCount % 600 == 0 { weather.refresh() }
        if settings.mascotSleeps, lionMood == .awake, presentation == .compact,
           !hasLiveActivity, now.timeIntervalSince(lastInteraction) > 120 {
            withAnimation(Theme.smooth) { lionMood = .sleeping }
        }
    }

    // MARK: - Den (focus)

    var focusToday: Int { FocusStats.minutes(data.focusLog, on: Date()) }
    var focusStreak: Int { FocusStats.streak(data.focusLog) }

    func focusStartPause() {
        noteInteraction()
        withAnimation(Theme.snappy) { focus.toggle() }
    }

    func focusReset() {
        withAnimation(Theme.smooth) { focus.reset() }
    }

    /// While a session runs, a new length applies to the next one (it never resets the current).
    func setFocusMinutes(_ minutes: Int) {
        settings.focusMinutes = minutes
        if focus.isRunning || focus.isPaused {
            showToast(L("Next focus: %d min", minutes))
        } else {
            withAnimation(Theme.smooth) { focus.setMinutes(minutes, mode: .focus) }
        }
    }

    func toggleBreak() {
        guard !focus.isRunning, !focus.isPaused else { return }
        withAnimation(Theme.smooth) {
            if focus.mode == .focus {
                focus.setMinutes(settings.breakMinutes, mode: .rest)
            } else {
                focus.setMinutes(settings.focusMinutes, mode: .focus)
            }
        }
    }

    private func completeFocus() {
        let minutes = Int((focus.duration / 60).rounded())
        let finished = withAnimation(Theme.smooth) {
            focus.complete(focusMinutes: settings.focusMinutes, breakMinutes: settings.breakMinutes)
        }
        if finished == .focus {
            data.focusLog.append(FocusEntry(start: Date().addingTimeInterval(-Double(minutes) * 60), minutes: minutes))
            let streak = focusStreak
            showPeek(Peek(icon: "cup.and.saucer.fill", title: "Focus complete",
                          subtitle: streak > 1 ? L("%d-day streak · take %d min", streak, settings.breakMinutes) : L("Take a %d-minute break", settings.breakMinutes),
                          tint: Accent.den.deep, tab: .den, mood: .happy, sound: "Glass", showsLion: true, urgent: true))
        } else {
            showPeek(Peek(icon: "bolt.fill", title: "Break complete",
                          subtitle: "Ready to focus again", tint: Accent.den.deep,
                          tab: .den, mood: .happy, sound: "Glass", showsLion: true, urgent: true))
        }
    }

    // MARK: - Hunt (tasks)

    func addTask(_ title: String, due: Date?) {
        let clean = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { return }
        withAnimation(Theme.smooth) {
            data.tasks.insert(TaskItem(title: clean, due: due), at: 0)
        }
    }

    func toggleTask(_ id: UUID) {
        guard let i = data.tasks.firstIndex(where: { $0.id == id }) else { return }
        withAnimation(Theme.smooth) { data.tasks[i].done.toggle() }
        if data.tasks[i].done {
            if settings.sounds { NSSound(named: "Tink")?.play() }
            makeLionHappy()
        }
    }

    func deleteTask(_ id: UUID) {
        guard let i = data.tasks.firstIndex(where: { $0.id == id }) else { return }
        let removed = data.tasks[i]
        withAnimation(Theme.smooth) { _ = data.tasks.remove(at: i) }
        showToast("Task deleted", action: "Undo") { [weak self] in
            guard let self else { return }
            withAnimation(Theme.smooth) { self.data.tasks.insert(removed, at: min(i, self.data.tasks.count)) }
        }
    }

    func clearDoneTasks() {
        let before = data.tasks
        let count = before.filter(\.done).count
        guard count > 0 else { return }
        withAnimation(Theme.smooth) { data.tasks.removeAll(where: \.done) }
        showToast(L("Cleared %d done", count), action: "Undo") { [weak self] in
            withAnimation(Theme.smooth) { self?.data.tasks = before }
        }
    }

    private func checkTasks(_ now: Date) {
        for i in data.tasks.indices {
            let t = data.tasks[i]
            guard !t.done, !t.reminded, let due = t.due, due <= now else { continue }
            data.tasks[i].reminded = true
            showPeek(Peek(icon: "checklist", title: "Task due", subtitle: t.title, tint: Accent.hunt.deep,
                          tab: .hunt, mood: .happy, sound: "Ping", showsLion: true, urgent: true))
            break
        }
    }

    // MARK: - Rally (calendar)

    private func checkMeetings(_ now: Date) {
        guard settings.rally else { return }
        for m in calendar.upcoming where !notifiedMeetings.contains(m.id) {
            let lead = m.start.timeIntervalSince(now)
            guard lead > 0, lead <= 300 else { continue }
            notifiedMeetings.insert(m.id)
            let time = m.start.formatted(date: .omitted, time: .shortened)
            showPeek(Peek(icon: "calendar", title: m.title, subtitle: L("Starts %@ · in %d min", time, Int((lead / 60).rounded(.up))),
                          tint: Accent.rally.deep, tab: .hunt, mood: .awake, sound: "Glass", link: m.link, urgent: true))
            break
        }
    }

    func join(_ url: URL) {
        NSWorkspace.shared.open(url)
        dismissPeek()
    }

    // MARK: - Scratch (notes + Catch)

    func addNote() {
        let note = NoteItem(title: "", body: "")
        withAnimation(Theme.smooth) {
            scratchMode = .notes
            data.notes.insert(note, at: 0)
            selectedNoteID = note.id
        }
        noteToFocus = note.id
    }

    func deleteNote(_ id: UUID) {
        guard let i = data.notes.firstIndex(where: { $0.id == id }) else { return }
        let removed = data.notes[i]
        withAnimation(Theme.smooth) {
            data.notes.remove(at: i)
            selectedNoteID = data.notes.first?.id
        }
        showToast("Note deleted", action: "Undo") { [weak self] in
            guard let self else { return }
            withAnimation(Theme.smooth) {
                self.data.notes.insert(removed, at: min(i, self.data.notes.count))
                self.selectedNoteID = removed.id
            }
        }
    }

    /// Called by the editor after typing pauses (it keeps its own buffer while you type).
    func updateNote(_ id: UUID, title: String, body: String) {
        guard let i = data.notes.firstIndex(where: { $0.id == id }),
              data.notes[i].title != title || data.notes[i].body != body else { return }
        data.notes[i].title = title
        data.notes[i].body = body
        data.notes[i].updated = Date()
    }

    func captureClipboard() {
        guard let text = NSPasteboard.general.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines),
              !text.isEmpty else {
            showToast("Clipboard has no text")
            return
        }
        withAnimation(Theme.smooth) {
            data.clips.removeAll { $0.text == text }
            data.clips.insert(ClipItem(text: text), at: 0)
            if data.clips.count > 30 { data.clips = Array(data.clips.prefix(30)) }
        }
        makeLionHappy()
    }

    func copyClip(_ clip: ClipItem) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(clip.text, forType: .string)
        showToast("Copied")
    }

    func removeClip(_ id: UUID) {
        guard let i = data.clips.firstIndex(where: { $0.id == id }) else { return }
        let removed = data.clips[i]
        withAnimation(Theme.smooth) { _ = data.clips.remove(at: i) }
        showToast("Clip removed", action: "Undo") { [weak self] in
            guard let self else { return }
            withAnimation(Theme.smooth) { self.data.clips.insert(removed, at: min(i, self.data.clips.count)) }
        }
    }

    // MARK: - Stash (shelf, Pounce, AirDrop)

    static let stashLimit = 60

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

    /// Resolve bookmarks, sizes and icons once, not on every render.
    func rebuildStash() {
        let items = data.shelf.map { record -> StashItem in
            let url = url(for: record)
            let values = try? url.resourceValues(forKeys: [.fileSizeKey, .isDirectoryKey, .isPackageKey])
            let exists = FileManager.default.fileExists(atPath: url.path)
            let detail: String
            if !exists {
                detail = "Missing"
            } else if values?.isPackage == true {
                detail = url.pathExtension == "app" ? "App" : "Package"
            } else if values?.isDirectory == true {
                detail = "Folder"
            } else if let size = values?.fileSize {
                detail = ByteCountFormatter.string(fromByteCount: Int64(size), countStyle: .file)
            } else {
                detail = ""
            }
            return StashItem(id: record.id, url: url, name: url.lastPathComponent, detail: detail, exists: exists,
                             icon: NSWorkspace.shared.icon(forFile: url.path))
        }
        if items != stashItems { stashItems = items }
    }

    func addToShelf(_ urls: [URL]) {
        var added = 0
        var dropped = 0
        withAnimation(Theme.bouncy) {
            let known = Set(data.shelf.map { URL(fileURLWithPath: $0.path).standardizedFileURL.path })
            for url in urls where url.isFileURL && !known.contains(url.standardizedFileURL.path) {
                let bookmark = try? url.bookmarkData(options: .minimalBookmark, includingResourceValuesForKeys: nil, relativeTo: nil)
                data.shelf.insert(ShelfRecord(path: url.path, bookmark: bookmark), at: 0)
                added += 1
            }
            if data.shelf.count > Self.stashLimit {
                dropped = data.shelf.count - Self.stashLimit
                data.shelf = Array(data.shelf.prefix(Self.stashLimit))
            }
        }
        rebuildStash()
        if added > 0 {
            if settings.sounds { NSSound(named: "Pop")?.play() }
            makeLionHappy()
        }
        if dropped > 0 { showToast(L("Stash keeps %d items · oldest removed", Self.stashLimit)) }
    }

    func receiveDrop(_ providers: [NSItemProvider]) -> Bool {
        let files = providers.filter { $0.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) }
        guard !files.isEmpty else { return false }
        expand(.stash)
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

    /// The open panel would sit under the open notch, so the notch tucks away while it's up.
    func chooseShelfFiles() {
        block("openPanel")
        collapse(explicit: false)
        let picker = NSOpenPanel()
        picker.title = "Add files to Stash"
        picker.prompt = "Add to Stash"
        picker.allowsMultipleSelection = true
        picker.canChooseDirectories = true
        picker.canChooseFiles = true
        NSApp.activate(ignoringOtherApps: true)
        picker.begin { [weak self] response in
            DispatchQueue.main.async {
                guard let self else { return }
                self.unblock("openPanel")
                if response == .OK {
                    self.addToShelf(picker.urls)
                    self.expand(.stash)
                }
            }
        }
    }

    func removeFromShelf(_ id: UUID) {
        guard let i = data.shelf.firstIndex(where: { $0.id == id }) else { return }
        let removed = data.shelf[i]
        withAnimation(Theme.smooth) { _ = data.shelf.remove(at: i) }
        rebuildStash()
        showToast("Removed from Stash", action: "Undo") { [weak self] in
            guard let self else { return }
            withAnimation(Theme.smooth) { self.data.shelf.insert(removed, at: min(i, self.data.shelf.count)) }
            self.rebuildStash()
        }
    }

    func clearShelf() {
        let before = data.shelf
        guard !before.isEmpty else { return }
        withAnimation(Theme.smooth) { data.shelf.removeAll() }
        rebuildStash()
        showToast("Stash cleared", action: "Undo") { [weak self] in
            guard let self else { return }
            withAnimation(Theme.smooth) { self.data.shelf = before }
            self.rebuildStash()
        }
    }

    /// True while a file is dragged out of Stash, so our own drag doesn't light up the notch.
    var isDraggingOut = false

    static var airDrop: NSSharingService? { NSSharingService(named: .sendViaAirDrop) }

    func airDrop(_ urls: [URL]) {
        let files = urls.filter { FileManager.default.fileExists(atPath: $0.path) }
        guard let service = Self.airDrop, !files.isEmpty, service.canPerform(withItems: files) else {
            showToast("AirDrop isn't available right now")
            return
        }
        block("sharing")
        service.perform(withItems: files)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in self?.unblock("sharing") }
    }

    /// Provided by the window controller: the view and the rect under the pointer, to anchor pickers.
    var pointerAnchor: () -> (NSView, NSRect)? = { nil }

    func share(_ urls: [URL]) {
        guard !urls.isEmpty, let (view, rect) = pointerAnchor() else { return }
        block("sharing")
        NSSharingServicePicker(items: urls).show(relativeTo: rect, of: view, preferredEdge: .minY)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in self?.unblock("sharing") }
    }

    // Pounce: new screenshots and downloads.

    private func pounced(_ url: URL, screenshot: Bool) {
        guard screenshot ? settings.pounceScreenshots : settings.pounceDownloads else { return }
        showPeek(Peek(icon: screenshot ? "camera.viewfinder" : "arrow.down.circle.fill",
                      title: screenshot ? "Screenshot" : "Downloaded",
                      subtitle: url.lastPathComponent, tint: Accent.stash.deep, tab: .stash, mood: .happy,
                      file: url))
    }

    func copyFile(_ url: URL) {
        let pb = NSPasteboard.general
        pb.clearContents()
        var items: [NSPasteboardWriting] = [url as NSURL]
        if let image = NSImage(contentsOf: url), UTType(filenameExtension: url.pathExtension)?.conforms(to: .image) == true {
            items.append(image)
        }
        pb.writeObjects(items)
        dismissPeek()
    }

    func stashFile(_ url: URL) {
        addToShelf([url])
        dismissPeek()
    }

    func reveal(_ url: URL) {
        NSWorkspace.shared.activateFileViewerSelecting([url])
        dismissPeek()
    }

    // MARK: - Oasis (water)

    var waterToday: Int { WaterLog.total(data.water, on: Date()) }
    var waterProgress: Double { min(1, Double(waterToday) / Double(max(1, settings.waterGoal))) }

    func addWater(_ ml: Int) {
        let before = waterToday
        withAnimation(.spring(response: 0.6, dampingFraction: 0.7)) {
            data.water.append(WaterEntry(ml: ml))
        }
        lastWaterReminder = nil
        if settings.sounds { NSSound(named: "Bottle")?.play() }
        makeLionHappy()
        if before < settings.waterGoal && waterToday >= settings.waterGoal {
            showToast(L("Goal reached · %d ml today", waterToday))
        }
    }

    func undoWater() {
        guard let i = data.water.lastIndex(where: { Calendar.current.isDateInToday($0.date) }) else {
            showToast("Nothing to undo today")
            return
        }
        let ml = data.water[i].ml
        withAnimation(Theme.smooth) { _ = data.water.remove(at: i) }
        showToast(L("Removed %d ml · %d ml today", ml, waterToday))
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
                      subtitle: L("%d of %d ml today", waterToday, settings.waterGoal), tint: Accent.oasis.deep,
                      tab: .oasis, mood: .happy, sound: "Purr", showsLion: true))
    }

    private let appLaunch = Date()

    // MARK: - Purr (music + Chorus)

    private func trackChanged(_ np: NowPlaying?) {
        guard let np, np.isPlaying, presentation == .compact else { return }
        showPeek(Peek(icon: "music.note", title: np.title, subtitle: np.artist,
                      tint: np.player.tint, tab: .purr, mood: .happy, artwork: true))
    }

    // MARK: - Mirror

    func setMirror(_ on: Bool) {
        noteInteraction()
        prowl.ignoreCamera = on
        withAnimation(Theme.smooth) { mirrorOn = on }
        if on { expand() }
    }

    /// Opening Purr is the moment to read the players (and for macOS to ask, if it hasn't yet).
    func allowMusic() {
        guard !settings.musicAllowed else { return }
        settings.musicAllowed = true
    }

    func updateMusicPolling() {
        music.fastPolling = presentation == .expanded && tab == .purr
    }

    func setMusicSource(_ source: MusicSource) {
        settings.musicSource = source
    }

    func toggleLyrics() {
        noteInteraction()
        withAnimation(Theme.smooth) { settings.showLyrics.toggle() }
        refreshLyrics()
    }

    func refreshLyrics() {
        lyrics.load(for: settings.showLyrics ? music.nowPlaying : nil)
    }

    // MARK: - Whiskers (system overlays and connection peeks)

    private func systemEvent(_ event: SystemMonitor.Event) {
        switch event {
        case .volume(let level, let muted):
            guard settings.systemOverlays else { return }
            let icon: String
            switch muted ? 0 : level {
            case ..<0.001: icon = "speaker.slash.fill"
            case ..<0.34: icon = "speaker.wave.1.fill"
            case ..<0.67: icon = "speaker.wave.2.fill"
            default: icon = "speaker.wave.3.fill"
            }
            showPeek(Peek(icon: icon, title: muted ? "Muted" : "Volume", subtitle: "",
                          tint: muted ? Accent.neutral.deep : Accent.whiskers.light, hudKey: "volume", level: muted ? 0 : level,
                          value: muted ? "Muted" : "\(Int((level * 100).rounded()))%"))
        case .capsLock(let on):
            guard settings.systemOverlays else { return }
            showPeek(Peek(icon: on ? "capslock.fill" : "capslock", title: on ? "Caps Lock on" : "Caps Lock off", subtitle: "",
                          tint: on ? Theme.teal : Accent.neutral.deep, hudKey: "keyboard"))
        case .inputSource(let name):
            guard settings.systemOverlays else { return }
            showPeek(Peek(icon: "globe", title: name, subtitle: "Keyboard", tint: Accent.whiskers.light, hudKey: "keyboard"))
        case .network(let online, let wifi):
            guard settings.connectionPeeks else { return }
            if online {
                showPeek(Peek(icon: wifi ? "wifi" : "network", title: wifi ? "Wi-Fi connected" : "Back online",
                              subtitle: "You're online again", tint: Theme.teal, mood: .happy))
            } else {
                showPeek(Peek(icon: "wifi.slash", title: "Connection lost", subtitle: "Check your network",
                              tint: Theme.berry, mood: .awake, sound: "Funk"))
            }
        case .output(let name, let kind, let previous):
            guard settings.connectionPeeks else { return }
            if kind == .bluetooth || kind == .wired {
                showPeek(Peek(icon: Self.headphonesIcon(name), title: name, subtitle: "Connected",
                              tint: Theme.teal, mood: .happy))
            } else if let previous, previous.kind == .bluetooth || previous.kind == .wired {
                showPeek(Peek(icon: Self.headphonesIcon(previous.name), title: L("%@ disconnected", previous.name),
                              subtitle: L("Now on %@", name), tint: Accent.neutral.deep, mood: .awake))
            }
        }
    }

    private static func headphonesIcon(_ name: String) -> String {
        let n = name.lowercased()
        if n.contains("airpods max") { return "airpodsmax" }
        if n.contains("airpods pro") { return "airpodspro" }
        if n.contains("airpods") { return "airpods" }
        return "headphones"
    }

    // MARK: - Battery

    private func batteryChanged(from old: BatteryState?, to new: BatteryState?) {
        guard let new else { return }
        let fraction = Double(new.percent) / 100
        if let old, old.onAC != new.onAC {
            if new.onAC {
                showPeek(Peek(icon: "bolt.fill", title: new.isCharging ? "Charging" : "Connected to power",
                              subtitle: "Charger connected", tint: Theme.teal, mood: .happy,
                              progress: fraction, value: "\(new.percent)%"))
            } else {
                showPeek(Peek(icon: Self.batteryIcon(new.percent), title: "On battery", subtitle: "Charger unplugged",
                              tint: Theme.gold, mood: .awake, progress: fraction, value: "\(new.percent)%"))
            }
        } else if let old, !new.onAC,
                  let threshold = [20, 10, 5].first(where: { old.percent > $0 && new.percent <= $0 }) {
            // Fires when the level crosses a threshold, even if it skips the exact number.
            showPeek(Peek(icon: Self.batteryIcon(new.percent), title: "Battery low", subtitle: threshold <= 10 ? "Plug in now" : "Plug in soon",
                          tint: Theme.berry, mood: .awake, sound: "Funk", progress: fraction, value: "\(new.percent)%", urgent: true))
        }
    }

    private static func batteryIcon(_ percent: Int) -> String {
        switch percent {
        case ..<13: return "battery.0"
        case ..<38: return "battery.25"
        case ..<63: return "battery.50"
        case ..<88: return "battery.75"
        default: return "battery.100"
        }
    }

    /// data.json couldn't be read at launch: say what happened (it was kept, never overwritten).
    private func reportRecovery() {
        switch store.recovery {
        case .restoredBackup(_, let date)?:
            let title = date.map { L("Restored your data from %@", $0.formatted(date: .abbreviated, time: .shortened)) } ?? L("Restored your data")
            showPeek(Peek(icon: "lifepreserver", title: title, subtitle: "The damaged file is kept in the data folder",
                          tint: Theme.gold, tab: .settings, mood: .awake, urgent: true))
        case .startedFresh?:
            showPeek(Peek(icon: "exclamationmark.triangle", title: "Couldn't read your data",
                          subtitle: "It's kept as data.broken-….json in the data folder", tint: Theme.berry, tab: .settings,
                          mood: .awake, urgent: true))
        case nil:
            break
        }
    }

    // MARK: - Updates

    /// Daily, quietly. `force` is the "Check now" button.
    func checkForUpdates(force: Bool = false) {
        guard force || settings.checkUpdates, !ProcessInfo.processInfo.arguments.contains("--self-test") else { return }
        if !force, let last = data.lastUpdateCheck, Date().timeIntervalSince(last) < 20 * 3600 { return }
        data.lastUpdateCheck = Date()
        updates.check(current: Self.version) { [weak self] release in
            guard let self else { return }
            // End-to-end updater test: with a test feed, `--update-now` installs right away.
            let args = ProcessInfo.processInfo.arguments
            if release != nil, args.contains("--update-feed"), args.contains("--update-now") {
                self.installUpdate()
                return
            }
            if let release, self.data.announcedVersion != release.version {
                self.data.announcedVersion = release.version
                self.showPeek(Peek(icon: "arrow.down.to.line", title: L("Ezkiel Notch %@ is out", release.version),
                                   subtitle: "Click to update in one step", tint: Theme.gold, tab: .settings, mood: .happy,
                                   showsLion: true))
                self.settingsSection = .about
            } else if force, release == nil {
                self.showToast(self.updates.upToDateAt != nil ? "You're on the latest version" : "Couldn't reach GitHub")
            }
        }
    }

    /// One click: download, verify, replace, reopen. Falls back to the release page when it can't.
    func installUpdate() {
        guard let release = updates.available else { return }
        if release.canInstall, AppUpdater.blocker == nil {
            updater.install(release, current: Self.version)
        } else {
            NSWorkspace.shared.open(release.page)
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
            showToast("Move the app to Applications first")
        }
    }

    func refreshLoginItemState() {
        let on = SMAppService.mainApp.status == .enabled
        if settings.launchAtLogin != on { settings.launchAtLogin = on }
    }

    func open(_ target: QuickLaunch) {
        noteInteraction()
        switch target {
        case .store: NSWorkspace.shared.open(URL(string: "https://ezkielyna.store")!)
        case .claude:
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString("Help me plan my day. Ask what matters most, then suggest a realistic schedule with breaks.", forType: .string)
            NSWorkspace.shared.open(URL(string: "https://claude.ai/new")!)
            showToast("Prompt copied · paste it in Claude")
        case .finder: NSWorkspace.shared.open(URL(fileURLWithPath: NSHomeDirectory()))
        case .terminal: NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/Utilities/Terminal.app"))
        case .downloads:
            if let url = PounceWatcher.downloadsURL { NSWorkspace.shared.open(url) }
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

    // MARK: - Self-test hooks

    func simulateMeetings(_ meetings: [CalendarService.Meeting]) {
        // The test shows its own Rally peek; the ticker shouldn't add a second one as a toast.
        notifiedMeetings.formUnion(meetings.map(\.id))
        calendar.simulate(meetings)
    }

    /// Feed a pointer change even though the real pointer is ignored.
    func simulatePointer(inside: Bool) {
        let ignoring = ignoresPointer
        ignoresPointer = false
        pointerMoved(inside: inside)
        ignoresPointer = ignoring
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
    var accent: Accent {
        switch self {
        case .store: return .gold
        case .claude: return .den
        case .finder: return .oasis
        case .terminal: return Accent(light: Color(hex: 0x9BF0C9), deep: Color(hex: 0x30D158))
        case .downloads: return .stash
        case .systemSettings: return .neutral
        }
    }
}
