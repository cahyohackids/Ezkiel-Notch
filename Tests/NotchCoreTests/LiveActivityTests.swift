import Foundation
@testable import NotchCore
import XCTest

final class TimerPhraseTests: XCTestCase {
    private func fixture() throws -> [String: Any] {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appendingPathComponent("../fixtures/timer-phrases.json").standardized
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
    }

    /// Every case in the fixture the Windows tests read too.
    func testSharedFixture() throws {
        let root = try fixture()
        let cases = root["cases"] as? [[String: Any]] ?? []
        XCTAssertGreaterThan(cases.count, 20)
        for c in cases {
            let text = c["text"] as? String ?? ""
            let r = TimerPhrase.parse(text)
            let got = r.map { "\($0.label) · \($0.seconds)" } ?? "(none)"
            let want = (c["seconds"] as? Int).map { "\(c["label"] as? String ?? "") · \($0)" } ?? "(none)"
            XCTAssertEqual(got, want, text)
        }
        for c in root["commands"] as? [[String: Any]] ?? [] {
            let text = c["text"] as? String ?? ""
            XCTAssertEqual(TimerPhrase.command(text) != nil, c["timer"] as? Bool ?? false, text)
        }
    }
}

final class QuickTimerTests: XCTestCase {
    func testSoonestAndDue() {
        let now = Date(timeIntervalSince1970: 1_000_000)
        let tea = QuickTimer.start("teh", seconds: 600, now: now)
        let soup = QuickTimer.start("sup", seconds: 120, now: now)
        let done = QuickTimer(label: "done", seconds: 60, ends: now.addingTimeInterval(-30))
        let old = QuickTimer(label: "old", seconds: 60, ends: now.addingTimeInterval(-7200))
        XCTAssertEqual(QuickTimers.soonest([tea, soup, done], now: now)?.label, "sup")
        let due = QuickTimers.due([tea, done, old], now: now)
        XCTAssertEqual(due.running.map(\.label), ["teh"])
        XCTAssertEqual(due.ring.map(\.label), ["done"])
        XCTAssertEqual(due.stale.map(\.label), ["old"])
        XCTAssertEqual(tea.remaining(at: now), 600)
        XCTAssertEqual(done.remaining(at: now), 0)
    }

    func testShort() {
        XCTAssertEqual(QuickTimers.short(600), "10m")
        XCTAssertEqual(QuickTimers.short(5400), "1h30m")
        XCTAssertEqual(QuickTimers.short(7200), "2h")
        XCTAssertEqual(QuickTimers.short(45), "45s")
        XCTAssertEqual(QuickTimers.short(150), "2m30s")
    }

    /// Timers survive a restart; a damaged one costs only itself, and old files without the key still load.
    func testTimersRoundTripTolerantly() throws {
        let json = """
        {"timers":[{"id":"6F9619FF-8B86-D011-B42D-00C04FC964FF","label":"teh","seconds":600,"ends":"2026-11-13T10:00:00Z"},
                   {"label":"broken"}],
         "settings":{"timerSound":"Hero","liveDownloads":true}}
        """
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let data = try decoder.decode(AppData.self, from: Data(json.utf8))
        XCTAssertEqual(data.timers.map(\.label), ["teh"])
        XCTAssertEqual(data.timers.first?.seconds, 600)
        XCTAssertEqual(data.settings.timerSound, "Hero")
        XCTAssertTrue(data.settings.liveDownloads)
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let again = try decoder.decode(AppData.self, from: encoder.encode(data))
        XCTAssertEqual(again.timers, data.timers)
        let empty = try decoder.decode(AppData.self, from: Data("{}".utf8))
        XCTAssertTrue(empty.timers.isEmpty)
        XCTAssertFalse(empty.settings.liveDownloads)
        XCTAssertEqual(empty.settings.timerSound, "Glass")
        XCTAssertEqual(TimerSounds.normalize("Klaxon"), "Glass")
        XCTAssertEqual(TimerSounds.normalize("none"), "none")
    }
}

final class DownloadTrackerTests: XCTestCase {
    func testFinalNames() {
        XCTAssertEqual(DownloadTracker.finalName("movie.mp4.crdownload"), "movie.mp4")
        XCTAssertEqual(DownloadTracker.finalName("Setup.exe.part"), "Setup.exe")
        XCTAssertEqual(DownloadTracker.finalName("photos.zip.download"), "photos.zip")
        XCTAssertEqual(DownloadTracker.finalName("Unconfirmed 123.crdownload"), "Unconfirmed 123")
        XCTAssertNil(DownloadTracker.finalName("notes.txt"))
        XCTAssertNil(DownloadTracker.finalName(".crdownload"))
        XCTAssertNil(DownloadTracker.finalName("archive"))
    }

