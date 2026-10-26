import AppKit
import Carbon.HIToolbox
import NotchCore

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private(set) var model: AppModel!
    private(set) var controller: NotchWindowController!
    private var statusItem: NSStatusItem?
    private var hotKey: HotKey?
    /// A URL can arrive before launching has finished (the app was opened by it).
    private var pendingURLs: [URL] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        quitOtherInstances()

        // `--data <file>`: a separate data file (self-test snapshots use demo data, never the user's).
        let args = ProcessInfo.processInfo.arguments
        if let i = args.firstIndex(of: "--data"), i + 1 < args.count {
            model = AppModel(store: DataStore(url: URL(fileURLWithPath: args[i + 1])))
        } else {
            model = AppModel()
        }
        model.onHotkeyChange = { [weak self] choice in self?.registerHotKey(choice) }
        controller = NotchWindowController(model: model)
        controller.start()
        installStatusItem()
        registerHotKey(model.settings.hotkey)
        pendingURLs.forEach { model.handle(url: $0) }
        pendingURLs = []

        if ProcessInfo.processInfo.arguments.contains("--self-test") {
            SelfTest.run(model: model, controller: controller)
        }
    }

    private(set) var hotKeyRegistered = false

    /// The chosen global shortcut (⌃⌥N by default; ⌥⌘I is the browsers' Web Inspector).
    func registerHotKey(_ choice: HotkeyChoice) {
        hotKey = nil
        let (code, mods): (Int, Int) = {
            switch choice {
            case .controlOptionN: return (kVK_ANSI_N, controlKey | optionKey)
            case .controlOptionSpace: return (kVK_Space, controlKey | optionKey)
            case .optionCommandI: return (kVK_ANSI_I, cmdKey | optionKey)
            }
        }()
        hotKey = HotKey(keyCode: code, modifiers: mods) { [weak self] in
            MainActor.assumeIsolated { self?.toggleFromKeyboard() }
        }
        hotKeyRegistered = hotKey != nil
        // A peek, so it's seen even with the notch closed (a toast only shows in the open notch).
        if !hotKeyRegistered {
            model?.showPeek(Peek(icon: "command", title: L("%@ is taken by another app", choice.label), subtitle: "Pick another in Settings",
                                 tint: Theme.gold, tab: .settings, mood: .awake, urgent: true))
        }
    }

    /// Opened from the keyboard: take key status so Esc and ⌘1…⌘8 work right away.
    private func toggleFromKeyboard() {
        model.toggle()
        if model.presentation == .expanded {
            controller.panel.makeKeyAndOrderFront(nil)
        }
    }

    /// `ezkielnotch://notify?title=…&body=…` from any script or app (Scout).
    func application(_ application: NSApplication, open urls: [URL]) {
        guard let model else { return pendingURLs += urls }
        urls.forEach { model.handle(url: $0) }
    }

    func applicationWillTerminate(_ notification: Notification) {
        model?.saveNow()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        controller?.panel.orderFrontRegardless()
        model?.expand()
        return false
    }

    /// Opening a new copy replaces an older running one.
    private func quitOtherInstances() {
        let me = ProcessInfo.processInfo.processIdentifier
        guard let own = Bundle.main.bundleIdentifier else { return }
        for app in NSRunningApplication.runningApplications(withBundleIdentifier: own) where app.processIdentifier != me {
            app.terminate()
        }
    }

    // MARK: - Menu bar item

    private func installStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = IconRenderer.statusImage()
        item.button?.toolTip = "Ezkiel Notch"
        let menu = NSMenu()
        menu.autoenablesItems = false
        menu.delegate = self
        item.menu = menu
        statusItem = item
        fillMenu(menu)
    }

    /// Rebuilt each time it opens, so it follows the language setting.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        fillMenu(menu)
    }

    private func fillMenu(_ menu: NSMenu) {

        menu.addItem(ActionMenuItem("Open Ezkiel Notch") { [weak self] in self?.model.toggle() })
        menu.addItem(ActionMenuItem("Keep Open") { [weak self] in
            guard let self else { return }
            self.model.pinned = true
            self.model.expand()
        })
        menu.addItem(.separator())
        for tab in NotchTab.allCases where tab != .settings {
            let entry = ActionMenuItem("\(tab.title) · \(L(tab.subtitle))") { [weak self] in self?.model.expand(tab) }
            entry.image = NSImage(systemSymbolName: tab.icon, accessibilityDescription: tab.title)
            menu.addItem(entry)
        }
        menu.addItem(.separator())
        menu.addItem(ActionMenuItem("Settings…", key: ",") { [weak self] in self?.model.expand(.settings) })
        menu.addItem(ActionMenuItem("Ezkiel Notch website") {
            NSWorkspace.shared.open(URL(string: "https://ezkielyna.store/notch")!)
        })
        menu.addItem(.separator())
        menu.addItem(ActionMenuItem("Mirror", key: "") { [weak self] in self?.model.setMirror(true) })
        menu.addItem(.separator())
        menu.addItem(ActionMenuItem("Quit Ezkiel Notch", key: "q") { NSApp.terminate(nil) })
    }
}
