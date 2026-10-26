import AppKit
import NotchCore
import SwiftUI

struct ExpandedView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    @Namespace private var tabSpace
    @State private var appeared = false

    var body: some View {
        VStack(spacing: 0) {
            header
                .frame(height: NotchMetrics.barHeight(model.geometry))
            tabStrip
                .padding(.top, 4)
            ZStack {
                page
                    .id(model.tab)
                    .transition(.tabSlide(forward: model.tabForward))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .padding(.top, 10)
            .clipped()
        }
        .padding(.horizontal, 14)
        .padding(.bottom, 12)
        .onAppear { appeared = true }
    }

    // MARK: Header (split around the camera housing on notched displays)

    private var header: some View {
        HStack(spacing: 0) {
            HStack(spacing: 7) {
                LionMascot(size: 20, mood: model.displayedMood)
                // A toast takes the title's place for a moment.
                Text(model.toast ?? model.tab.title)
                    .font(Theme.font(12, .semibold))
                    .foregroundStyle(model.toast != nil ? Theme.gold : Theme.ink)
                    .lineLimit(1)
                    .id(model.toast ?? model.tab.title)
                    .transition(.opacity.combined(with: .offset(y: 4)))
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity)
            .animation(Theme.soft, value: model.toast)

            Color.clear.frame(width: model.geometry.hasNotch ? model.geometry.notchWidth + 8 : 12)

            HStack(spacing: 4) {
                Spacer(minLength: 0)
                TimelineView(.everyMinute) { tl in
                    Text(tl.date, format: .dateTime.hour().minute())
                        .font(Theme.font(11, .medium))
                        .monospacedDigit()
                        .foregroundStyle(Theme.mist)
                        .padding(.trailing, 4)
                }
                IconButton(icon: model.pinned ? "pin.fill" : "pin", size: 22,
                           tint: model.pinned ? Theme.gold : Theme.mist,
                           help: model.pinned ? "Unpin · close when you move away" : "Keep the notch open") {
                    withAnimation(Theme.snappy) { model.pinned.toggle() }
                }
                IconButton(icon: "chevron.up", size: 22, tint: Theme.mist, help: "Collapse (Esc) · ⌥⌘I toggles") {
                    model.collapse()
                }
            }
            .frame(maxWidth: .infinity)
        }
    }

    // MARK: Tabs (icons only; the header names the open tab)

    private var tabStrip: some View {
        HStack(spacing: 2) {
            ForEach(NotchTab.allCases) { tab in
                TabButton(tab: tab, selected: model.tab == tab, namespace: tabSpace,
                          badge: badge(for: tab)) {
                    model.select(tab)
                }
                // Tabs drop in one after another as the notch opens.
                .opacity(appeared ? 1 : 0)
                .offset(y: appeared ? 0 : -8)
                .animation(Theme.liquid.delay(0.05 + Double(tab.index) * 0.025), value: appeared)
            }
        }
    }

    private func badge(for tab: NotchTab) -> Bool {
        switch tab {
        case .focus: return model.focus.isRunning
        case .music: return music.nowPlaying?.isPlaying ?? false
        case .tasks: return model.data.tasks.contains { !$0.done && ($0.due.map { $0 <= Date() } ?? false) }
        default: return false
        }
    }

    // MARK: Page

    @ViewBuilder
    private var page: some View {
        switch model.tab {
        case .focus: FocusPane(model: model)
        case .tasks: TasksPane(model: model)
        case .notes: NotesPane(model: model)
        case .capture: CapturePane(model: model)
        case .music: MusicPane(model: model, music: music)
        case .shelf: ShelfPane(model: model)
        case .water: WaterPane(model: model)
        case .settings: SettingsPane(model: model)
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
            ZStack(alignment: .topTrailing) {
                Image(systemName: tab.icon)
                    .font(.system(size: 13, weight: .medium))
                    .frame(maxWidth: .infinity)
                    .frame(height: 28)
                if badge {
                    Circle().fill(Theme.gold).frame(width: 4, height: 4).offset(x: -14, y: 6)
                }
            }
            .foregroundStyle(selected ? Theme.gold : (hovering ? Theme.ink : Theme.mist))
            .background {
                if selected {
                    Capsule().fill(Color.white.opacity(0.1))
                        .matchedGeometryEffect(id: "tab", in: namespace)
                } else if hovering {
                    Capsule().fill(Color.white.opacity(0.05))
                }
            }
        }
        .buttonStyle(PressableStyle(scale: 0.9))
        .help(tab.title)
        .accessibilityLabel(tab.title)
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}
