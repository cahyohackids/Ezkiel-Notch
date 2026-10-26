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

    private func visible(_ badge: BadgeWatcher.Badge) -> Bool { !settings.roarMuted.contains(badge.bundleID) }

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
        // The list already shows it.
        if presentation == .expanded && tab == .roar { return }
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
        let detail = [event.project, event.body.isEmpty ? nil : event.body].compactMap { $0 }.joined(separator: " · ")
        showPeek(Peek(icon: Self.scoutIcon(event), title: event.title, subtitle: detail,
                      tint: event.kind == .needsYou ? Accent.den.deep : Accent.scout.deep, tab: .roar,
                      mood: event.kind == .needsYou ? .awake : .happy,
                      sound: event.kind == .needsYou ? "Tink" : nil, app: event.appBundleID))
    }

    static func scoutIcon(_ event: ScoutEvent) -> String {
        switch event.kind {
        case .needsYou: return "hand.raised.fill"
        case .working: return "sparkle"
        case .done: return event.source == "codex" ? "chevron.left.forwardslash.chevron.right" : "flag.checkered"
        case .info: return event.source == "claude" ? "sparkle" : "terminal.fill"
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

    /// An agent that never reported back (closed terminal, crash) doesn't stay "working" forever.
    func expireAgents(_ now: Date) {
        let stale = agents.filter { now.timeIntervalSince($0.value.date) > 2 * 3600 }.map(\.key)
        guard !stale.isEmpty else { return }
        withAnimation(Theme.smooth) { stale.forEach { agents[$0] = nil } }
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
