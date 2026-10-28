import XCTest
@testable import NotchCore

final class FocusClockTests: XCTestCase {
    func testStartPauseResumeKeepsRemaining() {
        let t0 = Date(timeIntervalSince1970: 1_000)
        var clock = FocusClock(minutes: 25)
        XCTAssertEqual(clock.remaining(at: t0), 1500)

        clock.start(at: t0)
        XCTAssertTrue(clock.isRunning)
        XCTAssertEqual(clock.remaining(at: t0.addingTimeInterval(60)), 1440, accuracy: 0.001)

        clock.pause(at: t0.addingTimeInterval(100))
        XCTAssertTrue(clock.isPaused)
        // Time passing while paused must not count.
        XCTAssertEqual(clock.remaining(at: t0.addingTimeInterval(5_000)), 1400, accuracy: 0.001)

        clock.start(at: t0.addingTimeInterval(200))
        XCTAssertEqual(clock.remaining(at: t0.addingTimeInterval(300)), 1300, accuracy: 0.001)
    }

    func testFinishFlipsToBreakAndBack() {
        let t0 = Date(timeIntervalSince1970: 0)
        var clock = FocusClock(minutes: 1)
        clock.start(at: t0)
        XCTAssertFalse(clock.isFinished(at: t0.addingTimeInterval(59)))
        XCTAssertTrue(clock.isFinished(at: t0.addingTimeInterval(60)))
        XCTAssertEqual(clock.progress(at: t0.addingTimeInterval(30)), 0.5, accuracy: 0.001)

        XCTAssertEqual(clock.complete(focusMinutes: 25, breakMinutes: 5), .focus)
        XCTAssertEqual(clock.mode, .rest)
        XCTAssertEqual(clock.duration, 300)
        XCTAssertEqual(clock.phase, .ready)

        XCTAssertEqual(clock.complete(focusMinutes: 25, breakMinutes: 5), .rest)
        XCTAssertEqual(clock.mode, .focus)
        XCTAssertEqual(clock.duration, 1500)
    }

    func testFormat() {
        XCTAssertEqual(FocusClock.format(2700), "45:00")
        XCTAssertEqual(FocusClock.format(59.2), "01:00")
        XCTAssertEqual(FocusClock.format(3725), "1:02:05")
    }
}

final class WaterLogTests: XCTestCase {
    private var calendar: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }()

    func testTotalsAndStreak() {
        let today = Date(timeIntervalSince1970: 1_700_000_000)
        let yesterday = today.addingTimeInterval(-86_400)
        let entries = [
            WaterEntry(date: today, ml: 1000),
            WaterEntry(date: today, ml: 1000),
            WaterEntry(date: yesterday, ml: 2500)
        ]
        XCTAssertEqual(WaterLog.total(entries, on: today, calendar: calendar), 2000)
        XCTAssertEqual(WaterLog.streak(entries, goal: 2000, today: today, calendar: calendar), 2)

        let week = WaterLog.lastDays(7, entries: entries, today: today, calendar: calendar)
        XCTAssertEqual(week.count, 7)
        XCTAssertEqual(week.last?.ml, 2000)
        XCTAssertEqual(week[5].ml, 2500)
    }

    func testReminderRespectsHoursAndInterval() {
        var comps = DateComponents()
        comps.year = 2026; comps.month = 10; comps.day = 23; comps.hour = 10
        let now = calendar.date(from: comps)!
        XCTAssertTrue(WaterLog.reminderDue(lastDrink: nil, lastReminder: nil, intervalMinutes: 60,
                                           startHour: 8, endHour: 22, now: now, calendar: calendar))
        XCTAssertFalse(WaterLog.reminderDue(lastDrink: now.addingTimeInterval(-600), lastReminder: nil, intervalMinutes: 60,
                                            startHour: 8, endHour: 22, now: now, calendar: calendar))
        XCTAssertFalse(WaterLog.reminderDue(lastDrink: nil, lastReminder: nil, intervalMinutes: 60,
                                            startHour: 12, endHour: 22, now: now, calendar: calendar))
    }
}

final class PersistenceTests: XCTestCase {
    func testRoundTripAndTolerantDecoding() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let store = DataStore(url: dir.appendingPathComponent("data.json"))
        var data = AppData()
        data.tasks = [TaskItem(title: "Ship Ezkiel Notch", due: Date(timeIntervalSince1970: 42))]
        data.notes = [NoteItem(body: "Hello\nworld")]
        data.settings.waterGoal = 2400
        store.save(data, synchronously: true)

        let loaded = store.load()
        XCTAssertEqual(loaded.tasks.first?.title, "Ship Ezkiel Notch")
        XCTAssertEqual(loaded.notes.first?.displayTitle, "Hello")
        XCTAssertEqual(loaded.settings.waterGoal, 2400)

