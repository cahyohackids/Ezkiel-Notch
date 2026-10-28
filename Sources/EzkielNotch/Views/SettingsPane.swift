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
        StyleCard(model: model)
        if model.isIsland {
            OptionRow(title: "Size", icon: "capsule") {
                ForEach(IslandSize.allCases) { size in
                    Chip(title: Self.sizeLabel(size), selected: model.settings.islandSize == size) {
                        withAnimation(Theme.bouncy) { model.settings.islandSize = size }
                    }
                }
            }
            OptionRow(title: "Gap", icon: "arrow.up.and.down") {
                ForEach(IslandGap.allCases) { gap in
                    Chip(title: Self.gapLabel(gap), selected: model.settings.islandGap == gap) {
                        withAnimation(Theme.bouncy) { model.settings.islandGap = gap }
                    }
                }
            }
            ToggleRow(title: "Liquid Glass", subtitle: Self.glassSubtitle, icon: "drop.halffull",
                      isOn: Binding(get: { model.settings.islandGlass },
                                    set: { v in withAnimation(Theme.smooth) { model.settings.islandGlass = v } }))
            ToggleRow(title: "Shadow", subtitle: "A soft shadow under the floating island", icon: "shadow",
                      isOn: Binding(get: { model.settings.islandShadow },
                                    set: { v in withAnimation(Theme.smooth) { model.settings.islandShadow = v } }))
        }
        OptionRow(title: L("Language"), icon: "globe") {
            ForEach(AppLanguage.allCases) { lang in
                Chip(title: lang == .auto ? L("Auto") : lang.label, selected: model.settings.language == lang) {
                    model.settings.language = lang
                }
            }
        }
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
        OptionRow(title: "Closed face", icon: "eyes") {
            ForEach(ClosedFace.allCases) { face in
                Chip(title: face == .eyes ? "Eyes" : face == .lion ? "Lion" : "Off", selected: model.settings.closedFace == face) {
                    withAnimation(Theme.smooth) { model.settings.closedFace = face }
                }
            }
        }
        if model.settings.closedFace == .eyes { EyeStyleCard(model: model) }
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

    static func sizeLabel(_ size: IslandSize) -> String {
        switch size {
        case .small: return "Small"
        case .medium: return "Medium"
        case .large: return "Large"
        }
    }

    static func gapLabel(_ gap: IslandGap) -> String {
        switch gap {
        case .tight: return "Tight"
        case .medium: return "Medium"
        case .roomy: return "Roomy"
        }
    }

    static var glassSubtitle: String {
        if #available(macOS 26.0, *) { return "Apple's see-through glass, tinted dark so text stays readable" }
        return "A dark blur (real Liquid Glass needs macOS 26)"
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
            DockBadgesRow(model: model, badges: model.badges)
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
                    Text(L("Any script or app")).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                    Text(L("open \"ezkielnotch://notify?title=Done&body=…\"")).font(.system(size: Theme.Size.caption, design: .monospaced))
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
        WeatherRow(model: model, weather: model.weather)
        ToggleRow(title: "Prowl", subtitle: "Green and orange dots while a camera or microphone is on", icon: "eye",
                  accent: Accent(light: Color(hex: 0x9BF0B4), deep: Color(hex: 0x30D158)), isOn: $model.settings.prowl)
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
                Chip(title: L("%d min", m), selected: model.settings.breakMinutes == m, accent: .rest) { model.settings.breakMinutes = m }
            }
        }
        ToggleRow(title: "Oasis reminders", subtitle: L("Every %d min in your active hours", model.settings.waterIntervalMinutes), icon: "drop",
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
        case .granted: return calendar.upcoming.isEmpty ? "No more meetings today" : L("%d coming up today", calendar.upcoming.count)
        }
    }

    // MARK: About

    @ViewBuilder
    private var about: some View {
        // A waiting update goes first, where it's seen without scrolling.
        if model.updates.available != nil {
            UpdateRow(model: model, updates: model.updates, updater: model.updater)
        }
        HStack(spacing: Theme.Space.m) {
            LionMascot(size: 56, mood: .happy, animated: false, detailed: true)
            VStack(alignment: .leading, spacing: 2) {
                Text(L("Ezkiel Notch")).font(Theme.font(Theme.Size.title, .semibold)).foregroundStyle(Theme.ink)
                Text(L("Version %@ · open source", AppModel.version)).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                Text(L("Everything stays on this Mac. Chorus asks LRCLIB for lyrics only while it's on."))
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

        if model.updates.available == nil {
            UpdateRow(model: model, updates: model.updates, updater: model.updater)
        }

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

/// Weather glance: type a city, pick it, done. Open-Meteo, no account, no Location permission.
private struct WeatherRow: View {
    @ObservedObject var model: AppModel
    @ObservedObject var weather: WeatherService
    @State private var query = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 10) {
                Image(systemName: "cloud.sun").font(.system(size: 12, weight: .medium)).symbolRenderingMode(.multicolor).frame(width: 18)
                VStack(alignment: .leading, spacing: 1) {
                    Text(L("Weather")).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                    Text(subtitle).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist).lineLimit(1)
                }
                Spacer(minLength: 6)
                if model.settings.weatherCity.isEmpty {
                    NotchField(placeholder: L("City"), text: $query) { weather.search(query) }
                        .frame(width: 130)
                } else {
                    Chip(title: "°C", selected: !model.settings.fahrenheit) { model.settings.fahrenheit = false }
                    Chip(title: "°F", selected: model.settings.fahrenheit) { model.settings.fahrenheit = true }
                    TextButton(title: L("Remove"), tint: Theme.mist) {
                        model.settings.weatherCity = ""
                        query = ""
                    }
                }
            }
            if model.settings.weatherCity.isEmpty, !weather.results.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 5) {
                        ForEach(weather.results) { place in
                            Chip(title: place.detail.isEmpty ? place.name : "\(place.name), \(place.detail)", selected: false, accent: .oasis) {
                                model.settings.weatherLatitude = place.latitude
                                model.settings.weatherLongitude = place.longitude
                                model.settings.weatherCity = place.name
                                weather.clearResults()
                            }
                        }
                    }
                }
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .frame(minHeight: 36)
        .cardSurface()
    }

    private var subtitle: String {
        if model.settings.weatherCity.isEmpty {
            return weather.searching ? L("Searching…") : L("Type a city and press Return · no location access")
        }
        guard let now = weather.now else { return model.settings.weatherCity }
        let t = { (c: Double) in "\(Int((model.settings.fahrenheit ? c * 9 / 5 + 32 : c).rounded()))°" }
        return "\(model.settings.weatherCity) · \(WeatherService.word(now.code)) \(t(now.temperature)) · \(t(now.low))–\(t(now.high))"
    }
}

