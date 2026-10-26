import AppKit
import NotchCore
import SwiftUI

struct SettingsPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var calendar: CalendarService
    @State private var confirmQuit = false

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Space.s) {
            HStack(spacing: 5) {
                ForEach(SettingsSection.allCases) { s in
                    Chip(title: s.title, selected: model.settingsSection == s, accent: .gold) {
                        withAnimation(Theme.snappy) { model.settingsSection = s }
                    }
                }
                Spacer(minLength: 0)
            }

            ScrollView(.vertical, showsIndicators: false) {
                VStack(spacing: 6) {
                    switch model.settingsSection {
                    case .general: general
                    case .whiskers: whiskers
                    case .roar: roar
                    case .features: features
                    case .about: about
                    }
                }
                .padding(.vertical, 4)
                .id(model.settingsSection)
                .transition(.opacity)
            }
            .scrollEdgeFade(8)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .onAppear {
            model.refreshLoginItemState()
            model.refreshAgentLinks()
        }
    }

    // MARK: General

    @ViewBuilder
    private var general: some View {
        ToggleRow(title: "Open on hover", subtitle: "Click always works too", icon: "cursorarrow.motionlines",
                  isOn: $model.settings.expandOnHover)
        OptionRow(title: "Hover delay", icon: "timer") {
            ForEach([0.05, 0.12, 0.25, 0.45], id: \.self) { d in
                Chip(title: d < 0.1 ? "Instant" : "\(Int(d * 1000)) ms", selected: abs(model.settings.hoverDelay - d) < 0.01) {
                    model.settings.hoverDelay = d
                }
            }
        }
        OptionRow(title: "Shortcut", icon: "command") {
            ForEach(HotkeyChoice.allCases) { choice in
                Chip(title: choice.label, selected: model.settings.hotkey == choice) { model.settings.hotkey = choice }
            }
        }
        ToggleRow(title: "Launch at login", subtitle: model.loginItemError ?? "Start with your Mac", icon: "arrow.right.circle",
                  isOn: Binding(get: { model.settings.launchAtLogin }, set: { model.setLaunchAtLogin($0) }))
        ToggleRow(title: "Ezkiel the lion", subtitle: "In the closed notch", icon: "pawprint", isOn: $model.settings.showMascot)
        ToggleRow(title: "Lion naps when idle", subtitle: "Wakes up when you're back", icon: "moon.zzz", isOn: $model.settings.mascotSleeps)
        ToggleRow(title: "Minimal closed notch", subtitle: "Hide the wings until something is live", icon: "rectangle.compress.vertical",
                  isOn: $model.settings.minimalClosed)
        ToggleRow(title: "Calm in fullscreen", subtitle: "Videos, games and slides: no wings, only urgent peeks", icon: "arrow.up.left.and.arrow.down.right",
                  isOn: $model.settings.hideInFullscreen)
        ToggleRow(title: "Quiet during focus", subtitle: "Roar and finished agents wait while Den runs", icon: "moon",
                  accent: .den, isOn: $model.settings.quietDuringFocus)
        ToggleRow(title: "Hide from screen sharing", subtitle: "Zoom, Meet and recordings don't see the notch", icon: "rectangle.on.rectangle.slash",
                  isOn: $model.settings.hideFromScreenShare)
        ToggleRow(title: "Sounds", subtitle: "Chimes for timers and reminders", icon: "speaker.wave.2", isOn: $model.settings.sounds)
        ToggleRow(title: "Haptics", subtitle: "A tap on the trackpad when opening", icon: "hand.tap", isOn: $model.settings.haptics)
        OptionRow(title: "Display", icon: "display") {
            Chip(title: "Automatic", selected: model.settings.displayID == 0 && model.settings.displayName.isEmpty) {
                model.settings.displayID = 0
                model.settings.displayName = ""
            }
            ForEach(NSScreen.screens, id: \.displayID) { screen in
                Chip(title: screen.localizedName, selected: model.settings.displayID == screen.displayID) {
                    model.settings.displayID = screen.displayID
                    model.settings.displayName = screen.localizedName
                }
            }
        }
    }

    // MARK: Whiskers (small notifications)

    @ViewBuilder
    private var whiskers: some View {
        ToggleRow(title: "Volume & keyboard", subtitle: "Volume, Caps Lock and keyboard language", icon: "speaker.wave.2.bubble",
                  accent: .whiskers, isOn: $model.settings.systemOverlays)
        ToggleRow(title: "Wi-Fi & headphones", subtitle: "When they connect or drop", icon: "wifi", accent: .whiskers,
                  isOn: $model.settings.connectionPeeks)
        ToggleRow(title: "Pounce on screenshots", subtitle: "New screenshots appear with Copy · Stash · Show. macOS asks for Desktop access once.",
                  icon: "camera.viewfinder", accent: .stash, isOn: $model.settings.pounceScreenshots)
        ToggleRow(title: "Pounce on downloads", subtitle: "Finished downloads in ~/Downloads appear the same way. macOS asks for Downloads access once.",
                  icon: "arrow.down.circle", accent: .stash, isOn: $model.settings.pounceDownloads)
    }

    // MARK: Roar & Scout

    @ViewBuilder
    private var roar: some View {
        ToggleRow(title: "Roar", subtitle: "Unread counts from WhatsApp, Telegram, Teams and any app with a Dock badge. Only the number; no permission needed.",
                  icon: "bell.badge", accent: .roar, isOn: $model.settings.roar)
        if model.settings.roar {
            ToggleRow(title: "Peek on new messages", subtitle: "Not while that app is in front", icon: "bubble.left",
                      accent: .roar, isOn: $model.settings.roarPeeks)
            if !model.settings.roarMuted.isEmpty {
                OptionRow(title: "Muted", icon: "bell.slash") {
                    ForEach(model.settings.roarMuted, id: \.self) { id in
                        Chip(title: "\(AppIcons.name(id))  ✕", selected: false, accent: .roar) { model.unmute(id) }
                            .help("Unmute")
                    }
                }
            }
        }
        ToggleRow(title: "Scout", subtitle: "AI agents and scripts: a peek when they finish or need you", icon: "sparkle",
                  accent: .scout, isOn: $model.settings.scout)
        if model.settings.scout {
            ForEach(AgentLinks.Agent.allCases) { agent in
                AgentRow(agent: agent, state: model.agentLinks[agent] ?? .off) { connect in
                    model.setAgent(agent, connected: connect)
                }
            }
            HStack(spacing: 10) {
                Image(systemName: "terminal").font(.system(size: 12, weight: .medium)).foregroundStyle(Theme.mist).frame(width: 18)
                VStack(alignment: .leading, spacing: 1) {
                    Text("Any script or app").font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                    Text("open \"ezkielnotch://notify?title=Done&body=…\"").font(.system(size: Theme.Size.caption, design: .monospaced))
                        .foregroundStyle(Theme.mist).lineLimit(1).truncationMode(.middle)
                }
                Spacer(minLength: 6)
                PillButton(title: "Copy", icon: "doc.on.doc") { model.copyScoutCommand() }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .cardSurface()
        }
    }

    // MARK: Features

    @ViewBuilder
    private var features: some View {
        ToggleRow(title: "Rally", subtitle: rallySubtitle, icon: "calendar", accent: .rally, isOn: $model.settings.rally)
        if model.settings.rally {
            ToggleRow(title: "Meeting countdown", subtitle: "\"in 5m\" in the closed notch", icon: "timer", accent: .rally,
                      isOn: $model.settings.rallyCountdown)
        }
        OptionRow(title: "Purr source", icon: "waveform") {
            ForEach(MusicSource.allCases) { source in
                Chip(title: source.label, selected: model.settings.musicSource == source, accent: .purr) { model.setMusicSource(source) }
            }
        }
        ToggleRow(title: "Chorus", subtitle: "Synced lyrics from LRCLIB, only asked while on", icon: "quote.bubble", accent: .purr,
                  isOn: Binding(get: { model.settings.showLyrics }, set: { if $0 != model.settings.showLyrics { model.toggleLyrics() } }))
        ToggleRow(title: "Chorus under the notch", subtitle: "Sing along while it's closed", icon: "text.line.first.and.arrowtriangle.forward",
                  accent: .purr, isOn: $model.settings.closedLyrics)
        OptionRow(title: "Den break", icon: "cup.and.saucer") {
            ForEach([5, 10, 15], id: \.self) { m in
                Chip(title: "\(m) min", selected: model.settings.breakMinutes == m, accent: .rest) { model.settings.breakMinutes = m }
            }
        }
        ToggleRow(title: "Oasis reminders", subtitle: "Every \(model.settings.waterIntervalMinutes) min in your active hours", icon: "drop",
                  accent: .oasis, isOn: $model.settings.waterReminders)
        OptionRow(title: "Remind every", icon: "bell") {
            ForEach([30, 45, 60, 90], id: \.self) { m in
                Chip(title: "\(m)m", selected: model.settings.waterIntervalMinutes == m, accent: .oasis) { model.settings.waterIntervalMinutes = m }
            }
        }
        OptionRow(title: "Active hours", icon: "sun.max") {
            ForEach([(7, 21), (8, 22), (9, 23)], id: \.0) { start, end in
                Chip(title: "\(start)–\(end)", selected: model.settings.activeHourStart == start && model.settings.activeHourEnd == end,
                     accent: .oasis) {
                    model.settings.activeHourStart = start
                    model.settings.activeHourEnd = end
                }
            }
        }
    }

    private var rallySubtitle: String {
        guard model.settings.rally else { return "Next meeting in Hunt, a peek with Join 5 min before" }
        switch calendar.access {
        case .denied: return "Calendar access is off · Privacy & Security → Calendars"
        case .unknown: return "Waiting for Calendar access…"
        case .granted: return calendar.upcoming.isEmpty ? "No more meetings today" : "\(calendar.upcoming.count) coming up today"
        }
    }

    // MARK: About

    @ViewBuilder
    private var about: some View {
        HStack(spacing: Theme.Space.m) {
            LionMascot(size: 56, mood: .happy, animated: false, detailed: true)
            VStack(alignment: .leading, spacing: 2) {
                Text("Ezkiel Notch").font(Theme.font(Theme.Size.title, .semibold)).foregroundStyle(Theme.ink)
                Text("Version \(AppModel.version) · open source").font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                Text("Everything stays on this Mac. Chorus asks LRCLIB for lyrics only while it's on.")
                    .font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist).fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
        .padding(10)
        .cardSurface()

        HStack(spacing: 6) {
            ForEach(QuickLaunch.allCases) { item in
                LaunchTile(item: item) { model.open(item) }
            }
        }

        UpdateRow(model: model, updates: model.updates)

        HStack(spacing: 6) {
            PillButton(title: "ezkielyna.store/notch", icon: "safari") {
                NSWorkspace.shared.open(URL(string: "https://ezkielyna.store/notch")!)
            }
            PillButton(title: "Data folder", icon: "folder") {
                NSWorkspace.shared.activateFileViewerSelecting([model.dataFileURL])
            }
            Spacer(minLength: 0)
            Button {
                if confirmQuit { NSApp.terminate(nil) }
                withAnimation(Theme.snappy) { confirmQuit = true }
                DispatchQueue.main.asyncAfter(deadline: .now() + 3) { withAnimation(Theme.snappy) { confirmQuit = false } }
            } label: {
                Label(confirmQuit ? "Click to quit" : "Quit", systemImage: "power")
                    .font(Theme.font(Theme.Size.secondary, .semibold))
                    .foregroundStyle(confirmQuit ? .white : Accent.hunt.light)
                    .padding(.horizontal, 10)
                    .frame(height: 28)
                    .background(Capsule().fill(confirmQuit ? AnyShapeStyle(Accent.hunt.linear) : AnyShapeStyle(Accent.hunt.deep.opacity(0.14))))
            }
            .buttonStyle(PressableStyle())
            .help("Quit Ezkiel Notch")
        }
    }
}

/// "Ezkiel Notch 1.4.0 is out · Download", or the daily-check switch with "Check now".
private struct UpdateRow: View {
    @ObservedObject var model: AppModel
    @ObservedObject var updates: UpdateChecker

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: updates.available == nil ? "arrow.triangle.2.circlepath" : "arrow.down.app.fill")
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(updates.available == nil ? AnyShapeStyle(Theme.mist) : AnyShapeStyle(Accent.gold.linear))
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 1) {
                Text(updates.available.map { "Version \($0.version) is out" } ?? "Updates")
                    .font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                Text(subtitle).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist).lineLimit(1)
            }
            Spacer(minLength: 6)
            if let release = updates.available {
                PillButton(title: "Download", icon: "arrow.down", prominent: true, accent: .gold) {
                    NSWorkspace.shared.open(release.page)
                }
            } else {
                TextButton(title: updates.checking ? "Checking…" : "Check now") { model.checkForUpdates(force: true) }
                    .disabled(updates.checking)
                Button {
                    withAnimation(Theme.snappy) { model.settings.checkUpdates.toggle() }
                } label: {
                    NotchSwitch(isOn: model.settings.checkUpdates, accent: .gold)
                }
                .buttonStyle(PressableStyle())
                .help("Ask GitHub once a day")
                .accessibilityLabel("Check for updates daily")
                .accessibilityValue(model.settings.checkUpdates ? "On" : "Off")
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .frame(minHeight: 36)
        .cardSurface()
    }

    private var subtitle: String {
        if updates.available != nil { return "You have \(AppModel.version) · opens the release page" }
        if let at = updates.upToDateAt { return "Up to date · checked \(at.formatted(date: .omitted, time: .shortened))" }
        return model.settings.checkUpdates ? "Checks GitHub once a day" : "Daily check is off"
    }
}

