import AppKit
import SwiftUI

/// Borderless, non-activating panel that sits over the menu bar.
/// Its frame never changes while the notch animates: only the SwiftUI shape inside
/// moves. That keeps hit-testing stable (the main cause of "dead clicks" in IslandDesk,
/// whose window was resized every frame and per tab).
final class NotchPanel: NSPanel {
    init(size: CGSize) {
        super.init(contentRect: NSRect(origin: .zero, size: size),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered,
                   defer: false)
        title = "Ezkiel Notch"
        isFloatingPanel = true
        level = .statusBar
        collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        isMovable = false
        isMovableByWindowBackground = false
        hidesOnDeactivate = false
        // Buttons work without stealing focus; text fields still get the keyboard.
        becomesKeyOnlyIfNeeded = true
        worksWhenModal = true
        acceptsMouseMovedEvents = true
        animationBehavior = .none
        isReleasedWhenClosed = false
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    // The notch deliberately covers the menu-bar strip; don't let AppKit push it down.
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }

    /// Return true when handled.
    var onKeyDown: ((NSEvent) -> Bool)?
    var onEscape: (() -> Void)?

    override func keyDown(with event: NSEvent) {
        if onKeyDown?(event) == true { return }
        super.keyDown(with: event)
    }

    override func cancelOperation(_ sender: Any?) {
        if let onEscape {
            onEscape()
        } else {
            super.cancelOperation(sender)
        }
    }
}

/// Hosting view that only claims clicks inside the visible notch and always
/// accepts the first click (no "click once to focus, again to press").
final class NotchHostingView<Content: View>: NSHostingView<Content> {
    /// Returns true when a point (top-left origin, in this view) is on the notch.
    var isInteractive: (CGPoint) -> Bool = { _ in true }
    var onRightClick: ((NSEvent) -> Void)?

    required init(rootView: Content) {
        super.init(rootView: rootView)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override var mouseDownCanMoveWindow: Bool { false }

    func topLeftPoint(_ local: NSPoint) -> CGPoint {
        CGPoint(x: local.x, y: isFlipped ? local.y : bounds.height - local.y)
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = superview.map { convert(point, from: $0) } ?? point
        guard isInteractive(topLeftPoint(local)) else { return nil }
        return super.hitTest(point)
    }

    override func rightMouseDown(with event: NSEvent) {
        if let onRightClick {
            onRightClick(event)
        } else {
            super.rightMouseDown(with: event)
        }
    }
}
