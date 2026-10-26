import Foundation

/// Wall-clock based focus/break timer. It stores an end date instead of
/// counting ticks, so it stays correct across sleep, lag and app restarts.
public struct FocusClock: Equatable {
    public enum Mode: String, Codable, Equatable {
        case focus, rest
    }

    public enum Phase: Equatable {
        case ready
        case running(end: Date)
        case paused(remaining: TimeInterval)
    }

    public private(set) var mode: Mode = .focus
    public private(set) var phase: Phase = .ready
    public private(set) var duration: TimeInterval

    public init(minutes: Int = 25) {
        duration = TimeInterval(max(1, minutes) * 60)
    }

    public var isRunning: Bool {
        if case .running = phase { return true }
        return false
    }

    public var isPaused: Bool {
        if case .paused = phase { return true }
        return false
    }

    public func remaining(at now: Date = Date()) -> TimeInterval {
        switch phase {
        case .ready: return duration
        case .running(let end): return max(0, end.timeIntervalSince(now))
        case .paused(let remaining): return remaining
        }
    }

    /// 0 at start, 1 when finished.
    public func progress(at now: Date = Date()) -> Double {
        guard duration > 0 else { return 0 }
        return min(1, max(0, 1 - remaining(at: now) / duration))
    }

    public func isFinished(at now: Date = Date()) -> Bool {
        if case .running(let end) = phase { return now >= end }
        return false
    }

    public mutating func setMinutes(_ minutes: Int, mode newMode: Mode? = nil) {
        if let newMode { mode = newMode }
        duration = TimeInterval(max(1, minutes) * 60)
        phase = .ready
    }

    public mutating func start(at now: Date = Date()) {
        switch phase {
        case .ready: phase = .running(end: now.addingTimeInterval(duration))
        case .paused(let remaining): phase = .running(end: now.addingTimeInterval(remaining))
        case .running: break
        }
    }

    public mutating func pause(at now: Date = Date()) {
        if case .running(let end) = phase {
            phase = .paused(remaining: max(0, end.timeIntervalSince(now)))
        }
    }

    public mutating func toggle(at now: Date = Date()) {
        isRunning ? pause(at: now) : start(at: now)
    }

    public mutating func reset() {
        phase = .ready
    }

    /// Call when `isFinished` turns true. Flips focus <-> rest and returns the finished mode.
    @discardableResult
    public mutating func complete(focusMinutes: Int, breakMinutes: Int) -> Mode {
        let finished = mode
        if finished == .focus {
            setMinutes(breakMinutes, mode: .rest)
        } else {
            setMinutes(focusMinutes, mode: .focus)
        }
        return finished
    }

    public static func format(_ interval: TimeInterval) -> String {
        let total = Int(interval.rounded(.up))
        let h = total / 3600
        let m = (total % 3600) / 60
        let s = total % 60
        if h > 0 { return String(format: "%d:%02d:%02d", h, m, s) }
        return String(format: "%02d:%02d", m, s)
    }
}

// MARK: - Persistence

/// What `FocusClock` needs to survive a restart (a running session keeps its wall-clock end).
public struct FocusSnapshot: Codable, Equatable {
    public var mode: FocusClock.Mode
    public var duration: TimeInterval
    public var end: Date?
    public var pausedRemaining: TimeInterval?

    public init(mode: FocusClock.Mode, duration: TimeInterval, end: Date? = nil, pausedRemaining: TimeInterval? = nil) {
        self.mode = mode
        self.duration = duration
        self.end = end
        self.pausedRemaining = pausedRemaining
    }
}

extension FocusClock {
    public var snapshot: FocusSnapshot {
        switch phase {
        case .ready: return FocusSnapshot(mode: mode, duration: duration)
        case .running(let end): return FocusSnapshot(mode: mode, duration: duration, end: end)
        case .paused(let remaining): return FocusSnapshot(mode: mode, duration: duration, pausedRemaining: remaining)
        }
    }

    public init(snapshot s: FocusSnapshot) {
        self.init(minutes: max(1, Int((s.duration / 60).rounded())))
        mode = s.mode
        duration = max(60, s.duration)
        if let end = s.end {
            phase = .running(end: end)
        } else if let remaining = s.pausedRemaining {
            phase = .paused(remaining: max(0, min(remaining, duration)))
        }
    }
}
