import NotchCore
import SwiftUI

/// The system monitor in Den: CPU, memory, network, battery (when there is one) and the startup disk,
/// each with its number and, for the ones that move, a sparkline of the last two minutes.
struct SysMonCard: View {
    @ObservedObject var sysmon: SysMonService

    var body: some View {
        let s = sysmon.state
        let r = s.reading
        HStack(spacing: 6) {
            tile(icon: "cpu", label: "CPU", value: r.cpu.map(SysFormat.percent) ?? "–",
                 detail: L("%d cores", ProcessInfo.processInfo.activeProcessorCount),
                 color: (r.cpu ?? 0) >= 90 ? Theme.berry : Theme.ink,
                 spark: s.cpu.normalized(floor: 100), accent: .den)
            tile(icon: "memorychip", label: "Memory", value: SysFormat.memory(r.memUsed),
                 detail: L("of %@", SysFormat.memory(r.memTotal)),
                 color: pressureColor(r.pressure), dot: pressureColor(r.pressure),
                 help: L("Memory pressure: %@", L(pressureName(r.pressure))),
                 spark: s.memory.normalized(floor: 100), accent: .stash)
            if r.online {
                tile(icon: "arrow.down.arrow.up", label: "Network", value: r.down.map(SysFormat.rate) ?? "–",
                     detail: "↑ " + (r.up.map(SysFormat.rate) ?? "–"), color: Theme.ink,
                     spark: s.down.normalized(floor: 50_000), accent: .oasis)
            } else {
                tile(icon: "wifi.slash", label: "Network", value: L("Offline"), detail: L("No connection"),
                     color: Theme.berry, spark: [], accent: .oasis)
            }
            if let pct = r.battery {
                tile(icon: r.charging ? "battery.100.bolt" : "battery.75", label: "Battery", value: "\(pct)%",
                     detail: batteryDetail(r), color: pct <= 20 && !r.onAC ? Theme.berry : Theme.ink, spark: [], accent: .roar)
            }
            if let free = r.diskFree {
                tile(icon: "internaldrive", label: "Disk", value: SysFormat.disk(free), detail: L("free"),
                     color: free < 5e9 ? Theme.berry : Theme.ink, spark: [], accent: .neutral)
            }
        }
        .frame(height: NotchMetrics.sysmonCard - Theme.Space.m)
        .accessibilityElement(children: .contain)
    }

    private func tile(icon: String, label: String, value: String, detail: String, color: Color, dot: Color? = nil,
                      help: String? = nil, spark: [Double], accent: Accent) -> some View {
        ZStack(alignment: .bottom) {
            VStack(alignment: .leading, spacing: 1) {
                HStack(spacing: 3) {
                    Image(systemName: icon).font(.system(size: 8.5, weight: .semibold))
                    Text(L(label)).font(Theme.font(9, .medium)).lineLimit(1)
                    Spacer(minLength: 0)
                    // The last two minutes, small beside the label, so it never sits on the numbers.
                    if spark.count > 1 {
                        Sparkline(values: spark)
                            .fill(LinearGradient(colors: [accent.deep.opacity(0.3), accent.deep.opacity(0.03)], startPoint: .top, endPoint: .bottom))
                            .overlay(Sparkline(values: spark, closed: false).stroke(accent.deep.opacity(0.85), lineWidth: 1))
                            .frame(width: 30, height: 9)
                            .allowsHitTesting(false)
                    }
                    if let dot { Circle().fill(dot).frame(width: 5, height: 5) }
                }
                .foregroundStyle(Theme.mist)
                Text(value)
                    .font(Theme.number(12.5, .semibold))
                    .foregroundStyle(color)
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
                Text(detail)
                    .font(Theme.font(9))
                    .foregroundStyle(Theme.faint)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
            }
            .padding(.horizontal, 7)
            .padding(.vertical, 5)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity)
        .cardSurface()
        .help(help ?? "\(L(label)): \(value)")
        .accessibilityElement(children: .combine)
    }

    private func batteryDetail(_ r: SysMonService.Reading) -> String {
        if r.charging { return r.batteryMinutes.map { L("%@ to full", SysFormat.minutes($0)) } ?? L("Charging") }
        if r.onAC { return L("On power") }
        return r.batteryMinutes.map { L("%@ left", SysFormat.minutes($0)) } ?? L("On battery")
    }

    private func pressureColor(_ p: MemoryPressure) -> Color {
        switch p {
        case .normal: return Theme.ink
        case .warning: return Accent.den.deep
        case .critical: return Theme.berry
        }
    }

    private func pressureName(_ p: MemoryPressure) -> String {
        switch p {
        case .normal: return "normal"
        case .warning: return "high"
        case .critical: return "critical"
        }
    }
}

/// A sparkline through 0…1 values, left to right; closed along the bottom for the fill.
struct Sparkline: Shape {
    var values: [Double]
    var closed = true

    func path(in rect: CGRect) -> Path {
        var p = Path()
        guard values.count > 1 else { return p }
        let step = rect.width / CGFloat(values.count - 1)
        for (i, v) in values.enumerated() {
            let pt = CGPoint(x: rect.minX + CGFloat(i) * step, y: rect.maxY - CGFloat(v) * rect.height * 0.92)
            i == 0 ? p.move(to: pt) : p.addLine(to: pt)
        }
        if closed {
            p.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY))
            p.addLine(to: CGPoint(x: rect.minX, y: rect.maxY))
            p.closeSubpath()
        }
        return p
    }
}

/// The closed notch's system pill: CPU % or the network speed (the busier direction).
struct SysMonPill: View {
    @ObservedObject var sysmon: SysMonService
    var kind: SysPill

    var body: some View {
        let r = sysmon.state.reading
        HStack(spacing: 2) {
            switch kind {
            case .network where !r.online:
                Image(systemName: "wifi.slash").font(.system(size: 9.5, weight: .semibold))
            case .network:
                let down = r.down ?? 0, up = r.up ?? 0
                Image(systemName: up > down ? "arrow.up" : "arrow.down").font(.system(size: 8, weight: .bold))
                Text(SysFormat.compactRate(max(down, up))).font(Theme.number(10.5, .semibold))
            default:
                Image(systemName: "cpu").font(.system(size: 8.5, weight: .semibold))
                Text(r.cpu.map(SysFormat.percent) ?? "–").font(Theme.number(10.5, .semibold))
            }
        }
        .lineLimit(1)
        .minimumScaleFactor(0.7)
        .foregroundStyle(color(r))
        .help(help(r))
    }

    private func color(_ r: SysMonService.Reading) -> Color {
        switch kind {
        case .network: return r.online ? Accent.oasis.light : Theme.berry
        default: return (r.cpu ?? 0) >= 90 ? Theme.berry : Accent.den.light
        }
    }

    private func help(_ r: SysMonService.Reading) -> String {
        switch kind {
        case .network:
            guard r.online else { return L("Offline") }
            return "↓ \(r.down.map(SysFormat.rate) ?? "–")  ↑ \(r.up.map(SysFormat.rate) ?? "–")"
        default:
            return "CPU \(r.cpu.map(SysFormat.percent) ?? "–")"
        }
    }
}
