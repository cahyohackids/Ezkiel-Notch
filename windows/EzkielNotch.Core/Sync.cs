using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

// Sync between devices through a folder that a cloud drive syncs: a port of the Mac's NotchCore/SyncMerge.swift and
// SyncRunner.swift. The rules are written there; this file follows them line by line, and the shared fixtures in
// Tests/fixtures/sync check that both give the same answer.

/// <summary>Times in sync files: ISO 8601, compared in whole milliseconds, written in whole seconds UTC.</summary>
public static class SyncTime
{
    private static readonly string[] Formats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    public static long Ms(DateTime d)
    {
        var utc = d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    public static long? Ms(JsonNode? n) =>
        Json.String(n) is { } s && DateTimeOffset.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToUnixTimeMilliseconds() : null;

    public static string String(long ms)
    {
        long seconds = ms >= 0 ? ms / 1000 : -((-ms + 999) / 1000);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    public static DateTime? Date(JsonNode? n) => Ms(n) is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
}

/// <summary>JSON helpers that behave like the Mac's SyncJSON (numbers compare as numbers, keys unordered).</summary>
public static class SyncJson
{
    public static bool Equal(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return Kind(a) == JsonValueKind.Null && Kind(b) == JsonValueKind.Null;
        switch (a)
        {
            case JsonObject oa:
                if (b is not JsonObject ob || oa.Count != ob.Count) return false;
                foreach (var (key, value) in oa)
                    if (!ob.TryGetPropertyValue(key, out var other) || !Equal(value, other)) return false;
                return true;
            case JsonArray aa:
                if (b is not JsonArray ab || aa.Count != ab.Count) return false;
                for (int i = 0; i < aa.Count; i++) if (!Equal(aa[i], ab[i])) return false;
                return true;
            default:
                var ka = Kind(a);
                if (ka != Kind(b)) return false;
                return ka switch
                {
                    JsonValueKind.Number => Number(a) == Number(b),
                    JsonValueKind.String => a.GetValue<string>() == b.GetValue<string>(),
                    _ => true,
                };
        }
    }

    private static JsonValueKind Kind(JsonNode? n) => n?.GetValueKind() ?? JsonValueKind.Null;

    /// <summary>Any JSON number, whether parsed or made in code (an int node doesn't hand out a double).</summary>
    public static double? Number(JsonNode? n) =>
        n is JsonValue && n.GetValueKind() == JsonValueKind.Number
            ? double.Parse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture) : null;

    public static JsonNode? Parse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (Exception) { return null; }
    }

    public static string Text(JsonNode? n, bool pretty = false) =>
        n is null ? "null" : Json.Sorted(n)!.ToJsonString(pretty ? Json.Pretty : new JsonSerializerOptions { Encoder = Json.Pretty.Encoder });
}

/// <summary>One item that changes on this device because of another one.</summary>
public sealed record SyncChange(string Kind, string Id, string Change, string From)
{
    public JsonObject Json() => new() { ["kind"] = Kind, ["id"] = Id, ["change"] = Change, ["from"] = From };
}

public sealed record SyncInput(string DeviceId, DateTime Now, JsonObject Local, JsonObject? Base, IReadOnlyList<JsonNode?> Remotes,
                               string DeviceName = "", string Platform = "", string AppVersion = "");

public sealed record SyncResult(JsonObject Data, JsonObject File, List<SyncChange> Changes, List<string> Skipped, bool ChangedLocally);

public static class SyncMerge
{
    public const int Format = 1;
    public static readonly string[] Kinds = ["tasks", "notes", "water", "focusLog"];
    public const long TombstoneDays = 30;
    private const long Day = 86_400_000;
    private static readonly Dictionary<string, string> IntrinsicKey = new()
    {
        ["tasks"] = "created", ["notes"] = "updated", ["water"] = "date", ["focusLog"] = "start",
    };
    private static readonly Dictionary<string, HashSet<string>> Known = new()
    {
        ["tasks"] = ["id", "title", "done", "due", "reminded", "created", "modified"],
        ["notes"] = ["id", "title", "body", "updated", "modified"],
        ["water"] = ["id", "date", "ml", "modified"],
        ["focusLog"] = ["id", "start", "minutes", "modified"],
    };
    private static readonly Dictionary<string, long> KeepDays = new() { ["water"] = 30, ["focusLog"] = 60 };

