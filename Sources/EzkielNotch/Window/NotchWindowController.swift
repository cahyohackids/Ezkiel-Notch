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
    private let dragPasteboard = NSPasteboard(name: .drag)
    private lazy var dragChangeCount = dragPasteboard.changeCount
    private var fileDragActive = false

    init(model: AppModel) {
        self.model = model
    }

    func start() {
        let size = model.panelSize
        panel = NotchPanel(size: size)
        hosting = NotchHostingView(rootView: NotchRootView(model: model, chrome: model.chrome, music: model.music,
                                                           battery: model.battery, lyrics: model.lyrics, calendar: model.calendar))
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
        model.pointerAnchor = { [weak self] in
            guard let self, let hosting = self.hosting, let panel = self.panel else { return nil }
            let inWindow = panel.convertPoint(fromScreen: NSEvent.mouseLocation)
            let local = hosting.convert(inWindow, from: nil)
            return (hosting, NSRect(x: local.x - 1, y: local.y - 1, width: 2, height: 2))
        }
        model.onCollapse = { [weak self] in self?.releaseKeyboard() }

        position()
        panel.orderFrontRegardless()
        installMonitors()
        observe()
    }

    // MARK: - Placement

    func targetScreen() -> NSScreen? {
        let screens = NSScreen.screens
        if model.settings.displayID != 0, let match = screens.first(where: { $0.displayID == model.settings.displayID }) {
            return match
        }
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
        // Screenshots for docs/the website: draw as if this display had a camera housing.
        if ProcessInfo.processInfo.arguments.contains("--fake-notch") {
            g = NotchGeometry(hasNotch: true, notchWidth: 186, barHeight: 32)
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
        if model.hiddenForFullscreen { return false }
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

        // The first click inside the open notch takes the keyboard, so Space, Esc and ⌘1…7 work
        // without stealing focus on a mere hover.
        if let m = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown], handler: { [weak self] event in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.model.noteInteraction()
                if self.model.presentation == .expanded, !self.panel.isKeyWindow { self.panel.makeKey() }
            }
            return event
        }) { monitors.append(m) }

        // Any button press anywhere may start a drag: run the poll while it lasts.
        if let m = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .leftMouseDragged], handler: { [weak self] _ in
            MainActor.assumeIsolated { self?.startPolling() }
        }) { monitors.append(m) }
        startPolling()
    }

    /// Safety net while it matters: monitors miss other apps' drag sessions and Spaces transitions.
    /// The poll runs while a button is down, while the notch is open or peeking, or while hovered,
    /// and stops itself when none of that is true (a closed, idle notch costs nothing).
    private func startPolling() {
        guard pollTimer == nil else { return }
        let timer = Timer(timeInterval: 1.0 / 12, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.checkFileDrag()
                self.updatePointer()
                let busy = NSEvent.pressedMouseButtons != 0 || self.fileDragActive || self.model.presentation != .compact
                    || self.model.chrome.hovering
                if !busy { self.stopPolling() }
            }
        }
        timer.tolerance = 0.02
        RunLoop.main.add(timer, forMode: .common)
        pollTimer = timer
    }

    private func stopPolling() {
        pollTimer?.invalidate()
        pollTimer = nil
    }

    private func updatePointer() {
        guard let panel else { return }
        if model.presentation != .compact || model.chrome.hovering { startPolling() }
        let mouse = NSEvent.mouseLocation
        let f = panel.frame
        let inPanel = mouse.x >= f.minX && mouse.x <= f.maxX && mouse.y >= f.minY && mouse.y <= f.maxY
        let p = CGPoint(x: mouse.x - f.minX, y: f.maxY - mouse.y)

        let overShape = inPanel && model.shapeRect.contains(p) && !model.hiddenForFullscreen
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
        let pasteboard = dragPasteboard
        let buttonDown = NSEvent.pressedMouseButtons & 1 != 0
        guard buttonDown else {
            if fileDragActive {
                fileDragActive = false
                model.fileDrag(near: false)
            }
            model.isDraggingOut = false
            dragChangeCount = pasteboard.changeCount
            return
        }
        if !fileDragActive, pasteboard.changeCount != dragChangeCount {
            dragChangeCount = pasteboard.changeCount
            // A file dragged out of Stash is ours: don't pull the notch towards it.
            if pasteboard.types?.contains(.fileURL) == true, !model.isDraggingOut {
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
                    self?.scheduleFullscreenCheck()
                }
            })
        }
        // A fullscreen app arrives with a Space change or by becoming frontmost.
        observers.append(ws.addObserver(forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.scheduleFullscreenCheck() }
        })
        model.$data
            .map(\.settings.hideFromScreenShare)
            .removeDuplicates()
            .sink { [weak self] hidden in
                self?.panel.sharingType = hidden ? .none : .readOnly
            }
            .store(in: &cancellables)
        scheduleFullscreenCheck()
        model.$data
            .map { "\($0.settings.displayID)|\($0.settings.displayName)" }
            .removeDuplicates()
            .dropFirst()
            .sink { [weak self] _ in
                DispatchQueue.main.async { self?.position() }
            }
            .store(in: &cancellables)
    }
}

// MARK: - Fullscreen

extension NotchWindowController {
    /// The fullscreen animation takes a moment; look right away and again once it settles.
    func scheduleFullscreenCheck() {
        for delay in [0.1, 0.9, 2.0] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in self?.checkFullscreen() }
        }
    }

    private func checkFullscreen() {
        guard let screen = targetScreen() else { return }
        let active = Self.fullscreenWindow(on: screen)
        if model.fullscreenActive != active {
            withAnimation(Theme.smooth) { model.fullscreenActive = active }
        }
    }

    /// Another app's normal window covers the whole display (on a notched one, everything below the camera
    /// housing) and the menu bar is gone: that is a fullscreen Space, not just a big window.
    /// Window bounds and layers need no Screen Recording permission.
    static func fullscreenWindow(on screen: NSScreen) -> Bool {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]],
              let mainHeight = NSScreen.screens.first?.frame.height else { return false }
        let own = ProcessInfo.processInfo.processIdentifier
        let f = screen.frame
        // CoreGraphics measures from the top-left of the main display.
        let top = mainHeight - f.maxY
        let inset = screen.safeAreaInsets.top
        func bounds(_ w: [String: Any]) -> CGRect? {
            (w[kCGWindowBounds as String] as? NSDictionary).flatMap { CGRect(dictionaryRepresentation: $0) }
        }
        let menuBarShown = list.contains { w in
            guard (w[kCGWindowLayer as String] as? Int) == Int(CGWindowLevelForKey(.mainMenuWindow)), let b = bounds(w) else { return false }
            return abs(b.minY - top) < 1 && abs(b.minX - f.minX) < 1 && abs(b.width - f.width) < 1 && b.height < 80
        }
        guard !menuBarShown else { return false }
        return list.contains { w in
            guard (w[kCGWindowLayer as String] as? Int) == 0,
                  (w[kCGWindowOwnerPID as String] as? Int32) != own,
                  let b = bounds(w) else { return false }
            return abs(b.minX - f.minX) < 1 && abs(b.width - f.width) < 1
                && abs(b.maxY - (top + f.height)) < 1 && b.minY - top <= inset + 1
        }
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