    func testSpeedProgressAndFinish() {
        var t = DownloadTracker()
        let start = Date(timeIntervalSince1970: 0)
        // Chrome: no total, so no fraction; Safari: a total.
        var finished = t.update([DownloadSample(name: "a.zip.crdownload", finalName: "a.zip", bytes: 1_000_000),
                                 DownloadSample(name: "b.dmg.download", finalName: "b.dmg", bytes: 500, total: 1000)],
                                completed: [], now: start)
        XCTAssertEqual(finished, [])
        XCTAssertNil(t.active.first { $0.finalName == "a.zip" }?.bytesPerSecond)
        XCTAssertNil(t.active.first { $0.finalName == "a.zip" }?.fraction)
        XCTAssertEqual(t.active.first { $0.finalName == "b.dmg" }?.fraction, 0.5)

        finished = t.update([DownloadSample(name: "a.zip.crdownload", finalName: "a.zip", bytes: 3_000_000),
                             DownloadSample(name: "b.dmg.download", finalName: "b.dmg", bytes: 900, total: 1000)],
                            completed: [], now: start.addingTimeInterval(1))
        XCTAssertEqual(t.active.first { $0.finalName == "a.zip" }?.bytesPerSecond, 2_000_000)
        // The largest leads the pill.
        XCTAssertEqual(DownloadTracker.lead(t.active)?.download.finalName, "a.zip")
        XCTAssertEqual(DownloadTracker.lead(t.active)?.count, 2)

        // a.zip lands; b.dmg's bundle vanishes without a file (cancelled): only a.zip finished.
        finished = t.update([], completed: ["a.zip", "other.pdf"], now: start.addingTimeInterval(2))
        XCTAssertEqual(finished, ["a.zip"])
        XCTAssertTrue(t.active.isEmpty)
        XCTAssertNil(DownloadTracker.lead(t.active))
    }

    /// Chrome first writes "Unconfirmed 123.crdownload", then renames it: that is not a finished download.
    func testRenameIsNotFinish() {
        var t = DownloadTracker()
        let now = Date(timeIntervalSince1970: 0)
        _ = t.update([DownloadSample(name: "Unconfirmed 1.crdownload", finalName: "Unconfirmed 1", bytes: 10)], completed: [], now: now)
        let finished = t.update([DownloadSample(name: "song.mp3.crdownload", finalName: "song.mp3", bytes: 20)], completed: [],
                                now: now.addingTimeInterval(1))
        XCTAssertEqual(finished, [])
        XCTAssertEqual(t.active.map(\.finalName), ["song.mp3"])
    }

    func testFormatting() {
        XCTAssertEqual(DownloadTracker.bytes(512), "512 B")
        XCTAssertEqual(DownloadTracker.bytes(820_000), "820 KB")
        XCTAssertEqual(DownloadTracker.bytes(12_400_000), "12.4 MB")
        XCTAssertEqual(DownloadTracker.bytes(512_000_000), "512 MB")
        XCTAssertEqual(DownloadTracker.bytes(1_250_000_000), "1.25 GB")
        XCTAssertEqual(DownloadTracker.speed(1_200_000), "1.2 MB/s")
    }
}

final class LivePillTests: XCTestCase {
    func testOrder() {
        XCTAssertEqual(LivePill.pick([.music, .timer, .download]), .timer)
        XCTAssertEqual(LivePill.pick([.music, .meeting, .focus]), .focus)
        XCTAssertEqual(LivePill.pick([.download, .agent, .unread]), .download)
        XCTAssertEqual(LivePill.pick([.water, .charging]), .charging)
        XCTAssertNil(LivePill.pick([]))
    }

    func testMeetingMinutes() {
        let now = Date(timeIntervalSince1970: 0)
        XCTAssertEqual(LivePill.meetingMinutes(start: now.addingTimeInterval(400), now: now), 7)
        XCTAssertEqual(LivePill.meetingMinutes(start: now.addingTimeInterval(600), now: now), 10)
        XCTAssertEqual(LivePill.meetingMinutes(start: now.addingTimeInterval(20), now: now), 1)
        XCTAssertNil(LivePill.meetingMinutes(start: now.addingTimeInterval(601), now: now))
        XCTAssertNil(LivePill.meetingMinutes(start: now.addingTimeInterval(-5), now: now))
    }
}