    private sealed record Tomb(string Id, long Deleted, long Seen, string By);
    private sealed record Candidate(JsonObject Item, long Modified, string Source, int Order);

    private static string? Key(JsonObject item) => Json.String(item["id"]) is { Length: > 0 } id ? id.ToUpperInvariant() : null;

    private static List<JsonObject> Items(JsonNode? list)
    {
        var seen = new HashSet<string>();
        var output = new List<JsonObject>();
        if (list is not JsonArray a) return output;
        foreach (var value in a)
            if (value is JsonObject item && Key(item) is { } k && seen.Add(k)) output.Add(item);
        return output;
    }

    private static long? Intrinsic(JsonObject item, string kind) => SyncTime.Ms(item[IntrinsicKey[kind]]);

    private static long Modified(JsonObject item, string kind) => SyncTime.Ms(item["modified"]) ?? Intrinsic(item, kind) ?? 0;

    private static bool SameContent(JsonObject a, JsonObject b)
    {
        var x = (JsonObject)a.DeepClone();
        var y = (JsonObject)b.DeepClone();
        x.Remove("modified");
        y.Remove("modified");
        return SyncJson.Equal(x, y);
    }

    private static List<Tomb> Tombs(JsonNode? file, string kind)
    {
        var output = new List<Tomb>();
        if (file?["tombstones"]?[kind] is not JsonArray a) return output;
        foreach (var value in a)
            if (value is JsonObject t && Json.String(t["id"]) is { Length: > 0 } id && SyncTime.Ms(t["deleted"]) is { } deleted)
                output.Add(new Tomb(id, deleted, SyncTime.Ms(t["seen"]) ?? deleted, Json.String(t["by"]) ?? ""));
        return output;
    }

    /// <summary>Ordinal order on upper-cased ids, the same as the Mac's.</summary>
    private static bool Less(string a, string b) => string.CompareOrdinal(a.ToUpperInvariant(), b.ToUpperInvariant()) < 0;
    private static int Compare(string a, string b) => string.CompareOrdinal(a.ToUpperInvariant(), b.ToUpperInvariant());

    private static JsonNode? Get(JsonObject? o, string key) => o is not null && o.TryGetPropertyValue(key, out var v) ? v : null;

