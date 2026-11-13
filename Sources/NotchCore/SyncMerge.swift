import Foundation

/// Sync between devices through a folder that a cloud drive syncs. No server: every device writes only its own
/// file, `EzkielNotch-sync/<deviceId>.json`, and reads everyone else's. A file is a full copy of the synced data
/// (tasks, notes, water, focus log) with a `modified` time on every item and tombstones for deletions, so any one
/// file is enough to rebuild from and an old or duplicated file can't do harm.
///
/// The rules (the Windows app's EzkielNotch.Core/Sync.cs follows them line by line; the shared fixtures in
/// Tests/fixtures/sync check both give the same answer):
/// - Stamping. This device's data is compared with what it last wrote (the base). An item whose content changed
///   gets `modified = max(now, previous modified + 1 s)`, so an edit always beats the version it was made on, even
///   when another device's clock runs ahead. Unchanged items keep their time. An item that left since the base
///   becomes a tombstone that remembers which version was deleted (`seen`). Fields this version doesn't know are
///   carried over from the base.
/// - Merging. Per item id the newest `modified` wins; equal times go to the device id that sorts first.
/// - Deletion. A tombstone removes an item only when the surviving version is the one that was deleted (or older).
///   An edit the deleting device never saw keeps the item, so a delete never eats someone's later work.
/// - Tombstones and device files older than 30 days are ignored. Water and focus entries are logs: a union by id
///   (never counted twice), trimmed to the window the apps keep (30 and 60 days); trimming is not deleting.
public enum SyncMerge {
    public static let format = 1
    public static let kinds = ["tasks", "notes", "water", "focusLog"]
    public static let tombstoneDays: Int64 = 30
    static let day: Int64 = 86_400_000
    /// The time an item without `modified` falls back to.
    static let intrinsicKey = ["tasks": "created", "notes": "updated", "water": "date", "focusLog": "start"]
    /// Fields this version writes. Anything else on a synced item came from a newer version and is kept.
    static let known: [String: Set<String>] = [
        "tasks": ["id", "title", "done", "due", "reminded", "created", "modified"],
        "notes": ["id", "title", "body", "updated", "modified"],
        "water": ["id", "date", "ml", "modified"],
        "focusLog": ["id", "start", "minutes", "modified"],
    ]
    /// Logs keep this many days (the smaller of the two apps' pruning).
    static let keepDays: [String: Int64] = ["water": 30, "focusLog": 60]

    typealias Item = [String: SyncJSON]

    struct Tomb {
        var id: String
        var deleted: Int64
        var seen: Int64
        var by: String
    }

    struct Candidate {
        var item: Item
        var modified: Int64
        var source: String
        var order: Int
    }

    // MARK: Helpers

    static func key(_ item: Item) -> String? {
        guard let id = item["id"]?.string, !id.isEmpty else { return nil }
        return id.uppercased()
    }

    /// The objects with an id, first one wins when an id repeats.
    static func items(_ list: SyncJSON?) -> [Item] {
        var seen = Set<String>()
        var out: [Item] = []
        for value in list?.array ?? [] {
            guard let item = value.object, let k = key(item), !seen.contains(k) else { continue }
            seen.insert(k)
            out.append(item)
        }
        return out
    }

    static func intrinsic(_ item: Item, _ kind: String) -> Int64? { SyncTime.ms(item[intrinsicKey[kind] ?? ""]) }

    static func modified(_ item: Item, _ kind: String) -> Int64 {
        SyncTime.ms(item["modified"]) ?? intrinsic(item, kind) ?? 0
    }

    static func sameContent(_ a: Item, _ b: Item) -> Bool {
        var a = a, b = b
        a["modified"] = nil
        b["modified"] = nil
        return a == b
    }

    static func tombs(_ file: SyncJSON?, _ kind: String) -> [Tomb] {
        var out: [Tomb] = []
        for value in file?["tombstones"]?[kind]?.array ?? [] {
            guard let t = value.object, let id = t["id"]?.string, !id.isEmpty, let deleted = SyncTime.ms(t["deleted"]) else { continue }
            out.append(Tomb(id: id, deleted: deleted, seen: SyncTime.ms(t["seen"]) ?? deleted, by: t["by"]?.string ?? ""))
        }
        return out
    }

    /// Ordinal order on upper-cased ids, the same as C#'s string.CompareOrdinal.
    static func less(_ a: String, _ b: String) -> Bool { Array(a.uppercased().utf16).lexicographicallyPrecedes(Array(b.uppercased().utf16)) }

    // MARK: Merge

