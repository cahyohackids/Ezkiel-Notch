import AppKit
import NotchCore
import SwiftUI

enum RoarMode: String, CaseIterable, Identifiable {
    case roar, scout
    var id: String { rawValue }
    var title: String { self == .roar ? "Roar" : "Scout" }
}

/// Roar (unread counts from other apps) and Scout (AI agents and scripts).
extension AppModel {
    /// AI desktop apps: their badges show under Scout, not in the Roar list.
    static let aiApps: Set<String> = [
        "com.anthropic.claudefordesktop", "com.openai.chat", "com.openai.codex",
        "com.todesktop.230313mzl4w4u92", "com.exafunction.windsurf", "ai.perplexity.mac",
    ]

    /// Badges that mean "an update is waiting", not a message.
    static let notMessages: Set<String> = ["com.apple.systempreferences", "com.apple.AppStore", "com.apple.appstore"]

    private func visible(_ badge: BadgeWatcher.Badge) -> Bool {
        !settings.roarMuted.contains(badge.bundleID) && !Self.notMessages.contains(badge.bundleID)
    }

    var roarBadges: [BadgeWatcher.Badge] { badges.badges.filter { visible($0) && !Self.aiApps.contains($0.bundleID) } }
    var aiBadges: [BadgeWatcher.Badge] { badges.badges.filter { visible($0) && Self.aiApps.contains($0.bundleID) } }
    var unreadTotal: Int { roarBadges.reduce(0) { $0 + $1.count } }

    /// Live agents, the ones waiting for you first.
    var liveAgents: [ScoutEvent] {
        agents.values.sorted { a, b in
            if (a.kind == .needsYou) != (b.kind == .needsYou) { return a.kind == .needsYou }
            return a.date > b.date
        }
    }

    // MARK: Roar

    func badgeIncreased(_ badge: BadgeWatcher.Badge, before: Int) {
        guard settings.roar, settings.roarPeeks, visible(badge),
              NSWorkspace.shared.frontmostApplication?.bundleIdentifier != badge.bundleID else { return }
        // The list already shows it; and focus means focus.
        if presentation == .expanded && tab == .roar { return }
        if focusQuiet { return }
        let now = Date()
        if let last = lastRoarPeek[badge.bundleID], now.timeIntervalSince(last) < 20 { return }
        lastRoarPeek[badge.bundleID] = now
        let added = badge.count - before
        showPeek(Peek(icon: "bell.fill", title: badge.name,
                      subtitle: before == 0 ? "\(badge.count) unread" : "\(added) new · \(badge.count) unread",
                      tint: Self.aiApps.contains(badge.bundleID) ? Accent.scout.deep : Accent.roar.deep,
                      tab: .roar, mood: .happy, value: badge.label, app: badge.bundleID, appIcon: true))
    }

    func mute(_ bundleID: String, name: String) {
        guard !settings.roarMuted.contains(bundleID) else { return }
        withAnimation(Theme.smooth) { settings.roarMuted.append(bundleID) }
        showToast("\(name) muted", action: "Undo") { [weak self] in self?.unmute(bundleID) }
    }

    func unmute(_ bundleID: String) {
        withAnimation(Theme.smooth) { settings.roarMuted.removeAll { $0 == bundleID } }
    }