    public static SyncResult Merge(SyncInput input)
    {
        long now = SyncTime.Ms(input.Now);
        string me = input.DeviceId;
        long expired = now - TombstoneDays * Day;

        var skipped = new List<string>();
        var remotes = new List<(JsonObject File, string Id, long Written, int Index)>();
        for (int index = 0; index < input.Remotes.Count; index++)
        {
            if (input.Remotes[index] is not JsonObject file || Json.String(file["deviceId"]) is not { Length: > 0 } id)
            {
                skipped.Add("?");
                continue;
            }
            if (SyncJson.Number(file["format"]) is not { } f || f != Math.Round(f) || f < 1 || f > Format) { skipped.Add(id); continue; }
            long written = SyncTime.Ms(file["written"]) ?? 0;
            if (written < expired) { skipped.Add(id); continue; }
            remotes.Add((file, id, written, index));
        }
        remotes.Sort((a, b) =>
        {
            int c = Compare(a.Id, b.Id);
            if (c != 0) return c;
            if (a.Written != b.Written) return b.Written.CompareTo(a.Written);
            return a.Index.CompareTo(b.Index);
        });

        var data = new JsonObject();
        var tombOut = new JsonObject();
        var changes = new List<SyncChange>();
        bool changedLocally = false;

        foreach (var kind in Kinds)
        {
            var known = Known[kind];
            bool isLog = KeepDays.ContainsKey(kind);
            var localItems = Items(Get(input.Local, kind));
            var baseItems = Items(Get(input.Base, kind));
            var baseByKey = new Dictionary<string, JsonObject>();
            foreach (var b in baseItems) baseByKey[Key(b)!] = b;
            var baseTombs = new Dictionary<string, Tomb>();
            foreach (var t in Tombs(input.Base, kind)) baseTombs[t.Id.ToUpperInvariant()] = t;

            // 1. Stamp this device's items.
            var mine = new List<JsonObject>();
            var mineKeys = new HashSet<string>();
            foreach (var local in localItems)
            {
                var k = Key(local)!;
                mineKeys.Add(k);
                JsonObject o;
                if (baseByKey.TryGetValue(k, out var b))
                {
                    o = [];
                    foreach (var (field, value) in b) if (!known.Contains(field)) o[field] = value?.DeepClone();
                    foreach (var (field, value) in local) if (field != "modified") o[field] = value?.DeepClone();
                    long bm = Modified(b, kind);
                    if (SameContent(o, b))
                        o["modified"] = SyncTime.Ms(b["modified"]) is not null ? b["modified"]!.DeepClone() : SyncTime.String(bm);
                    else
                        o["modified"] = SyncTime.String(Math.Max(now, bm + 1000));
                }
                else
                {
                    o = (JsonObject)local.DeepClone();
                    o.Remove("modified");
                    if (input.Base is not null)
                    {
                        // New since the last sync (or back after a delete, e.g. Undo): this device just made it.
                        long m = now;
                        if (baseTombs.TryGetValue(k, out var t)) m = Math.Max(m, Math.Max(t.Deleted + 1000, t.Seen + 1000));
                        o["modified"] = SyncTime.String(m);
                    }
                    else if (SyncTime.Ms(local["modified"]) is not null)
                        o["modified"] = local["modified"]!.DeepClone();
                    else
                        o["modified"] = SyncTime.String(Intrinsic(local, kind) ?? now);
                }
                mine.Add(o);
            }
            var myTombs = baseTombs.Values.Where(t => !mineKeys.Contains(t.Id.ToUpperInvariant())).ToList();
            foreach (var b in baseItems)
            {
                var k = Key(b)!;
                if (mineKeys.Contains(k)) continue;
                // A log entry that aged out was trimmed, not deleted.
                if (KeepDays.TryGetValue(kind, out var keep) && Intrinsic(b, kind) is { } t && t < now - (keep - 2) * Day) continue;
                myTombs.Add(new Tomb(Json.String(b["id"]) ?? k, now, Modified(b, kind), me));
            }

            // 2. Every version of every item, and every tombstone.
            var candidates = new Dictionary<string, List<Candidate>>();
            int order = 0;
            void Add(JsonObject item, string source)
            {
                var k = Key(item)!;
                if (!candidates.TryGetValue(k, out var list)) candidates[k] = list = [];
                list.Add(new Candidate(item, Modified(item, kind), source, order++));
            }
            foreach (var item in mine) Add(item, me);
            foreach (var r in remotes) foreach (var item in Items(r.File[kind])) Add(item, r.Id);

            var allTombs = new List<Tomb>(myTombs);
            foreach (var r in remotes) allTombs.AddRange(Tombs(r.File, kind));
            var tombByKey = new Dictionary<string, (Tomb Best, long Deleted, long Seen)>();
            foreach (var t in allTombs)
            {
                if (t.Deleted < expired) continue;
                var k = t.Id.ToUpperInvariant();
                if (!tombByKey.TryGetValue(k, out var current))
                {
                    tombByKey[k] = (t, t.Deleted, t.Seen);
                    continue;
                }
                var b = current.Best;
                bool better = t.Seen != b.Seen ? t.Seen > b.Seen
                    : t.Deleted != b.Deleted ? t.Deleted > b.Deleted
                    : Less(t.By, b.By);
                tombByKey[k] = (better ? t : b, Math.Max(current.Deleted, t.Deleted), Math.Max(current.Seen, t.Seen));
            }

            // 3. The winner per id; a tombstone wins only over the version it deleted.
            var winners = new Dictionary<string, Candidate>();
            var removedBy = new Dictionary<string, string>();
            foreach (var (k, list) in candidates)
            {
                var w = list[0];
                foreach (var c in list.Skip(1))
                {
                    bool better = c.Modified != w.Modified ? c.Modified > w.Modified
                        : Compare(c.Source, w.Source) != 0 ? Less(c.Source, w.Source)
                        : c.Order < w.Order;
                    if (better) w = c;
                }
                if (tombByKey.TryGetValue(k, out var tomb) && w.Modified <= tomb.Seen)
                {
                    removedBy[k] = tomb.Best.By;
                    continue;
                }
                tombByKey.Remove(k);
                if (KeepDays.TryGetValue(kind, out var keep) && Intrinsic(w.Item, kind) is { } t && t < now - keep * Day) continue;
                var item = (JsonObject)w.Item.DeepClone();
                if (SyncTime.Ms(item["modified"]) is null) item["modified"] = SyncTime.String(w.Modified);
                winners[k] = w with { Item = item };
            }

            // 4. Order: logs by time; tasks and notes keep this device's order, with new ones on top (newest first).
            var mineByKey = mine.ToDictionary(m => Key(m)!);
            List<Candidate> result;
            if (isLog)
            {
                result = [.. winners.Values];
                result.Sort((a, b) =>
                {
                    long ta = Intrinsic(a.Item, kind) ?? 0, tb = Intrinsic(b.Item, kind) ?? 0;
                    return ta != tb ? ta.CompareTo(tb) : Compare(Key(a.Item)!, Key(b.Item)!);
                });
            }
            else
            {
                var fresh = winners.Where(p => !mineByKey.ContainsKey(p.Key)).Select(p => p.Value).ToList();
                fresh.Sort((a, b) =>
                {
                    long ta = Intrinsic(a.Item, kind) ?? 0, tb = Intrinsic(b.Item, kind) ?? 0;
                    return ta != tb ? tb.CompareTo(ta) : Compare(Key(a.Item)!, Key(b.Item)!);
                });
                result = [.. fresh, .. mine.Select(m => winners.GetValueOrDefault(Key(m)!)).OfType<Candidate>()];
            }

            foreach (var c in result)
            {
                var k = Key(c.Item)!;
                string id = Json.String(c.Item["id"]) ?? k;
                if (mineByKey.TryGetValue(k, out var m))
                {
                    if (!SameContent(c.Item, m)) changes.Add(new SyncChange(kind, id, "updated", c.Source));
                }
                else changes.Add(new SyncChange(kind, id, "added", c.Source));
            }
            foreach (var m in mine)
                if (removedBy.TryGetValue(Key(m)!, out var by)) changes.Add(new SyncChange(kind, Json.String(m["id"]) ?? Key(m)!, "removed", by));

            var output = new JsonArray(result.Select(c => (JsonNode?)c.Item.DeepClone()).ToArray());
            if (!SyncJson.Equal(output, Get(input.Local, kind) as JsonArray ?? [])) changedLocally = true;
            data[kind] = output;
            var tombList = tombByKey.OrderBy(p => p.Key, Comparer<string>.Create(Compare)).Select(p => (JsonNode?)new JsonObject
            {
                ["id"] = p.Value.Best.Id,
                ["deleted"] = SyncTime.String(p.Value.Deleted),
                ["seen"] = SyncTime.String(p.Value.Seen),
                ["by"] = p.Value.Best.By,
            }).ToArray();
            tombOut[kind] = new JsonArray(tombList);
        }

        var fileOut = (JsonObject)data.DeepClone();
        fileOut["format"] = Format;
        fileOut["deviceId"] = me;
        fileOut["deviceName"] = input.DeviceName;
        fileOut["platform"] = input.Platform;
        fileOut["appVersion"] = input.AppVersion;
        fileOut["written"] = SyncTime.String(now);
        fileOut["tombstones"] = tombOut;
        return new SyncResult(data, fileOut, changes, skipped, changedLocally);
    }
}

