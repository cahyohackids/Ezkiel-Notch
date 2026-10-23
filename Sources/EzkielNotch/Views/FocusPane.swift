import AppKit
import NotchCore
import SwiftUI

struct FocusPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        let focus = model.focus
        let isRest = focus.mode == .rest
        let tint = isRest ? Theme.teal : Theme.gold

        HStack(spacing: 22) {
            TimelineView(.animation(minimumInterval: 1.0 / 20, paused: !focus.isRunning)) { tl in
                let remaining = focus.remaining(at: tl.date)
                let progress = 1 - focus.progress(at: tl.date)
                ZStack {
                    // Soft pulse behind the ring while running
                    Circle()
                        .fill(RadialGradient(colors: [tint.opacity(focus.isRunning ? 0.22 : 0.1), .clear],
                                             center: .center, startRadius: 10, endRadius: 86))
                        .scaleEffect(focus.isRunning ? 1 + 0.04 * sin(tl.date.timeIntervalSinceReferenceDate * 2) : 1)
                    GlowRing(progress: progress, lineWidth: 9,
                             colors: isRest ? [Theme.teal, Theme.teal2, Theme.teal] : [Theme.gold, Theme.orange, Theme.ember, Theme.gold],
                             glow: isRest ? Theme.teal : Theme.orange)
                        .padding(14)
                    VStack(spacing: 2) {
                        Text(FocusClock.format(remaining))
                            .font(.system(size: 30, weight: .bold, design: .rounded))
                            .monospacedDigit()
                            .foregroundStyle(Theme.ink)
                            .contentTransition(.numericText())
                        Text(isRest ? "BREAK" : "FOCUS")
                            .font(Theme.rounded(9, .heavy))
                            .tracking(1.6)
                            .foregroundStyle(tint)
                    }
                }
            }
            .frame(width: 168, height: 168)

            VStack(alignment: .leading, spacing: 12) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(title)
                        .font(Theme.rounded(19, .bold))
                        .foregroundStyle(Theme.ink)
                        .contentTransition(.opacity)
                    Text(subtitle)
                        .font(Theme.rounded(11.5))
                        .foregroundStyle(Theme.mist)
                }

                HStack(spacing: 8) {
                    PillButton(title: focus.isRunning ? "Pause" : (focus.isPaused ? "Resume" : "Start"),
                               icon: focus.isRunning ? "pause.fill" : "play.fill",
                               prominent: true, tint: tint) {
                        model.focusStartPause()
                    }
                    .keyboardShortcut(.space, modifiers: [])
                    IconButton(icon: "arrow.counterclockwise", size: 30, help: "Reset timer") {
                        model.focusReset()
                    }
                }

                HStack(spacing: 6) {
                    ForEach([15, 25, 45, 60], id: \.self) { minutes in
                        Chip(title: "\(minutes)m",
                             selected: !isRest && Int(focus.duration / 60) == minutes) {
                            model.setFocusMinutes(minutes)
                        }
                    }
                    Spacer(minLength: 4)
                    Chip(title: isRest ? "Back to focus" : "\(model.settings.breakMinutes)m break",
                         selected: isRest, tint: Theme.teal) {
                        model.toggleBreak()
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 6)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var title: String {
        let f = model.focus
        if f.isRunning { return f.mode == .focus ? "Stay with it" : "Breathe out" }
        if f.isPaused { return "Paused" }
        return f.mode == .focus ? "Ready when you are" : "Time for a break"
    }

    private var subtitle: String {
        let f = model.focus
        if f.isRunning {
            let end = Date().addingTimeInterval(f.remaining())
            return "Ends at \(end.formatted(date: .omitted, time: .shortened)) · Ezkiel guards your focus"
        }
        return f.mode == .focus ? "Pick a length and press Start" : "Stretch, sip some water, look away"
    }
}
