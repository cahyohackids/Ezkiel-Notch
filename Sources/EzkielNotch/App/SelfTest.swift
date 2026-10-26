import AppKit
import NotchCore

/// `EzkielNotch --self-test` drives the real panel through every state and checks
/// that clicks land on the notch (and pass through everywhere else). Exit code 0 = pass.
@MainActor
enum SelfTest {
    private static var failures: [String] = []

    static func run(model: AppModel, controller: NotchWindowController) {
        var steps: [(String, () -> Void)] = []
        let args = ProcessInfo.processInfo.arguments
        let snapshotDir = args.firstIndex(of: "--snapshots").flatMap { i in i + 1 < args.count ? args[i + 1] : nil }
        if snapshotDir != nil { seedDemoData(model) }
        model.ignoresPointer = true

        steps.append(("panel on screen", {
            check(controller.panel.isVisible, "panel is visible")
            if let screen = controller.targetScreen() {
                check(abs(controller.panel.frame.maxY - screen.frame.maxY) < 0.5, "panel is flush with the top edge")
            }
            checkClicks(model: model, controller: controller, label: "compact")
        }))

        steps.append(("expand", { model.expand(.focus) }))
        steps.append(("expanded clicks", {
            check(model.presentation == .expanded, "expanded after expand()")
            checkClicks(model: model, controller: controller, label: "expanded")
        }))

        for tab in NotchTab.allCases {
            steps.append(("tab \(tab.title)", {
                model.select(tab)
                check(model.tab == tab, "selected \(tab.title)")
                let r = model.shapeRect
                check(controller.hitTest(topLeft: CGPoint(x: r.midX, y: r.midY)) != nil, "\(tab.title): content accepts clicks")
            }))
        }

        steps.append(("focus timer", {
            model.select(.focus)
            model.focusStartPause()
            check(model.focus.isRunning, "focus timer starts")
            model.focusStartPause()
            check(model.focus.isPaused, "focus timer pauses")
            model.focusReset()
        }))

        steps.append(("collapse", { model.collapse() }))
        steps.append(("compact again", {
            check(model.presentation == .compact, "collapsed after collapse()")
            let expandedArea = CGPoint(x: model.panelSize.width / 2, y: model.panelSize.height - 40)
            check(controller.hitTest(topLeft: expandedArea) == nil, "area below the closed notch passes clicks through")
        }))

        steps.append(("peek", {
            model.showPeek(Peek(icon: "sparkle", title: "Self test", subtitle: "Peek card"))
            check(model.presentation == .peek, "peek shows (\(model.presentation))")
        }))
        steps.append(("peek tap opens", {
            model.openPeek()
            check(model.presentation == .expanded, "tapping a peek opens the notch")
            model.collapse()
        }))

        steps.append(("volume overlay", {
            model.showPeek(Peek(icon: "speaker.wave.2.fill", title: "Volume", subtitle: "", hudKey: "volume", level: 0.4, value: "40%"))
            let first = model.peek?.id
            model.showPeek(Peek(icon: "speaker.wave.3.fill", title: "Volume", subtitle: "", hudKey: "volume", level: 0.7, value: "70%"))
            check(model.presentation == .peek && model.peek?.isHUD == true, "overlay shows (\(model.presentation))")
            check(model.peek?.id == first && model.peek?.level == 0.7, "overlay updates in place")
        }))
        steps.append(("headphones peek", {
            model.collapse()
            model.showPeek(Peek(icon: "airpodspro", title: "AirPods Pro", subtitle: "Connected · sound plays here", tint: Theme.teal))
            check(model.presentation == .peek && model.peek?.isHUD == false, "card peek replaces overlay")
        }))
        steps.append(("battery peek", {
            model.collapse()
            model.showPeek(Peek(icon: "bolt.fill", title: "Charging", subtitle: "Charger connected", tint: Theme.teal,
                                progress: 0.7, value: "70%"))
            check(model.presentation == .peek, "battery peek shows")
        }))
        steps.append(("splash settles", {
            // Give the wobble's reset a moment even when the machine is busy.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                check(model.ripple == 0, "notch edge settles back to straight (\(model.ripple))")
            }
        }))

