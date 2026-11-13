import Foundation

// System monitor: the platform-free half (series, rates from counters, alert rules, formatting).
// The Windows app has the same logic in EzkielNotch.Core/SysMon.cs; both test suites read
// Tests/fixtures/sysmon-alerts.json, so the two apps format and alert the same way.

/// What the closed notch shows while the system monitor is on.
public enum SysPill: String, Codable, CaseIterable, Identifiable {
    case off, cpu, network
    public var id: String { rawValue }
}

/// macOS memory pressure (Windows has none and passes nil).
public enum MemoryPressure: Int, Codable {
    case normal, warning, critical

    /// kern.memorystatus_vm_pressure_level: 1 normal, 2 warning, 4 critical.
    public init(sysctlLevel: Int) {
        switch sysctlLevel {
        case 4: self = .critical
        case 2: self = .warning
        default: self = .normal
        }
    }
}

/// The last `capacity` samples, oldest first (for sparklines).
public struct SysSeries: Equatable {
    public let capacity: Int
    public private(set) var values: [Double] = []

    public init(capacity: Int = 60) { self.capacity = max(1, capacity) }

    public mutating func append(_ value: Double) {
        values.append(value)
        if values.count > capacity { values.removeFirst(values.count - capacity) }
    }

    public mutating func clear() { values.removeAll() }

    public var last: Double? { values.last }
    public var peak: Double { values.max() ?? 0 }

    /// 0…1 for drawing, scaled to the peak but never to less than `floor` (so idle traffic stays flat).
    public func normalized(floor: Double) -> [Double] {
        let top = max(peak, floor)
        guard top > 0 else { return values.map { _ in 0 } }
        return values.map { min(1, max(0, $0 / top)) }
    }
}

public enum SysCounters {
    /// Bytes moved between two readings of one counter. A counter that went backwards wrapped (32-bit
    /// counters near the top) or was reset (an interface came back): a reset gives nil.
    public static func delta(previous: UInt64, current: UInt64, bits: Int) -> UInt64? {
        if current >= previous { return current - previous }
        guard bits == 32, previous <= UInt64(UInt32.max), previous >= 3_221_225_472 else { return nil }
        let wrapped = (UInt64(UInt32.max) - previous) + 1 + current
        return wrapped <= 2_147_483_648 ? wrapped : nil
    }

    /// Bytes per second across interfaces, by name. Interfaces that appear, vanish or reset add nothing.
    public static func rate(previous: [String: UInt64], current: [String: UInt64], seconds: Double, bits: Int) -> Double? {
        guard seconds > 0 else { return nil }
        var total: UInt64 = 0
        for (name, now) in current {
            guard let before = previous[name], let d = delta(previous: before, current: now, bits: bits) else { continue }
            total += d
        }
        return Double(total) / seconds
    }
}

public enum SysCPU {
    /// CPU busy % between two tick readings (busy = user + system [+ nice], idle). Nil when the counters
    /// went backwards or nothing passed.
    public static func percent(previousBusy: UInt64, previousIdle: UInt64, busy: UInt64, idle: UInt64) -> Double? {
        guard busy >= previousBusy, idle >= previousIdle else { return nil }
        let b = Double(busy - previousBusy), i = Double(idle - previousIdle)
        guard b + i > 0 else { return nil }
        return min(100, max(0, b / (b + i) * 100))
    }
}

public enum SysMemory {
    /// The number the memory alert watches. With pressure (macOS) it alone decides: RAM that looks full is
    /// normal there (cache, compression). Without it (Windows), the share of RAM in use.
    public static func alertLoad(usedPercent: Double, pressure: MemoryPressure?) -> Double {
        switch pressure {
        case .normal?: return 0
        case .warning?: return 95
        case .critical?: return 100
        case nil: return usedPercent
        }
    }
}

/// One alert: fires when the value has stayed past `on` for `sustain` seconds; quiet again until it
/// comes back past `off` (hysteresis), and never twice within `cooldown`.
public struct SysAlertRule: Equatable {
    public var id: String
    public var above: Bool
    public var on: Double
    public var off: Double
    public var sustain: Double
    public var cooldown: Double

    public init(id: String, above: Bool, on: Double, off: Double, sustain: Double, cooldown: Double) {
        self.id = id
        self.above = above
        self.on = on
        self.off = off
        self.sustain = sustain
        self.cooldown = cooldown
    }

    /// CPU above 90 % for 30 s; again after it drops under 70 %, at most every 15 min.
    public static let cpu = SysAlertRule(id: "cpu", above: true, on: 90, off: 70, sustain: 30, cooldown: 900)
    /// Memory (alertLoad) at 90 or more for 20 s; again under 80, at most every 30 min.
    public static let memory = SysAlertRule(id: "memory", above: true, on: 90, off: 80, sustain: 20, cooldown: 1800)
    /// Free space on the system drive under 5 GB; again once it's 6 GB or more, at most every 6 h.
    public static let disk = SysAlertRule(id: "disk", above: false, on: 5, off: 6, sustain: 0, cooldown: 21_600)
}