    public static func merge(_ input: SyncInput) -> SyncResult {
        let now = SyncTime.ms(input.now)
        let me = input.deviceId
        let expired = now - tombstoneDays * day

        // Other devices' files: newer formats and files nobody wrote for 30 days are left out.
        var skipped: [String] = []
        var remotes: [(file: SyncJSON, id: String, written: Int64, index: Int)] = []
        for (index, file) in input.remotes.enumerated() {
            guard let id = file["deviceId"]?.string, !id.isEmpty else { skipped.append("?"); continue }
            guard let f = file["format"]?.int, f >= 1, f <= format else { skipped.append(id); continue }
            let written = SyncTime.ms(file["written"]) ?? 0
            guard written >= expired else { skipped.append(id); continue }
            remotes.append((file, id, written, index))
        }
        remotes.sort { a, b in
            if a.id.uppercased() != b.id.uppercased() { return less(a.id, b.id) }
            if a.written != b.written { return a.written > b.written }
            return a.index < b.index
        }

        var data: [String: SyncJSON] = [:]
        var tombOut: [String: SyncJSON] = [:]
        var changes: [SyncChange] = []
        var changedLocally = false

        for kind in kinds {
            let known = self.known[kind] ?? []
            let isLog = keepDays[kind] != nil
            let localItems = items(input.local[kind])
            let baseItems = items(input.base?[kind])
            var baseByKey: [String: Item] = [:]
            for b in baseItems { if let k = key(b) { baseByKey[k] = b } }
            var baseTombs: [String: Tomb] = [:]
            for t in tombs(input.base, kind) { baseTombs[t.id.uppercased()] = t }

            // 1. Stamp this device's items.
            var mine: [Item] = []
            var mineKeys = Set<String>()
            for local in localItems {
                guard let k = key(local) else { continue }
                mineKeys.insert(k)
                var o: Item
                if let b = baseByKey[k] {
                    o = b.filter { !known.contains($0.key) }
                    for (field, value) in local where field != "modified" { o[field] = value }
                    let bm = modified(b, kind)
                    if sameContent(o, b) {
                        o["modified"] = SyncTime.ms(b["modified"]) != nil ? b["modified"] : .string(SyncTime.string(bm))
                    } else {
                        o["modified"] = .string(SyncTime.string(max(now, bm + 1000)))
                    }
                } else {
                    o = local
                    o["modified"] = nil
                    if input.base != nil {
                        // New since the last sync (or back after a delete, e.g. Undo): this device just made it.
                        var m = now
                        if let t = baseTombs[k] { m = max(m, t.deleted + 1000, t.seen + 1000) }
                        o["modified"] = .string(SyncTime.string(m))
                    } else if SyncTime.ms(local["modified"]) != nil {
                        o["modified"] = local["modified"]
                    } else {
                        o["modified"] = .string(SyncTime.string(intrinsic(local, kind) ?? now))
                    }
                }
                mine.append(o)
            }
            var myTombs: [Tomb] = baseTombs.values.filter { !mineKeys.contains($0.id.uppercased()) }
            for b in baseItems {
                guard let k = key(b), !mineKeys.contains(k) else { continue }
                // A log entry that aged out was trimmed, not deleted.
                if let keep = keepDays[kind], let t = intrinsic(b, kind), t < now - (keep - 2) * day { continue }
                myTombs.append(Tomb(id: b["id"]?.string ?? k, deleted: now, seen: modified(b, kind), by: me))
            }

            // 2. Every version of every item, and every tombstone.
            var candidates: [String: [Candidate]] = [:]
            var order = 0
            func add(_ item: Item, source: String) {
                guard let k = key(item) else { return }
                candidates[k, default: []].append(Candidate(item: item, modified: modified(item, kind), source: source, order: order))
                order += 1
            }
            for item in mine { add(item, source: me) }
            for r in remotes { for item in items(r.file[kind]) { add(item, source: r.id) } }

            var allTombs = myTombs
            for r in remotes { allTombs += tombs(r.file, kind) }
            var tombByKey: [String: (best: Tomb, deleted: Int64, seen: Int64)] = [:]
            for t in allTombs where t.deleted >= expired {
                let k = t.id.uppercased()
                guard let current = tombByKey[k] else {
                    tombByKey[k] = (t, t.deleted, t.seen)
                    continue
                }
                let b = current.best
                let better = t.seen != b.seen ? t.seen > b.seen
                    : t.deleted != b.deleted ? t.deleted > b.deleted
                    : less(t.by, b.by)
                tombByKey[k] = (better ? t : b, max(current.deleted, t.deleted), max(current.seen, t.seen))
            }

            // 3. The winner per id; a tombstone wins only over the version it deleted.
            var winners: [String: Candidate] = [:]
            var removedBy: [String: String] = [:]
            for (k, list) in candidates {
                let w = list.min { a, b in
                    if a.modified != b.modified { return a.modified > b.modified }
                    if a.source.uppercased() != b.source.uppercased() { return less(a.source, b.source) }
                    return a.order < b.order
                }!
                if let t = tombByKey[k], w.modified <= t.seen {
                    removedBy[k] = t.best.by
                    continue
                }
                tombByKey[k] = nil
                if let keep = keepDays[kind], let t = intrinsic(w.item, kind), t < now - keep * day { continue }
                var item = w.item
                if SyncTime.ms(item["modified"]) == nil { item["modified"] = .string(SyncTime.string(w.modified)) }
                winners[k] = Candidate(item: item, modified: w.modified, source: w.source, order: w.order)
            }

            // 4. Order: logs by time; tasks and notes keep this device's order, with new ones on top (newest first).
            var mineByKey: [String: Item] = [:]
            for m in mine { if let k = key(m) { mineByKey[k] = m } }
            var result: [Candidate]
            if isLog {
                result = winners.values.sorted { a, b in
                    let ta = intrinsic(a.item, kind) ?? 0, tb = intrinsic(b.item, kind) ?? 0
                    if ta != tb { return ta < tb }
                    return less(key(a.item)!, key(b.item)!)
                }
            } else {
                let fresh = winners.filter { mineByKey[$0.key] == nil }.values.sorted { a, b in
                    let ta = intrinsic(a.item, kind) ?? 0, tb = intrinsic(b.item, kind) ?? 0
                    if ta != tb { return ta > tb }
                    return less(key(a.item)!, key(b.item)!)
                }
                result = fresh + mine.compactMap { key($0).flatMap { winners[$0] } }
            }

            for c in result {
                let k = key(c.item)!
                if let m = mineByKey[k] {
                    if !sameContent(c.item, m) { changes.append(SyncChange(kind: kind, id: c.item["id"]?.string ?? k, change: "updated", from: c.source)) }
                } else {
                    changes.append(SyncChange(kind: kind, id: c.item["id"]?.string ?? k, change: "added", from: c.source))
                }
            }
            for m in mine {
                guard let k = key(m), let by = removedBy[k] else { continue }
                changes.append(SyncChange(kind: kind, id: m["id"]?.string ?? k, change: "removed", from: by))
            }

            let out = result.map { SyncJSON.object($0.item) }
            data[kind] = .array(out)
            if out != (input.local[kind]?.array ?? []) { changedLocally = true }
            let tombList = tombByKey.sorted { less($0.key, $1.key) }.map { _, t in
                SyncJSON.object(["id": .string(t.best.id), "deleted": .string(SyncTime.string(t.deleted)),
                                 "seen": .string(SyncTime.string(t.seen)), "by": .string(t.best.by)])
            }
            tombOut[kind] = .array(tombList)
        }

        var file = data
        file["format"] = .number(Double(format))
        file["deviceId"] = .string(me)
        file["deviceName"] = .string(input.deviceName)
        file["platform"] = .string(input.platform)
        file["appVersion"] = .string(input.appVersion)
        file["written"] = .string(SyncTime.string(now))
        file["tombstones"] = .object(tombOut)
        return SyncResult(data: .object(data), file: .object(file), changes: changes, skipped: skipped, changedLocally: changedLocally)
    }
}

