using System.Text.Json.Nodes;
using EzkielNotch.Core;
using Xunit;

namespace EzkielNotch.Core.Tests;

public class SyncTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sync");

    private static List<(string Name, JsonObject Fx)> Fixtures() =>
        [.. Directory.GetFiles(FixtureDir, "*.json").OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (Path.GetFileName(p), JsonNode.Parse(File.ReadAllText(p))!.AsObject()))];

    private static SyncInput Input(JsonObject fx) =>
        new(fx["deviceId"]!.GetValue<string>(), SyncTime.Date(fx["now"])!.Value, fx["local"]!.AsObject(),
            fx["base"] as JsonObject, [.. fx["remotes"]!.AsArray()], "Test", "windows", "1.9.0");

    /// <summary>Every case in the fixtures the Mac's tests read too.</summary>
    [Fact]
    public void SharedFixtures()
    {
        var all = Fixtures();
        Assert.True(all.Count >= 7, $"{all.Count} fixtures in {FixtureDir}");
        var failures = new List<string>();
        foreach (var (name, fx) in all)
        {
            var r = SyncMerge.Merge(Input(fx));
            var expect = fx["expect"]!;
            void Same(string what, JsonNode? got, JsonNode? want)
            {
                if (!SyncJson.Equal(got, want)) failures.Add($"{name} {what}: got {SyncJson.Text(got)}\n   want {SyncJson.Text(want)}");
            }
            Same("data", r.Data, expect["data"]);
            Same("tombstones", r.File["tombstones"], expect["tombstones"]);
            Same("changes", new JsonArray([.. r.Changes.Select(c => (JsonNode?)c.Json())]), expect["changes"]);
            Same("skipped", new JsonArray([.. r.Skipped.Select(s => (JsonNode?)s)]), expect["skipped"]);
            foreach (var kind in SyncMerge.Kinds) Same($"file {kind}", r.File[kind], r.Data[kind]);
            Same("written", r.File["written"], fx["now"]);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Syncing again with nothing new changes nothing.</summary>
    [Fact]
    public void MergeIsStable()
    {
        foreach (var (name, fx) in Fixtures())
        {
            var input = Input(fx);
            var first = SyncMerge.Merge(input);
            var second = SyncMerge.Merge(input with { Local = (JsonObject)first.Data.DeepClone(), Base = (JsonObject)first.File.DeepClone() });
            Assert.True(SyncJson.Equal(second.Data, first.Data), name);
            Assert.Empty(second.Changes);
            Assert.False(second.ChangedLocally, name);
            Assert.True(SyncJson.Equal(second.File["tombstones"], first.File["tombstones"]), name);
        }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ezkiel-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static JsonObject Device(string id, string written, params JsonObject[] tasks) => new()
    {
        ["format"] = 1, ["deviceId"] = id, ["deviceName"] = id + " name", ["platform"] = "mac", ["appVersion"] = "1.9.0",
        ["written"] = written, ["tasks"] = new JsonArray(tasks), ["notes"] = new JsonArray(), ["water"] = new JsonArray(),
        ["focusLog"] = new JsonArray(), ["tombstones"] = new JsonObject(),
    };

    private static JsonObject Task(string id, string title, string modified) => new()
    {
        ["id"] = id, ["title"] = title, ["done"] = false, ["reminded"] = false, ["created"] = "2026-11-01T00:00:00Z", ["modified"] = modified,
    };

    [Fact]
    public void TwoFakeDevicesInAFolder()
    {
        string root = TempDir(), state = TempDir();
        try
        {
            var dir = SyncRunner.Directory(root);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "MAC-1.json"), Device("MAC-1", "2026-11-13T11:00:00Z",
                Task("C0FFEE00-0000-4000-8000-000000000001", "From Mac 1", "2026-11-13T10:00:00Z"),
                // A newer version's entry this one can't load: left alone.
                Task("not-a-guid", "Unreadable here", "2026-11-13T10:00:00Z")).ToJsonString());
            File.WriteAllText(Path.Combine(dir, "MAC-2.json"), Device("MAC-2", "2026-11-13T11:30:00Z",
                Task("C0FFEE00-0000-4000-8000-000000000002", "From Mac 2", "2026-11-13T10:30:00Z")).ToJsonString());
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ half a file");

            var data = new AppData();
            data.Tasks.Add(new TaskItem(Guid.NewGuid(), "On the PC", false, null, false, new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc)));
            var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
            var basePath = Path.Combine(state, "sync-base.json");
            var plan = SyncRunner.MakePlan(root, "PC-1", "PC", "windows", "1.9.0", data.SyncedJson(), null, now);
            Assert.Equal(new[] { "MAC-2", "MAC-1" }, plan.Devices.Select(d => d.Id));
            Assert.Equal(new[] { "broken.json" }, plan.Unreadable);
            Assert.True(plan.NeedsWrite);
            Assert.False(plan.NewDeviceId);
            Assert.Equal(new[] { "added", "added" }, plan.Result.Changes.Select(c => c.Change));
            Assert.True(data.ApplySynced(plan.Result.Data));
            Assert.Equal(new[] { "From Mac 1", "From Mac 2", "On the PC" }, data.Tasks.Select(t => t.Title).Order());
            Assert.NotNull(data.Tasks.Single(t => t.Title == "On the PC").Modified);
            SyncRunner.Commit(plan, root, basePath);
            Assert.True(File.Exists(Path.Combine(dir, "PC-1.json")));
            Assert.Equal(3, SyncRunner.ReadJson(Path.Combine(dir, "PC-1.json"))!["tasks"]!.AsArray().Count);
            Assert.Equal(2, SyncRunner.ReadJson(Path.Combine(dir, "MAC-1.json"))!["tasks"]!.AsArray().Count);
            Assert.False(Directory.GetFiles(dir).Any(f => Path.GetFileName(f).StartsWith('.')), "no temp file left behind");

            // Nothing new a minute later: nothing to write, nothing changes.
            var again = SyncRunner.MakePlan(root, "PC-1", "PC", "windows", "1.9.0", data.SyncedJson(), SyncRunner.ReadJson(basePath), now.AddMinutes(1));
            Assert.False(again.NeedsWrite);
            Assert.Empty(again.Result.Changes);
            Assert.False(data.ApplySynced(again.Result.Data));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(state, true);
        }
    }

    [Fact]
    public void ClonedDeviceGetsANewId()
    {
        var root = TempDir();
        try
        {
            var dir = SyncRunner.Directory(root);
            Directory.CreateDirectory(dir);
            var baseFile = Device("PC-1", "2026-11-13T10:00:00Z");
            File.WriteAllText(Path.Combine(dir, "PC-1.json"), Device("PC-1", "2026-11-13T11:00:00Z",
                Task("C0FFEE00-0000-4000-8000-000000000003", "Written by the clone", "2026-11-13T11:00:00Z")).ToJsonString());
            var plan = SyncRunner.MakePlan(root, "PC-1", "PC", "windows", "1.9.0", new AppData().SyncedJson(), baseFile,
                                           new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc));
            Assert.True(plan.NewDeviceId);
            Assert.NotEqual("PC-1", plan.DeviceId);
            Assert.Single(plan.Result.Data["tasks"]!.AsArray());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingFolderIsAPlainError()
    {
        var e = Assert.Throws<SyncRunner.Failure>(() =>
            SyncRunner.MakePlan(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid()), "X", "", "", "", new AppData().SyncedJson(), null, DateTime.UtcNow));
        Assert.Equal("The sync folder isn't there. Is the drive connected?", e.Message);
    }

    [Fact]
    public void OldDataLoadsAndModifiedRoundTrips()
    {
        var d = AppData.FromJson("""{"tasks":[{"id":"6F1D1C9E-0000-4000-8000-000000000001","title":"Old","done":false,"reminded":false,"created":"2026-11-01T00:00:00Z"}],"water":[{"id":"6F1D1C9E-0000-4000-8000-000000000002","date":"2026-11-13T08:00:00Z","ml":250}]}""")!;
        Assert.Single(d.Tasks);
        Assert.Null(d.Tasks[0].Modified);
        Assert.False(d.SyncedJson()["tasks"]![0]!.AsObject().ContainsKey("modified"), "nothing is invented before the first sync");

        var merged = d.SyncedJson();
        merged["tasks"]![0]!["modified"] = "2026-11-02T00:00:00Z";
        Assert.True(d.ApplySynced(merged));
        Assert.Equal(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), d.Tasks[0].Modified);
        Assert.Single(d.Water);
        Assert.Equal("2026-11-02T00:00:00Z", (string?)d.SyncedJson()["tasks"]![0]!["modified"]);
    }

    [Fact]
    public void ConfigIsTolerant()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "sync.json");
            File.WriteAllText(path, """{"enabled":true,"folder":"C:\\x","deviceId":"D","devices":[{"bad":1}],"future":true}""");
            var c = SyncConfig.Load(path);
            Assert.True(c.Enabled);
            Assert.Equal("C:\\x", c.Folder);
            Assert.Empty(c.Devices);
            c.Devices.Add(new SyncDevice("M", "Mac", "mac", new DateTime(2026, 11, 13, 1, 2, 3, DateTimeKind.Utc), false));
            c.Save(path);
            Assert.Equal("Mac", SyncConfig.Load(path).Devices.Single().Name);
        }
        finally { Directory.Delete(dir, true); }
    }
}
