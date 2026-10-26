import AppKit
import CoreAudio
import CoreMediaIO

/// Prowl: is a microphone or camera on right now? Public CoreAudio / CoreMediaIO properties,
/// read every 2 s off the main thread. No permission, and nothing is recorded.
@MainActor
final class PrivacyWatch: ObservableObject {
    @Published private(set) var micInUse = false
    @Published private(set) var cameraInUse = false
    /// Apps using the mic (macOS 14.2+ can tell; empty when unknown).
    @Published private(set) var micApps: [String] = []

    /// Our own camera mirror shouldn't count.
    var ignoreCamera = false

    private var timer: Timer?
    private var reading = false
    private var paused = false
    private let queue = DispatchQueue(label: "store.ezkielyna.notch.prowl", qos: .utility)

    func setEnabled(_ on: Bool) {
        guard on != (timer != nil) else { return }
        timer?.invalidate()
        timer = nil
        guard on else {
            micInUse = false
            cameraInUse = false
            micApps = []
            return
        }
        let t = Timer(timeInterval: 2, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        t.tolerance = 0.5
        RunLoop.main.add(t, forMode: .common)
        timer = t
        refresh()
    }

    var isPaused: Bool {
        get { paused }
        set { paused = newValue; if !newValue { refresh() } }
    }

    func refresh() {
        guard !reading, !paused, timer != nil else { return }
        reading = true
        queue.async { [weak self] in
            let mic = Self.micState()
            let camera = Self.cameraRunning()
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    guard let self else { return }
                    self.reading = false
                    let cam = camera && !self.ignoreCamera
                    if self.micInUse != mic.inUse || self.micApps != mic.apps || self.cameraInUse != cam {
                        self.micInUse = mic.inUse
                        self.micApps = mic.apps
                        self.cameraInUse = cam
                    }
                }
            }
        }
    }

    /// Self-test snapshots.
    func simulate(mic: Bool, camera: Bool) {
        timer?.invalidate()
        timer = nil
        micInUse = mic
        cameraInUse = camera
        micApps = mic ? ["Zoom"] : []
    }

    // MARK: Microphone

    nonisolated private static func address(_ selector: AudioObjectPropertySelector,
                                            scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }

    nonisolated private static func objects(_ selector: AudioObjectPropertySelector, of object: AudioObjectID = AudioObjectID(kAudioObjectSystemObject),
                                            scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal) -> [AudioObjectID] {
        var addr = address(selector, scope: scope)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(object, &addr, 0, nil, &size) == noErr, size > 0 else { return [] }
        var ids = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(object, &addr, 0, nil, &size, &ids) == noErr else { return [] }
        return ids
    }

    nonisolated private static func uint32(_ selector: AudioObjectPropertySelector, of object: AudioObjectID) -> UInt32? {
        var addr = address(selector)
        var value: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        return AudioObjectGetPropertyData(object, &addr, 0, nil, &size, &value) == noErr ? value : nil
    }

    nonisolated private static func micState() -> (inUse: Bool, apps: [String]) {
        if #available(macOS 14.2, *) {
            // Per process: exactly who is recording, and playback through AirPods doesn't count.
            let own = ProcessInfo.processInfo.processIdentifier
            var names: [String] = []
            var any = false
            for process in objects(kAudioHardwarePropertyProcessObjectList) {
                guard uint32(kAudioProcessPropertyIsRunningInput, of: process) == 1 else { continue }
                var addr = address(kAudioProcessPropertyPID)
                var pid: pid_t = 0
                var size = UInt32(MemoryLayout<pid_t>.size)
                guard AudioObjectGetPropertyData(process, &addr, 0, nil, &size, &pid) == noErr, pid != own else { continue }
                any = true
                if let app = NSRunningApplication(processIdentifier: pid), let name = app.localizedName,
                   app.activationPolicy == .regular, !names.contains(name) {
                    names.append(name)
                }
            }
            return (any, names)
        }
        // macOS 13: input-only devices that are running (a headset playing music would look like recording).
        let running = objects(kAudioHardwarePropertyDevices).contains { device in
            !objects(kAudioDevicePropertyStreams, of: device, scope: kAudioObjectPropertyScopeInput).isEmpty
                && objects(kAudioDevicePropertyStreams, of: device, scope: kAudioObjectPropertyScopeOutput).isEmpty
                && uint32(kAudioDevicePropertyDeviceIsRunningSomewhere, of: device) == 1
        }
        return (running, [])
    }

    // MARK: Camera

    nonisolated private static func cameraRunning() -> Bool {
        let system = CMIOObjectID(kCMIOObjectSystemObject)
        var addr = CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(kCMIOHardwarePropertyDevices),
                                             mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeGlobal),
                                             mElement: CMIOObjectPropertyElement(0))
        var size: UInt32 = 0
        guard CMIOObjectGetPropertyDataSize(system, &addr, 0, nil, &size) == 0, size > 0 else { return false }
        var ids = [CMIOObjectID](repeating: 0, count: Int(size) / MemoryLayout<CMIOObjectID>.size)
        var used: UInt32 = 0
        guard CMIOObjectGetPropertyData(system, &addr, 0, nil, size, &used, &ids) == 0 else { return false }
        return ids.contains { device in
            var a = CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(kCMIODevicePropertyDeviceIsRunningSomewhere),
                                              mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeGlobal),
                                              mElement: CMIOObjectPropertyElement(0))
            var running: UInt32 = 0
            var u: UInt32 = 0
            return CMIOObjectGetPropertyData(device, &a, 0, nil, UInt32(MemoryLayout<UInt32>.size), &u, &running) == 0 && running != 0
        }
    }
}
