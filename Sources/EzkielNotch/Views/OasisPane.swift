import AppKit
import NotchCore
import SwiftUI

/// Oasis: water through the day, the last week, the goal and gentle reminders.
struct OasisPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        let today = model.waterToday
        let goal = model.settings.waterGoal
        let progress = model.waterProgress
        let week = WaterLog.lastDays(7, entries: model.data.water)
        let streak = WaterLog.streak(model.data.water, goal: goal)

        HStack(alignment: .top, spacing: Theme.Space.l) {
            // The wave only moves while there is something between empty and full.
            TimelineView(.animation(minimumInterval: 1.0 / 24, paused: progress <= 0 || progress >= 1)) { tl in
                WaterDrop(level: progress, t: tl.date.timeIntervalSinceReferenceDate)
            }
            .frame(width: 70, height: 92)
            .padding(.top, 4)

            VStack(alignment: .leading, spacing: Theme.Space.s) {
                HStack(alignment: .firstTextBaseline, spacing: 5) {
                    Text("\(today) ml")
                        .font(Theme.number(Theme.Size.title))
                        .foregroundStyle(Theme.ink)
                        .contentTransition(.numericText())
                        .animation(Theme.snappy, value: today)
                    Text(progress >= 1 ? "goal reached" : "of \(goal)")
                        .font(Theme.font(Theme.Size.secondary))
                        .foregroundStyle(progress >= 1 ? Accent.oasis.light : Theme.mist)
                    Spacer(minLength: 0)
                    if streak > 0 {
                        Text("\(streak)-day streak").font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.mist)
                    }
                }

                HStack(spacing: 5) {
                    ForEach([200, 350, 500], id: \.self) { ml in
                        PillButton(title: "+\(ml)", prominent: ml == 350, accent: .oasis) { model.addWater(ml) }
                    }
                    IconButton(icon: "arrow.uturn.backward", label: "Undo last drink", size: 28, tint: Theme.mist) {
                        model.undoWater()
                    }
                }

                WeekChart(week: week, goal: goal)
                    .frame(height: 34)

                HStack(spacing: 4) {
                    ForEach([1500, 2000, 2500, 3000], id: \.self) { g in
                        Chip(title: "\(g / 1000).\(g % 1000 / 100)L", selected: goal == g, accent: .oasis) {
                            model.settings.waterGoal = g
                        }
                    }
                    Spacer(minLength: 0)
                    Button {
                        withAnimation(Theme.snappy) { model.settings.waterReminders.toggle() }
                    } label: {
                        Label(model.settings.waterReminders ? "\(model.settings.waterIntervalMinutes)m" : "Off",
                              systemImage: model.settings.waterReminders ? "bell.fill" : "bell.slash")
                            .font(Theme.font(Theme.Size.caption, .medium))
                            .foregroundStyle(model.settings.waterReminders ? Accent.oasis.light : Theme.mist)
                            .padding(.horizontal, 8)
                            .frame(height: 24)
                            .background(Capsule().fill(Color.white.opacity(0.06)))
                    }
                    .buttonStyle(PressableStyle())
                    .help("Reminders between \(model.settings.activeHourStart):00 and \(model.settings.activeHourEnd):00")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
    }
}

/// A drop silhouette filled with a gently waving water level.
private struct WaterDrop: View {
    var level: Double
    var t: Double

    var body: some View {
        let moving = level > 0 && level < 1
        ZStack {
            DropShape().fill(Color.white.opacity(0.07))
            Wave(level: level, amplitude: moving ? 3 : 0, phase: t * 2.2)
                .fill(Accent.oasis.linear)
                .clipShape(DropShape())
            Wave(level: level, amplitude: moving ? 2 : 0, phase: t * 2.2 + 1.6)
                .fill(Color.white.opacity(0.14))
                .clipShape(DropShape())
            DropShape().stroke(Accent.oasis.light.opacity(0.35), lineWidth: 1)
        }
        .animation(.spring(response: 0.8, dampingFraction: 0.75), value: level)
    }
}