public struct SyncInput {
    public var deviceId: String
    public var deviceName: String
    public var platform: String
    public var appVersion: String
    public var now: Date
    /// `{ "tasks": [...], "notes": [...], "water": [...], "focusLog": [...] }` as data.json has them.
    public var local: SyncJSON
    /// What this device wrote last time (nil before its first sync).
    public var base: SyncJSON?
    /// Other devices' files.
    public var remotes: [SyncJSON]

    public init(deviceId: String, deviceName: String = "", platform: String = "", appVersion: String = "", now: Date,
                local: SyncJSON, base: SyncJSON?, remotes: [SyncJSON]) {
        self.deviceId = deviceId
        self.deviceName = deviceName
        self.platform = platform
        self.appVersion = appVersion
        self.now = now
        self.local = local
        self.base = base
        self.remotes = remotes
    }
}

/// One item that changes on this device because of another one.
public struct SyncChange: Equatable {
    public var kind: String
    public var id: String
    /// "added", "updated" or "removed".
    public var change: String
    /// The device the change came from.
    public var from: String

    public init(kind: String, id: String, change: String, from: String) {
        self.kind = kind
        self.id = id
        self.change = change
        self.from = from
    }

    public var json: SyncJSON {
        .object(["kind": .string(kind), "id": .string(id), "change": .string(change), "from": .string(from)])
    }
}

public struct SyncResult {
    /// The merged lists to put into data.json.
    public var data: SyncJSON
    /// This device's new file (and next base).
    public var file: SyncJSON
    public var changes: [SyncChange]
    /// Device ids whose files were left out (a newer format, or nobody wrote them for 30 days).
    public var skipped: [String]
    /// The merged lists differ from the local ones in any way (also just new `modified` times).
    public var changedLocally: Bool
}