/// <summary>Another device seen in the sync folder.</summary>
public sealed record SyncDevice(string Id, string Name, string Platform, DateTime? Written, bool Ignored);

/// <summary>This device's sync settings, in sync.json next to data.json (never in data.json: a copied data.json must
/// not bring this device's id along).</summary>
public sealed class SyncConfig
{
    public bool Enabled { get; set; }
    /// <summary>The folder the user picked (their cloud drive's). The files go in its EzkielNotch-sync subfolder.</summary>
    public string Folder { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTime? LastSync { get; set; }
    /// <summary>The last problem in plain words (null when the last sync worked).</summary>
    public string? LastError { get; set; }
    public List<SyncDevice> Devices { get; set; } = [];

    public static SyncConfig Load(string path)
    {
        var c = new SyncConfig();
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception) { return c; }
        if (SyncJson.Parse(text) is not JsonObject o) return c;
        c.Enabled = Json.Bool(o["enabled"]) ?? false;
        c.Folder = Json.String(o["folder"]) ?? "";
        c.DeviceId = Json.String(o["deviceId"]) ?? "";
        c.DeviceName = Json.String(o["deviceName"]) ?? "";
        c.LastSync = Json.Date(o["lastSync"]);
        c.LastError = Json.String(o["lastError"]);
        c.Devices = Json.List(o["devices"], d =>
            Json.String(d["id"]) is { } id && Json.Bool(d["ignored"]) is { } ignored
                ? new SyncDevice(id, Json.String(d["name"]) ?? "", Json.String(d["platform"]) ?? "", Json.Date(d["written"]), ignored) : null);
        return c;
    }

