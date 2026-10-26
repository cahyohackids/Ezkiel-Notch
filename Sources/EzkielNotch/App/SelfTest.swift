import AppKit
import NotchCore
import SwiftUI

/// `EzkielNotch --self-test` drives the real panel through every state and checks that clicks
/// land on the notch (and pass through everywhere else). Exit code 0 = pass.
///   --snapshots <dir>  also saves a PNG of every step (use with --data <file> for demo data)
///   --fake-notch       lay out as if the display had a camera housing
@MainActor
enum SelfTest {
    private static var failures: [String] = []

    static func run(model: AppModel, controller: NotchWindowController) {
        var steps: [(String, () -> Void)] = []
        let args = ProcessInfo.processInfo.arguments
        let snapshotDir = args.firstIndex(of: "--snapshots").flatMap { i in i + 1 < args.count ? args[i + 1] : nil }
        if snapshotDir != nil { seedDemoData(model) }
        model.ignoresPointer = true
        let saved = model.settings

        steps.append(("panel on screen", {
            check(controller.panel.isVisible, "panel is visible")
            if let screen = controller.targetScreen() {
                check(abs(controller.panel.frame.maxY - screen.frame.maxY) < 0.5, "panel is flush with the top edge")
            }
            checkClicks(model: model, controller: controller, label: "compact")
        }))

        steps.append(("expand", { model.expand(.den) }))
        steps.append(("expanded clicks", {
            check(model.presentation == .expanded, "expanded after expand()")
            checkClicks(model: model, controller: controller, label: "expanded")
        }))

        for tab in NotchTab.allCases {
            steps.append(("tab \(tab.title)", {
                model.select(tab)
                check(model.tab == tab, "selected \(tab.title)")
                let expected = NotchMetrics.expandedSize(model.geometry, pane: NotchMetrics.paneHeight(tab, lyrics: tab == .purr && model.purrShowsLyrics)).height
                check(abs(model.shapeRect.height - expected) < 0.5,
                      "\(tab.title): notch is \(Int(expected))pt tall (got \(Int(model.shapeRect.height)), \(model.presentation), tab \(model.tab.title))")
                let r = model.shapeRect
                check(controller.hitTest(topLeft: CGPoint(x: r.midX, y: r.midY)) != nil, "\(tab.title): content accepts clicks")
            }))
        }

        steps.append(("den timer", {
            model.select(.den)
            model.focusStartPause()
            check(model.focus.isRunning, "focus timer starts")
            model.setFocusMinutes(45)
            check(model.focus.isRunning, "changing the length doesn't reset a running session")
            model.focusStartPause()
            check(model.focus.isPaused, "focus timer pauses")
            model.focusReset()
            model.setFocusMinutes(saved.focusMinutes)
        }))

        steps.append(("undo", {
            model.select(.scratch)
            model.addNote()
            guard let id = model.data.notes.first?.id else { return check(false, "note added") }
            model.deleteNote(id)
            check(!model.data.notes.contains { $0.id == id }, "note deleted")
            model.chrome.toast?.action?()
            check(model.data.notes.contains { $0.id == id }, "Undo restores the note")
            model.deleteNote(id)
            model.clearToast()
        }))

        steps.append(("collapse", { model.collapse() }))
        steps.append(("compact again", {
            check(model.presentation == .compact, "collapsed after collapse()")
            let below = CGPoint(x: model.panelSize.width / 2, y: model.panelSize.height - 40)
            check(controller.hitTest(topLeft: below) == nil, "area below the closed notch passes clicks through")
        }))

        steps.append(("peek", {
            model.showPeek(Peek(icon: "sparkle", title: "Self test", subtitle: "Peek card"))
            check(model.presentation == .peek, "peek shows")
        }))
        steps.append(("peek waits for the pointer", {
            model.simulatePointer(inside: true)
            check(model.presentation == .peek, "a hovered peek stays")
            model.simulatePointer(inside: false)
        }))
        // Leaving reschedules the dismiss 1.2 s out; give it two step slots.
        steps.append(("peek leaving", {}))
        steps.append(("peek gone", {
            check(model.presentation == .compact, "a peek dismisses after the pointer leaves")
        }))
        steps.append(("peek tap opens", {
            model.showPeek(Peek(icon: "sparkle", title: "Self test", subtitle: "Tap me", tab: .oasis))
            model.openPeek()
            check(model.presentation == .expanded && model.tab == .oasis, "tapping a peek opens its tab")
            model.collapse()
        }))
        steps.append(("volume overlay", {
            model.showPeek(Peek(icon: "speaker.wave.2.fill", title: "Volume", subtitle: "", hudKey: "volume", level: 0.4, value: "40%"))
            let first = model.peek?.id
            model.showPeek(Peek(icon: "speaker.wave.3.fill", title: "Volume", subtitle: "", hudKey: "volume", level: 0.7, value: "70%"))
            check(model.presentation == .peek && model.peek?.isHUD == true, "overlay shows")
            check(model.peek?.id == first && model.peek?.level == 0.7, "overlay updates in place")
        }))
        steps.append(("overlay waits for a card", {
            model.dismissPeek()
            model.showPeek(Peek(icon: "airpodspro", title: "AirPods Pro", subtitle: "Connected", tint: Theme.teal))
            model.showPeek(Peek(icon: "speaker.wave.2.fill", title: "Volume", subtitle: "", hudKey: "volume", level: 0.5, value: "50%"))
            check(model.peek?.isHUD == false, "an overlay doesn't push a card away")
        }))
        steps.append(("battery peek", {
            model.dismissPeek()
            model.showPeek(Peek(icon: "bolt.fill", title: "Charging", subtitle: "Charger connected", tint: Theme.teal,
                                progress: 0.7, value: "70%"))
            check(model.presentation == .peek, "battery peek shows")
        }))

        let demoFile = demoScreenshot()
        steps.append(("pounce peek", {
            model.dismissPeek()
            if let demoFile {
                model.showPeek(Peek(icon: "camera.viewfinder", title: "Screenshot", subtitle: demoFile.lastPathComponent,
                                    tint: Accent.stash.deep, tab: .stash, file: demoFile))
            }
            check(model.peek?.isRich == true, "Pounce peek shows with actions")
        }))

        let meeting = CalendarService.Meeting(id: "selftest", title: "Design review", start: Date().addingTimeInterval(4 * 60),
                                              end: Date().addingTimeInterval(34 * 60),
                                              link: URL(string: "https://meet.google.com/abc-defg-hij"), color: Accent.rally.deep)
        steps.append(("rally peek", {
            model.dismissPeek()
            model.simulateMeetings([meeting])
            model.settings.rally = true
            model.showPeek(Peek(icon: "calendar", title: meeting.title, subtitle: "Starts soon · in 4 min",
                                tint: Accent.rally.deep, tab: .hunt, link: meeting.link))
            check(model.peek?.link != nil, "Rally peek shows Join")
            check(CalendarService.joinLink(in: "Notes: join at https://us02web.zoom.us/j/123456?pwd=x.") != nil, "finds a Zoom link")
        }))
        steps.append(("hunt with rally", {
            model.expand(.hunt)
            check(model.meetingSoon?.id == meeting.id, "the meeting counts down")
        }))

        if snapshotDir != nil {
            // Built when the step runs, so the song is at 0:41 in the snapshot, not wherever the clock got to.
            let np = { NowPlaying(player: .spotify, title: "Paper Sky", artist: "Lumen Coast", album: "Summer Flows",
                                  duration: 196, position: 41, isPlaying: true, artworkURL: nil, fetchedAt: Date()) }
            let lines = ["we fold the summer light", "into paper birds tonight", "and send them through the sky",
                         "to find you by the sea", "every wing a little song", "that carries us along",
                         "and if the night gets long", "we'll hum the morning on"]
                .enumerated().map { LyricLine(time: 30 + Double($0.offset) * 5, text: $0.element) }
            steps.append(("purr player", {
                model.music.simulate(np())
                model.settings.showLyrics = false
                model.expand(.purr)
            }))
            steps.append(("purr chorus", {
                model.settings.showLyrics = true
                let song = np()
                model.music.simulate(song)
                model.lyrics.preview(lines, for: song)
            }))
            steps.append(("chorus closed notch", {
                model.collapse()
                check(model.showsClosedLyrics, "closed notch sings along")
            }))
            steps.append(("chorus off", {
                model.music.simulate(nil)
                model.music.refresh()
            }))
        }

        steps.append(("splash settles", {
            model.collapse()
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                check(model.chrome.ripple == 0, "notch edge settles back to straight (\(model.chrome.ripple))")
            }
        }))
        steps.append(("hotkey and icons", {
            check((NSApp.delegate as? AppDelegate)?.hotKeyRegistered == true, "global shortcut \(model.settings.hotkey.label) registered")
            check(IconRenderer.statusImage().isTemplate, "menu-bar lion is a template image")
            // Leave the settings as they were (the Rally step turned Calendar on).
            model.settings = saved
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
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.2 + Double(steps.count) * interval) {
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

    /// A stand-in screenshot for the Pounce step (the app icon, saved as a PNG in a temp folder).
    private static func demoScreenshot() -> URL? {
        let renderer = ImageRenderer(content: LionIconArtwork(size: 256))
        guard let cg = renderer.cgImage,
              let png = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]) else { return nil }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("Screenshot 2026-10-26 at 12.03.41.png")
        return (try? png.write(to: url)) != nil ? url : nil
    }

    /// Demo content for snapshots. Only fills what is empty (run with --data so it's never real data).
    private static func seedDemoData(_ model: AppModel) {
        if model.data.tasks.isEmpty {
            model.addTask("Upload new lion stickers to the store", due: Date().addingTimeInterval(3600))
            model.addTask("Reply to customer chats", due: nil)
            model.addTask("Check Ezkielyna API usage", due: nil)
            if let id = model.data.tasks.last?.id { model.toggleTask(id) }
        }
        if model.data.notes.count <= 1 {
            model.data.notes = [
                NoteItem(title: "Launch checklist", body: "Screenshots for the landing page\nRelease notes for 1.2\nTell the community group"),
                NoteItem(title: "Ideas", body: "Weather in the closed notch?\nA tiny pomodoro sound pack"),
                NoteItem(title: "Groceries", body: "Coffee beans, oat milk, lemons")
            ]
            model.selectedNoteID = model.data.notes.first?.id
        }
        if model.data.clips.isEmpty {
            model.data.clips = [ClipItem(text: "https://ezkielyna.store/notch"),
                                ClipItem(text: "Meeting moved to Thursday 10:00, same link")]
        }
        let cal = Calendar.current
        if model.data.water.isEmpty {
            for back in 1...6 {
                if let day = cal.date(byAdding: .day, value: -back, to: Date()) {
                    model.data.water.append(WaterEntry(date: day, ml: [1800, 2200, 1500, 2400, 2000, 1200][back - 1]))
                }
            }
            model.data.water.append(WaterEntry(date: Date(), ml: 1150))
        }
        if model.data.focusLog.isEmpty {
            for (back, minutes) in [(0, 50), (1, 75), (2, 25), (3, 100), (4, 50), (6, 25)] {
                if let day = cal.date(byAdding: .day, value: -back, to: Date()) {
                    model.data.focusLog.append(FocusEntry(start: day, minutes: minutes))
                }
            }
        }
        if model.data.shelf.isEmpty {
            model.addToShelf([URL(fileURLWithPath: "/System/Applications/Calculator.app"),
                              URL(fileURLWithPath: "/System/Library/Desktop Pictures"),
                              URL(fileURLWithPath: "/etc/hosts")])
        }
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
