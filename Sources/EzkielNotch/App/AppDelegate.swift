import AppKit
import Carbon.HIToolbox

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private(set) var model: AppModel!
    private(set) var controller: NotchWindowController!
    private var statusItem: NSStatusItem?
    private var hotKey: HotKey?

    func applicationDidFinishLaunching(_ notification: Notification) {
        quitOtherInstances()

        model = AppModel()
        controller = NotchWindowController(model: model)
        controller.start()
        installStatusItem()

        hotKey = HotKey(keyCode: kVK_ANSI_I, modifiers: cmdKey | optionKey) { [weak self] in
            MainActor.assumeIsolated { self?.model.toggle() }
        }

        if ProcessInfo.processInfo.arguments.contains("--self-test") {
            SelfTest.run(model: model, controller: controller)
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        model?.saveNow()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        controller?.panel.orderFrontRegardless()
        model?.expand()
        return false
    }

    /// Opening a new copy replaces older ones, including IslandDesk.
    private func quitOtherInstances() {
        let me = ProcessInfo.processInfo.processIdentifier
        var ids = ["app.islanddesk.mac"]
        if let own = Bundle.main.bundleIdentifier { ids.append(own) }
        for id in ids {
            for app in NSRunningApplication.runningApplications(withBundleIdentifier: id) where app.processIdentifier != me {
                app.terminate()
            }
        }
    }

    // MARK: - Menu bar item

    private func installStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = IconRenderer.statusImage()
        item.button?.toolTip = "Ezkiel Notch"
        let menu = NSMenu()
        menu.autoenablesItems = false

        let open = ActionMenuItem("Open Ezkiel Notch") { [weak self] in self?.model.toggle() }
        open.keyEquivalent = "i"
        open.keyEquivalentModifierMask = [.command, .option]
        menu.addItem(open)
        menu.addItem(ActionMenuItem("Keep Open") { [weak self] in
            guard let self else { return }
            self.model.pinned = true
            self.model.expand()
        })
        menu.addItem(.separator())
        for tab in NotchTab.allCases where tab != .settings {
            let entry = ActionMenuItem(tab.title) { [weak self] in self?.model.expand(tab) }
            entry.image = NSImage(systemSymbolName: tab.icon, accessibilityDescription: tab.title)
            menu.addItem(entry)
        }
        menu.addItem(.separator())
        menu.addItem(ActionMenuItem("Settings…", key: ",") { [weak self] in self?.model.expand(.settings) })
        menu.addItem(ActionMenuItem("Visit ezkielyna.store") { [weak self] in self?.model.open(.store) })
        menu.addItem(.separator())
        menu.addItem(ActionMenuItem("Quit Ezkiel Notch", key: "q") { NSApp.terminate(nil) })
        item.menu = menu
        statusItem = item
    }
}