/// Claude Code or Codex: connected or not, with one button.
private struct AgentRow: View {
    var agent: AgentLinks.Agent
    var state: ScoutSetup.LinkState
    var set: (Bool) -> Void

    private var subtitle: String {
        let file = agent.file.path.replacingOccurrences(of: NSHomeDirectory(), with: "~")
        switch state {
        case .on: return "Connected · new sessions report here"
        case .stale: return "Linked to an older copy of the app · reconnect"
        case .off: return agent.installed ? "Adds a hook to \(file), backed up first" : "Not set up on this Mac"
        }
    }

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: agent == .claude ? "sparkle" : "chevron.left.forwardslash.chevron.right")
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(state == .on ? AnyShapeStyle(Accent.scout.linear) : AnyShapeStyle(Theme.mist))
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 1) {
                Text(agent.title).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                Text(subtitle).font(Theme.font(Theme.Size.caption)).foregroundStyle(state == .stale ? Accent.den.light : Theme.mist)
                    .lineLimit(1).truncationMode(.middle)
            }
            Spacer(minLength: 6)
            switch state {
            case .on: TextButton(title: "Disconnect", tint: Theme.mist) { set(false) }
            case .stale: PillButton(title: "Reconnect", prominent: true, accent: .scout) { set(true) }
            case .off: PillButton(title: "Connect", prominent: agent.installed, accent: .scout) { set(true) }
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .frame(minHeight: 36)
        .cardSurface()
    }
}

