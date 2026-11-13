import AppKit
import NotchCore
import SwiftUI

/// Live activities: quick timers ("Masak 10m"), downloads in progress, and what the closed notch shows when
/// several things are live at once.
///
/// The closed notch's order (NotchCore's LiveKind): Den's focus timer, a meeting within 10 minutes, the soonest
/// quick timer, a download in progress, an AI agent at work, music, unread messages, charging, the water ring.
extension AppModel {
    // MARK: What the closed notch shows

    /// The live thing the closed notch shows in its wings (nil while nothing is live and the wings are hidden).
    var closedLive: LiveKind? {
        var live = Set<LiveKind>()
        if focus.isRunning { live.insert(.focus) }
        if meetingSoon != nil { live.insert(.meeting) }
        if soonestTimer != nil { live.insert(.timer) }
        if leadDownload != nil { live.insert(.download) }
        // With the eyes on, they show the agent instead (they glance while it works and glow when it waits).
        if settings.closedFace != .eyes, !liveAgents.isEmpty { live.insert(.agent) }
        if music.nowPlaying?.isPlaying == true { live.insert(.music) }
        if unreadTotal > 0 { live.insert(.unread) }
        if battery.state?.isCharging == true { live.insert(.charging) }
        live.insert(.water)
        return LivePill.pick(live)
    }

    /// The live things that take both wings: their own icon on the left, the count on the right.
    var closedLiveOwnsLeftWing: Bool {
        switch closedLive {
        case .meeting, .timer, .download: return true
        default: return false
        }
    }

    // MARK: Quick timers

    /// The quick timer the closed notch counts down.
    var soonestTimer: QuickTimer? { QuickTimers.soonest(data.timers) }

    var runningTimers: [QuickTimer] { data.timers.sorted { $0.ends < $1.ends } }

    /// Starts a timer from typed words ("timer 10m teh", "1h30m"). False when they hold no length.
    @discardableResult
    func startTimer(_ text: String) -> Bool {
        guard let r = TimerPhrase.parse(text) else { return false }
        return startTimer(r.label, seconds: r.seconds) != nil
    }

    @discardableResult
    func startTimer(_ label: String, seconds: Int) -> QuickTimer? {
        guard seconds > 0 else { return nil }
        let t = QuickTimer.start(label, seconds: min(seconds, TimerPhrase.maxSeconds))
        noteInteraction()
        withAnimation(Theme.smooth) {
            data.timers.append(t)
            // The most allowed: the one that would end last makes room.
            while data.timers.count > QuickTimers.limit, let last = data.timers.max(by: { $0.ends < $1.ends }) {
                data.timers.removeAll { $0.id == last.id }
            }
        }
        return data.timers.contains(t) ? t : nil
    }

    func cancelTimer(_ id: UUID) {
        withAnimation(Theme.smooth) { data.timers.removeAll { $0.id == id } }
    }

    /// "+5 min" on a timer that just rang: the same label again, five minutes from now.
    func snoozeTimer(_ timer: QuickTimer, minutes: Int = 5) {
        data.timers.removeAll { $0.id == timer.id }
        startTimer(timer.label, seconds: minutes * 60)
        if presentation == .peek, peek?.timer?.id == timer.id { dismissPeek() }
    }

    func stopTimer(_ timer: QuickTimer) {
        cancelTimer(timer.id)
        if presentation == .peek, peek?.timer?.id == timer.id { dismissPeek() }
    }

    static func timerName(_ t: QuickTimer) -> String { t.label.isEmpty ? L("Timer") : t.label }

    /// Once a second: timers that ended ring (a peek with "+5 min" and "Stop"); ones that ended long ago while the app
    /// was closed go quietly.
    func checkTimers(_ now: Date) {
        guard data.timers.contains(where: { $0.ends <= now }) else { return }
        let due = QuickTimers.due(data.timers, now: now)
        withAnimation(Theme.smooth) { data.timers = due.running }
        for t in due.ring.sorted(by: { $0.ends < $1.ends }) { ring(t) }
    }

    func ring(_ t: QuickTimer) {
        let sound = TimerSounds.normalize(settings.timerSound)
        let name = Self.timerName(t)
        if presentation == .expanded {
            // The open notch gets a toast with the same "+5 min".
            if settings.sounds, sound != "none" { NSSound(named: sound)?.play() }
            showToast(L("%@ is done", name), action: L("+5 min")) { [weak self] in self?.snoozeTimer(t) }
            return
        }
        showPeek(Peek(icon: "timer", title: L("%@ is done", name), subtitle: L("%@ timer", QuickTimers.short(t.seconds)),
                      tint: Accent.den.deep, tab: .den, mood: .happy, sound: sound == "none" ? nil : sound,
                      timer: t, urgent: true))
    }

    // MARK: Downloads

    /// The download the closed notch shows (the largest), and how many there are.
    var leadDownload: (download: ActiveDownload, count: Int)? {
        guard settings.liveDownloads else { return nil }
        return DownloadTracker.lead(downloads)
    }

    /// A download landed: the same peek as Pounce, with Open · Add to Stash · Show in Finder. It shows when Pounce
    /// on downloads is on, or when live downloads followed it while in progress.
    func downloadFinished(_ url: URL, followed: Bool) {
        guard settings.pounceDownloads || (followed && settings.liveDownloads) else { return }
        showPeek(Peek(icon: "arrow.down.circle.fill", title: "Downloaded", subtitle: url.lastPathComponent,
                      tint: Accent.stash.deep, tab: .stash, mood: .happy, file: url, opensFile: true))
    }

    func openFile(_ url: URL) {
        NSWorkspace.shared.open(url)
        dismissPeek()
    }
}