        // Old or partial files must still load with defaults.
        let partial = #"{"settings":{"waterGoal":1500},"tasks":[]}"#.data(using: .utf8)!
        let decoded = try JSONDecoder().decode(AppData.self, from: partial)
        XCTAssertEqual(decoded.settings.waterGoal, 1500)
        XCTAssertEqual(decoded.settings.waterIntervalMinutes, 60)
        XCTAssertTrue(decoded.settings.expandOnHover)
    }
}

final class LyricsTests: XCTestCase {
    func testParsesTimedLinesAndSkipsMetadata() {
        let lrc = """
        [ar:Lumen Coast]
        [ti:Paper Sky]
        [00:12.50]we fold the summer light
        [00:15.00][01:02.25]into paper birds tonight
        [00:18:40]
        """
        let lines = LRC.parse(lrc)
        XCTAssertEqual(lines.map(\.time), [12.5, 15, 18.4, 62.25])
        XCTAssertEqual(lines[0].text, "we fold the summer light")
        XCTAssertEqual(lines[1].text, "into paper birds tonight")
        XCTAssertEqual(lines[2].text, "")
        XCTAssertEqual(lines[3].text, "into paper birds tonight")
    }

    func testOffsetShiftsEarlier() {
        let lines = LRC.parse("[offset:500]\n[00:10.00]hello")
        XCTAssertEqual(lines.first?.time ?? 0, 9.5, accuracy: 0.001)
    }

    func testIndexFindsCurrentLine() {
        let lines = [LyricLine(time: 5, text: "a"), LyricLine(time: 10, text: "b"), LyricLine(time: 20, text: "c")]
        XCTAssertNil(LRC.index(at: 4.9, in: lines))
        XCTAssertEqual(LRC.index(at: 5, in: lines), 0)
        XCTAssertEqual(LRC.index(at: 19.99, in: lines), 1)
        XCTAssertEqual(LRC.index(at: 500, in: lines), 2)
        XCTAssertNil(LRC.index(at: 1, in: []))
    }
}

final class FocusStatsTests: XCTestCase {
    private let cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }()

    private func day(_ d: Int, _ h: Int = 10) -> Date {
        cal.date(from: DateComponents(year: 2026, month: 10, day: d, hour: h))!
    }

    func testMinutesAndLastDays() {
        let log = [FocusEntry(start: day(26), minutes: 25), FocusEntry(start: day(26, 15), minutes: 50),
                   FocusEntry(start: day(24), minutes: 15)]
        XCTAssertEqual(FocusStats.minutes(log, on: day(26), calendar: cal), 75)
        let week = FocusStats.lastDays(3, entries: log, today: day(26), calendar: cal)
        XCTAssertEqual(week.map(\.minutes), [15, 0, 75])
    }

    func testStreakCountsBackFromTodayOrYesterday() {
        let log = [FocusEntry(start: day(23), minutes: 25), FocusEntry(start: day(24), minutes: 25),
                   FocusEntry(start: day(25), minutes: 25)]
        // Nothing yet today: the streak still counts up to yesterday.
        XCTAssertEqual(FocusStats.streak(log, today: day(26), calendar: cal), 3)
        XCTAssertEqual(FocusStats.streak(log + [FocusEntry(start: day(26), minutes: 25)], today: day(26), calendar: cal), 4)
        // A gap of two days ends it.
        XCTAssertEqual(FocusStats.streak(log, today: day(27), calendar: cal), 0)
    }

    func testSnapshotRoundTripKeepsRunningEnd() {
        let t0 = Date(timeIntervalSince1970: 10_000)
        var clock = FocusClock(minutes: 25)
        clock.start(at: t0)
        let restored = FocusClock(snapshot: clock.snapshot)
        XCTAssertTrue(restored.isRunning)
        XCTAssertEqual(restored.remaining(at: t0.addingTimeInterval(60)), 1440, accuracy: 0.001)

        clock.pause(at: t0.addingTimeInterval(100))
        let paused = FocusClock(snapshot: clock.snapshot)
        XCTAssertTrue(paused.isPaused)
        XCTAssertEqual(paused.remaining(), 1400, accuracy: 0.001)
    }
}