    func openApp(_ bundleID: String) {
        noteInteraction()
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) else { return }
        NSWorkspace.shared.openApplication(at: url, configuration: NSWorkspace.OpenConfiguration())
        if presentation == .expanded && !pinned { collapse(explicit: false) }
    }

    // MARK: Scout

    func scoutReceived(_ event: ScoutEvent) {
        guard settings.scout else { return }
        let key = event.session ?? "\(event.source)·\(event.cwd ?? "")"
        switch event.kind {
        case .working:
            withAnimation(Theme.smooth) { agents[key] = event }
            return
        case .needsYou:
            // Claude's idle reminder after a turn that already finished (and peeked): nothing new.
            if event.body.localizedCaseInsensitiveContains("waiting for your input"), agents[key] == nil { return }
            withAnimation(Theme.smooth) { agents[key] = event }
        case .done:
            withAnimation(Theme.smooth) { agents[key] = nil }
        case .ended:
            withAnimation(Theme.smooth) { agents[key] = nil }
            return
        case .info:
            break
        }
        withAnimation(Theme.smooth) {
            data.scoutLog.insert(event, at: 0)
            if data.scoutLog.count > 40 { data.scoutLog = Array(data.scoutLog.prefix(40)) }
        }
        // Looking at the agent already: no peek.
        if let app = event.appBundleID, NSWorkspace.shared.frontmostApplication?.bundleIdentifier == app { return }
        if presentation == .expanded && tab == .roar && roarMode == .scout { return }
        if focusQuiet && event.kind != .needsYou { return }
        let detail = [event.project, event.body.isEmpty ? nil : event.body].compactMap { $0 }.joined(separator: " · ")
        showPeek(Peek(icon: Self.scoutIcon(event), title: event.title, subtitle: detail,
                      tint: event.kind == .needsYou ? Accent.den.deep : Accent.scout.deep, tab: .roar,
                      mood: event.kind == .needsYou ? .awake : .happy,
                      sound: event.kind == .needsYou ? "Tink" : nil, app: event.appBundleID, urgent: event.kind == .needsYou))
    }

    static func scoutIcon(_ event: ScoutEvent) -> String {
        switch event.kind {
        case .needsYou: return "hand.raised.fill"
        case .working: return "sparkle"
        case .done: return event.source == "codex" ? "chevron.left.forwardslash.chevron.right" : "flag.checkered"
        case .info, .ended: return event.source == "claude" ? "sparkle" : "terminal.fill"
        }
    }

    func openScout(_ event: ScoutEvent) {
        if let app = event.appBundleID { openApp(app) }
    }

    func clearScoutLog() {
        let before = data.scoutLog
        guard !before.isEmpty else { return }
        withAnimation(Theme.smooth) { data.scoutLog.removeAll() }
        showToast("Scout cleared", action: "Undo") { [weak self] in
            withAnimation(Theme.smooth) { self?.data.scoutLog = before }
        }
    }

    /// Log rows that aren't also showing as a live agent above them.
    var scoutHistory: [ScoutEvent] {
        let live = Set(agents.values.map(\.id))
        return data.scoutLog.filter { !live.contains($0.id) }
    }

    /// An agent that never reported back (Esc in Claude Code sends no Stop, a closed terminal, a crash)
    /// doesn't stay live: "needs you" for 30 minutes, "working" for 45.
    func expireAgents(_ now: Date) {
        let stale = agents.filter { _, e in now.timeIntervalSince(e.date) > (e.kind == .needsYou ? 1800 : 2700) }.map(\.key)
        guard !stale.isEmpty else { return }
        withAnimation(Theme.smooth) { stale.forEach { agents[$0] = nil } }
    }

    /// You went to the agent's terminal: its "needs you" has been seen, so it's back to working.
    func agentAppActivated(_ bundleID: String?) {
        guard let bundleID else { return }
        let seen = agents.filter { $0.value.kind == .needsYou && $0.value.appBundleID == bundleID }
        guard !seen.isEmpty else { return }
        withAnimation(Theme.smooth) {
            for (key, var e) in seen {
                e.kind = .working
                e.title = e.source == "codex" ? "Codex is working" : "Claude is working"
                e.body = ""
                agents[key] = e
            }
        }
    }

    func dismissAgent(_ event: ScoutEvent) {
        withAnimation(Theme.smooth) {
            for key in agents.filter({ $0.value.id == event.id }).map(\.key) { agents[key] = nil }
        }
    }

    /// Connected agents whose hook points at an older copy or format get fixed quietly at launch.
    func repairAgentLinks() {
        for agent in AgentLinks.Agent.allCases where AgentLinks.state(agent) == .stale {
            AgentLinks.set(agent, connected: true)
        }
        refreshAgentLinks()
    }

    // MARK: Connecting agents

    func refreshAgentLinks() {
        var links: [AgentLinks.Agent: ScoutSetup.LinkState] = [:]
        for agent in AgentLinks.Agent.allCases { links[agent] = AgentLinks.state(agent) }
        if links != agentLinks { agentLinks = links }
    }

    func setAgent(_ agent: AgentLinks.Agent, connected: Bool) {
        if let problem = AgentLinks.set(agent, connected: connected) {
            showToast(problem)
        } else {
            showToast(connected ? "\(agent.title) connected · new sessions report to Scout" : "\(agent.title) disconnected")
        }
        refreshAgentLinks()
    }

    /// For any script: `open "ezkielnotch://notify?title=…"`.
    func copyScoutCommand() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(#"open "ezkielnotch://notify?title=Build%20done&body=All%20green""#, forType: .string)
        showToast("Copied · run it from any script")
    }

    func handle(url: URL) {
        guard let event = ScoutParser.url(url) else { return }
        scout.receive(event)
    }
}
