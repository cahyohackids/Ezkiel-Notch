import AppKit
import NotchCore
import SwiftUI

struct ExpandedView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    @Namespace private var tabSpace

    var body: some View {
        VStack(spacing: 0) {
            header
                .frame(height: NotchMetrics.barHeight(model.geometry))
            tabStrip
                .padding(.top, 6)
            ZStack {
                page
                    .id(model.tab)
                    .transition(.tabSlide(forward: model.tabForward))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .padding(.top, 10)
            .clipped()
            footer
                .frame(height: 22)
        }
        .padding(.horizontal, 18)
        .padding(.bottom, 6)
    }

    // MARK: Header (split around the camera housing on notched displays)

    private var header: some View {
        HStack(spacing: 0) {
            HStack(spacing: 8) {
                LionMascot(size: 24, mood: model.displayedMood)
                VStack(alignment: .leading, spacing: 0) {
                    Text("EZKIEL NOTCH")
                        .font(Theme.rounded(9.5, .heavy))
                        .tracking(1.6)
                        .foregroundStyle(Theme.lionGradient)
                    Text(headerSubtitle)
                        .font(Theme.rounded(11, .semibold))
                        .foregroundStyle(Theme.ink.opacity(0.9))
                        .lineLimit(1)
                        .contentTransition(.opacity)
                }
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity)

            Color.clear.frame(width: model.geometry.hasNotch ? model.geometry.notchWidth + 8 : 12)

            HStack(spacing: 7) {
                Spacer(minLength: 0)
                TimelineView(.everyMinute) { tl in
                    Text(tl.date, format: .dateTime.weekday(.abbreviated).day().hour().minute())
                        .font(Theme.rounded(11, .semibold))
                        .monospacedDigit()
                        .foregroundStyle(Theme.mist)
                        .lineLimit(1)
                }
                IconButton(icon: model.pinned ? "pin.fill" : "pin", size: 24,
                           tint: model.pinned ? Theme.gold : Theme.mist,
                           help: model.pinned ? "Unpin · close when you move away" : "Keep the notch open") {
                    withAnimation(Theme.snappy) { model.pinned.toggle() }
                }
                IconButton(icon: "chevron.up", size: 24, tint: Theme.mist, help: "Collapse (Esc)") {
                    model.collapse()
                }
            }
            .frame(maxWidth: .infinity)
        }
    }

    private var headerSubtitle: String {
        if model.tab == .focus && model.focus.isRunning {
            return model.focus.mode == .focus ? "In your flow" : "Taking a breather"
        }
        if model.tab == .music, let np = music.nowPlaying, np.isPlaying { return "Now playing" }
        return model.tab.title
    }

    // MARK: Tabs

    private var tabStrip: some View {
        HStack(spacing: 2) {
            ForEach(NotchTab.allCases) { tab in
                TabButton(tab: tab, selected: model.tab == tab, namespace: tabSpace,
                          badge: badge(for: tab)) {
                    model.select(tab)
                }
            }
        }
        .padding(3)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Theme.surface))
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

    // MARK: Footer

    private var footer: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(model.toast != nil ? Theme.gold : Theme.teal)
                .frame(width: 5, height: 5)
                .shadow(color: (model.toast != nil ? Theme.gold : Theme.teal).opacity(0.8), radius: 3)
            Text(model.toast ?? (model.pinned ? "Pinned open · your workspace is ready" : "Move away to tuck the notch back · ⌥⌘I"))
                .font(Theme.rounded(10, .medium))
                .foregroundStyle(model.toast != nil ? Theme.ink : Theme.mist)
                .lineLimit(1)
                .id(model.toast ?? "hint")
                .transition(.opacity.combined(with: .offset(y: 4)))
            Spacer()
            Text("ezkielyna.store · \(AppModel.version)")
                .font(.system(size: 9, weight: .semibold, design: .monospaced))
                .foregroundStyle(Theme.mist.opacity(0.6))
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
            VStack(spacing: 4) {
                ZStack(alignment: .topTrailing) {
                    Image(systemName: tab.icon)
                        .font(.system(size: 13, weight: selected ? .bold : .medium))
                        .scaleEffect(selected ? 1.1 : (hovering ? 1.05 : 1))
                    if badge {
                        Circle().fill(Theme.orange).frame(width: 5, height: 5).offset(x: 5, y: -2)
                    }
                }
                Text(tab.title)
                    .font(Theme.rounded(9.5, selected ? .bold : .medium))
                    .lineLimit(1)
            }
            .foregroundStyle(selected ? Theme.gold : (hovering ? Theme.ink : Theme.mist))
            .frame(maxWidth: .infinity)
            .frame(height: 42)
            .background {
                if selected {
                    RoundedRectangle(cornerRadius: 13, style: .continuous)
                        .fill(Theme.gold.opacity(0.12))
                        .overlay(RoundedRectangle(cornerRadius: 13, style: .continuous).strokeBorder(Theme.gold.opacity(0.35), lineWidth: 1))
                        .matchedGeometryEffect(id: "tab", in: namespace)
                } else if hovering {
                    RoundedRectangle(cornerRadius: 13, style: .continuous).fill(Color.white.opacity(0.05))
                }
            }
        }
        .buttonStyle(PressableStyle(scale: 0.9))
        .help(tab.title)
        .accessibilityLabel(tab.title)
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}
