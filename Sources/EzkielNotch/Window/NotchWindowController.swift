import AppKit
import Combine
import SwiftUI

/// Owns the panel, places it on the right display and turns raw pointer
/// positions into hover / click-through decisions for the model.
@MainActor
final class NotchWindowController {
    let model: AppModel
    private(set) var panel: NotchPanel!
    private var hosting: NotchHostingView<NotchRootView>!
    private var monitors: [Any] = []
    private var observers: [NSObjectProtocol] = []
    private var cancellables = Set<AnyCancellable>()
    private var pollTimer: Timer?
    private var dragChangeCount = NSPasteboard(name: .drag).changeCount
    private var fileDragActive = false

    init(model: AppModel) {
        self.model = model
    }

    func start() {
        let size = model.panelSize
        panel = NotchPanel(size: size)
        hosting = NotchHostingView(rootView: NotchRootView(model: model, music: model.music, battery: model.battery))
        hosting.sizingOptions = []
        hosting.frame = NSRect(origin: .zero, size: size)
        hosting.autoresizingMask = [.width, .height]
        hosting.isInteractive = { [weak self] point in
            MainActor.assumeIsolated { self?.isInteractive(point) ?? false }
        }
        hosting.onRightClick = { [weak self] event in
            MainActor.assumeIsolated { self?.showContextMenu(event) }
        }
        panel.contentView = hosting
        panel.onEscape = { [weak self] in
            MainActor.assumeIsolated { self?.model.collapse() }
        }
        panel.onKeyDown = { [weak self] event in
            MainActor.assumeIsolated { self?.handleKey(event) ?? false }
        }

        model.isEditingText = { [weak self] in self?.isEditingText ?? false }
        model.onCollapse = { [weak self] in self?.releaseKeyboard() }

        position()
        panel.orderFrontRegardless()
        installMonitors()
        observe()
    }

    // MARK: - Placement

    func targetScreen() -> NSScreen? {
        let screens = NSScreen.screens
        let wanted = model.settings.displayName
        if !wanted.isEmpty, let match = screens.first(where: { $0.localizedName == wanted }) {
            return match
        }
        return screens.first(where: { $0.safeAreaInsets.top > 0 }) ?? screens.first
    }

    func position() {
        guard let panel, let screen = targetScreen() else { return }
        var g = NotchGeometry()
        g.hasNotch = screen.safeAreaInsets.top > 0
        if g.hasNotch, let left = screen.auxiliaryTopLeftArea, let right = screen.auxiliaryTopRightArea {
            g.notchWidth = max(120, screen.frame.width - left.width - right.width)
            g.barHeight = screen.safeAreaInsets.top
        } else {
            g.notchWidth = 190
            let bar = screen.frame.maxY - screen.visibleFrame.maxY
            g.barHeight = bar > 0 ? bar : 24
        }
        if model.geometry != g { model.geometry = g }

        let size = model.panelSize
        let frame = NSRect(x: (screen.frame.midX - size.width / 2).rounded(),
                           y: screen.frame.maxY - size.height,
                           width: size.width, height: size.height)
        if panel.frame != frame { panel.setFrame(frame, display: true) }
    }

    // MARK: - Pointer

    private func isInteractive(_ point: CGPoint) -> Bool {
        if model.shapeRect.contains(point) { return true }
        return fileDragActive && model.dragAttractRect.contains(point)
    }

    /// Hit-test a point given in panel top-left coordinates (used by --self-test).
    func hitTest(topLeft point: CGPoint) -> NSView? {
        guard let hosting else { return nil }
        let local = NSPoint(x: point.x, y: hosting.isFlipped ? point.y : hosting.bounds.height - point.y)
        let inSuper = hosting.superview.map { hosting.convert(local, to: $0) } ?? local
        return hosting.hitTest(inSuper)
    }

    private var isEditingText: Bool {
        guard let panel, panel.isKeyWindow else { return false }
        return panel.firstResponder is NSTextView
    }