/// Label on the left, chips scrolling sideways on the right.
private struct OptionRow<Content: View>: View {
    var title: String
    var icon: String
    @ViewBuilder var content: () -> Content

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: icon).font(.system(size: 12, weight: .medium)).symbolRenderingMode(.hierarchical)
                .foregroundStyle(Theme.mist).frame(width: 18)
            Text(title).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                .frame(width: 92, alignment: .leading)
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 5) { content() }.padding(.trailing, 8)
            }
            .mask(LinearGradient(stops: [.init(color: .black, location: 0.9), .init(color: .clear, location: 1)],
                                 startPoint: .leading, endPoint: .trailing))
        }
        .padding(.horizontal, 10)
        .frame(height: 36)
        .cardSurface()
    }
}

private struct LaunchTile: View {
    var item: QuickLaunch
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(spacing: 4) {
                Image(systemName: item.icon)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(item.accent.linear)
                    .frame(width: 30, height: 30)
                    .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(item.accent.deep.opacity(0.16)))
                Text(item.title).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.ink).lineLimit(1)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 6)
            .cardSurface(hover: hovering)
        }
        .buttonStyle(PressableStyle(scale: 0.95))
        .help(item.title)
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

extension NSScreen {
    /// Stable per-display ID (two identical monitors have the same name, never the same ID).
    var displayID: UInt32 {
        (deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
    }
}
