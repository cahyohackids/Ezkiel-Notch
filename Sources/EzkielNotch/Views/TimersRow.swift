import NotchCore
import SwiftUI

/// Den's quick timers, under the focus stats: type "10m teh" or "1h30m" (or tap a preset) to start one; the running
/// ones count down beside it, each with its own ×. Several can run; the closed notch counts down the soonest.
struct TimersRow: View {
    @ObservedObject var model: AppModel
    @State private var draft = ""

    var body: some View {
        let timers = model.runningTimers
        HStack(spacing: 6) {
            Image(systemName: "timer")
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(Accent.den.linear)
                .help(L("Quick timers"))
            NotchField(placeholder: "Timer: 10m teh, 1h30m…", text: $draft, onSubmit: start)
                .frame(width: timers.isEmpty ? 190 : 150)
            if timers.isEmpty {
                ForEach([5, 10, 30], id: \.self) { m in
                    Chip(title: "\(m)m", selected: false, accent: .den) { model.startTimer("", seconds: m * 60) }
                }
                Spacer(minLength: 0)
            } else {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 5) {
                        ForEach(timers) { t in
                            TimerChip(timer: t) { model.cancelTimer(t.id) }
                                .transition(.scale.combined(with: .opacity))
                        }
                    }
                }
            }
        }
        .frame(height: 28)
        .animation(Theme.snappy, value: timers.map(\.id))
    }

    private func start() {
        let text = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return }
        if model.startTimer(text) {
            draft = ""
        } else {
            model.showToast(L("Type a length, like 10m or 1h30m"))
        }
    }
}

/// One running timer: its name and what's left, and × to stop it.
private struct TimerChip: View {
    var timer: QuickTimer
    var cancel: () -> Void

    var body: some View {
        HStack(spacing: 4) {
            Text(AppModel.timerName(timer))
                .font(Theme.font(Theme.Size.secondary, .medium))
                .foregroundStyle(Theme.ink)
                .lineLimit(1)
                .truncationMode(.tail)
                .frame(maxWidth: 70, alignment: .leading)
            TimelineView(.periodic(from: .now, by: 1)) { tl in
                Text(FocusClock.format(timer.remaining(at: tl.date)))
                    .font(Theme.number(Theme.Size.secondary, .semibold))
                    .foregroundStyle(Accent.den.linear)
            }
            Button(action: cancel) {
                Image(systemName: "xmark").font(.system(size: 8, weight: .bold)).foregroundStyle(Theme.mist)
                    .frame(width: 14, height: 14)
                    .background(Circle().fill(Color.white.opacity(0.08)))
            }
            .buttonStyle(PressableStyle(scale: 0.85))
            .help(L("Stop timer"))
            .accessibilityLabel(L("Stop timer"))
        }
        .padding(.leading, 9)
        .padding(.trailing, 5)
        .frame(height: 24)
        .background(Capsule().fill(Color.white.opacity(0.07)))
        .accessibilityElement(children: .combine)
    }
}
