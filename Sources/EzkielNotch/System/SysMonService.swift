import Darwin
import Foundation
import IOKit.ps
import NotchCore

/// System monitor: reads CPU, memory, network, disk and battery when asked (`sample`), never on its own.
/// AppModel asks every 2 s while the Den card or the closed pill shows, every 10 s while only alerts
/// listen, and not at all while it's off. Each reading is a handful of cheap kernel calls.
@MainActor
final class SysMonService: ObservableObject {
    struct Reading: Equatable {
        var cpu: Double?
        var memUsed: Double = 0
        var memTotal: Double = 0
        var pressure: MemoryPressure = .normal
        var down: Double?
        var up: Double?
        var online = true
        var diskFree: Double?
        var diskTotal: Double?
        var battery: Int?
        var charging = false
        var onAC = false
        /// Minutes to empty on battery, to full while charging; nil while macOS is still estimating.
        var batteryMinutes: Int?

        var memPercent: Double { memTotal > 0 ? memUsed / memTotal * 100 : 0 }
    }

    struct State: Equatable {
        var reading = Reading()
        var cpu = SysSeries(capacity: 60)
        var memory = SysSeries(capacity: 60)
        var down = SysSeries(capacity: 60)
        var up = SysSeries(capacity: 60)
        /// At least one full reading has arrived.
        var ready = false
    }

    @Published private(set) var state = State()

    nonisolated private static let host = mach_host_self()
    private var lastSample: Date?
    private var previousCPU: (busy: UInt64, idle: UInt64)?
    private var previousNet: (down: [String: UInt64], up: [String: UInt64], at: Date)?
    private var diskAt: Date?
    private var pressureSource: DispatchSourceMemoryPressure?
    private var simulated = false

    /// Due for a reading? 2 s while something shows it, 10 s for alerts only.
    func isDue(now: Date, fast: Bool) -> Bool {
        guard let last = lastSample else { return true }
        return now.timeIntervalSince(last) >= (fast ? 2 : 10) - 0.4
    }

    /// The monitor was switched on or off.
    func setRunning(_ on: Bool) {
        if on {
            guard pressureSource == nil else { return }
            // macOS says when memory pressure changes; the card's colour follows at once.
            let source = DispatchSource.makeMemoryPressureSource(eventMask: [.normal, .warning, .critical], queue: .main)
            source.setEventHandler { [weak self] in
                MainActor.assumeIsolated { self?.pressureChanged() }
            }
            source.resume()
            pressureSource = source
        } else {
            pressureSource?.cancel()
            pressureSource = nil
            lastSample = nil
            previousCPU = nil
            previousNet = nil
            diskAt = nil
            if !simulated { state = State() }
        }
    }

    /// One reading of everything. `battery` comes from BatteryMonitor (event-driven), `online` from the path monitor.
    @discardableResult
    func sample(now: Date = Date(), battery: BatteryState?, online: Bool?) -> Reading {
        guard !simulated else { return state.reading }
        lastSample = now
        var s = state
        var r = s.reading

        if let ticks = Self.cpuTicks() {
            if let prev = previousCPU {
                r.cpu = SysCPU.percent(previousBusy: prev.busy, previousIdle: prev.idle, busy: ticks.busy, idle: ticks.idle) ?? r.cpu
            }
            previousCPU = ticks
        }

        if let mem = Self.memory() {
            r.memUsed = mem.used
            r.memTotal = mem.total
        }
        r.pressure = Self.pressure()

        let net = Self.netCounters()
        if let prev = previousNet {
            let seconds = now.timeIntervalSince(prev.at)
            r.down = SysCounters.rate(previous: prev.down, current: net.down, seconds: seconds, bits: 32) ?? r.down
            r.up = SysCounters.rate(previous: prev.up, current: net.up, seconds: seconds, bits: 32) ?? r.up
        }
        previousNet = (net.down, net.up, now)
        r.online = online ?? !net.down.isEmpty

        // The disk changes slowly and the free-space query is the priciest call here: every 30 s.
        if diskAt == nil || now.timeIntervalSince(diskAt!) >= 30 {
            diskAt = now
            if let disk = Self.disk() {
                r.diskFree = disk.free
                r.diskTotal = disk.total
            }
        }

        r.battery = battery?.percent
        r.charging = battery?.isCharging ?? false
        r.onAC = battery?.onAC ?? false
        r.batteryMinutes = battery == nil ? nil : Self.batteryMinutes(charging: r.charging)

        s.reading = r
        if let cpu = r.cpu { s.cpu.append(cpu) }
        if r.memTotal > 0 { s.memory.append(r.memPercent) }
        if let down = r.down { s.down.append(down) }
        if let up = r.up { s.up.append(up) }
        s.ready = r.cpu != nil
        state = s
        return r
    }

    private func pressureChanged() {
        let level = Self.pressure()
        guard state.reading.pressure != level, !simulated else { return }
        state.reading.pressure = level
    }

    // MARK: - Self-test

    /// Fake readings and history for the self-test (real ones would make every run look different).
    func simulate(_ reading: Reading?, cpu: [Double] = [], memory: [Double] = [], down: [Double] = [], up: [Double] = []) {
        guard let reading else {
            simulated = false
            state = State()
            return
        }
        simulated = true
        var s = State()
        s.reading = reading
        cpu.forEach { s.cpu.append($0) }
        memory.forEach { s.memory.append($0) }
        down.forEach { s.down.append($0) }
        up.forEach { s.up.append($0) }
        s.ready = true
        state = s
    }

    // MARK: - Readers

