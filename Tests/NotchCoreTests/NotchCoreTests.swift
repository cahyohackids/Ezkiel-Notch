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