final class ScoutParserTests: XCTestCase {
    func testClaudeNotificationAndStop() {
        let note = #"{"session_id":"s1","cwd":"/Users/me/ezkiel-notch","hook_event_name":"Notification","message":"Claude needs your permission to use Bash"}"#
        let e = ScoutParser.claudeHook(Data(note.utf8), appBundleID: "com.apple.Terminal")
        XCTAssertEqual(e?.kind, .needsYou)
        XCTAssertEqual(e?.body, "Claude needs your permission to use Bash")
        XCTAssertEqual(e?.project, "ezkiel-notch")
        XCTAssertEqual(e?.appBundleID, "com.apple.Terminal")

        let stop = #"{"session_id":"s1","cwd":"/tmp","hook_event_name":"Stop","stop_hook_active":false}"#
        XCTAssertEqual(ScoutParser.claudeHook(Data(stop.utf8))?.kind, .done)
        XCTAssertNil(ScoutParser.claudeHook(Data(#"{"hook_event_name":"PreToolUse"}"#.utf8)))
        XCTAssertNil(ScoutParser.claudeHook(Data("not json".utf8)))
    }

    func testCodexTurnComplete() {
        let arg = #"{"type":"agent-turn-complete","turn-id":"t1","input-messages":["fix it"],"last-assistant-message":"  Fixed the failing test.  "}"#
        let e = ScoutParser.codexNotify(arg)
        XCTAssertEqual(e?.kind, .done)
        XCTAssertEqual(e?.source, "codex")
        XCTAssertEqual(e?.body, "Fixed the failing test.")
    }

    func testURL() {
        let e = ScoutParser.url(URL(string: "ezkielnotch://notify?title=Build%20done&body=All%20green&source=ci&kind=done")!)
        XCTAssertEqual(e?.title, "Build done")
        XCTAssertEqual(e?.kind, .done)
        XCTAssertEqual(e?.source, "ci")
        XCTAssertNil(ScoutParser.url(URL(string: "ezkielnotch://notify?body=x")!))
        XCTAssertNil(ScoutParser.url(URL(string: "https://example.com/notify?title=x")!))
    }
}

final class ScoutSetupTests: XCTestCase {
    let exe = "/Applications/Ezkiel Notch.app/Contents/MacOS/EzkielNotch"

    func testBadgeLabel() {
        XCTAssertEqual(BadgeLabel.count("8"), 8)
        XCTAssertEqual(BadgeLabel.count("99+"), 99)
        XCTAssertEqual(BadgeLabel.count("•"), 1)
        XCTAssertNil(BadgeLabel.count(""))
        XCTAssertNil(BadgeLabel.count("0"))
    }

    func testClaudeConnectKeepsOtherHooksAndIsIdempotent() throws {
        let cmd = ScoutSetup.claudeCommand(executable: exe)
        let existing = #"{"model":"opus","hooks":{"Stop":[{"hooks":[{"type":"command","command":"say done"}]}]}}"#
        let once = try ScoutSetup.connectClaude(Data(existing.utf8), command: cmd)
        let twice = try ScoutSetup.connectClaude(once, command: cmd)
        XCTAssertEqual(ScoutSetup.claudeState(twice, command: cmd), .on)
        let root = try JSONSerialization.jsonObject(with: twice) as! [String: Any]
        XCTAssertEqual(root["model"] as? String, "opus")
        let stop = (root["hooks"] as! [String: Any])["Stop"] as! [[String: Any]]
        XCTAssertEqual(stop.count, 2, "user's hook kept, ours added once")

        let off = try ScoutSetup.disconnectClaude(twice)
        XCTAssertEqual(ScoutSetup.claudeState(off, command: cmd), .off)
        let hooks = (try JSONSerialization.jsonObject(with: off) as! [String: Any])["hooks"] as! [String: Any]
        XCTAssertEqual(hooks.keys.sorted(), ["Stop"])
        XCTAssertEqual(ScoutSetup.claudeState(try ScoutSetup.connectClaude(nil, command: "'/old/EzkielNotch' --claude-hook"), command: cmd), .stale)
        XCTAssertThrowsError(try ScoutSetup.connectClaude(Data("{oops".utf8), command: cmd))
    }

    func testCodex() throws {
        let toml = "model = \"o3\"\n\n[tui]\nnotify = true\n"
        let on = try ScoutSetup.connectCodex(toml, executable: exe)
        XCTAssertTrue(on.hasPrefix(ScoutSetup.codexLine(executable: exe) + "\n"))
        XCTAssertTrue(on.contains("--codex-notify"))
        XCTAssertEqual(ScoutSetup.codexState(on, executable: exe), .on)
        let old = "notify = [\"\(exe)\", \"--codex-notify\"]\n"
        XCTAssertEqual(ScoutSetup.codexState(old, executable: exe), .stale, "1.3.0's unguarded line gets repaired")
        XCTAssertEqual(try ScoutSetup.connectCodex(on, executable: exe), on)
        XCTAssertEqual(ScoutSetup.disconnectCodex(on), toml)
        XCTAssertThrowsError(try ScoutSetup.connectCodex("notify = [\"terminal-notifier\"]\n", executable: exe))
    }
}

final class DataSafetyTests: XCTestCase {
    func testOneBadItemDoesNotLoseTheList() throws {
        let json = #"{"tasks":[{"id":"6F9619FF-8B86-D011-B42D-00C04FC964FF","title":"ok","done":false,"reminded":false,"created":"2026-10-26T01:00:00Z"},{"title":42}],"settings":{}}"#
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        let data = try d.decode(AppData.self, from: Data(json.utf8))
        XCTAssertEqual(data.tasks.map(\.title), ["ok"])
    }

    func testBrokenFileIsKeptAndBackupRestored() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ezk-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let store = DataStore(url: dir.appendingPathComponent("data.json"))
        var good = AppData()
        good.notes = [NoteItem(title: "keep me")]
        store.save(good, synchronously: true)
        _ = store.load()  // makes the daily backup
        try Data("{ not json".utf8).write(to: store.url)
        let loaded = store.load()
        XCTAssertEqual(loaded.notes.first?.title, "keep me")
        guard case .restoredBackup(let broken, _) = store.recovery else { return XCTFail("expected a restore") }
        XCTAssertTrue(FileManager.default.fileExists(atPath: broken.path))
    }

    func testCalmerDefaultsMigrate() throws {
        let old = #"{"hoverDelay":0.12,"minimalClosed":false}"#
        let s = try JSONDecoder().decode(NotchSettings.self, from: Data(old.utf8))
        XCTAssertEqual(s.hoverDelay, 0.25)
        XCTAssertTrue(s.minimalClosed)
        XCTAssertTrue(s.musicAllowed)
        let chosen = try JSONDecoder().decode(NotchSettings.self, from: Data(#"{"hoverDelay":0.45,"minimalClosed":false,"defaultsRevision":2}"#.utf8))
        XCTAssertEqual(chosen.hoverDelay, 0.45)
        XCTAssertFalse(chosen.minimalClosed)
    }

    func testVersionCompare() {
        XCTAssertTrue(AppVersion.isNewer("v1.4.0", than: "1.3.1"))
        XCTAssertTrue(AppVersion.isNewer("1.3.10", than: "1.3.9"))
        XCTAssertFalse(AppVersion.isNewer("1.3.1", than: "1.3.1"))
        XCTAssertFalse(AppVersion.isNewer("1.3", than: "1.3.0"))
    }
}

final class UpdateVerifyTests: XCTestCase {
    func testChecksumLookup() {
        let sums = """
        8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4  EzkielNotch-1.5.0-macOS-universal.dmg
        a3f1c2e4b5d6978812345678901234567890abcdefabcdefabcdefabcdefab12 *EzkielNotch-1.5.0-macOS-universal.zip
        """
        XCTAssertEqual(UpdateVerify.checksum(for: "EzkielNotch-1.5.0-macOS-universal.dmg", in: sums),
                       "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4")
        XCTAssertNotNil(UpdateVerify.checksum(for: "EzkielNotch-1.5.0-macOS-universal.zip", in: sums))
        XCTAssertNil(UpdateVerify.checksum(for: "Other.dmg", in: sums))
        XCTAssertNil(UpdateVerify.checksum(for: "x.dmg", in: "nothex  x.dmg"))
    }

    func testFileHash() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("ezk-\(UUID().uuidString)")
        try Data("abc".utf8).write(to: url)
        XCTAssertEqual(try UpdateVerify.sha256(of: url), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
    }

    func testAcceptable() {
        XCTAssertTrue(UpdateVerify.acceptable(bundleID: "store.ezkielyna.notch", version: "1.5.0", expected: "1.5.0", current: "1.4.0"))
        XCTAssertFalse(UpdateVerify.acceptable(bundleID: "com.evil.app", version: "1.5.0", expected: "1.5.0", current: "1.4.0"))
        XCTAssertFalse(UpdateVerify.acceptable(bundleID: "store.ezkielyna.notch", version: "1.4.0", expected: "1.5.0", current: "1.4.0"))
        XCTAssertFalse(UpdateVerify.acceptable(bundleID: "store.ezkielyna.notch", version: "1.3.0", expected: "1.3.0", current: "1.4.0"))
    }
}

final class DuePhraseTests: XCTestCase {
    /// Every case in the fixture the Windows tests read too.
    func testSharedFixture() throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appendingPathComponent("../fixtures/due-phrases.json").standardized
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "Asia/Jakarta")!
        let f = DateFormatter()
        f.calendar = cal
        f.timeZone = cal.timeZone
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm"
        let now = try XCTUnwrap(f.date(from: root["now"] as? String ?? ""))
        for c in root["cases"] as? [[String: Any]] ?? [] {
            let text = c["text"] as? String ?? "", title = c["title"] as? String ?? "", due = c["due"] as? String
            let r = DuePhrase.parse(text, now: now, calendar: cal)
            let got = r.map { "\($0.title) · \(f.string(from: $0.due))" } ?? "(none)"
            let want = due.map { "\(title) · \($0)" } ?? "(none)"
            XCTAssertEqual(got, want, text)
        }
    }
}
