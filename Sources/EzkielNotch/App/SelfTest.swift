import AppKit
import CoreAudio
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
        // Never the real Dock badges: they'd make results (and snapshots) depend on this Mac's chats.
        model.badges.simulate([])
        // Nor the real Spotify/Music: only the simulated song below.
        model.music.paused = true
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
            }))
        }

        // Roar: Dock badges from other apps (simulated, so the result doesn't depend on what runs here).
        let telegram = BadgeWatcher.Badge(bundleID: "ru.keepcoder.Telegram", name: "Telegram", label: "8", count: 8, since: Date())
        steps.append(("roar list", {
            model.collapse()
            model.badges.simulate([
                telegram,
                BadgeWatcher.Badge(bundleID: "net.whatsapp.WhatsApp", name: "WhatsApp", label: "3", count: 3, since: Date().addingTimeInterval(-240)),
                BadgeWatcher.Badge(bundleID: "com.microsoft.teams2", name: "Microsoft Teams", label: "2", count: 2, since: Date().addingTimeInterval(-1500)),
                BadgeWatcher.Badge(bundleID: "com.anthropic.claudefordesktop", name: "Claude", label: "1", count: 1, since: Date().addingTimeInterval(-600)),
            ])
            model.settings.roar = true
            model.settings.scout = true
            // The Rally countdown would take the closed notch's wing.
            model.settings.rally = false
            check(model.unreadTotal == 13, "Roar counts 13 unread (got \(model.unreadTotal))")
            check(model.aiBadges.map(\.bundleID) == ["com.anthropic.claudefordesktop"], "AI apps go to Scout, not the Roar list")
            model.roarMode = .roar
            model.expand(.roar)
        }))
        steps.append(("roar mute", {
            model.mute(telegram.bundleID, name: telegram.name)
            check(model.unreadTotal == 5, "a muted app leaves the count")
            model.chrome.toast?.action?()
            check(model.unreadTotal == 13, "Undo unmutes")
            model.clearToast()
            model.collapse()
        }))
        steps.append(("roar peek", {
            model.lastRoarPeek = [:]
            model.badgeIncreased(telegram, before: 6)
            check(model.presentation == .peek && model.peek?.app == telegram.bundleID && model.peek?.appIcon == true,
                  "a new message peeks with the app's icon")
        }))
        steps.append(("roar closed notch", {
            model.dismissPeek()
            check(model.presentation == .compact && model.unreadTotal > 0, "closed notch shows the unread count")
        }))

        // Scout: an agent session, as Claude Code's hook would send it.
        let hook = { (event: String, extra: String) -> ScoutEvent? in
            ScoutParser.claudeHook(Data(#"{"session_id":"selftest","cwd":"/Users/demo/ezkiel-notch","hook_event_name":"\#(event)"\#(extra)}"#.utf8),
                                   appBundleID: "com.microsoft.VSCode")
        }
        steps.append(("scout working", {
            if let e = hook("UserPromptSubmit", #","prompt":"Add Roar and Scout to the notch""#) { model.scout.receive(e) }
            check(model.agents.count == 1 && model.hasLiveActivity, "a working agent is live in the closed notch")
        }))
        steps.append(("scout needs you", {
            if let e = hook("Notification", #","message":"Claude needs your permission to use Bash""#) { model.scout.receive(e) }
            check(model.liveAgents.first?.kind == .needsYou, "the agent is waiting for you")
            check(model.presentation == .peek && model.peek?.title == "Claude needs you", "and peeks")
        }))
        steps.append(("scout done", {
            model.dismissPeek()
            let before = model.data.scoutLog.count
            if let e = hook("Stop", "") { model.scout.receive(e) }
            check(model.agents.isEmpty, "finished agents leave the live list")
            check(model.data.scoutLog.count == before + 1, "and are logged")
            if let e = hook("Notification", #","message":"Claude is waiting for your input""#) { model.scout.receive(e) }
            check(model.data.scoutLog.count == before + 1, "the idle reminder after a finish is dropped")
        }))
        steps.append(("scout pane", {
            model.dismissPeek()
            model.roarMode = .scout
            model.expand(.roar)
            check(model.tab == .roar && model.roarMode == .scout, "Scout shows")
        }))
        let wireTitle = "Wire test \(UUID().uuidString.prefix(6))"
        steps.append(("scout wire", {
            model.collapse()
            // The same path `EzkielNotch --notify` and the hooks use: a distributed notification.
            ScoutService.post(ScoutEvent(source: "script", kind: .info, title: wireTitle))
            if let url = URL(string: "ezkielnotch://notify?title=URL%20test&source=ci&kind=done") { model.handle(url: url) }
        }))
        steps.append(("scout wire arrived", {
            check(model.data.scoutLog.contains { $0.title == wireTitle }, "an event posted by another process arrives")
            check(model.data.scoutLog.contains { $0.title == "URL test" && $0.source == "ci" }, "ezkielnotch:// URLs arrive")
            model.dismissPeek()
        }))
        steps.append(("scout connect", {
            let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ezkiel-scout-\(UUID().uuidString)")
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let settingsFile = dir.appendingPathComponent("settings.json")
            try? Data(#"{"model":"opus"}"#.utf8).write(to: settingsFile)
            check(AgentLinks.set(.claude, connected: true, file: settingsFile) == nil, "connects Claude Code")
            check(AgentLinks.state(.claude, file: settingsFile) == .on, "Claude Code hook is in place")
            check(FileManager.default.fileExists(atPath: settingsFile.path + ".ezkiel-backup"), "the old settings are backed up")
            AgentLinks.set(.claude, connected: false, file: settingsFile)
            check(AgentLinks.state(.claude, file: settingsFile) == .off, "disconnects Claude Code")
            let toml = dir.appendingPathComponent("config.toml")
            try? Data("model = \"o3\"\n".utf8).write(to: toml)
            check(AgentLinks.set(.codex, connected: true, file: toml) == nil && AgentLinks.state(.codex, file: toml) == .on, "connects Codex")
            try? FileManager.default.removeItem(at: dir)
        }))

        // 1.3.1: calm in fullscreen, quiet during focus, Scout clean-up, update parsing.
        steps.append(("fullscreen calm", {
            model.collapse()
            model.fullscreenActive = true
            model.showPeek(Peek(icon: "music.note", title: "Song", subtitle: "Not urgent"))
            check(model.presentation == .compact, "fullscreen holds back ordinary peeks")
            model.showPeek(Peek(icon: "battery.25", title: "Battery low", subtitle: "Plug in soon", urgent: true))
            check(model.presentation == .peek, "urgent peeks still show in fullscreen")
            model.dismissPeek()
            check(!model.showsWings, "no wings over a fullscreen app")
            let notched = model.geometry
            model.geometry = NotchGeometry(hasNotch: false, notchWidth: 190, barHeight: 24)
            check(model.hiddenForFullscreen, "no pill over fullscreen on a display without a notch")
            let r = model.shapeRect
            check(controller.hitTest(topLeft: CGPoint(x: r.midX, y: r.midY)) == nil, "the hidden pill lets clicks through")
            model.geometry = notched
            model.fullscreenActive = false
        }))
        steps.append(("focus quiet", {
            model.select(.den)
            model.focusStartPause()
            model.lastRoarPeek = [:]
            model.badgeIncreased(telegram, before: 7)
            check(model.presentation == .compact, "Roar waits while Den is focusing")
            model.focusReset()
        }))
        steps.append(("scout clean-up", {
            if let e = hook("UserPromptSubmit", #","prompt":"one more thing""#) { model.scout.receive(e) }
            if let e = hook("Notification", #","message":"Claude needs your permission to use Edit""#) { model.scout.receive(e) }
            model.dismissPeek()
            let live = model.liveAgents.first
            check(live?.kind == .needsYou && !model.scoutHistory.contains { $0.id == live?.id }, "a live agent isn't listed twice")
            model.agentAppActivated("com.microsoft.VSCode")
            check(model.liveAgents.first?.kind == .working, "opening the agent's app turns 'needs you' back into working")
            if let e = hook("SessionEnd", "") { model.scout.receive(e) }
            check(model.agents.isEmpty, "SessionEnd clears the agent")
            check(ScoutParser.url(URL(string: "ezkielnotch://notify?title=x&app=com.apple.Terminal")!)?.appBundleID == nil,
                  "links can't choose an app to open")
            let release = UpdateChecker.parse(Data(#"{"tag_name":"v9.9.0","html_url":"https://github.com/cahyohackids/Ezkiel-Notch/releases/tag/v9.9.0","draft":false,"prerelease":false}"#.utf8))
            check(release?.version == "9.9.0" && AppVersion.isNewer(release?.version ?? "", than: AppModel.version), "reads a GitHub release")
        }))

        steps.append(("settings roar", {
            model.settingsSection = .roar
            model.expand(.settings)
            check(model.agentLinks.count == AgentLinks.Agent.allCases.count, "Settings knows each agent's link state")
        }))

        // 1.4: Prowl, weather, sound outputs, Indonesian.
        steps.append(("prowl closed notch", {
            model.collapse()
            model.prowl.simulate(mic: true, camera: true)
            check(model.hasLiveActivity && model.showsWings, "a live mic or camera shows in the closed notch")
        }))
        steps.append(("weather header", {
            model.prowl.simulate(mic: false, camera: false)
            let json = #"{"current":{"temperature_2m":30.6,"weather_code":3,"is_day":1},"daily":{"temperature_2m_max":[33.6],"temperature_2m_min":[25.1],"precipitation_probability_max":[73]}}"#
            let now = WeatherService.parseForecast(Data(json.utf8))
            check(now?.temperature == 30.6 && now?.rainChance == 73, "reads an Open-Meteo forecast")
            check(WeatherService.parsePlaces(Data(#"{"results":[{"name":"Jakarta","admin1":"Jakarta","country":"Indonesia","latitude":-6.2,"longitude":106.8}]}"#.utf8))?.first?.name == "Jakarta",
                  "reads an Open-Meteo place")
            model.settings.weatherCity = "Jakarta"
            model.weather.simulate(now)
            model.expand(.den)
        }))
        steps.append(("purr outputs", {
            model.outputs.simulate([
                AudioOutputs.Device(id: 1, name: "AirPods Pro", transport: kAudioDeviceTransportTypeBluetooth, isDefault: true),
                AudioOutputs.Device(id: 2, name: "MacBook Air Speakers", transport: kAudioDeviceTransportTypeBuiltIn, isDefault: false),
            ])
            check(model.outputs.devices.first?.icon == "airpodspro", "outputs get their own icons")
        }))
        steps.append(("bahasa indonesia", {
            model.settings.language = .id
            check(L("Settings") == "Pengaturan" && L("%d unread", 3) == "3 belum dibaca", "Indonesian text and formats")
            model.roarMode = .roar
            model.expand(.roar)
        }))
        steps.append(("bahasa settings", {
            model.settingsSection = .general
            model.expand(.settings)
        }))
        steps.append(("bahasa den", {
            model.expand(.den)
        }))
        steps.append(("english again", {
            model.settings.language = .en
            check(L("Settings") == "Settings", "back to English")
            model.settings.weatherCity = ""
        }))

        steps.append(("settings about", {
            model.settingsSection = .about
            model.expand(.settings)
        }))

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

        // One step after another: a slow capture must never let the next step run before it.
        let interval = snapshotDir == nil ? 0.7 : 1.4
        func run(_ i: Int) {
            guard i < steps.count else {
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.4) {
                    print(failures.isEmpty ? "SELF-TEST PASSED" : "SELF-TEST FAILED: \(failures.count) check(s)")
                    fflush(stdout)
                    exit(failures.isEmpty ? 0 : 1)
                }
                return
            }
            let step = steps[i]
            print("▶︎ \(step.0)")
            step.1()
            guard let snapshotDir else {
                DispatchQueue.main.asyncAfter(deadline: .now() + interval) { run(i + 1) }
                return
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
                capture(controller.panel, name: String(format: "%02d-", i) + step.0.replacingOccurrences(of: " ", with: "-"), dir: snapshotDir)
                DispatchQueue.main.asyncAfter(deadline: .now() + interval - 1.0) { run(i + 1) }
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { run(0) }
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
        if model.data.scoutLog.isEmpty {
            model.data.scoutLog = [
                ScoutEvent(source: "codex", kind: .done, title: "Codex finished", body: "Fixed the failing badge test and added a fallback.",
                           cwd: "/Users/demo/ezkielyna-api", date: Date().addingTimeInterval(-11 * 60)),
                ScoutEvent(source: "ci", kind: .done, title: "Deploy finished", body: "ezkielyna.store/notch is live",
                           date: Date().addingTimeInterval(-47 * 60)),
            ]
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
