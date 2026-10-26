import AppKit
import AudioToolbox
import Carbon
import CoreAudio
import Network

/// Watches volume, the audio output device, Caps Lock, the keyboard language and the
/// network, and reports changes (never the initial state). No Accessibility or Input
/// Monitoring permission is needed: it never sees which keys are typed.
@MainActor
final class SystemMonitor {
    enum OutputKind: Equatable { case bluetooth, wired, builtIn, other }

    enum Event {
        case volume(level: Double, muted: Bool)
        case capsLock(on: Bool)
        case inputSource(name: String)
        case network(online: Bool, wifi: Bool)
        case output(name: String, kind: OutputKind, previous: (name: String, kind: OutputKind)?)
    }

    var onEvent: ((Event) -> Void)?

    private var outputDevice = AudioObjectID(kAudioObjectUnknown)
    private var outputInfo: (name: String, kind: OutputKind)?
    private var lastVolume: (level: Double, muted: Bool)?
    private var quietUntil = Date.distantPast
    private var capsTimer: Timer?
    private var capsOn = false
    private var inputName: String?
    private var pathMonitor: NWPathMonitor?
    private var online: Bool?
    private var pendingNetwork: DispatchWorkItem?
    private var observers: [NSObjectProtocol] = []

    func start() {
        // Audio output + volume.
        var defaultOut = Self.address(kAudioHardwarePropertyDefaultOutputDevice, scope: kAudioObjectPropertyScopeGlobal)
        AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &defaultOut, .main) { [weak self] _, _ in
            MainActor.assumeIsolated { self?.outputChanged(initial: false) }
        }
        outputChanged(initial: true)

