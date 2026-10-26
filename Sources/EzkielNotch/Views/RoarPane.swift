import AppKit
import NotchCore
import SwiftUI

/// Roar: unread counts from other apps. Scout: what AI agents and scripts reported.
struct RoarPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var badges: BadgeWatcher

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Space.s) {
            HStack(spacing: 6) {
                Chip(title: model.unreadTotal > 0 ? "Roar · \(Self.short(model.unreadTotal))" : "Roar",
                     selected: model.roarMode == .roar, accent: .roar) {
                    withAnimation(Theme.snappy) { model.roarMode = .roar }
                }
                Chip(title: model.agents.isEmpty ? "Scout" : "Scout · \(model.agents.count)",
                     selected: model.roarMode == .scout, accent: .scout) {
                    withAnimation(Theme.snappy) { model.roarMode = .scout }
                }
                Spacer(minLength: 0)
                if model.roarMode == .scout, !model.data.scoutLog.isEmpty {
                    IconButton(icon: "trash", label: "Clear Scout", size: 26, tint: Theme.mist) { model.clearScoutLog() }
                }
                IconButton(icon: "slider.horizontal.3", label: "Roar & Scout settings", size: 26, tint: Theme.mist) {
                    model.settingsSection = .roar
                    model.select(.settings)
                }
            }

            Group {
                if model.roarMode == .roar { RoarList(model: model) } else { ScoutList(model: model) }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .transition(.opacity)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .onAppear { model.refreshAgentLinks() }
    }

    static func short(_ n: Int) -> String { n > 999 ? "999+" : "\(n)" }
}

// MARK: - Roar

private struct RoarList: View {
    @ObservedObject var model: AppModel

    var body: some View {
        if !model.settings.roar {
            EmptyHint(title: "Roar is off", subtitle: "Turn it on in Settings › Roar.", mood: .sleeping)
        } else if model.roarBadges.isEmpty {
            EmptyHint(title: "All quiet",
                      subtitle: "Unread counts from WhatsApp, Telegram, Teams and any app with a Dock badge show here. Only the number, never the message.")
        } else {
            ScrollView(.vertical, showsIndicators: false) {
                LazyVStack(spacing: 4) {
                    ForEach(model.roarBadges) { badge in
                        BadgeRow(badge: badge, accent: .roar,
                                 open: { model.openApp(badge.bundleID) },
                                 mute: { model.mute(badge.bundleID, name: badge.name) })
                            .transition(.asymmetric(insertion: .move(edge: .top).combined(with: .opacity), removal: .opacity))
                    }
                }
                .padding(.vertical, 2)
            }
            .scrollEdgeFade(6)
        }
    }
}

private struct BadgeRow: View {
    var badge: BadgeWatcher.Badge
    var accent: Accent
    var open: () -> Void
    var mute: () -> Void
    @State private var hovering = false

    var body: some View {
        HStack(spacing: 10) {
            AppIconView(bundleID: badge.bundleID, size: 26)
            VStack(alignment: .leading, spacing: 1) {
                Text(badge.name).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink).lineLimit(1)
                Text(badge.since.formatted(.relative(presentation: .named)))
                    .font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.faint).lineLimit(1)
            }
            Spacer(minLength: 6)
            IconButton(icon: "bell.slash", label: "Mute \(badge.name)", size: 24, tint: Theme.mist, action: mute)
                .opacity(hovering ? 1 : 0)
            CountPill(label: badge.label, accent: accent)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .cardSurface(hover: hovering)
        .contentShape(Rectangle())
        .onTapGesture(perform: open)
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
        .help("Open \(badge.name)")
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(badge.name), \(badge.label) unread")
        .accessibilityAddTraits(.isButton)
    }
}

/// The unread count, like a Dock badge but in the feature's colour.
struct CountPill: View {
    var label: String
    var accent: Accent

    var body: some View {
        Text(label)
            .font(Theme.number(Theme.Size.secondary, .bold))
            .foregroundStyle(Theme.onAccent)
            .lineLimit(1)
            .padding(.horizontal, 7)
            .frame(minWidth: 22, minHeight: 20)
            .background(Capsule().fill(accent.linear))
    }
}

// MARK: - Scout

private struct ScoutList: View {
    @ObservedObject var model: AppModel

    var body: some View {
        if !model.settings.scout {
            EmptyHint(title: "Scout is off", subtitle: "Turn it on in Settings › Roar.", mood: .sleeping)
        } else if model.agents.isEmpty && model.data.scoutLog.isEmpty && model.aiBadges.isEmpty {
            ScoutSetupCard(model: model)
        } else {
            ScrollView(.vertical, showsIndicators: false) {
                LazyVStack(spacing: 4) {
                    if !model.aiBadges.isEmpty {
                        HStack(spacing: 6) {
                            ForEach(model.aiBadges) { badge in
                                AIAppChip(badge: badge) { model.openApp(badge.bundleID) }
                            }
                            Spacer(minLength: 0)
                        }
                    }
                    ForEach(model.liveAgents) { event in
                        ScoutRow(event: event, live: true) { model.openScout(event) }
                    }
                    ForEach(model.data.scoutLog) { event in
                        ScoutRow(event: event, live: false) { model.openScout(event) }
                            .transition(.asymmetric(insertion: .move(edge: .top).combined(with: .opacity), removal: .opacity))
                    }
                }
                .padding(.vertical, 2)
            }
            .scrollEdgeFade(6)
        }
    }
}