/// "Ezkiel Notch 1.4.0 is out · Download", or the daily-check switch with "Check now".
private struct UpdateRow: View {
    @ObservedObject var model: AppModel
    @ObservedObject var updates: UpdateChecker
    @ObservedObject var updater: AppUpdater

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 10) {
                Image(systemName: updates.available == nil ? "arrow.triangle.2.circlepath" : "arrow.down.app.fill")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(updates.available == nil ? AnyShapeStyle(Theme.mist) : AnyShapeStyle(Accent.gold.linear))
                    .frame(width: 18)
                VStack(alignment: .leading, spacing: 1) {
                    Text(updates.available.map { L("Version %@ is out", $0.version) } ?? L("Updates"))
                        .font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                    Text(subtitle).font(Theme.font(Theme.Size.caption))
                        .foregroundStyle(isFailed ? Accent.hunt.light : Theme.mist).lineLimit(1)
                }
                Spacer(minLength: 6)
                trailing
            }
            if case .downloading(let fraction) = updater.phase {
                LevelBar(level: max(0.03, fraction), accent: .gold)
                    .padding(.leading, 28)
                    .transition(.opacity)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .frame(minHeight: 36)
        .cardSurface()
        .animation(Theme.snappy, value: updater.phase)
    }

    @ViewBuilder
    private var trailing: some View {
        if let release = updates.available {
            switch updater.phase {
            case .downloading, .verifying:
                TextButton(title: "Cancel", tint: Theme.mist) { updater.cancel() }
            case .installing, .relaunching:
                ProgressView().controlSize(.small)
            case .idle, .failed:
                if release.canInstall && AppUpdater.blocker == nil {
                    TextButton(title: "Release notes", tint: Theme.mist) { NSWorkspace.shared.open(release.page) }
                    PillButton(title: isFailed ? "Try again" : "Update now", icon: "arrow.down", prominent: true, accent: .gold) {
                        model.installUpdate()
                    }
                } else {
                    PillButton(title: "Download", icon: "arrow.down", prominent: true, accent: .gold) {
                        NSWorkspace.shared.open(release.page)
                    }
                }
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
            .help(L("Ask GitHub once a day"))
            .accessibilityLabel(L("Check for updates daily"))
            .accessibilityValue(model.settings.checkUpdates ? L("On") : L("Off"))
        }
    }

    private var isFailed: Bool { if case .failed = updater.phase { return true } else { return false } }

    private var subtitle: String {
        switch updater.phase {
        case .downloading(let f): return L("Downloading… %d%%", Int((f * 100).rounded()))
        case .verifying: return L("Checking the download…")
        case .installing: return L("Installing…")
        case .relaunching: return L("Reopening…")
        case .failed(let reason): return L(reason)
        case .idle: break
        }
        if let release = updates.available {
            if let blocker = AppUpdater.blocker { return L(blocker) }
            return release.canInstall ? L("You have %@ · updates in place, your data stays", AppModel.version)
                                      : L("You have %@ · opens the release page", AppModel.version)
        }
        if let at = updates.upToDateAt { return L("Up to date · checked %@", at.formatted(date: .omitted, time: .shortened)) }
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
        case .off: return agent.installed ? L("Adds a hook to %@, backed up first", file) : "Not set up on this Mac"
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
                Text(L(subtitle)).font(Theme.font(Theme.Size.caption)).foregroundStyle(state == .stale ? Accent.den.light : Theme.mist)
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

/// Roar can also read the Dock's badges, so WhatsApp or Teams count even while they're closed.
private struct DockBadgesRow: View {
    @ObservedObject var model: AppModel
    @ObservedObject var badges: BadgeWatcher

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ToggleRow(title: "Apps that aren't open", subtitle: subtitle, icon: "dock.rectangle", accent: .roar,
                      isOn: $model.settings.roarFromDock)
            if model.settings.roarFromDock && !badges.dockAccess {
                HStack(spacing: 8) {
                    Text(L("Allow Ezkiel Notch under Accessibility, then come back"))
                        .font(Theme.font(Theme.Size.caption)).foregroundStyle(Accent.den.light)
                    Spacer(minLength: 0)
                    TextButton(title: "Open Settings", tint: Accent.roar.light) {
                        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
                    }
                }
                .padding(.horizontal, 10)
            }
        }
    }

    private var subtitle: String {
        "Reads the Dock's badges, so WhatsApp, Teams and others count while closed. Needs Accessibility; only the numbers are read."
    }
}

