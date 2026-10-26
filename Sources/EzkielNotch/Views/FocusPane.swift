import AppKit
import NotchCore
import SwiftUI

struct FocusPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        let focus = model.focus
        let isRest = focus.mode == .rest
        let tint = isRest ? Theme.teal : Theme.gold

        HStack(spacing: 20) {
            TimelineView(.animation(minimumInterval: 1.0 / 20, paused: !focus.isRunning)) { tl in
                let remaining = focus.remaining(at: tl.date)
                let progress = 1 - focus.progress(at: tl.date)
                ZStack {
                    ProgressRing(progress: progress, lineWidth: 6, color: tint)
                    VStack(spacing: 0) {
                        Text(FocusClock.format(remaining))
                            .font(.system(size: 24, weight: .semibold))
                            .monospacedDigit()
                            .foregroundStyle(Theme.ink)
                            .contentTransition(.numericText())
                        Text(isRest ? "Break" : "Focus")
                            .font(Theme.font(10, .medium))
                            .foregroundStyle(Theme.mist)
                    }
                }
            }
            .frame(width: 112, height: 112)

            VStack(alignment: .leading, spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                        .font(Theme.font(15, .semibold))
                        .foregroundStyle(Theme.ink)
                        .contentTransition(.opacity)
                    Text(subtitle)
                        .font(Theme.font(11))
                        .foregroundStyle(Theme.mist)
                }

                HStack(spacing: 8) {
                    PillButton(title: focus.isRunning ? "Pause" : (focus.isPaused ? "Resume" : "Start"),
                               icon: focus.isRunning ? "pause.fill" : "play.fill",
                               prominent: true, tint: tint) {
                        model.focusStartPause()
                    }
                    .keyboardShortcut(.space, modifiers: [])
                    IconButton(icon: "arrow.counterclockwise", size: 28, help: "Reset timer") {
                        model.focusReset()
                    }
                }

                HStack(spacing: 5) {
                    ForEach([15, 25, 45, 60], id: \.self) { minutes in
                        Chip(title: "\(minutes)m",
                             selected: !isRest && Int(focus.duration / 60) == minutes) {
                            model.setFocusMinutes(minutes)
                        }
                    }
                    Spacer(minLength: 4)
                    Chip(title: isRest ? "Focus" : "\(model.settings.breakMinutes)m break",
                         selected: isRest, tint: Theme.teal) {
                        model.toggleBreak()
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 4)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var title: String {
        let f = model.focus
        let minutes = Int(f.duration / 60)
        if f.isRunning || f.isPaused {
            return f.isPaused ? "Paused" : (f.mode == .focus ? "Focusing" : "On a break")
        }
        return f.mode == .focus ? "\(minutes) min focus" : "\(minutes) min break"
    }

    private var subtitle: String {
        let f = model.focus
        if f.isRunning {
            let end = Date().addingTimeInterval(f.remaining())
            return "Ends at \(end.formatted(date: .omitted, time: .shortened))"
        }
        return "Space starts and pauses"
    }
}