private struct DropShape: Shape {
    func path(in r: CGRect) -> Path {
        var p = Path()
        let w = r.width, h = r.height
        let radius = min(w / 2, h * 0.42)
        let cy = h - radius
        p.move(to: CGPoint(x: w / 2, y: 0))
        p.addCurve(to: CGPoint(x: w / 2 + radius, y: cy),
                   control1: CGPoint(x: w * 0.62, y: cy * 0.32), control2: CGPoint(x: w / 2 + radius, y: cy * 0.62))
        p.addArc(center: CGPoint(x: w / 2, y: cy), radius: radius, startAngle: .degrees(0), endAngle: .degrees(180), clockwise: false)
        p.addCurve(to: CGPoint(x: w / 2, y: 0),
                   control1: CGPoint(x: w / 2 - radius, y: cy * 0.62), control2: CGPoint(x: w * 0.38, y: cy * 0.32))
        p.closeSubpath()
        return p
    }
}

/// Level and amplitude both animate, so crossing empty or full never pops.
private struct Wave: Shape {
    var level: Double
    var amplitude: Double
    var phase: Double

    var animatableData: AnimatablePair<Double, Double> {
        get { AnimatablePair(level, amplitude) }
        set {
            level = newValue.first
            amplitude = newValue.second
        }
    }

    func path(in r: CGRect) -> Path {
        let y0 = r.height * (1 - max(0, min(1, level)) * 0.98)
        var p = Path()
        p.move(to: CGPoint(x: 0, y: r.height))
        p.addLine(to: CGPoint(x: 0, y: y0))
        let steps = 20
        for i in 0...steps {
            let x = r.width * Double(i) / Double(steps)
            let y = y0 + sin(Double(i) / Double(steps) * 2 * .pi * 1.3 + phase) * amplitude
            p.addLine(to: CGPoint(x: x, y: y))
        }
        p.addLine(to: CGPoint(x: r.width, y: r.height))
        p.closeSubpath()
        return p
    }
}

private struct WeekChart: View {
    var week: [(day: Date, ml: Int)]
    var goal: Int
    @State private var appeared = false

    var body: some View {
        let maxValue = Double(max(goal, week.map(\.ml).max() ?? 0, 1))
        GeometryReader { geo in
            let barArea = geo.size.height - 12
            ZStack(alignment: .bottomLeading) {
                Path { p in
                    let y = barArea * (1 - Double(goal) / maxValue)
                    p.move(to: CGPoint(x: 0, y: y))
                    p.addLine(to: CGPoint(x: geo.size.width, y: y))
                }
                .stroke(Color.white.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [2, 3]))

                HStack(alignment: .bottom, spacing: 0) {
                    ForEach(Array(week.enumerated()), id: \.offset) { index, entry in
                        let isToday = index == week.count - 1
                        VStack(spacing: 2) {
                            Capsule()
                                .fill(entry.ml > 0 ? AnyShapeStyle(Accent.oasis.linear) : AnyShapeStyle(Color.white.opacity(0.1)))
                                .opacity(isToday || entry.ml == 0 ? 1 : 0.6)
                                .frame(width: 7, height: max(3, appeared ? barArea * Double(entry.ml) / maxValue : 3))
                                .animation(.spring(response: 0.6, dampingFraction: 0.75).delay(Double(index) * 0.03), value: appeared)
                                .animation(Theme.smooth, value: entry.ml)
                            Text(entry.day.formatted(.dateTime.weekday(.narrow)))
                                .font(Theme.font(8, isToday ? .semibold : .regular))
                                .foregroundStyle(isToday ? Theme.ink : Theme.faint)
                                .frame(height: 10)
                        }
                        .frame(maxWidth: .infinity)
                        .help("\(entry.ml) ml")
                    }
                }
            }
        }
        .onAppear { appeared = true }
    }
}
