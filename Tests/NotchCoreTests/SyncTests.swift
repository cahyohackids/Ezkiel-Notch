import Foundation
import XCTest
@testable import NotchCore

final class SyncTests: XCTestCase {
    static let fixtureDir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        .appendingPathComponent("../fixtures/sync").standardized

    static func fixtures() throws -> [(String, SyncJSON)] {
        let names = try FileManager.default.contentsOfDirectory(atPath: fixtureDir.path).filter { $0.hasSuffix(".json") }.sorted()
        return try names.map { name in
            (name, try XCTUnwrap(SyncJSON.parse(Data(contentsOf: fixtureDir.appendingPathComponent(name))), name))
        }
    }

    static func input(_ fx: SyncJSON) throws -> SyncInput {
        SyncInput(deviceId: fx["deviceId"]?.string ?? "", deviceName: "Test", platform: "mac", appVersion: "1.9.0",
                  now: try XCTUnwrap(SyncTime.date(fx["now"])), local: fx["local"] ?? .object([:]),
                  base: fx["base"] == .null ? nil : fx["base"], remotes: fx["remotes"]?.array ?? [])
    }

    static func text(_ j: SyncJSON?) -> String { String(decoding: (j ?? .null).encoded(), as: UTF8.self) }

    /// Every case in the fixtures the Windows tests read too.
    func testSharedFixtures() throws {
        let all = try Self.fixtures()
        XCTAssertGreaterThanOrEqual(all.count, 7)
        for (name, fx) in all {
            let r = SyncMerge.merge(try Self.input(fx))
            let expect = fx["expect"]
            XCTAssertEqual(r.data, expect?["data"], "\(name) data: \(Self.text(r.data))")
            XCTAssertEqual(r.file["tombstones"], expect?["tombstones"], "\(name) tombstones: \(Self.text(r.file["tombstones"]))")
            let changes = SyncJSON.array(r.changes.map(\.json))
            XCTAssertEqual(changes, expect?["changes"], "\(name) changes: \(Self.text(changes))")
            XCTAssertEqual(SyncJSON.array(r.skipped.map { .string($0) }), expect?["skipped"], name)
            for kind in SyncMerge.kinds { XCTAssertEqual(r.file[kind], r.data[kind], "\(name): the file carries the merged \(kind)") }
            XCTAssertEqual(r.file["written"], fx["now"], name)
            XCTAssertEqual(r.file["deviceId"], fx["deviceId"], name)
        }
    }

    /// Syncing again with nothing new changes nothing.
    func testMergeIsStable() throws {
        for (name, fx) in try Self.fixtures() {
            var input = try Self.input(fx)
            let first = SyncMerge.merge(input)
            input.local = first.data
            input.base = first.file
            let second = SyncMerge.merge(input)
            XCTAssertEqual(second.data, first.data, name)
            XCTAssertEqual(second.changes, [], name)
            XCTAssertFalse(second.changedLocally, name)
            XCTAssertEqual(second.file["tombstones"], first.file["tombstones"], name)
        }
    }

    // MARK: The folder

