import AppKit
import NotchCore
import SwiftUI

struct WaterPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        let today = model.waterToday
        let goal = model.settings.waterGoal
        let progress = model.waterProgress
        let week = WaterLog.lastDays(7, entries: model.data.water)
        let streak = WaterLog.streak(model.data.water, goal: goal)

        HStack(spacing: 18) {
            // Animated water drop that fills up
            TimelineView(.animation(minimumInterval: 1.0 / 30)) { tl in
                WaterDrop(level: progress, t: tl.date.timeIntervalSinceReferenceDate)
            }
            .frame(width: 104, height: 128)
            .overlay(alignment: .bottom) {
                VStack(spacing: 0) {
                    Text("\(today)")
                        .font(.system(size: 22, weight: .heavy, design: .rounded))
                        .monospacedDigit()
                        .contentTransition(.numericText())
                    Text("of \(goal) ml")
                        .font(Theme.rounded(9.5, .semibold))
                        .opacity(0.85)
                }
                .foregroundStyle(.white)
                .shadow(color: .black.opacity(0.35), radius: 3)
                .padding(.bottom, 26)
            }

            VStack(alignment: .leading, spacing: 9) {
                HStack(alignment: .firstTextBaseline) {
                    Text(progress >= 1 ? "Goal reached, nice!" : "Make room for a sip.")
                        .font(Theme.rounded(16, .bold))
                        .foregroundStyle(Theme.ink)
                    Spacer()
                    if streak > 0 {
                        Label("\(streak)-day streak", systemImage: "flame.fill")
                            .font(Theme.rounded(10, .bold))
                            .foregroundStyle(Theme.orange)
                    }
                }

                HStack(spacing: 6) {
                    ForEach([200, 350, 500], id: \.self) { ml in
                        PillButton(title: "+\(ml)", icon: "drop.fill", prominent: ml == 350, tint: Color(hex: 0x7FD3FF)) {
                            model.addWater(ml)
                        }
                    }
                    IconButton(icon: "arrow.uturn.backward", size: 30, tint: Theme.mist, help: "Undo last drink") {
                        model.undoWater()
                    }
                }

                WeekChart(week: week, goal: goal)
                    .frame(height: 58)

                HStack(spacing: 5) {
                    Text("Goal").font(Theme.rounded(10, .semibold)).foregroundStyle(Theme.mist)
                    ForEach([1500, 2000, 2500, 3000], id: \.self) { g in
                        Chip(title: "\(g / 1000).\(g % 1000 / 100)L", selected: goal == g, tint: Color(hex: 0x7FD3FF)) {
                            model.settings.waterGoal = g
                        }
                    }
                    Spacer(minLength: 0)
                    Button {
                        withAnimation(Theme.snappy) { model.settings.waterReminders.toggle() }
                    } label: {
                        Label(model.settings.waterReminders ? "Every \(model.settings.waterIntervalMinutes)m" : "Reminder off",
                              systemImage: model.settings.waterReminders ? "bell.fill" : "bell.slash")
                            .font(Theme.rounded(10, .semibold))
                            .foregroundStyle(model.settings.waterReminders ? Theme.gold : Theme.mist)
                    }
                    .buttonStyle(PressableStyle())
                    .help("Gentle in-notch reminder between \(model.settings.activeHourStart):00 and \(model.settings.activeHourEnd):00")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 4)
        .frame(maxHeight: .infinity)
    }
}

/// A drop silhouette filled with a gently waving water level.
private struct WaterDrop: View {
    var level: Double
    var t: Double

    var body: some View {
        GeometryReader { geo in
            let rect = CGRect(origin: .zero, size: geo.size)
            ZStack {
                DropShape().fill(Color.white.opacity(0.07))
                Wave(level: level, phase: t * 2.2, amplitude: level > 0 && level < 1 ? 3.5 : 0)
                    .fill(Theme.waterGradient)
                    .clipShape(DropShape())
                    .animation(.spring(response: 0.8, dampingFraction: 0.7), value: level)
                Wave(level: level, phase: t * 2.2 + 1.6, amplitude: level > 0 && level < 1 ? 2.5 : 0)
                    .fill(Color.white.opacity(0.18))
                    .clipShape(DropShape())
                    .animation(.spring(response: 0.8, dampingFraction: 0.7), value: level)
                DropShape().stroke(Color(hex: 0x9FE7FF).opacity(0.45), lineWidth: 1.2)
            }
            .frame(width: rect.width, height: rect.height)
        }
    }
}

private struct DropShape: Shape {
    func path(in r: CGRect) -> Path {
        var p = Path()
        let w = r.width, h = r.height
        let radius = min(w / 2, h * 0.42)
        let cy = h - radius // bottom of the round part touches the frame
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

private struct Wave: Shape {
    var level: Double
    var phase: Double
    var amplitude: Double

    var animatableData: Double {
        get { level }
        set { level = newValue }
    }

    func path(in r: CGRect) -> Path {
        let y0 = r.height * (1 - max(0, min(1, level)) * 0.98)
        var p = Path()
        p.move(to: CGPoint(x: 0, y: r.height))
        p.addLine(to: CGPoint(x: 0, y: y0))
        let steps = 24
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
            let barArea = geo.size.height - 14
            ZStack(alignment: .bottomLeading) {
                // Goal line
                Path { p in
                    let y = barArea * (1 - Double(goal) / maxValue)
                    p.move(to: CGPoint(x: 0, y: y))
                    p.addLine(to: CGPoint(x: geo.size.width, y: y))
                }
                .stroke(Theme.gold.opacity(0.4), style: StrokeStyle(lineWidth: 1, dash: [2, 3]))

                HStack(alignment: .bottom, spacing: 0) {
                    ForEach(Array(week.enumerated()), id: \.offset) { index, entry in
                        let isToday = index == week.count - 1
                        VStack(spacing: 3) {
                            Capsule()
                                .fill(entry.ml >= goal ? AnyShapeStyle(Theme.tealGradient)
                                                       : AnyShapeStyle(LinearGradient(colors: [Color(hex: 0x9FE7FF), Color(hex: 0x4FA3E8)], startPoint: .top, endPoint: .bottom)))
                                .opacity(entry.ml > 0 ? (isToday ? 1 : 0.75) : 0.18)
                                .frame(width: 9, height: max(4, appeared ? barArea * Double(entry.ml) / maxValue : 4))
                                .animation(.spring(response: 0.7, dampingFraction: 0.6).delay(Double(index) * 0.04), value: appeared)
                                .animation(.spring(response: 0.6, dampingFraction: 0.65), value: entry.ml)
                            Text(entry.day.formatted(.dateTime.weekday(.narrow)))
                                .font(Theme.rounded(8.5, isToday ? .heavy : .medium))
                                .foregroundStyle(isToday ? Theme.gold : Theme.mist)
                                .frame(height: 11)
                        }
                        .frame(maxWidth: .infinity)
                    }
                }
            }
        }
        .onAppear { appeared = true }
    }
}
