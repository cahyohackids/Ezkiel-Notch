import Carbon.HIToolbox
import Foundation

/// System-wide shortcut through Carbon's RegisterEventHotKey. Unlike a menu
/// key equivalent it works while another app is frontmost, and it needs no
/// Accessibility or Input Monitoring permission.
final class HotKey {
    private static var registry: [UInt32: HotKey] = [:]
    private static var handlerInstalled = false

    private var ref: EventHotKeyRef?
    private let id: UInt32
    private let action: () -> Void

    init?(keyCode: Int, modifiers: Int, id: UInt32 = 1, action: @escaping () -> Void) {
        self.id = id
        self.action = action
        guard HotKey.installHandler() else { return nil }
        let hotKeyID = EventHotKeyID(signature: OSType(0x455A_4B4E), id: id) // 'EZKN'
        let status = RegisterEventHotKey(UInt32(keyCode), UInt32(modifiers), hotKeyID,
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == noErr else { return nil }
        HotKey.registry[id] = self
    }

    deinit {
        if let ref { UnregisterEventHotKey(ref) }
        HotKey.registry[id] = nil
    }

    private static func installHandler() -> Bool {
        if handlerInstalled { return true }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let status = InstallEventHandler(GetApplicationEventTarget(), { (_, event, _) -> OSStatus in
            var hotKeyID = EventHotKeyID()
            let err = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                        nil, MemoryLayout<EventHotKeyID>.size, nil, &hotKeyID)
            guard err == noErr else { return err }
            let id = hotKeyID.id
            DispatchQueue.main.async { HotKey.registry[id]?.action() }
            return noErr
        }, 1, &spec, nil, nil)
        handlerInstalled = status == noErr
        return handlerInstalled
    }
}