    private func tempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ezkiel-sync-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: dir) }
        return dir
    }

    private func device(_ id: String, written: String, tasks: [SyncJSON]) -> SyncJSON {
        .object(["format": .number(1), "deviceId": .string(id), "deviceName": .string("\(id) name"), "platform": .string("windows"),
                 "appVersion": .string("1.9.0"), "written": .string(written), "tasks": .array(tasks), "notes": .array([]),
                 "water": .array([]), "focusLog": .array([]), "tombstones": .object([:])])
    }

    private func task(_ id: String, _ title: String, _ modified: String) -> SyncJSON {
        .object(["id": .string(id), "title": .string(title), "done": .bool(false), "reminded": .bool(false),
                 "created": .string("2026-11-01T00:00:00Z"), "modified": .string(modified)])
    }

    func testTwoFakeDevicesInAFolder() throws {
        let root = try tempDir(), state = try tempDir()
        let dir = SyncRunner.directory(root)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try device("PC-1", written: "2026-11-13T11:00:00Z", tasks: [task("C0FFEE00-0000-4000-8000-000000000001", "From PC 1", "2026-11-13T10:00:00Z"),
                                                                     // A newer version's entry this one can't load: left alone.
                                                                     task("not-a-uuid", "Unreadable here", "2026-11-13T10:00:00Z")]).encoded()
            .write(to: dir.appendingPathComponent("PC-1.json"))
        try device("PC-2", written: "2026-11-13T11:30:00Z", tasks: [task("C0FFEE00-0000-4000-8000-000000000002", "From PC 2", "2026-11-13T10:30:00Z")]).encoded()
            .write(to: dir.appendingPathComponent("PC-2.json"))
        try Data("{ half a file".utf8).write(to: dir.appendingPathComponent("broken.json"))

        var data = AppData()
        data.tasks = [TaskItem(title: "On the Mac", created: Date(timeIntervalSince1970: 1_763_000_000))]
        let now = try XCTUnwrap(SyncTime.date(.string("2026-11-13T12:00:00Z")))
        let baseURL = state.appendingPathComponent("sync-base.json")
        let plan = try SyncRunner.plan(root: root, deviceId: "MAC-1", deviceName: "Mac", platform: "mac", appVersion: "1.9.0",
                                       local: data.syncedJSON, base: nil, now: now)
        XCTAssertEqual(plan.devices.map(\.id), ["PC-2", "PC-1"], "newest first")
        XCTAssertEqual(plan.unreadable, ["broken.json"])
        XCTAssertTrue(plan.needsWrite)
        XCTAssertFalse(plan.newDeviceId)
        XCTAssertEqual(plan.result.changes.map(\.change), ["added", "added"])
        XCTAssertTrue(data.applySynced(plan.result.data))
        XCTAssertEqual(Set(data.tasks.map(\.title)), ["From PC 1", "From PC 2", "On the Mac"])
        XCTAssertNotNil(data.tasks.first { $0.title == "On the Mac" }?.modified, "modified is set by the sync")
        try SyncRunner.commit(plan, root: root, baseURL: baseURL)
        XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent("MAC-1.json").path))
        let mine = try XCTUnwrap(SyncRunner.readJSON(dir.appendingPathComponent("MAC-1.json")))
        XCTAssertEqual(mine["tasks"]?.array?.count, 3, "the entry it can't load stays out of this device's file")
        XCTAssertEqual(SyncRunner.readJSON(dir.appendingPathComponent("PC-1.json"))?["tasks"]?.array?.count, 2, "and in PC-1's")

        // Nothing new a minute later: nothing to write, nothing changes.
        let base = SyncRunner.readJSON(baseURL)
        let again = try SyncRunner.plan(root: root, deviceId: "MAC-1", deviceName: "Mac", platform: "mac", appVersion: "1.9.0",
                                        local: data.syncedJSON, base: base, now: now.addingTimeInterval(60))
        XCTAssertFalse(again.needsWrite)
        XCTAssertEqual(again.result.changes, [])
        XCTAssertFalse(data.applySynced(again.result.data))
    }

    /// Another computer cloned from this one writes "our" file: its data is merged and this device takes a new id.
    func testClonedDeviceGetsANewId() throws {
        let root = try tempDir()
        let dir = SyncRunner.directory(root)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let base = device("MAC-1", written: "2026-11-13T10:00:00Z", tasks: [])
        try device("MAC-1", written: "2026-11-13T11:00:00Z", tasks: [task("C0FFEE00-0000-4000-8000-000000000003", "Written by the clone", "2026-11-13T11:00:00Z")]).encoded()
            .write(to: dir.appendingPathComponent("MAC-1.json"))
        let now = try XCTUnwrap(SyncTime.date(.string("2026-11-13T12:00:00Z")))
        let plan = try SyncRunner.plan(root: root, deviceId: "MAC-1", deviceName: "Mac", platform: "mac", appVersion: "1.9.0",
                                       local: AppData().syncedJSON, base: base, now: now)
        XCTAssertTrue(plan.newDeviceId)
        XCTAssertNotEqual(plan.deviceId, "MAC-1")
        XCTAssertEqual(plan.result.data["tasks"]?.array?.count, 1)
    }

    func testMissingFolderIsAPlainError() {
        let missing = URL(fileURLWithPath: "/nonexistent-\(UUID().uuidString)")
        XCTAssertThrowsError(try SyncRunner.plan(root: missing, deviceId: "X", deviceName: "", platform: "", appVersion: "",
                                                 local: AppData().syncedJSON, base: nil)) { error in
            XCTAssertEqual((error as? SyncRunner.Failure)?.message, "The sync folder isn't there. Is the drive connected?")
        }
    }

    // MARK: The model

    func testOldDataLoadsAndModifiedRoundTrips() throws {
        let old = #"{"tasks":[{"id":"6F1D1C9E-0000-4000-8000-000000000001","title":"Old","done":false,"reminded":false,"created":"2026-11-01T00:00:00Z"}],"notes":[],"water":[{"id":"6F1D1C9E-0000-4000-8000-000000000002","date":"2026-11-13T08:00:00Z","ml":250}]}"#
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        var data = try decoder.decode(AppData.self, from: Data(old.utf8))
        XCTAssertEqual(data.tasks.count, 1)
        XCTAssertNil(data.tasks[0].modified)
        XCTAssertNil(data.syncedJSON["tasks"]?.array?.first?["modified"], "nothing is invented before the first sync")

        var merged = data.syncedJSON.object!
        var t = merged["tasks"]!.array![0].object!
        t["modified"] = .string("2026-11-02T00:00:00Z")
        merged["tasks"] = .array([.object(t)])
        XCTAssertTrue(data.applySynced(.object(merged)))
        XCTAssertEqual(data.tasks[0].modified, SyncTime.date(.string("2026-11-02T00:00:00Z")))
        XCTAssertEqual(data.water.count, 1)
        XCTAssertEqual(data.syncedJSON["tasks"]?.array?.first?["modified"], .string("2026-11-02T00:00:00Z"))
    }

    func testConfigIsTolerant() throws {
        let dir = try tempDir()
        let url = dir.appendingPathComponent("sync.json")
        try Data(#"{"enabled":true,"folder":"/x","deviceId":"D","devices":[{"bad":1}],"future":true}"#.utf8).write(to: url)
        let c = SyncConfig.load(url)
        XCTAssertTrue(c.enabled)
        XCTAssertEqual(c.folder, "/x")
        XCTAssertEqual(c.devices, [])
    }
}
