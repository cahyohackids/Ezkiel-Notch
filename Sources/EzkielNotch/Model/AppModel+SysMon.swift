import AppKit
import NotchCore
import SwiftUI

/// System monitor: when to read, and the peeks it sends. Lost and regained connections are Whiskers'
/// peek (Wi-Fi & headphones), and a low battery is the battery peek; neither is repeated here.
extension AppModel {
    /// The Den card is on screen.
    var sysmonCardShowing: Bool { settings.sysmon && presentation == .expanded && tab == .den }

    /// The closed notch shows the CPU or network pill.
    var sysmonPillShowing: Bool {
        settings.sysmon && settings.sysmonPill != .off && presentation == .compact && !hiddenForFullscreen && !inFullscreenCalm
    }

    var sysmonAlertsOn: Bool {
        settings.sysmon && (settings.sysmonCpuAlert || settings.sysmonMemoryAlert || settings.sysmonDiskAlert)
    }

    /// From the 1 s tick: read every 2 s while the card or the pill shows, every 10 s while only alerts
    /// listen, never while the monitor is off.
    func sysmonTick(now: Date) {
        let fast = sysmonCardShowing || sysmonPillShowing
        guard settings.sysmon, fast || sysmonAlertsOn, sysmon.isDue(now: now, fast: fast) else { return }
        let reading = sysmon.sample(now: now, battery: battery.state, online: networkOnline)
        checkSysAlerts(reading, now: now)
    }

    /// The monitor was switched on or off.
    func sysmonSettingsChanged(old: NotchSettings?) {
        guard old?.sysmon != settings.sysmon else { return }
        sysmon.setRunning(settings.sysmon)
        if !settings.sysmon { sysAlerts.reset() }
    }

    private func checkSysAlerts(_ r: SysMonService.Reading, now: Date) {
        let t = now.timeIntervalSinceReferenceDate
        if settings.sysmonCpuAlert, let cpu = r.cpu, sysAlerts.feed("cpu", value: cpu, at: t) {
            cpuAlert(cpu)
        }
        if settings.sysmonMemoryAlert, r.memTotal > 0,
           sysAlerts.feed("memory", value: SysMemory.alertLoad(usedPercent: r.memPercent, pressure: r.pressure), at: t) {
            showPeek(Peek(icon: "memorychip", title: r.pressure == .critical ? "Memory pressure is critical" : "Memory pressure is high",
                          subtitle: "Quit apps you aren't using", tint: Theme.berry, tab: .den, mood: .awake,
                          value: SysFormat.percent(r.memPercent)))
        }
        if settings.sysmonDiskAlert, let free = r.diskFree, sysAlerts.feed("disk", value: free / 1e9, at: t) {
            showPeek(Peek(icon: "internaldrive", title: "Startup disk almost full",
                          subtitle: L("%@ free", SysFormat.disk(free)), tint: Theme.berry, tab: .den, mood: .awake, sound: "Funk"))
        }
    }

    /// The busiest app takes a second to find (two passes over the process list), off the main thread.
    private func cpuAlert(_ cpu: Double) {
        Task.detached(priority: .utility) { [weak self] in
            let top = SysMonService.topProcess()
            await self?.showCpuPeek(cpu, top: top)
        }
    }

    func showCpuPeek(_ cpu: Double, top: String?) {
        showPeek(Peek(icon: "cpu", title: "CPU is busy", subtitle: top.map { L("Mostly %@", $0) } ?? "Above 90% for 30 seconds",
                      tint: Accent.den.deep, tab: .den, mood: .awake, value: SysFormat.percent(cpu)))
    }
}