/// Notch or Island, as two little pictures of the top of a screen.
private struct StyleCard: View {
    @ObservedObject var model: AppModel

    var body: some View {
        HStack(spacing: 8) {
            tile(.notch, title: "Notch", subtitle: "Hangs from the top edge")
            tile(.island, title: "Island", subtitle: "Floats like the Dynamic Island")
        }
    }

    private func tile(_ style: NotchStyle, title: String, subtitle: String) -> some View {
        let selected = model.settings.style == style
        return Button {
            withAnimation(Theme.bouncy) { model.settings.style = style }
        } label: {
            VStack(alignment: .leading, spacing: 6) {
                ZStack(alignment: .top) {
                    RoundedRectangle(cornerRadius: 7, style: .continuous)
                        .fill(LinearGradient(colors: [Color(hex: 0x3A2F4F), Color(hex: 0x1B2236)], startPoint: .top, endPoint: .bottom))
                    if style == .notch {
                        NotchShape(bottomRadius: 5, ear: 3).fill(Color.black).frame(width: 52, height: 11)
                    } else {
                        NotchShape(bottomRadius: 6, ear: 0, topRadius: 6).fill(Color.black)
                            .overlay(NotchShape(bottomRadius: 6, ear: 0, topRadius: 6).stroke(Color.white.opacity(0.15), lineWidth: 0.5))
                            .frame(width: 44, height: 12)
                            .padding(.top, 3)
                            .shadow(color: .black.opacity(0.4), radius: 2, y: 1)
                    }
                }
                .frame(height: 34)
                .clipShape(RoundedRectangle(cornerRadius: 7, style: .continuous))
                VStack(alignment: .leading, spacing: 0) {
                    Text(L(title)).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                    Text(L(subtitle)).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist).lineLimit(1)
                }
            }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .cardSurface()
            .overlay(RoundedRectangle(cornerRadius: Theme.Radius.control, style: .continuous)
                .strokeBorder(selected ? Theme.gold : Color.clear, lineWidth: 1.5))
        }
        .buttonStyle(PressableStyle(scale: 0.97))
        .accessibilityLabel(L(title))
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}

/// The five eye styles, each a small live preview that blinks like the real thing.
private struct EyeStyleCard: View {
    @ObservedObject var model: AppModel

    var body: some View {
        HStack(spacing: 6) {
            ForEach(EyeStyle.allCases) { style in tile(style) }
        }
    }

    private func tile(_ style: EyeStyle) -> some View {
        let selected = model.settings.eyeStyle == style
        return Button {
            withAnimation(Theme.smooth) { model.settings.eyeStyle = style }
        } label: {
            VStack(spacing: 5) {
                ZStack {
                    Capsule().fill(Color.black)
                    if style == .anime {
                        // A hint of the sparkly eyes' ambient glow.
                        Capsule().stroke(AmbientGlow.colors[1].opacity(0.5), lineWidth: 1).blur(radius: 2)
                    }
                    EzkielEyes(state: .awake, hovering: selected, height: 8, style: style)
                }
                .frame(height: 22)
                .padding(.horizontal, 4)
                Text(L(style.title)).font(Theme.font(Theme.Size.caption, .medium))
                    .foregroundStyle(selected ? Theme.ink : Theme.mist).lineLimit(1)
            }
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity)
            .cardSurface()
            .overlay(RoundedRectangle(cornerRadius: Theme.Radius.control, style: .continuous)
                .strokeBorder(selected ? Theme.gold : Color.clear, lineWidth: 1.5))
        }
        .buttonStyle(PressableStyle(scale: 0.95))
        .accessibilityLabel(L(style.title))
        .accessibilityAddTraits(selected ? .isSelected : [])
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
            Text(L(title)).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
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
                Text(L(item.title)).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.ink).lineLimit(1)
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
