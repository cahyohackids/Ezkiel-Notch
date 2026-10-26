import Foundation
import IOKit.ps

struct BatteryState: Equatable {
    var percent: Int
    var isCharging: Bool
    var onAC: Bool
}

/// Event-driven battery reader (no polling): IOKit tells us when the power source changes.
@MainActor
final class BatteryMonitor: ObservableObject {
    @Published private(set) var state: BatteryState?
    var onChange: ((BatteryState?, BatteryState?) -> Void)?
    private var source: CFRunLoopSource?

    func start() {
        state = Self.read()
        let context = Unmanaged.passUnretained(self).toOpaque()
        guard let src = IOPSNotificationCreateRunLoopSource({ ctx in
            guard let ctx else { return }
            let monitor = Unmanaged<BatteryMonitor>.fromOpaque(ctx).takeUnretainedValue()
            DispatchQueue.main.async { monitor.update() }
        }, context)?.takeRetainedValue() else { return }
        // Common modes: updates keep arriving while a menu is tracking.
        CFRunLoopAddSource(CFRunLoopGetMain(), src, .commonModes)
        source = src
    }

    private func update() {
        let old = state
        let new = Self.read()
        guard new != old else { return }
        state = new
        onChange?(old, new)
    }

    nonisolated static func read() -> BatteryState? {
        guard let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let list = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef] else { return nil }
        for ps in list {
            guard let desc = IOPSGetPowerSourceDescription(info, ps)?.takeUnretainedValue() as? [String: Any],
                  (desc[kIOPSTypeKey] as? String) == kIOPSInternalBatteryType else { continue }
            let current = desc[kIOPSCurrentCapacityKey] as? Int ?? 0
            let max = desc[kIOPSMaxCapacityKey] as? Int ?? 100
            let percent = max > 0 ? Int((Double(current) / Double(max) * 100).rounded()) : current
            return BatteryState(
                percent: percent,
                isCharging: desc[kIOPSIsChargingKey] as? Bool ?? false,
                onAC: (desc[kIOPSPowerSourceStateKey] as? String) == kIOPSACPowerValue
            )
        }
        return nil
    }
}