    public void Save(string path)
    {
        var o = new JsonObject
        {
            ["enabled"] = Enabled, ["folder"] = Folder, ["deviceId"] = DeviceId, ["deviceName"] = DeviceName,
            ["devices"] = new JsonArray(Devices.Select(d =>
            {
                var j = new JsonObject { ["id"] = d.Id, ["name"] = d.Name, ["platform"] = d.Platform, ["ignored"] = d.Ignored };
                if (d.Written is { } w) j["written"] = Json.Date(w);
                return (JsonNode?)j;
            }).ToArray()),
        };
        if (LastSync is { } at) o["lastSync"] = Json.Date(at);
        if (LastError is not null) o["lastError"] = LastError;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Sorted(o)!.ToJsonString(Json.Pretty), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception) { }
    }
}

/// <summary>Reads the sync folder, merges, and writes this device's file.</summary>
public static class SyncRunner
{
    public const string FolderName = "EzkielNotch-sync";
    /// <summary>Even when nothing changed, a device rewrites its file this often so others see it's still around.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromHours(1);

    public sealed class Failure(string message) : Exception(message);

    public sealed record Plan(SyncResult Result, string DeviceId, bool NewDeviceId, List<SyncDevice> Devices,
                              List<string> Unreadable, int Waiting, bool NeedsWrite);

    public static string Directory(string root) => System.IO.Path.Combine(root, FolderName);

    /// <summary>A device file without the entries this version can't load (see AppData.CanHold).</summary>
    internal static JsonObject Loadable(JsonObject file)
    {
        var copy = (JsonObject)file.DeepClone();
        foreach (var kind in SyncMerge.Kinds)
            if (copy[kind] is JsonArray items)
                copy[kind] = new JsonArray([.. items.Where(i => AppData.CanHold(kind, i)).Select(i => i?.DeepClone())]);
        return copy;
    }

