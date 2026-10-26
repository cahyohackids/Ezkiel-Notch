import AppKit
import NotchCore
import SwiftUI

struct ExpandedView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var chrome: NotchChrome
    @ObservedObject var music: MusicController
    @Namespace private var tabSpace
    @State private var appeared = false

    var body: some View {
        VStack(spacing: 0) {
            header
                .frame(height: model.rowHeight)
            tabStrip
                .frame(height: NotchMetrics.tabStripHeight)
                .padding(.top, NotchMetrics.tabStripTop)
            ZStack(alignment: .top) {
                page
                    .id(model.tab)
                    .transition(.tabSlide(forward: model.tabForward))
                if model.mirrorOn {
                    MirrorOverlay(mirror: model.mirror) { model.setMirror(false) }
                        .transition(.opacity.combined(with: .scale(scale: 0.97)))
                }
            }
            .frame(maxWidth: .infinity)
            .frame(height: model.paneHeight, alignment: .top)
            .padding(.top, NotchMetrics.pageTop)
            .clipped()
            Spacer(minLength: 0)
        }
        .padding(.horizontal, NotchMetrics.pageInset)
        .padding(.bottom, NotchMetrics.pageBottom)
        .overlay(alignment: .bottom) {
            if let toast = chrome.toast {
                ToastBar(toast: toast) { model.clearToast() }
                    .padding(.bottom, 10)
                    .transition(.opacity.combined(with: .offset(y: 8)))
                    .id(toast.id)
            }
        }
        .onAppear { appeared = true }
    }

    // MARK: Header (split around the camera housing on notched displays)

    private var header: some View {
        HStack(spacing: 0) {
            HStack(spacing: 7) {
                LionMascot(size: 20, mood: model.displayedMood, detailed: false)
                Text(model.tab.title)
                    .font(Theme.font(Theme.Size.body, .semibold))
                    .foregroundStyle(Theme.ink)
                    .lineLimit(1)
                    .id(model.tab)
                    .transition(.opacity)
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity)

            Color.clear.frame(width: model.geometry.hasNotch && !model.isIsland ? model.geometry.notchWidth + 8 : 12)

            HStack(spacing: 4) {
                Spacer(minLength: 0)
                // The weather takes the clock's place (the menu bar keeps its own clock).
                if let w = model.weather.now {
                    WeatherChip(now: w, city: model.settings.weatherCity, fahrenheit: model.settings.fahrenheit)
                        .padding(.trailing, 4)
                } else {
                    TimelineView(.everyMinute) { tl in
                        Text(tl.date, format: .dateTime.hour().minute())
                            .font(Theme.number(Theme.Size.secondary, .medium))
                            .foregroundStyle(Theme.mist)
                            .padding(.trailing, 4)
                    }
                }
                IconButton(icon: model.pinned ? "pin.fill" : "pin",
                           label: model.pinned ? "Unpin · close when you move away" : "Keep the notch open",
                           size: 24, tint: model.pinned ? Theme.gold : Theme.mist) {
                    withAnimation(Theme.snappy) { model.pinned.toggle() }
                }
                IconButton(icon: "chevron.up", label: L("Collapse (Esc) · %@ toggles", model.settings.hotkey.label),
                           size: 24, tint: Theme.mist) {
                    model.collapse()
                }
            }
            .frame(maxWidth: .infinity)
        }
    }

    // MARK: Tabs (icons; the header names the open tab)

    private var tabStrip: some View {
        HStack(spacing: 2) {
            ForEach(NotchTab.allCases) { tab in
                TabButton(tab: tab, selected: model.tab == tab, namespace: tabSpace, badge: badge(for: tab)) {
                    model.select(tab)
                }
                // Tabs drop in one after another as the notch opens.
                .opacity(appeared ? 1 : 0)
                .offset(y: appeared ? 0 : -6)
                .animation(Theme.bouncy.delay(0.04 + Double(tab.index) * 0.022), value: appeared)
            }
        }
    }

    private func badge(for tab: NotchTab) -> Bool {
        switch tab {
        case .den: return model.focus.isRunning
        case .purr: return music.nowPlaying?.isPlaying ?? false
        case .roar: return model.unreadTotal > 0 || !model.agents.isEmpty
        case .hunt:
            return model.data.tasks.contains { !$0.done && ($0.due.map { $0 <= Date() } ?? false) } || model.meetingSoon != nil
        default: return false
        }
    }

    // MARK: Page

    @ViewBuilder
    private var page: some View {
        switch model.tab {
        case .den: DenPane(model: model)
        case .hunt: HuntPane(model: model, calendar: model.calendar)
        case .roar: RoarPane(model: model, badges: model.badges)
        case .scratch: ScratchPane(model: model)
        case .purr: PurrPane(model: model, music: music)
        case .stash: StashPane(model: model)
        case .oasis: OasisPane(model: model)
        case .settings: SettingsPane(model: model, calendar: model.calendar)
        }
    }
}

private struct TabButton: View {
    var tab: NotchTab
    var selected: Bool
    var namespace: Namespace.ID
    var badge: Bool
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: selected ? tab.selectedIcon : tab.icon)
                .font(.system(size: 13.5, weight: selected ? .semibold : .medium))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(selected ? AnyShapeStyle(tab.accent.linear) : AnyShapeStyle(hovering ? Theme.ink : Theme.mist))
                .symbolBounce(value: selected)
                // The badge rides on the glyph, so it follows it at any tab width.
                .overlay(alignment: .topTrailing) {
                    if badge {
                        Circle().fill(tab.accent.deep)
                            .frame(width: 5, height: 5)
                            .overlay(Circle().stroke(Color.black, lineWidth: 1.5))
                            .offset(x: 4, y: -2)
                            .transition(.scale.combined(with: .opacity))
                    }
                }
                .frame(maxWidth: .infinity)
                .frame(height: NotchMetrics.tabStripHeight)
                .background {
                    if selected {
                        Capsule().fill(tab.accent.deep.opacity(0.16))
                            .overlay(Capsule().strokeBorder(tab.accent.deep.opacity(0.22), lineWidth: 0.5))
                            .matchedGeometryEffect(id: "tab", in: namespace)
                    } else if hovering {
                        Capsule().fill(Color.white.opacity(0.05))
                    }
                }
        }
        .buttonStyle(PressableStyle(scale: 0.9))
        .help("\(tab.title) · \(L(tab.subtitle))")
        .accessibilityLabel("\(tab.title), \(L(tab.subtitle))")
        .accessibilityAddTraits(selected ? .isSelected : [])
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}

/// "☀︎ 31°" beside the clock; hover for today's range and rain.
private struct WeatherChip: View {
    var now: WeatherService.Now
    var city: String
    var fahrenheit: Bool

    private func t(_ c: Double) -> String { "\(Int((fahrenheit ? c * 9 / 5 + 32 : c).rounded()))°" }

    var body: some View {
        HStack(spacing: 3) {
            Image(systemName: WeatherService.symbol(now.code, day: now.isDay))
                .symbolRenderingMode(.multicolor)
                .font(.system(size: 11, weight: .medium))
            Text(t(now.temperature)).font(Theme.number(Theme.Size.secondary, .medium)).foregroundStyle(Theme.ink.opacity(0.85))
        }
        .help(help)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(help)
    }

    private var help: String {
        var parts = [city, WeatherService.word(now.code), "\(t(now.low))–\(t(now.high))"]
        if let rain = now.rainChance { parts.append(L("rain %d%%", rain)) }
        return parts.joined(separator: " · ")
    }
}