/// The alert rules together: no two system peeks within `spacing` seconds, and a pause in sampling
/// longer than `maxGap` starts the sustain over.
public struct SysAlerts {
    public static let defaultRules: [SysAlertRule] = [.cpu, .memory, .disk]
    public let rules: [String: SysAlertRule]
    public let spacing: Double
    public let maxGap: Double

    private struct State {
        var armed = true
        var since: Double?
        var lastFired: Double?
        var lastSeen: Double?
    }

    private var states: [String: State] = [:]
    private var lastAny: Double?

    public init(rules: [SysAlertRule] = SysAlerts.defaultRules, spacing: Double = 120, maxGap: Double = 25) {
        self.rules = Dictionary(rules.map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a })
        self.spacing = spacing
        self.maxGap = maxGap
    }

    /// A sample for one rule at time `t` (seconds). True when its peek should show now.
    public mutating func feed(_ id: String, value: Double, at t: Double) -> Bool {
        guard let rule = rules[id] else { return false }
        var s = states[id] ?? State()
        defer { states[id] = s }
        if let seen = s.lastSeen, t - seen > maxGap { s.since = nil }
        s.lastSeen = t
        let hit = rule.above ? value >= rule.on : value < rule.on
        if !s.armed {
            let released = rule.above ? value < rule.off : value >= rule.off
            if released { s.armed = true }
            s.since = nil
            return false
        }
        guard hit else {
            s.since = nil
            return false
        }
        let since = s.since ?? t
        s.since = since
        guard t - since >= rule.sustain else { return false }
        if let last = s.lastFired, t - last < rule.cooldown { return false }
        if let any = lastAny, t - any < spacing { return false }
        s.lastFired = t
        s.armed = false
        s.since = nil
        lastAny = t
        return true
    }

    /// Forget everything (the monitor was switched off).
    public mutating func reset() {
        states = [:]
        lastAny = nil
    }
}

/// Numbers as the card and the pill show them. Always "." and rounded half away from zero, so the Mac
/// and Windows print the same thing.
public enum SysFormat {
    private static func roundAway(_ x: Double) -> Int64 { Int64(x.rounded(.toNearestOrAwayFromZero)) }

    /// "4.2" (one decimal); with `trimZero`, "8" instead of "8.0".
    private static func oneDecimal(_ x: Double, trimZero: Bool = false) -> String {
        let r = roundAway(x * 10)
        if trimZero && r % 10 == 0 { return "\(r / 10)" }
        return "\(r / 10).\(r % 10)"
    }

    /// "34%".
    public static func percent(_ value: Double) -> String {
        "\(min(100, max(0, roundAway(value))))%"
    }

    /// Network speed, decimal units: "0 KB/s", "4.2 KB/s", "820 KB/s", "1.2 MB/s", "12 MB/s", "1.1 GB/s".
    public static func rate(_ bytesPerSecond: Double) -> String {
        scaled(bytesPerSecond, units: ["KB/s", "MB/s", "GB/s"], space: true)
    }

    /// The closed pill's short form: "0K", "4.2K", "820K", "1.2M", "12M", "1.1G".
    public static func compactRate(_ bytesPerSecond: Double) -> String {
        scaled(bytesPerSecond, units: ["K", "M", "G"], space: false)
    }

    private static func scaled(_ bytesPerSecond: Double, units: [String], space: Bool) -> String {
        let v = max(0, bytesPerSecond.isFinite ? bytesPerSecond : 0)
        let sep = space ? " " : ""
        if v < 50 { return "0\(sep)\(units[0])" }
        var x = v / 1000
        for (i, unit) in units.enumerated() {
            let lastUnit = i == units.count - 1
            if x < 9.95 { return "\(oneDecimal(x))\(sep)\(unit)" }
            if x < 999.5 || lastUnit { return "\(roundAway(x))\(sep)\(unit)" }
            x /= 1000
        }
        return "0\(sep)\(units[0])"
    }

    /// Memory, binary units like the RAM on the box: "512 MB", "6.1 GB", "8 GB", "128 GB".
    public static func memory(_ bytes: Double) -> String {
        let gib = max(0, bytes) / 1_073_741_824
        if gib < 0.995 { return "\(roundAway(max(0, bytes) / 1_048_576)) MB" }
        if gib < 99.95 { return "\(oneDecimal(gib, trimZero: true)) GB" }
        return "\(roundAway(gib)) GB"
    }

    /// Disk space, decimal units like Finder: "850 MB", "4.2 GB", "120 GB", "1.2 TB".
    public static func disk(_ bytes: Double) -> String {
        let b = max(0, bytes)
        let gb = b / 1e9
        if gb < 0.995 { return "\(roundAway(b / 1e6)) MB" }
        if gb < 99.95 { return "\(oneDecimal(gb, trimZero: true)) GB" }
        if gb < 999.5 { return "\(roundAway(gb)) GB" }
        let tb = b / 1e12
        if tb < 9.95 { return "\(oneDecimal(tb, trimZero: true)) TB" }
        return "\(roundAway(tb)) TB"
    }

    /// Battery time: "45m", "2h 05m".
    public static func minutes(_ minutes: Int) -> String {
        let m = max(0, minutes)
        if m < 60 { return "\(m)m" }
        let rest = m % 60
        return "\(m / 60)h \(rest < 10 ? "0" : "")\(rest)m"
    }
}