    public static JsonObject? ReadJson(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 20_000_000) return null;
            return SyncJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
        }
        catch (Exception) { return null; }
    }

    public static Plan MakePlan(string root, string deviceId, string deviceName, string platform, string appVersion,
                                JsonObject local, JsonObject? baseFile, DateTime now)
    {
        if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root))
            throw new Failure("The sync folder isn't there. Is the drive connected?");
        var dir = Directory(root);
        try { System.IO.Directory.CreateDirectory(dir); }
        catch (Exception) { throw new Failure("Can't write to the sync folder"); }
        string[] names;
        try { names = [.. System.IO.Directory.GetFiles(dir).Select(p => System.IO.Path.GetFileName(p))]; }
        catch (Exception) { throw new Failure("Can't read the sync folder"); }
        Array.Sort(names, string.CompareOrdinal);

        string ownName = deviceId + ".json";
        var remotes = new List<JsonNode?>();
        JsonObject? own = null;
        var unreadable = new List<string>();
        int waiting = 0;
        foreach (var name in names)
        {
            if (name.StartsWith('.'))
            {
                if (name.EndsWith(".json.icloud", StringComparison.Ordinal)) waiting++;
                continue;
            }
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (ReadJson(System.IO.Path.Combine(dir, name)) is not { } file || Json.String(file["deviceId"]) is null)
            {
                unreadable.Add(name);
                continue;
            }
            if (name == ownName && string.Equals(Json.String(file["deviceId"]), deviceId, StringComparison.OrdinalIgnoreCase)) own = file;
            else remotes.Add(Loadable(file));
        }

        string id = deviceId;
        bool newDeviceId = false;
        if (own is not null && !(baseFile is not null && SyncJson.Equal(own["written"], baseFile["written"])))
        {
            // Not what this device wrote: its own older copy (the base was lost) or another computer cloned from this
            // one. Its data is merged either way; when a base exists, the other computer keeps the file and this
            // device takes a new id.
            remotes.Add(Loadable(own));
            if (baseFile is not null)
            {
                id = Guid.NewGuid().ToString("D").ToUpperInvariant();
                newDeviceId = true;
            }
        }

        var result = SyncMerge.Merge(new SyncInput(id, now, local, baseFile, remotes, deviceName, platform, appVersion));

        var devices = new Dictionary<string, SyncDevice>();
        foreach (var node in remotes)
        {
            if (node is not JsonObject file || Json.String(file["deviceId"]) is not { } fid
                || string.Equals(fid, id, StringComparison.OrdinalIgnoreCase)) continue;
            var written = SyncTime.Date(file["written"]);
            var device = new SyncDevice(fid, Json.String(file["deviceName"]) ?? "", Json.String(file["platform"]) ?? "", written,
                                        result.Skipped.Contains(fid));
            var k = fid.ToUpperInvariant();
            if (devices.TryGetValue(k, out var current) && (current.Written ?? DateTime.MinValue) >= (written ?? DateTime.MinValue)) continue;
            devices[k] = device;
        }
        var deviceList = devices.Values.OrderByDescending(d => d.Written ?? DateTime.MinValue).ToList();

        bool needsWrite = newDeviceId || baseFile is null || own is null;
        if (!needsWrite)
        {
            var a = (JsonObject)result.File.DeepClone();
            var b = (JsonObject)baseFile!.DeepClone();
            a.Remove("written");
            b.Remove("written");
            long baseWritten = SyncTime.Ms(baseFile["written"]) ?? 0;
            needsWrite = !SyncJson.Equal(a, b) || SyncTime.Ms(now) - baseWritten >= (long)Heartbeat.TotalMilliseconds;
        }
        return new Plan(result, id, newDeviceId, deviceList, unreadable, waiting, needsWrite);
    }

    /// <summary>After data.json has the merged lists: the base first, then this device's file.</summary>
    public static void Commit(Plan plan, string root, string basePath)
    {
        if (!plan.NeedsWrite) return;
        var text = SyncJson.Text(plan.Result.File, pretty: true);
        try { WriteAtomic(basePath, text); }
        catch (Exception) { throw new Failure("Couldn't save the sync state"); }
        try { WriteAtomic(System.IO.Path.Combine(Directory(root), plan.DeviceId + ".json"), text); }
        catch (Exception) { throw new Failure("Can't write to the sync folder"); }
    }

    private static void WriteAtomic(string path, string text)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        // A dot name, so other devices never read a half-written file.
        var tmp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "." + System.IO.Path.GetFileName(path) + ".tmp");
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Counts for "what will merge": per list, how many items come in, change and go.</summary>
    public static Dictionary<string, (int Added, int Updated, int Removed)> Summary(IEnumerable<SyncChange> changes)
    {
        var output = new Dictionary<string, (int Added, int Updated, int Removed)>();
        foreach (var c in changes)
        {
            var v = output.GetValueOrDefault(c.Kind);
            output[c.Kind] = c.Change switch
            {
                "added" => (v.Added + 1, v.Updated, v.Removed),
                "updated" => (v.Added, v.Updated + 1, v.Removed),
                _ => (v.Added, v.Updated, v.Removed + 1),
            };
        }
        return output;
    }
}