    private func installMonitors() {
        let moveEvents: NSEvent.EventTypeMask = [.mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged]
        if let m = NSEvent.addGlobalMonitorForEvents(matching: moveEvents, handler: { [weak self] _ in
            MainActor.assumeIsolated { self?.updatePointer() }
        }) { monitors.append(m) }

        if let m = NSEvent.addLocalMonitorForEvents(matching: moveEvents.union([.mouseEntered, .mouseExited]), handler: { [weak self] event in
            MainActor.assumeIsolated { self?.updatePointer() }
            return event
        }) { monitors.append(m) }

        // Clicks that go to other apps mean "outside the notch".
        if let m = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown], handler: { [weak self] _ in
            MainActor.assumeIsolated { self?.model.clickedOutside() }
        }) { monitors.append(m) }

        if let m = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown], handler: { [weak self] event in
            MainActor.assumeIsolated { self?.model.noteInteraction() }
            return event
        }) { monitors.append(m) }

        // Safety net: monitors don't see everything (other apps' drag sessions, Spaces
        // transitions), so a light poll keeps hover and drag state honest.
        let timer = Timer(timeInterval: 1.0 / 12, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.checkFileDrag()
                self?.updatePointer()
            }
        }
        timer.tolerance = 0.02
        RunLoop.main.add(timer, forMode: .common)
        pollTimer = timer
    }

    private func updatePointer() {
        guard let panel else { return }
        let mouse = NSEvent.mouseLocation
        let f = panel.frame
        let inPanel = mouse.x >= f.minX && mouse.x <= f.maxX && mouse.y >= f.minY && mouse.y <= f.maxY
        let p = CGPoint(x: mouse.x - f.minX, y: f.maxY - mouse.y)

        let overShape = inPanel && model.shapeRect.contains(p)
        let nearForDrop = fileDragActive && inPanel && model.dragAttractRect.contains(p)
        let wantsEvents = overShape || nearForDrop
        if panel.ignoresMouseEvents == wantsEvents {
            panel.ignoresMouseEvents = !wantsEvents
        }

        model.pointerMoved(inside: inPanel && model.hotRect.contains(p))
        if fileDragActive {
            model.fileDrag(near: nearForDrop)
        }
    }

    private func checkFileDrag() {
        let pasteboard = NSPasteboard(name: .drag)
        let buttonDown = NSEvent.pressedMouseButtons & 1 != 0
        guard buttonDown else {
            if fileDragActive {
                fileDragActive = false
                model.fileDrag(near: false)
            }
            dragChangeCount = pasteboard.changeCount
            return
        }
        if !fileDragActive, pasteboard.changeCount != dragChangeCount {
            dragChangeCount = pasteboard.changeCount
            if pasteboard.types?.contains(.fileURL) == true {
                fileDragActive = true
            }
        }
    }

    // MARK: - Keyboard

    private func releaseKeyboard() {
        guard let panel else { return }
        guard panel.isKeyWindow || NSApp.isActive else { return }
        panel.makeFirstResponder(nil)
        if NSApp.isActive {
            NSApp.deactivate()
        }
        if panel.isKeyWindow {
            // Hand key status back to the app the user was in.
            panel.orderOut(nil)
            panel.orderFrontRegardless()
        }
    }

    private func handleKey(_ event: NSEvent) -> Bool {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        if event.keyCode == 53 { // Esc
            model.collapse()
            return true
        }
        if flags.isEmpty, !isEditingText {
            switch event.keyCode {
            case 123: model.selectNeighbor(-1); return true // ←
            case 124: model.selectNeighbor(1); return true  // →
            default: break
            }
        }
        guard flags.contains(.command), let chars = event.charactersIgnoringModifiers else { return false }
        if let n = Int(chars), (1...NotchTab.allCases.count).contains(n) {
            model.select(NotchTab.allCases[n - 1])
            return true
        }
        if chars == "," {
            model.select(.settings)
            return true
        }
        return false
    }

    // MARK: - Context menu

    private func showContextMenu(_ event: NSEvent) {
        let menu = NSMenu()
        menu.autoenablesItems = false
        menu.addItem(ActionMenuItem(model.presentation == .expanded ? "Collapse Notch" : "Open Notch") { [weak self] in
            self?.model.toggle()
        })
        let pin = ActionMenuItem(model.pinned ? "Unpin" : "Keep Open") { [weak self] in
            guard let self else { return }
            self.model.pinned.toggle()
            if self.model.pinned { self.model.expand() }
        }
        menu.addItem(pin)
        menu.addItem(.separator())
        let hover = ActionMenuItem("Open on Hover") { [weak self] in self?.model.settings.expandOnHover.toggle() }
        hover.state = model.settings.expandOnHover ? .on : .off
        menu.addItem(hover)
        let minimal = ActionMenuItem("Minimal Closed Notch") { [weak self] in self?.model.settings.minimalClosed.toggle() }
        minimal.state = model.settings.minimalClosed ? .on : .off
        menu.addItem(minimal)
        menu.addItem(.separator())
        menu.addItem(ActionMenuItem("Settings…") { [weak self] in self?.model.expand(.settings) })
        menu.addItem(ActionMenuItem("Quit Ezkiel Notch") { NSApp.terminate(nil) })

        model.block("menu")
        NSMenu.popUpContextMenu(menu, with: event, for: hosting)
        model.unblock("menu")
    }

    // MARK: - Observers

    private func observe() {
        let nc = NotificationCenter.default
        observers.append(nc.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.position() }
        })
        // Any menu (context menus, our right-click menu) keeps the notch open while it tracks.
        observers.append(nc.addObserver(forName: NSMenu.didBeginTrackingNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.model.block("menuTracking") }
        })
        observers.append(nc.addObserver(forName: NSMenu.didEndTrackingNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.model.unblock("menuTracking") }
        })
        let ws = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.activeSpaceDidChangeNotification, NSWorkspace.didWakeNotification, NSWorkspace.screensDidWakeNotification] {
            observers.append(ws.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    self?.position()
                    self?.panel.orderFrontRegardless()
                }
            })
        }
        model.$data
            .map(\.settings.displayName)
            .removeDuplicates()
            .dropFirst()
            .sink { [weak self] _ in
                DispatchQueue.main.async { self?.position() }
            }
            .store(in: &cancellables)
    }
}

/// NSMenuItem with a closure action.
final class ActionMenuItem: NSMenuItem {
    private let handler: () -> Void

    init(_ title: String, key: String = "", handler: @escaping () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(fire), keyEquivalent: key)
        target = self
    }

    @available(*, unavailable)
    required init(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    @objc private func fire() { handler() }
}