    /// Busy and idle ticks for all CPUs together.
    nonisolated static func cpuTicks() -> (busy: UInt64, idle: UInt64)? {
        var info = host_cpu_load_info()
        var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info_data_t>.stride / MemoryLayout<integer_t>.stride)
        let kr = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { host_statistics(host, HOST_CPU_LOAD_INFO, $0, &count) }
        }
        guard kr == KERN_SUCCESS else { return nil }
        let t = info.cpu_ticks   // user, system, idle, nice
        return (UInt64(t.0) + UInt64(t.1) + UInt64(t.3), UInt64(t.2))
    }

    /// Used the way Activity Monitor counts it: app memory + wired + compressed.
    nonisolated static func memory() -> (used: Double, total: Double)? {
        var vm = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64_data_t>.stride / MemoryLayout<integer_t>.stride)
        let kr = withUnsafeMutablePointer(to: &vm) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { host_statistics64(host, HOST_VM_INFO64, $0, &count) }
        }
        guard kr == KERN_SUCCESS else { return nil }
        var pageSize: vm_size_t = 0
        host_page_size(host, &pageSize)
        let app = UInt64(vm.internal_page_count) - min(UInt64(vm.internal_page_count), UInt64(vm.purgeable_count))
        let pages = app + UInt64(vm.wire_count) + UInt64(vm.compressor_page_count)
        return (Double(pages) * Double(pageSize), Double(ProcessInfo.processInfo.physicalMemory))
    }

    nonisolated static func pressure() -> MemoryPressure {
        var level: Int32 = 1
        var size = MemoryLayout<Int32>.size
        guard sysctlbyname("kern.memorystatus_vm_pressure_level", &level, &size, nil, 0) == 0 else { return .normal }
        return MemoryPressure(sysctlLevel: Int(level))
    }

    /// Bytes in and out per interface (Wi-Fi, Ethernet, cellular). VPN tunnels are left out: their
    /// traffic already passes through one of these.
    nonisolated static func netCounters() -> (down: [String: UInt64], up: [String: UInt64]) {
        var down: [String: UInt64] = [:], up: [String: UInt64] = [:]
        var ifap: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifap) == 0, let first = ifap else { return (down, up) }
        defer { freeifaddrs(ifap) }
        var cursor: UnsafeMutablePointer<ifaddrs>? = first
        while let p = cursor {
            defer { cursor = p.pointee.ifa_next }
            let a = p.pointee
            guard let addr = a.ifa_addr, addr.pointee.sa_family == UInt8(AF_LINK), let data = a.ifa_data,
                  (a.ifa_flags & UInt32(IFF_UP)) != 0 else { continue }
            let name = String(cString: a.ifa_name)
            guard name.hasPrefix("en") || name.hasPrefix("pdp_ip") else { continue }
            let d = data.assumingMemoryBound(to: if_data.self).pointee
            down[name] = UInt64(d.ifi_ibytes)
            up[name] = UInt64(d.ifi_obytes)
        }
        return (down, up)
    }

    /// Free space the way Finder shows it (purgeable space counts as free).
    nonisolated static func disk() -> (free: Double, total: Double)? {
        let keys: Set<URLResourceKey> = [.volumeAvailableCapacityForImportantUsageKey, .volumeTotalCapacityKey]
        guard let v = try? URL(fileURLWithPath: "/").resourceValues(forKeys: keys),
              let free = v.volumeAvailableCapacityForImportantUsage else { return nil }
        return (Double(free), Double(v.volumeTotalCapacity ?? 0))
    }

    /// macOS's own estimate, in minutes: to empty on battery, to full while charging.
    nonisolated static func batteryMinutes(charging: Bool) -> Int? {
        guard let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let list = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef] else { return nil }
        for ps in list {
            guard let desc = IOPSGetPowerSourceDescription(info, ps)?.takeUnretainedValue() as? [String: Any],
                  (desc[kIOPSTypeKey] as? String) == kIOPSInternalBatteryType else { continue }
            let minutes = desc[charging ? kIOPSTimeToFullChargeKey : kIOPSTimeToEmptyKey] as? Int ?? -1
            return minutes > 0 ? minutes : nil
        }
        return nil
    }

    /// The process that used the most CPU over one second (for the CPU alert only: two quick passes over
    /// the process list). Processes of other users can't be read and are skipped.
    nonisolated static func topProcess() -> String? {
        func times() -> [pid_t: UInt64] {
            let count = proc_listpids(UInt32(PROC_ALL_PIDS), 0, nil, 0)
            guard count > 0 else { return [:] }
            var pids = [pid_t](repeating: 0, count: Int(count) / MemoryLayout<pid_t>.size + 16)
            let bytes = proc_listpids(UInt32(PROC_ALL_PIDS), 0, &pids, Int32(pids.count * MemoryLayout<pid_t>.size))
            var result: [pid_t: UInt64] = [:]
            for pid in pids.prefix(Int(bytes) / MemoryLayout<pid_t>.size) where pid > 0 {
                var info = proc_taskinfo()
                let size = Int32(MemoryLayout<proc_taskinfo>.size)
                if proc_pidinfo(pid, PROC_PIDTASKINFO, 0, &info, size) == size {
                    result[pid] = info.pti_total_user + info.pti_total_system
                }
            }
            return result
        }
        let before = times()
        Thread.sleep(forTimeInterval: 1)
        let after = times()
        guard let top = after.compactMap({ pid, t -> (pid_t, UInt64)? in
            guard let b = before[pid], t > b else { return nil }
            return (pid, t - b)
        }).max(by: { $0.1 < $1.1 }) else { return nil }
        var name = [CChar](repeating: 0, count: 256)
        guard proc_name(top.0, &name, UInt32(name.count)) > 0 else { return nil }
        let text = String(cString: name)
        return text.isEmpty ? nil : text
    }
}