        if snapshotDir != nil {
            let np = NowPlaying(player: .spotify, title: "Paper Sky", artist: "Lumen Coast", album: "Summer Flows",
                                duration: 196, position: 41, isPlaying: true, artworkURL: nil, fetchedAt: Date())
            let lines = ["we fold the summer light", "into paper birds tonight", "and send them through the sky",
                         "to find you by the sea", "every wing a little song", "that carries us along"]
                .enumerated().map { LyricLine(time: 30 + Double($0.offset) * 5, text: $0.element) }
            let lyricsWereOn = model.settings.showLyrics
            steps.append(("lyrics split", {
                model.music.simulate(np)
                model.settings.showLyrics = true
                model.lyrics.preview(lines, for: np)
                model.expand(.music)
            }))
            steps.append(("lyrics closed notch", {
                model.collapse()
                check(model.showsClosedLyrics, "closed notch sings along")
            }))
            steps.append(("lyrics off", {
                model.settings.showLyrics = lyricsWereOn
                model.music.simulate(nil)
                model.music.refresh()
            }))
        }

        steps.append(("lion icon", {
            check(IconRenderer.statusImage().size.width > 0, "lion renders")
        }))

        let interval = snapshotDir == nil ? 0.7 : 1.4
        for (i, step) in steps.enumerated() {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8 + Double(i) * interval) {
                print("▶︎ \(step.0)")
                step.1()
            }
            if let snapshotDir {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8 + Double(i) * interval + 1.0) {
                    capture(controller.panel, name: String(format: "%02d-", i) + step.0.replacingOccurrences(of: " ", with: "-"), dir: snapshotDir)
                }
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5 + Double(steps.count) * interval) {
            print(failures.isEmpty ? "SELF-TEST PASSED" : "SELF-TEST FAILED: \(failures.count) check(s)")
            fflush(stdout)
            exit(failures.isEmpty ? 0 : 1)
        }
    }

    /// Real screenshot of the panel area (needs a GUI session, e.g. a CI macOS runner).
    private static func capture(_ panel: NSPanel, name: String, dir: String) {
        guard let main = NSScreen.screens.first else { return }
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        let f = panel.frame
        let rect = "\(Int(f.minX)),\(Int(main.frame.maxY - f.maxY)),\(Int(f.width)),\(Int(f.height))"
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        let path = (dir as NSString).appendingPathComponent("\(name).png")
        task.arguments = ["-x", "-R", rect, path]
        try? task.run()
        task.waitUntilExit()
        // No Screen Recording permission (local runs): draw the panel's own view instead.
        if !FileManager.default.fileExists(atPath: path), let view = panel.contentView,
           let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
            view.cacheDisplay(in: view.bounds, to: rep)
            try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: path))
        }
    }

    private static func seedDemoData(_ model: AppModel) {
        if model.data.tasks.isEmpty {
            model.addTask("Upload new lion stickers to the store", due: Date().addingTimeInterval(3600))
            model.addTask("Reply to customer chats", due: nil)
            model.addTask("Check Ezkielyna API usage", due: nil)
            if let id = model.data.tasks.last?.id { model.toggleTask(id) }
        }
        if model.data.clips.isEmpty {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString("https://ezkielyna.store/community/challenges", forType: .string)
            model.captureClipboard()
        }
        if model.data.water.isEmpty {
            let cal = Calendar.current
            for back in 1...6 {
                if let day = cal.date(byAdding: .day, value: -back, to: Date()) {
                    model.data.water.append(WaterEntry(date: day, ml: [1800, 2200, 1500, 2400, 2000, 1200][back - 1]))
                }
            }
            model.data.water.append(WaterEntry(date: Date(), ml: 1150))
        }
        model.addToShelf([URL(fileURLWithPath: "/System/Applications/Calculator.app"),
                          URL(fileURLWithPath: "/System/Library/Desktop Pictures"),
                          URL(fileURLWithPath: "/etc/hosts")])
    }

    private static func checkClicks(model: AppModel, controller: NotchWindowController, label: String) {
        let r = model.shapeRect
        check(controller.hitTest(topLeft: CGPoint(x: r.midX, y: r.midY)) != nil, "\(label): notch accepts clicks")
        check(controller.hitTest(topLeft: CGPoint(x: 2, y: model.panelSize.height - 2)) == nil, "\(label): corner passes clicks through")
    }

    private static func check(_ ok: Bool, _ message: String) {
        print("  \(ok ? "✓" : "✗") \(message)")
        if !ok { failures.append(message) }
    }
}
