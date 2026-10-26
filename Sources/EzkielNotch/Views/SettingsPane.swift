import AppKit
import NotchCore
import SwiftUI

struct SettingsPane: View {
    @ObservedObject var model: AppModel
    @State private var section: Page = .general

    enum Page: String, CaseIterable, Identifiable {
        case general = "General", launch = "Quick launch", about = "About"
        var id: String { rawValue }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 6) {
                ForEach(Page.allCases) { s in
                    Chip(title: s.rawValue, selected: section == s) {
                        withAnimation(Theme.snappy) { section = s }
                    }
                }
                Spacer()
                Button {
                    NSApp.terminate(nil)
                } label: {
                    Label("Quit", systemImage: "power")
                        .font(Theme.font(11, .semibold))
                        .foregroundStyle(Theme.berry)
                        .padding(.horizontal, 10)
                        .frame(height: 26)
                        .background(Capsule().fill(Theme.berry.opacity(0.13)))
                }
                .buttonStyle(PressableStyle())
                .help("Quit Ezkiel Notch")
            }

            Group {
                switch section {
                case .general: general
                case .launch: quickLaunch
                case .about: about
                }
            }
            .transition(.opacity.combined(with: .offset(y: 6)))
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
        .onAppear { model.refreshLoginItemState() }
    }

    private var general: some View {
        ScrollView(.vertical, showsIndicators: false) {
            VStack(spacing: 6) {
                LazyVGrid(columns: [GridItem(.flexible(), spacing: 6), GridItem(.flexible(), spacing: 6)], spacing: 6) {
                    ToggleRow(title: "Open on hover", subtitle: "Click always works too", icon: "cursorarrow.motionlines",
                              isOn: $model.settings.expandOnHover)
                    ToggleRow(title: "Launch at login", subtitle: model.loginItemError ?? "Start with your Mac", icon: "power",
                              isOn: Binding(get: { model.settings.launchAtLogin }, set: { model.setLaunchAtLogin($0) }))
                    ToggleRow(title: "Ezkiel the lion", subtitle: "Mascot in the closed notch", icon: "pawprint.fill",
                              isOn: $model.settings.showMascot)
                    ToggleRow(title: "Lion naps when idle", subtitle: "Wakes up when you return", icon: "moon.zzz.fill",
                              isOn: $model.settings.mascotSleeps)
                    ToggleRow(title: "Minimal closed notch", subtitle: "Hide wings until something is live", icon: "rectangle.compress.vertical",
                              isOn: $model.settings.minimalClosed)
                    ToggleRow(title: "Water reminders", subtitle: "Every \(model.settings.waterIntervalMinutes) min, in the notch", icon: "drop.fill",
                              isOn: $model.settings.waterReminders)
                    ToggleRow(title: "Sounds", subtitle: "Chimes for timers and reminders", icon: "speaker.wave.2.fill",
                              isOn: $model.settings.sounds)
                    ToggleRow(title: "Haptics", subtitle: "Trackpad tap when opening", icon: "hand.tap.fill",
                              isOn: $model.settings.haptics)
                    ToggleRow(title: "Synced lyrics", subtitle: "From LRCLIB, only while on", icon: "quote.bubble.fill",
                              isOn: Binding(get: { model.settings.showLyrics }, set: { if $0 != model.settings.showLyrics { model.toggleLyrics() } }))
                    ToggleRow(title: "Lyrics under the notch", subtitle: "Sing along while it's closed", icon: "text.line.first.and.arrowtriangle.forward",
                              isOn: $model.settings.closedLyrics)
                    ToggleRow(title: "Volume & keyboard", subtitle: "Volume, Caps Lock, language", icon: "speaker.wave.2.circle.fill",
                              isOn: $model.settings.systemOverlays)
                    ToggleRow(title: "Wi-Fi & headphones", subtitle: "Peek when they connect", icon: "wifi",
                              isOn: $model.settings.connectionPeeks)
                }
                optionRow("Music", icon: "music.note") {
                    ForEach(MusicSource.allCases) { source in
                        Chip(title: source.label, selected: model.settings.musicSource == source) {
                            model.setMusicSource(source)
                        }
                    }
                }
                optionRow("Hover delay", icon: "timer") {
                    ForEach([0.05, 0.12, 0.25, 0.45], id: \.self) { d in
                        Chip(title: d < 0.1 ? "Instant" : "\(Int(d * 1000)) ms", selected: abs(model.settings.hoverDelay - d) < 0.01) {
                            model.settings.hoverDelay = d
                        }
                    }
                }
                optionRow("Remind every", icon: "bell") {
                    ForEach([30, 45, 60, 90], id: \.self) { m in
                        Chip(title: "\(m)m", selected: model.settings.waterIntervalMinutes == m) {
                            model.settings.waterIntervalMinutes = m
                        }
                    }
                }
                optionRow("Display", icon: "display") {
                    Chip(title: "Automatic", selected: model.settings.displayName.isEmpty) {
                        model.settings.displayName = ""
                    }
                    ForEach(NSScreen.screens.map(\.localizedName), id: \.self) { name in
                        Chip(title: name, selected: model.settings.displayName == name) {
                            model.settings.displayName = name
                        }
                    }
                }
            }
            .padding(.bottom, 4)
        }
    }

    private func optionRow<Content: View>(_ title: String, icon: String, @ViewBuilder content: () -> Content) -> some View {
        HStack(spacing: 6) {
            Image(systemName: icon).font(.system(size: 10, weight: .medium)).foregroundStyle(Theme.mist)
                .frame(width: 18)
            Text(title).font(Theme.font(11.5, .medium)).foregroundStyle(Theme.ink)
                .frame(width: 88, alignment: .leading)
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 5) { content() }
            }
        }
        .padding(.horizontal, 9)
        .frame(height: 36)
        .background(RoundedRectangle(cornerRadius: 11, style: .continuous).fill(Theme.surface))
    }

    private var quickLaunch: some View {
        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
            ForEach(QuickLaunch.allCases) { item in
                LaunchTile(item: item) { model.open(item) }
            }
        }
    }

    private var about: some View {
        HStack(spacing: 18) {
            LionMascot(size: 92, mood: .happy)
            VStack(alignment: .leading, spacing: 5) {
                Text("Ezkiel Notch")
                    .font(Theme.font(18, .semibold))
                    .foregroundStyle(Theme.ink)
                Text("Version \(AppModel.version) · made for ezkielyna.store")
                    .font(Theme.font(11))
                    .foregroundStyle(Theme.mist)
                Text("Everything stays on this Mac. No account, no tracking. Music uses Apple Events only when Spotify or Music is open. With lyrics on, the song's title, artist and length go to LRCLIB to find them.")
                    .font(Theme.font(11))
                    .foregroundStyle(Theme.ink.opacity(0.8))
                    .fixedSize(horizontal: false, vertical: true)
                HStack(spacing: 8) {
                    PillButton(title: "ezkielyna.store", icon: "storefront.fill", prominent: true) { model.open(.store) }
                    PillButton(title: "Data folder", icon: "folder") {
                        NSWorkspace.shared.activateFileViewerSelecting([DataStore.defaultURL()])
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

private struct LaunchTile: View {
    var item: QuickLaunch
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: item.icon)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(item.tint)
                    .frame(width: 34, height: 34)
                    .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(item.tint.opacity(0.14)))
                    .scaleEffect(hovering ? 1.08 : 1)
                Text(item.title)
                    .font(Theme.font(12, .semibold))
                    .foregroundStyle(Theme.ink)
                Spacer(minLength: 0)
            }
            .padding(10)
            .frame(height: 54)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color.white.opacity(hovering ? 0.1 : 0.055)))
            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(hovering ? item.tint.opacity(0.4) : .clear, lineWidth: 1))
        }
        .buttonStyle(PressableStyle(scale: 0.95))
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}