        // Caps Lock: read the modifier state a few times a second (no key events involved).
        capsOn = NSEvent.modifierFlags.contains(.capsLock)
        let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.pollCapsLock() }
        }
        timer.tolerance = 0.1
        RunLoop.main.add(timer, forMode: .common)
        capsTimer = timer

        // Keyboard language.
        inputName = Self.currentInputSourceName()
        let tis = DistributedNotificationCenter.default().addObserver(
            forName: Notification.Name(kTISNotifySelectedKeyboardInputSourceChanged as String), object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.inputSourceChanged() }
        }
        observers.append(tis)

        // After sleep the network and audio reconnect on their own; don't announce that.
        let wake = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.quietUntil = Date().addingTimeInterval(12) }
        }
        observers.append(wake)

        let monitor = NWPathMonitor()
        monitor.pathUpdateHandler = { [weak self] path in
            let isOnline = path.status == .satisfied
            let wifi = path.usesInterfaceType(.wifi)
            DispatchQueue.main.async { self?.networkChanged(online: isOnline, wifi: wifi) }
        }
        monitor.start(queue: DispatchQueue(label: "store.ezkielyna.notch.network", qos: .utility))
        pathMonitor = monitor
    }

    private var quiet: Bool { Date() < quietUntil }

    // MARK: - Audio

    private func outputChanged(initial: Bool) {
        var id = AudioObjectID(kAudioObjectUnknown)
        var addr = Self.address(kAudioHardwarePropertyDefaultOutputDevice, scope: kAudioObjectPropertyScopeGlobal)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &id) == noErr,
              id != kAudioObjectUnknown, id != outputDevice else { return }

        outputDevice = id
        for selector in [kAudioHardwareServiceDeviceProperty_VirtualMainVolume, kAudioDevicePropertyMute] {
            var a = Self.address(selector, scope: kAudioDevicePropertyScopeOutput)
            guard AudioObjectHasProperty(id, &a) else { continue }
            AudioObjectAddPropertyListenerBlock(id, &a, .main) { [weak self] _, _ in
                MainActor.assumeIsolated {
                    guard let self, self.outputDevice == id else { return }
                    self.volumeChanged()
                }
            }
        }
        lastVolume = Self.volume(of: id)

        let info = (name: Self.name(of: id), kind: Self.kind(of: id))
        let previous = outputInfo
        outputInfo = info
        guard !initial else { return }
        // A device switch also nudges the volume listeners; keep that from showing a volume overlay.
        quietVolumeUntil = Date().addingTimeInterval(1.2)
        if !quiet { onEvent?(.output(name: info.name, kind: info.kind, previous: previous)) }
    }

    private var quietVolumeUntil = Date.distantPast

    private func volumeChanged() {
        guard let v = Self.volume(of: outputDevice) else { return }
        defer { lastVolume = v }
        if let last = lastVolume, abs(last.level - v.level) < 0.001, last.muted == v.muted { return }
        guard Date() >= quietVolumeUntil, !quiet else { return }
        onEvent?(.volume(level: v.level, muted: v.muted))
    }

    private static func address(_ selector: AudioObjectPropertySelector, scope: AudioObjectPropertyScope) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }

    private static func volume(of id: AudioObjectID) -> (level: Double, muted: Bool)? {
        var addr = address(kAudioHardwareServiceDeviceProperty_VirtualMainVolume, scope: kAudioDevicePropertyScopeOutput)
        var level = Float32(0)
        var size = UInt32(MemoryLayout<Float32>.size)
        guard AudioObjectHasProperty(id, &addr), AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &level) == noErr else { return nil }
        var muteAddr = address(kAudioDevicePropertyMute, scope: kAudioDevicePropertyScopeOutput)
        var mute = UInt32(0)
        size = UInt32(MemoryLayout<UInt32>.size)
        if AudioObjectHasProperty(id, &muteAddr) {
            AudioObjectGetPropertyData(id, &muteAddr, 0, nil, &size, &mute)
        }
        return (Double(level), mute != 0)
    }

    private static func name(of id: AudioObjectID) -> String {
        var addr = address(kAudioObjectPropertyName, scope: kAudioObjectPropertyScopeGlobal)
        var name: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        guard AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &name) == noErr, let name else { return "Speakers" }
        return name.takeRetainedValue() as String
    }

    private static func kind(of id: AudioObjectID) -> OutputKind {
        var addr = address(kAudioDevicePropertyTransportType, scope: kAudioObjectPropertyScopeGlobal)
        var transport = UInt32(0)
        var size = UInt32(MemoryLayout<UInt32>.size)
        AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &transport)
        switch transport {
        case kAudioDeviceTransportTypeBluetooth, kAudioDeviceTransportTypeBluetoothLE:
            return .bluetooth
        case kAudioDeviceTransportTypeBuiltIn:
            // The built-in output reports 'hdpn' as its data source when something is in the jack.
            var src = address(kAudioDevicePropertyDataSource, scope: kAudioDevicePropertyScopeOutput)
            var source = UInt32(0)
            if AudioObjectHasProperty(id, &src), AudioObjectGetPropertyData(id, &src, 0, nil, &size, &source) == noErr,
               source == 0x6864_706E { // 'hdpn'
                return .wired
            }
            return .builtIn
        case kAudioDeviceTransportTypeUSB:
            return .wired
        default:
            return .other
        }
    }

    // MARK: - Keyboard

    private func pollCapsLock() {
        let on = NSEvent.modifierFlags.contains(.capsLock)
        guard on != capsOn else { return }
        capsOn = on
        onEvent?(.capsLock(on: on))
    }

    private func inputSourceChanged() {
        guard let name = Self.currentInputSourceName(), name != inputName else { return }
        inputName = name
        onEvent?(.inputSource(name: name))
    }

    private static func currentInputSourceName() -> String? {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue(),
              let raw = TISGetInputSourceProperty(source, kTISPropertyLocalizedName) else { return nil }
        return Unmanaged<CFString>.fromOpaque(raw).takeUnretainedValue() as String
    }

    // MARK: - Network

    private func networkChanged(online isOnline: Bool, wifi: Bool) {
        guard let previous = online else {
            online = isOnline
            return
        }
        pendingNetwork?.cancel()
        guard previous != isOnline else { return }
        // Wait for the connection to settle so a short blip doesn't flash two peeks.
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.online != isOnline else { return }
            self.online = isOnline
            if !self.quiet { self.onEvent?(.network(online: isOnline, wifi: wifi)) }
        }
        pendingNetwork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 2, execute: work)
    }
}
