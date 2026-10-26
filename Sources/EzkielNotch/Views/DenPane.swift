import AppKit
import NotchCore
import SwiftUI

/// Den: the focus timer, with today's minutes, the streak and the last seven days.
struct DenPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        let focus = model.focus
        let isRest = focus.mode == .rest
        let accent: Accent = isRest ? .rest : .den
        let busy = focus.isRunning || focus.isPaused

        VStack(spacing: Theme.Space.m) {
            HStack(alignment: .top, spacing: Theme.Space.l) {
                TimelineView(.periodic(from: .now, by: 1)) { tl in
                    let remaining = focus.remaining(at: tl.date)
                    ZStack {
                        ProgressRing(progress: 1 - focus.progress(at: tl.date), lineWidth: 7, accent: accent)
                        VStack(spacing: 0) {
                            Text(FocusClock.format(remaining))
                                .font(Theme.number(remaining >= 3600 ? 17 : 20))
                                .foregroundStyle(Theme.ink)
                                .contentTransition(.numericText(countsDown: true))
                                .animation(Theme.snappy, value: Int(remaining))
                            Text(isRest ? L("break") : L("focus"))
                                .font(Theme.font(Theme.Size.caption, .medium))
                                .foregroundStyle(Theme.mist)
                        }
                    }
                }
                .frame(width: 92, height: 92)

                VStack(alignment: .leading, spacing: Theme.Space.s) {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(L(title))
                            .font(Theme.font(Theme.Size.title, .semibold))
                            .foregroundStyle(Theme.ink)
                        Text(L(subtitle))
                            .font(Theme.font(Theme.Size.secondary))
                            .foregroundStyle(Theme.mist)
                            .lineLimit(1)
                    }
                    HStack(spacing: Theme.Space.s) {
                        PillButton(title: focus.isRunning ? "Pause" : (focus.isPaused ? "Resume" : "Start"),
                                   icon: focus.isRunning ? "pause.fill" : "play.fill",
                                   prominent: true, accent: accent) {
                            model.focusStartPause()
                        }
                        .keyboardShortcut(.space, modifiers: [])
                        IconButton(icon: "arrow.counterclockwise", label: "Reset timer") { model.focusReset() }
                    }
                    HStack(spacing: 5) {
                        ForEach([15, 25, 45, 60], id: \.self) { minutes in
                            Chip(title: "\(minutes)m", selected: !isRest && model.settings.focusMinutes == minutes, accent: .den) {
                                model.setFocusMinutes(minutes)
                            }
                        }
                        Spacer(minLength: 4)
                        Chip(title: isRest ? "Focus" : L("%dm break", model.settings.breakMinutes), selected: isRest,
                             accent: .rest, disabled: busy) {
                            model.toggleBreak()
                        }
                        .help(busy ? "Available when the timer isn't running" : "Switch between focus and break")
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            stats
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
    }

    private var stats: some View {
        let week = FocusStats.lastDays(7, entries: model.data.focusLog)
        let peak = max(1, week.map(\.minutes).max() ?? 1)
        return HStack(spacing: Theme.Space.l) {
            stat(value: L("%d min", model.focusToday), label: L("today"))
            stat(value: model.focusStreak == 1 ? L("1 day") : L("%d days", model.focusStreak), label: L("streak"))
            Spacer(minLength: 0)
            HStack(alignment: .bottom, spacing: 4) {
                ForEach(Array(week.enumerated()), id: \.offset) { i, entry in
                    VStack(spacing: 2) {
                        Capsule()
                            .fill(entry.minutes > 0 ? AnyShapeStyle(Accent.den.linear) : AnyShapeStyle(Color.white.opacity(0.1)))
                            .frame(width: 6, height: max(3, 18 * CGFloat(entry.minutes) / CGFloat(peak)))
                        Text(entry.day.formatted(.dateTime.weekday(.narrow).locale(Lang.locale)))
                            .font(Theme.font(8, i == week.count - 1 ? .semibold : .regular))
                            .foregroundStyle(i == week.count - 1 ? Theme.ink : Theme.faint)
                    }
                    .help("\(entry.minutes) min")
                }
            }
            .frame(height: 30, alignment: .bottom)
        }
        .padding(.horizontal, Theme.Space.m)
        .frame(height: 40)
        .cardSurface()
    }

    private func stat(value: String, label: String) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(value).font(Theme.number(Theme.Size.body)).foregroundStyle(Theme.ink)
            Text(label).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist)
        }
    }

    private var title: String {
        let f = model.focus
        let minutes = Int(f.duration / 60)
        if f.isPaused { return "Paused" }
        if f.isRunning { return f.mode == .focus ? "In the Den" : "On a break" }
        return f.mode == .focus ? L("%d min focus", minutes) : L("%d min break", minutes)
    }

    private var subtitle: String {
        let f = model.focus
        if f.isRunning {
            let end = Date().addingTimeInterval(f.remaining())
            return L("Ends at %@", end.formatted(date: .omitted, time: .shortened))
        }
        return "Click the notch, then Space to start"
    }
}