private struct ScoutRow: View {
    var event: ScoutEvent
    var live: Bool
    var open: () -> Void
    @State private var hovering = false

    private var accent: Accent { event.kind == .needsYou ? .den : .scout }

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: AppModel.scoutIcon(event))
                .font(.system(size: 12, weight: .semibold))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(accent.linear)
                .symbolPulse(live)
                .frame(width: 26, height: 26)
                .background(Circle().fill(accent.deep.opacity(live ? 0.2 : 0.12)))
            VStack(alignment: .leading, spacing: 1) {
                HStack(spacing: 5) {
                    Text(event.title).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                    if let project = event.project {
                        Text(project).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(accent.light)
                    }
                }
                .lineLimit(1)
                if !event.body.isEmpty {
                    Text(event.body).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist).lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            TimelineView(.everyMinute) { tl in
                Text(Self.age(event.date, now: tl.date))
                    .font(Theme.number(Theme.Size.caption, .medium))
                    .foregroundStyle(live ? accent.light : Theme.faint)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .cardSurface(hover: hovering)
        .overlay {
            if live {
                RoundedRectangle(cornerRadius: Theme.Radius.control, style: .continuous)
                    .strokeBorder(accent.deep.opacity(0.35), lineWidth: 0.75)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture(perform: open)
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
        .help(event.appBundleID == nil ? event.title : "Open where it ran")
        .accessibilityElement(children: .combine)
    }

    /// "now", "4m", "2h", "3d".
    static func age(_ date: Date, now: Date) -> String {
        let s = max(0, now.timeIntervalSince(date))
        if s < 60 { return "now" }
        if s < 3600 { return "\(Int(s / 60))m" }
        if s < 86_400 { return "\(Int(s / 3600))h" }
        return "\(Int(s / 86_400))d"
    }
}

private struct AIAppChip: View {
    var badge: BadgeWatcher.Badge
    var open: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: open) {
            HStack(spacing: 6) {
                AppIconView(bundleID: badge.bundleID, size: 18)
                Text(badge.name).font(Theme.font(Theme.Size.secondary, .medium)).foregroundStyle(Theme.ink).lineLimit(1)
                CountPill(label: badge.label, accent: .scout).scaleEffect(0.85)
            }
            .padding(.leading, 6)
            .padding(.trailing, 2)
            .frame(height: 28)
            .background(Capsule().fill(Color.white.opacity(0.06 + (hovering ? Theme.hoverLift : 0))))
        }
        .buttonStyle(PressableStyle())
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
        .help("Open \(badge.name)")
    }
}

/// First run: connect an agent, or send from any script.
private struct ScoutSetupCard: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Space.s) {
            HStack(spacing: 10) {
                Image(systemName: "sparkle.magnifyingglass")
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(Accent.scout.linear)
                    .frame(width: 34, height: 34)
                    .background(Circle().fill(Accent.scout.deep.opacity(0.14)))
                VStack(alignment: .leading, spacing: 1) {
                    Text("Hear from your agents").font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                    Text("A peek when Claude Code or Codex finishes or needs you, while you're in another app.")
                        .font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            HStack(spacing: 6) {
                ForEach(AgentLinks.Agent.allCases) { agent in
                    let state = model.agentLinks[agent] ?? .off
                    PillButton(title: state == .on ? "\(agent.title) ✓" : agent.title,
                               icon: state == .on ? nil : "link", prominent: state != .on && agent.installed, accent: .scout) {
                        if state != .on { model.setAgent(agent, connected: true) }
                    }
                    .opacity(agent.installed || state == .on ? 1 : 0.5)
                    .help(agent.installed ? "Adds a small hook to \(agent.file.path.replacingOccurrences(of: NSHomeDirectory(), with: "~")) (a backup is kept)"
                                          : "\(agent.title) isn't set up on this Mac")
                }
                PillButton(title: "Script", icon: "doc.on.doc") { model.copyScoutCommand() }
                    .help("Copies open \"ezkielnotch://notify?title=…\"")
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface(radius: Theme.Radius.card)
    }
}

// MARK: - App icons

/// Another app's own icon, by bundle ID (looked up once per app).
struct AppIconView: View {
    var bundleID: String
    var size: CGFloat

    var body: some View {
        Image(nsImage: AppIcons.icon(bundleID))
            .resizable()
            .interpolation(.high)
            .frame(width: size, height: size)
    }
}

@MainActor
enum AppIcons {
    private static var icons: [String: NSImage] = [:]

    static func icon(_ bundleID: String) -> NSImage {
        if let cached = icons[bundleID] { return cached }
        let icon = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID)
            .map { NSWorkspace.shared.icon(forFile: $0.path) } ?? NSWorkspace.shared.icon(for: .applicationBundle)
        icons[bundleID] = icon
        return icon
    }

    static func name(_ bundleID: String) -> String {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) else { return bundleID }
        return FileManager.default.displayName(atPath: url.path).replacingOccurrences(of: ".app", with: "")
    }
}

extension View {
    /// A soft pulse on a symbol while something is live (macOS 14+; still on 13).
    @ViewBuilder
    func symbolPulse(_ active: Bool) -> some View {
        if #available(macOS 14.0, *) {
            symbolEffect(.pulse, options: .repeating, isActive: active)
        } else {
            self
        }
    }
}
