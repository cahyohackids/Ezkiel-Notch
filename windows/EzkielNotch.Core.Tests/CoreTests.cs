using System.Text.Json.Nodes;
using EzkielNotch.Core;
using Xunit;

namespace EzkielNotch.Core.Tests;

public class DataFormatTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ReadsAMacFileAndSkipsOnlyDamagedEntries()
    {
        var d = AppData.FromJson(Fixture("mac-data.json"))!;
        Assert.Single(d.Tasks);
        Assert.Equal("Kirim laporan", d.Tasks[0].Title);
        Assert.Equal(new DateTime(2026, 10, 26, 5, 0, 0, DateTimeKind.Utc), d.Tasks[0].Due);
        Assert.Single(d.Notes);
        Assert.Equal("Beli kopi", d.Notes[0].DisplayTitle("Untitled note"));
        Assert.Single(d.Clips);
        Assert.Equal(350, d.Water.Single().Ml);
        Assert.Equal(25, d.FocusLog.Single().Minutes);
        Assert.Equal(FocusMode.Focus, d.FocusState!.Mode);
        Assert.Equal(1500, d.FocusState.Duration);
        Assert.Equal(45, d.Settings.FocusMinutes);
        Assert.Equal(10, d.Settings.BreakMinutes);
        Assert.Equal(2500, d.Settings.WaterGoal);
        Assert.Equal(AppLanguage.Id, d.Settings.Language);
        Assert.True(d.Settings.ExpandOnHover);   // missing → default
    }

    [Fact]
    public void WritingKeepsWhatWindowsDoesNotUse()
    {
        var d = AppData.FromJson(Fixture("mac-data.json"))!;
        d.Tasks.Add(TaskItem.New("baru"));
        var o = JsonNode.Parse(d.ToJson())!.AsObject();
        Assert.Equal("1.5.0", (string?)o["announcedVersion"]);
        Assert.True((bool)o["onboarded"]!);
        Assert.Single(o["shelf"]!.AsArray());
        Assert.Single(o["scoutLog"]!.AsArray());
        var s = o["settings"]!.AsObject();
        Assert.Equal("controlOptionN", (string?)s["hotkey"]);
        Assert.Equal("Jakarta", (string?)s["weatherCity"]);
        Assert.Equal("com.tinyspeck.slackmacgap", (string?)s["roarMuted"]![0]);
        Assert.Equal(2, (int)s["defaultsRevision"]!);
    }

    /// <summary>The "new version is out" peek is remembered in the Mac's key, so it isn't shown again every launch.</summary>
    [Fact]
    public void AnnouncedVersionRoundTrips()
    {
        var d = AppData.FromJson(Fixture("mac-data.json"))!;
        Assert.Equal("1.5.0", d.AnnouncedVersion);
        d.AnnouncedVersion = "1.9.0";
        Assert.Equal("1.9.0", AppData.FromJson(d.ToJson())!.AnnouncedVersion);
        Assert.False(JsonNode.Parse(new AppData().ToJson())!.AsObject().ContainsKey("announcedVersion"));
    }

    [Fact]
    public void WritesTheMacsDateUuidAndKeyFormat()
    {
        var d = new AppData();
        d.Tasks.Add(new TaskItem(Guid.Parse("c0ffee00-1234-4abc-9def-000000000001"), "x", false, null, false,
                                 new DateTime(2026, 10, 26, 8, 30, 15, 789, DateTimeKind.Utc)));
        var json = d.ToJson();
        var task = JsonNode.Parse(json)!["tasks"]![0]!.AsObject();
        Assert.Equal("2026-10-26T08:30:15Z", (string?)task["created"]);          // whole seconds, Z
        Assert.Equal("C0FFEE00-1234-4ABC-9DEF-000000000001", (string?)task["id"]); // upper case
        Assert.False(task.ContainsKey("due"));                                    // nil is left out
        Assert.Equal(task.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal), task.Select(p => p.Key));
        Assert.Equal(2, (int)JsonNode.Parse(json)!["settings"]!["defaultsRevision"]!); // the Mac won't re-migrate
    }

    [Fact]
    public void RoundTrips()
    {
        var d = AppData.FromJson(Fixture("mac-data.json"))!;
        var again = AppData.FromJson(d.ToJson())!;
        Assert.Equal(d.Tasks, again.Tasks);
        Assert.Equal(d.Notes, again.Notes);
        Assert.Equal(d.Clips, again.Clips);
        Assert.Equal(d.Water, again.Water);
        Assert.Equal(d.FocusLog, again.FocusLog);
        Assert.Equal(d.FocusState, again.FocusState);
        Assert.Equal(d.ToJson(), again.ToJson());
    }

    [Fact]
    public void NotJsonIsRejected()
    {
        Assert.Null(AppData.FromJson("{ broken"));
        Assert.Null(AppData.FromJson("[1,2]"));
        Assert.NotNull(AppData.FromJson("{}"));
    }
}

public class DataStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ezk-test-" + Guid.NewGuid().ToString("N"));
    private string DataPath => Path.Combine(_dir, "data.json");

    public DataStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void SavesAndLoads()
    {
        var store = new DataStore(DataPath);
        var d = new AppData();
        d.Notes.Add(NoteItem.New() with { Body = "halo" });
        store.Save(d, synchronously: true);
        Assert.Equal("halo", new DataStore(DataPath).Load().Notes.Single().Body);
        Assert.True(File.Exists(DataPath + ".bak"));
    }

    [Fact]
    public void ABrokenFileIsKeptAndTheBackupUsed()
    {
        var store = new DataStore(DataPath);
        var d = new AppData();
        d.Tasks.Add(TaskItem.New("aman"));
        store.Save(d, synchronously: true);
        store.Load();                                  // makes the daily backup
        File.WriteAllText(DataPath, "{ not json");
        var loaded = store.Load();
        Assert.Equal("aman", loaded.Tasks.Single().Title);
        Assert.IsType<DataStore.RestoredBackup>(store.LastRecovery);
        Assert.True(File.Exists(store.LastRecovery!.Broken));
        Assert.Equal("{ not json", File.ReadAllText(store.LastRecovery.Broken));
    }

    [Fact]
    public void NoBackupStartsFreshButKeepsTheFile()
    {
        File.WriteAllText(DataPath, "nope");
        var store = new DataStore(DataPath);
        Assert.Empty(store.Load().Tasks);
        Assert.IsType<DataStore.StartedFresh>(store.LastRecovery);
        Assert.False(File.Exists(DataPath));
    }

    /// <summary>A data.json that's there but locked for a moment (a sync tool, antivirus) is read once it's free,
    /// not replaced by an empty start.</summary>
    [Fact]
    public void ALockedFileIsReadOnceItsFree()
    {
        var store = new DataStore(DataPath);
        var d = new AppData();
        d.Tasks.Add(TaskItem.New("jangan hilang"));
        store.Save(d, synchronously: true);
        var lockFile = new FileStream(DataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Run(async () => { await Task.Delay(300); lockFile.Dispose(); });
        var loaded = new DataStore(DataPath).Load();
        Assert.Equal("jangan hilang", loaded.Tasks.Single().Title);
    }

    /// <summary>A data.json that stays unreadable is handled like a damaged one: the user is told, and the next save
    /// never writes over the original (it's kept as data.broken-….json).</summary>
    [Fact]
    public void AnUnreadableFileIsKeptNotOverwritten()
    {
        var store = new DataStore(DataPath);
        var d = new AppData();
        d.Tasks.Add(TaskItem.New("jangan hilang"));
        store.Save(d, synchronously: true);
        var original = File.ReadAllText(DataPath);
        DataStore.Recovery? recovery;
        using (new FileStream(DataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Empty(store.Load().Tasks);
            recovery = store.LastRecovery;
            Assert.IsType<DataStore.StartedFresh>(recovery);
            store.Save(new AppData(), synchronously: true);   // e.g. onboarding, while it's still locked
        }
        store.Save(new AppData(), synchronously: true);
        Assert.Equal(original, File.ReadAllText(recovery!.Broken));
    }

    [Fact]
    public void MissingFileIsAFreshStartWithoutRecovery()
    {
        var store = new DataStore(DataPath);
        Assert.Empty(store.Load().Tasks);
        Assert.Null(store.LastRecovery);
    }
}

public class FocusClockTests
{
    private static readonly DateTime T0 = new(2026, 10, 26, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RunsPausesAndResumesOnTheWallClock()
    {
        var c = new FocusClock(25);
        c.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(15), c.Remaining(T0.AddMinutes(10)));
        c.Pause(T0.AddMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(15), c.Remaining(T0.AddHours(3)));
        c.Start(T0.AddHours(3));
        Assert.False(c.IsFinished(T0.AddHours(3).AddMinutes(14)));
        Assert.True(c.IsFinished(T0.AddHours(3).AddMinutes(15)));
    }

    [Fact]
    public void CompleteFlipsBetweenFocusAndBreak()
    {
        var c = new FocusClock(25);
        Assert.Equal(FocusMode.Focus, c.Complete(25, 5));
        Assert.Equal(FocusMode.Rest, c.Mode);
        Assert.Equal(TimeSpan.FromMinutes(5), c.Duration);
        Assert.Equal(FocusMode.Rest, c.Complete(25, 5));
        Assert.Equal(FocusMode.Focus, c.Mode);
    }

    [Fact]
    public void SnapshotSurvivesARestart()
    {
        var c = new FocusClock(45);
        c.Start(T0);
        var back = FocusClock.From(c.Snapshot);
        Assert.True(back.IsRunning);
        Assert.Equal(c.Remaining(T0.AddMinutes(5)), back.Remaining(T0.AddMinutes(5)));
        c.Pause(T0.AddMinutes(5));
        back = FocusClock.From(c.Snapshot);
        Assert.True(back.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(40), back.Remaining(T0.AddDays(1)));
    }

    [Theory]
    [InlineData(1500, "25:00")]
    [InlineData(59.2, "01:00")]
    [InlineData(3600, "1:00:00")]
    [InlineData(0, "00:00")]
    public void FormatsLikeTheMac(double seconds, string expected) =>
        Assert.Equal(expected, FocusClock.Format(TimeSpan.FromSeconds(seconds)));
}

public class StatsTests
{
    [Fact]
    public void StreakCountsBackFromTodayOrYesterday()
    {
        var today = DateTime.Now.Date.AddHours(12);
        var log = new List<FocusEntry>
        {
            FocusEntry.New(today.AddDays(-1).ToUniversalTime(), 25),
            FocusEntry.New(today.AddDays(-2).ToUniversalTime(), 25),
            FocusEntry.New(today.AddDays(-4).ToUniversalTime(), 25),
        };
        Assert.Equal(2, FocusStats.Streak(log, today));          // today not done yet: still 2
        log.Add(FocusEntry.New(today.ToUniversalTime(), 15));
        Assert.Equal(3, FocusStats.Streak(log, today));
        Assert.Equal(15, FocusStats.Minutes(log, today));
        var week = FocusStats.LastDays(7, log, today);
        Assert.Equal(7, week.Count);
        Assert.Equal(today.Date, week[^1].Day);
        Assert.Equal(15, week[^1].Minutes);
    }

    [Fact]
    public void WaterReminderOnlyInsideActiveHoursAndAfterTheInterval()
    {
        var noon = DateTime.Now.Date.AddHours(12);
        Assert.True(WaterLog.ReminderDue(noon.AddHours(-2), null, 60, 8, 22, noon));
        Assert.False(WaterLog.ReminderDue(noon.AddMinutes(-30), null, 60, 8, 22, noon));
        Assert.False(WaterLog.ReminderDue(null, noon.AddMinutes(-10), 60, 8, 22, noon));
        Assert.False(WaterLog.ReminderDue(null, null, 60, 8, 22, noon.Date.AddHours(23)));
    }

    [Fact]
    public void TomorrowIsNineInTheMorningLocalTime()
    {
        var due = Due.Date(DueChoice.Tomorrow, DateTime.UtcNow)!.Value.ToLocalTime();
        Assert.Equal(DateTime.Now.Date.AddDays(1).AddHours(9), due);
    }
}

[Collection("Lang")]
public class LangTests
{
    [Fact]
    public void UsesTheMacTableAndFallsBackToEnglish()
    {
        Lang.Choice = AppLanguage.Id;
        Assert.Equal("Pengaturan", Lang.L("Settings"));
        Assert.Equal("Not in the table", Lang.L("Not in the table"));
        Assert.Equal("5 hari", Lang.L("%d days", 5));
        Lang.Choice = AppLanguage.En;
        Assert.Equal("Settings", Lang.L("Settings"));
        Assert.Equal("5 days", Lang.L("%d days", 5));
    }

    [Fact]
    public void FormatsPlaceholdersInOrder()
    {
        Lang.Choice = AppLanguage.En;
        Assert.Equal("Open Den now 50%", Lang.Format("Open %@ now %d%%", "Den", 50));
        Assert.Equal("1.5 L", Lang.Format("%.1f L", 1.5));
    }

    [Fact]
    public void EveryWindowsKeyHasTheSamePlaceholders()
    {
        foreach (var (key, value) in Lang.Indonesian)
            Assert.True(Placeholders(key) == Placeholders(value), $"{key} → {value}");
    }

    private static string Placeholders(string s) =>
        string.Concat(System.Text.RegularExpressions.Regex.Matches(s, @"%(\.\d)?l?[d@fs]").Select(m => m.Value));
}

[Collection("Lang")]
public class ModelTests
{
    private static NotchModel Model(out List<PeekRequest> peeks, out List<ToastRequest> toasts)
    {
        var p = new List<PeekRequest>();
        var t = new List<ToastRequest>();
        var m = new NotchModel(new AppData());
        m.Peek += p.Add;
        m.Toast += t.Add;
        peeks = p;
        toasts = t;
        return m;
    }

    [Fact]
    public void DeletingATaskCanBeUndone()
    {
        var m = Model(out _, out var toasts);
        m.AddTask("a", null);
        m.AddTask("b", null);
        m.DeleteTask(m.Data.Tasks[1].Id);
        Assert.Equal(["b"], m.Data.Tasks.Select(t => t.Title));
        toasts.Last().Action!();
        Assert.Equal(["b", "a"], m.Data.Tasks.Select(t => t.Title));
    }

    [Fact]
    public void ADueTaskPeeksOnce()
    {
        var m = Model(out var peeks, out _);
        var now = DateTime.UtcNow;
        m.Now = () => now;
        m.AddTask("call", now.AddMinutes(-1));
        for (int i = 0; i < 10; i++) m.Tick();
        Assert.Single(peeks);
        Assert.Equal(Room.Hunt, peeks[0].Room);
        Assert.True(m.Data.Tasks[0].Reminded);
    }

    [Fact]
    public void AFinishedFocusSessionIsLoggedAndBreakStarts()
    {
        var m = Model(out var peeks, out _);
        var now = DateTime.UtcNow;
        m.Now = () => now;
        m.FocusStartPause();
        now = now.AddMinutes(25).AddSeconds(1);
        m.Tick();
        Assert.Equal(25, m.Data.FocusLog.Single().Minutes);
        Assert.Equal(FocusMode.Rest, m.Focus.Mode);
        Assert.True(peeks.Single().Urgent);
    }

    [Fact]
    public void ChangingLengthWhileRunningWaitsForTheNextSession()
    {
        var m = Model(out _, out var toasts);
        m.FocusStartPause();
        m.SetFocusMinutes(45);
        Assert.True(m.Focus.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), m.Focus.Duration);
        Assert.Single(toasts);
    }

    [Fact]
    public void CatchKeepsThirtyNewestWithoutDuplicates()
    {
        var m = Model(out _, out var toasts);
        for (int i = 0; i < 35; i++) m.Capture($"clip {i}");
        m.Capture("clip 34");
        Assert.Equal(30, m.Data.Clips.Count);
        Assert.Equal("clip 34", m.Data.Clips[0].Text);
        m.Capture("   ");
        Assert.Single(toasts);
    }

    [Fact]
    public void WaterGoalToastAndUndo()
    {
        var m = Model(out _, out var toasts);
        m.Settings.WaterGoal = 500;
        m.AddWater(350);
        Assert.Empty(toasts);
        m.AddWater(200);
        Assert.Single(toasts);
        m.UndoWater();
        Assert.Equal(350, m.WaterToday);
    }
}


/// <summary>Runs only from tools/check_format.sh, which compiles the Mac's NotchCore into a small reader.</summary>
public class MacRoundTripTests
{
    [Fact]
    public void TheMacReadsWhatWindowsWritesAndBack()
    {
        var work = Environment.GetEnvironmentVariable("EZK_FORMAT_CHECK");
        if (string.IsNullOrEmpty(work)) return;

        var d = AppData.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mac-data.json")))!;
        d.Tasks.Insert(0, TaskItem.New("dari Windows ✓", DateTime.UtcNow.AddHours(1)));
        d.Notes.Insert(0, NoteItem.New() with { Title = "Catatan \"kutip\"", Body = "baris 1\nbaris 2 — é" });
        d.Water.Add(WaterEntry.New(200));
        d.FocusLog.Add(FocusEntry.New(DateTime.UtcNow.AddMinutes(-25), 25));
        var clock = new FocusClock(45);
        clock.Start(DateTime.UtcNow);
        clock.Pause(DateTime.UtcNow.AddMinutes(3));
        d.FocusState = clock.Snapshot;
        d.Settings.Language = AppLanguage.En;
        var fromWindows = Path.Combine(work, "windows.json");
        File.WriteAllText(fromWindows, d.ToJson());

        var fromMac = Path.Combine(work, "mac.json");
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(work, "format-check"), [fromWindows, fromMac])
            { RedirectStandardOutput = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Console.WriteLine(output);
        Assert.Equal(0, p.ExitCode);
        Assert.Contains("tasks=2 notes=2 clips=1 water=2 focusLog=2 focusState=focus/2700", output);
        Assert.Contains("lang=en hotkey=controlOptionN musicAllowed=true", output);
        Assert.Contains("shelf=1 scout=1", output);

        // The file keeps whole seconds, so compare with what Windows wrote, not the in-memory copy.
        d = AppData.FromJson(File.ReadAllText(fromWindows))!;
        var back = AppData.FromJson(File.ReadAllText(fromMac))!;
        Assert.Equal(d.Tasks, back.Tasks);
        Assert.Equal(d.Notes, back.Notes);
        Assert.Equal(d.Water, back.Water);
        Assert.Equal(d.FocusLog, back.FocusLog);
        Assert.Equal(d.FocusState, back.FocusState);
        Assert.Equal(AppLanguage.En, back.Settings.Language);
    }
}

public class LyricsTests
{
    [Fact]
    public void ParsesLrcLikeTheMac()
    {
        var lines = Lrc.Parse("[ar:Someone]\n[offset:+500]\n[00:12.50][01:00.00]Chorus line\n[00:05.00]First\n[00:30:25]Third\nno tag");
        Assert.Equal(["First", "Chorus line", "Third", "Chorus line"], lines.Select(l => l.Text));
        Assert.Equal(4.5, lines[0].Time, 3);
        Assert.Equal(12.0, lines[1].Time, 3);
        Assert.Equal(29.75, lines[2].Time, 3);
    }

    [Fact]
    public void FindsTheLineBeingSung()
    {
        var lines = Lrc.Parse("[00:01.00]a\n[00:03.00]b\n[00:05.00]c");
        Assert.Null(Lrc.Index(0.5, lines));
        Assert.Equal(0, Lrc.Index(1, lines));
        Assert.Equal(1, Lrc.Index(4.9, lines));
        Assert.Equal(2, Lrc.Index(100, lines));
    }

    [Theory]
    [InlineData("Song - Remastered 2011", "Song")]
    [InlineData("Song (feat. X)", "Song")]
    [InlineData("Song [feat. Y]", "Song")]
    [InlineData("Plain", "Plain")]
    public void CleansTitlesForSearch(string title, string clean) => Assert.Equal(clean, Lrc.CleanTitle(title));

    [Fact]
    public void PrefersSyncedLyricsOfTheRightLength()
    {
        var best = LyricsClient.Best([
            new(null, "plain", false, 200),
            new("[00:01.00]x", null, false, 260),
            new("[00:01.00]y", null, false, 201),
        ], 200);
        Assert.Equal("[00:01.00]y", best!.SyncedLyrics);
        Assert.Equal(LyricsKind.Synced, LyricsClient.StateOf(best).Kind);
        Assert.Equal(LyricsKind.Instrumental, LyricsClient.StateOf(new(null, null, true, 1)).Kind);
    }
}

public class ScoutTests
{
    [Fact]
    public void ParsesClaudeHooks()
    {
        var e = ScoutParser.ClaudeHook("""{"hook_event_name":"Notification","message":"Claude needs your permission to use Bash","cwd":"C:\\Users\\me\\ezkiel-notch","session_id":"s1"}""")!;
        Assert.Equal(ScoutKind.NeedsYou, e.Kind);
        Assert.Equal("ezkiel-notch", e.Project);
        Assert.Equal(ScoutKind.Done, ScoutParser.ClaudeHook("""{"hook_event_name":"Stop"}""")!.Kind);
        Assert.Null(ScoutParser.ClaudeHook("""{"hook_event_name":"PreToolUse"}"""));
        Assert.Null(ScoutParser.ClaudeHook("not json"));
    }

    [Fact]
    public void ParsesCodexAndLinks()
    {
        var c = ScoutParser.CodexNotify("""{"type":"agent-turn-complete","last-assistant-message":"  All tests pass  ","cwd":"/tmp/x"}""")!;
        Assert.Equal("All tests pass", c.Body);
        var u = ScoutParser.Url("ezkielnotch://notify?title=Build%20done&body=All+green&kind=done")!;
        Assert.Equal(("Build done", "All green", ScoutKind.Done), (u.Title, u.Body, u.Kind));
        Assert.Null(u.AppBundleId);
        Assert.Null(ScoutParser.Url("https://notify?title=x"));
    }

    [Fact]
    public void WireAndLogRoundTripInTheMacFormat()
    {
        var e = ScoutEvent.New("claude", ScoutKind.NeedsYou, "Claude needs you", "y", "C:/x", "s", "C:/Windows/System32/cmd.exe", new DateTime(2026, 10, 26, 1, 2, 3, DateTimeKind.Utc));
        var back = ScoutWire.Decode(ScoutWire.Encode(e))!;
        Assert.Equal(e, back);
        var d = new AppData();
        d.ScoutLog.Add(e);
        var json = d.ToJson();
        Assert.Contains("\"kind\": \"needsYou\"", json);
        Assert.Contains("\"appBundleID\"", json);
        Assert.Equal(e, AppData.FromJson(json)!.ScoutLog.Single());
    }

    [Fact]
    public void ConnectsAndDisconnectsClaudeKeepingOtherHooks()
    {
        var cmd = ScoutSetup.ClaudeCommand(@"C:\Users\me\AppData\Local\EzkielNotch\EzkielNotch.exe");
        Assert.Equal("[ -f 'C:/Users/me/AppData/Local/EzkielNotch/EzkielNotch.exe' ] && 'C:/Users/me/AppData/Local/EzkielNotch/EzkielNotch.exe' --claude-hook; true", cmd);
        var existing = """{"model":"opus","hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo hi"}]}]}}""";
        var on = ScoutSetup.ConnectClaude(existing, cmd);
        Assert.Equal(LinkState.On, ScoutSetup.ClaudeState(on, cmd));
        Assert.Contains("echo hi", on);
        Assert.Contains("\"model\": \"opus\"", on);
        Assert.Equal(LinkState.Stale, ScoutSetup.ClaudeState(on, ScoutSetup.ClaudeCommand(@"D:\Other\EzkielNotch.exe")));
        var off = ScoutSetup.DisconnectClaude(on);
        Assert.Equal(LinkState.Off, ScoutSetup.ClaudeState(off, cmd));
        Assert.Contains("echo hi", off);
        Assert.DoesNotContain("UserPromptSubmit", off);
        Assert.Throws<ScoutSetup.SetupException>(() => ScoutSetup.ConnectClaude("{ broken", cmd));
    }

    [Fact]
    public void ConnectsCodexWithoutTouchingOtherSettings()
    {
        const string exe = @"C:\Apps\EzkielNotch.exe";
        var text = ScoutSetup.ConnectCodex("model = \"o4\"\n[profiles.x]\nnotify = \"not top level\"\n", exe);
        Assert.StartsWith("notify = [\"C:/Apps/EzkielNotch.exe\", \"--codex-notify\"]", text);
        Assert.Equal(LinkState.On, ScoutSetup.CodexState(text, exe));
        Assert.Contains("notify = \"not top level\"", ScoutSetup.DisconnectCodex(text));
        Assert.Equal(LinkState.Off, ScoutSetup.CodexState(ScoutSetup.DisconnectCodex(text), exe));
        Assert.Throws<ScoutSetup.SetupException>(() => ScoutSetup.ConnectCodex("notify = [\"other\"]\n", exe));
    }

    [Theory]
    [InlineData("8", 8)]
    [InlineData("99+", 99)]
    [InlineData("•", 1)]
    [InlineData("0", null)]
    [InlineData("", null)]
    public void ReadsBadgeLabels(string label, int? count) => Assert.Equal(count, BadgeLabel.Count(label));
}

[Collection("Lang")]
public class RoarModelTests
{
    [Fact]
    public void ARisingCountPeeksOnceButNotForTheAppInFront()
    {
        Lang.Choice = AppLanguage.En;
        var peeks = new List<PeekRequest>();
        var m = new NotchModel(new AppData());
        m.Peek += peeks.Add;
        var t = DateTime.UtcNow;
        m.UpdateBadges([new Badge("WhatsApp", "WhatsApp", 2, "2", t)]);
        m.UpdateBadges([new Badge("WhatsApp", "WhatsApp", 5, "5", t)]);
        Assert.Equal(["2 unread"], peeks.Select(p => p.Subtitle));   // the second rise is within 20 s
        m.ForegroundApp = () => "Teams";
        m.UpdateBadges([new Badge("Teams", "Teams", 1, "1", t)]);
        Assert.Single(peeks);
        Assert.Equal(1, m.UnreadTotal);
    }

    [Fact]
    public void ScoutTracksLiveAgentsAndLogsTheRest()
    {
        var m = new NotchModel(new AppData());
        m.ScoutReceived(ScoutEvent.New("claude", ScoutKind.Working, "Claude is working", session: "a"));
        Assert.Single(m.LiveAgents);
        Assert.Empty(m.Data.ScoutLog);
        m.ScoutReceived(ScoutEvent.New("claude", ScoutKind.Done, "Claude finished", session: "a"));
        Assert.Empty(m.LiveAgents);
        Assert.Single(m.Data.ScoutLog);
    }
}

[Collection("Lang")]
public class StashTests
{
    [Fact]
    public void ShelfKeepsSixtyNewestWithoutDuplicates()
    {
        var m = new NotchModel(new AppData());
        Assert.Equal(2, m.AddToShelf([@"C:\a.txt", @"C:\b.txt", @"c:/a.txt"]));
        Assert.Equal(@"C:\b.txt", m.Data.Shelf[0].Path);
        m.AddToShelf(Enumerable.Range(0, 70).Select(i => $@"C:\f{i}.txt"));
        Assert.Equal(60, m.Data.Shelf.Count);
        Assert.Equal("f69.txt", m.Data.Shelf[0].Name);
    }

    [Fact]
    public void MacShelfRecordsSurviveAWindowsSave()
    {
        var json = """{"shelf":[{"id":"99999999-8888-7777-6666-555555555555","path":"/Users/x/a.pdf","bookmark":"Ym9vaw==","added":"2026-10-26T01:00:00Z"}]}""";
        var d = AppData.FromJson(json)!;
        Assert.Equal("a.pdf", d.Shelf.Single().Name);
        Assert.Contains("\"bookmark\": \"Ym9vaw==\"", d.ToJson());
    }
}

public class CalendarTests
{
    private const string Ics = """
        BEGIN:VCALENDAR
        BEGIN:VEVENT
        UID:standup
        SUMMARY:Daily standup
        DTSTART;TZID=Asia/Jakarta:20261019T093000
        DTEND;TZID=Asia/Jakarta:20261019T094500
        RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR
        EXDATE;TZID=Asia/Jakarta:20261028T093000
        LOCATION:https://meet.google.com/abc-defg-hij
        END:VEVENT
        BEGIN:VEVENT
        UID:standup
        RECURRENCE-ID;TZID=Asia/Jakarta:20261030T093000
        SUMMARY:Daily standup (moved)
        DTSTART;TZID=Asia/Jakarta:20261030T110000
        DTEND;TZID=Asia/Jakarta:20261030T111500
        END:VEVENT
        BEGIN:VEVENT
        UID:review
        SUMMARY:Design review
        DTSTART:20261026T070000Z
        DURATION:PT1H
        DESCRIPTION:Join: https://teams.microsoft.com/l/meetup-join/19%3ameeting_x/0?context=y\, thanks
        END:VEVENT
        BEGIN:VEVENT
        UID:cancelled
        SUMMARY:Cancelled thing
        DTSTART:20261026T080000Z
        DTEND:20261026T090000Z
        STATUS:CANCELLED
        END:VEVENT
        BEGIN:VEVENT
        UID:allday
        SUMMARY:Holiday
        DTSTART;VALUE=DATE:20261026
        END:VEVENT
        END:VCALENDAR
        """;

    private static DateTime Jkt(int day, int h, int m) => new DateTime(2026, 10, day, h, m, 0, DateTimeKind.Utc).AddHours(-7);

    [Fact]
    public void ExpandsWeeklyMeetingsInTheirOwnZone()
    {
        var week = ICal.Occurrences(Ics, Jkt(26, 0, 0), Jkt(31, 0, 0)).Where(m => m.Title.StartsWith("Daily")).OrderBy(m => m.Start).ToList();
        // Mon 26, (Wed 28 excluded), Fri 30 moved to 11:00.
        Assert.Equal([Jkt(26, 9, 30), Jkt(30, 11, 0)], week.Select(m => m.Start));
        Assert.Equal("Daily standup (moved)", week[1].Title);
        Assert.Equal("https://meet.google.com/abc-defg-hij", week[0].Link);
    }

    [Fact]
    public void SkipsCancelledAndAllDayAndFindsTeamsLinks()
    {
        var day = ICal.Occurrences(Ics, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));
        Assert.DoesNotContain(day, m => m.Title is "Cancelled thing" or "Holiday");
        var review = day.Single(m => m.Title == "Design review");
        Assert.Equal(TimeSpan.FromHours(1), review.End - review.Start);
        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/", review.Link);
        Assert.DoesNotContain("thanks", review.Link);
    }

    [Theory]
    [InlineData("webcal://p01-caldav.icloud.com/x.ics", "https://p01-caldav.icloud.com/x.ics")]
    [InlineData("  https://calendar.google.com/calendar/ical/x/basic.ics ", "https://calendar.google.com/calendar/ical/x/basic.ics")]
    [InlineData("not a link", null)]
    public void AcceptsCalendarAddresses(string input, string? expected) => Assert.Equal(expected, RallyClient.Normalize(input));
}

public class WeatherTests
{
    [Fact]
    public void ReadsOpenMeteo()
    {
        var now = WeatherClient.ParseForecast("""{"current":{"temperature_2m":29.4,"weather_code":61,"is_day":1},"daily":{"temperature_2m_max":[32.1],"temperature_2m_min":[24.0],"precipitation_probability_max":[80]}}""")!;
        Assert.Equal((29.4, 61, 80, true), (now.Temperature, now.Code, now.RainChance, now.IsDay));
        Assert.Equal("Rain", WeatherClient.Word(now.Code));
        var places = WeatherClient.ParsePlaces("""{"results":[{"name":"Jakarta","latitude":-6.2,"longitude":106.8,"admin1":"DKI Jakarta","country":"Indonesia"}]}""");
        Assert.Equal("DKI Jakarta, Indonesia", places.Single().Detail);
        Assert.Empty(WeatherClient.ParsePlaces("{}"));
        Assert.Equal(86, WeatherClient.Display(30, true), 3);
    }
}

public class UpdateTests
{
    [Fact]
    public void ReadsChecksumsInBothFormats()
    {
        var h = new string('a', 64);
        Assert.Equal(h, UpdateVerify.Checksum("EzkielNotch-windows-win-x64.zip", $"{new string('b', 64)}  Other.dmg\n{h} *EzkielNotch-windows-win-x64.zip\n"));
        Assert.Null(UpdateVerify.Checksum("missing.zip", $"{h}  x.zip"));
        Assert.Null(UpdateVerify.Checksum("x.zip", "nothex  x.zip"));
    }

    [Fact]
    public void PicksTheNewestReleaseWithAWindowsBuild()
    {
        const string json = """
            [
              {"tag_name":"v1.7.0","draft":false,"prerelease":false,"html_url":"p17","assets":[{"name":"Ezkiel-Notch-1.7.0.dmg","browser_download_url":"d"}]},
              {"tag_name":"v1.6.5","draft":false,"prerelease":false,"html_url":"p165","assets":[
                {"name":"EzkielNotch-windows-win-arm64.zip","browser_download_url":"z"},{"name":"SHA256SUMS-windows.txt","browser_download_url":"s"}]},
              {"tag_name":"v1.6.9","draft":true,"prerelease":false,"html_url":"draft","assets":[
                {"name":"EzkielNotch-windows-win-arm64.zip","browser_download_url":"z2"},{"name":"SHA256SUMS.txt","browser_download_url":"s2"}]}
            ]
            """;
        var r = UpdateVerify.Pick(json, "win-arm64")!;
        Assert.Equal(("v1.6.5", "z", "s"), (r.Version, r.ZipUrl, r.SumsUrl));
        Assert.Null(UpdateVerify.Pick(json, "win-x64"));
        Assert.True(AppVersion.IsNewer("windows-v0.3.0", "0.2.9"));
        Assert.False(AppVersion.IsNewer("v1.6", "1.6.0"));
    }
}

public class WindowsReleaseTests
{
    [Fact]
    public void WindowsPreviewReleasesCountButOtherPrereleasesDont()
    {
        const string json = """
            [
              {"tag_name":"windows-v0.9.0","draft":false,"prerelease":true,"html_url":"w","assets":[
                {"name":"EzkielNotch-windows-win-x64.zip","browser_download_url":"z"},{"name":"SHA256SUMS-windows.txt","browser_download_url":"s"}]},
              {"tag_name":"v2.0.0-beta","draft":false,"prerelease":true,"html_url":"b","assets":[
                {"name":"EzkielNotch-windows-win-x64.zip","browser_download_url":"zb"},{"name":"SHA256SUMS.txt","browser_download_url":"sb"}]}
            ]
            """;
        var r = UpdateVerify.Pick(json, "win-x64")!;
        Assert.Equal("windows-v0.9.0", r.Version);
        Assert.Equal([0, 9, 0], AppVersion.Parts(r.Version));
    }
}

public class DuePhraseTests
{
    /// <summary>Every case in the fixture the Mac's tests read too.</summary>
    [Fact]
    public void ReadsTheSharedFixture()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "due-phrases.json")))!;
        var now = DateTime.Parse(root["now"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        var failures = new List<string>();
        foreach (var c in root["cases"]!.AsArray())
        {
            var text = c!["text"]!.GetValue<string>();
            var title = c["title"]!.GetValue<string>();
            var due = c["due"]?.GetValue<string>();
            var r = DuePhrase.Parse(text, now);
            var got = r is null ? $"{text} → (none)" : $"{r.Title} · {r.Due:yyyy-MM-ddTHH:mm}";
            var want = due is null ? $"{title} → (none)" : $"{title} · {due}";
            if (r is null && due is null && title == text) continue;
            if (got != want) failures.Add($"\"{text}\": got {got}, want {want}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

public class GlassSettingTests
{
    /// <summary>The Mac's islandGlass comes through to Windows and back.</summary>
    [Fact]
    public void IslandGlassRoundTrips()
    {
        var d = AppData.FromJson("""{"settings":{"islandGlass":true,"eyeStyle":"anime"}}""")!;
        Assert.True(d.Settings.IslandGlass);
        var again = AppData.FromJson(d.ToJson())!;
        Assert.True(again.Settings.IslandGlass);
        Assert.Equal("anime", again.Settings.EyeStyle);
        Assert.False(AppData.FromJson("{}")!.Settings.IslandGlass);
    }
}

public class ClosedAndSharingSettingTests
{
    /// <summary>The Mac's minimalClosed and hideFromScreenShare come through to Windows and back.</summary>
    [Fact]
    public void MinimalClosedAndHideFromScreenShareRoundTrip()
    {
        var d = AppData.FromJson("""{"settings":{"minimalClosed":false,"hideFromScreenShare":true,"defaultsRevision":2}}""")!;
        Assert.False(d.Settings.MinimalClosed);
        Assert.True(d.Settings.HideFromScreenShare);
        var again = AppData.FromJson(d.ToJson())!;
        Assert.False(again.Settings.MinimalClosed);
        Assert.True(again.Settings.HideFromScreenShare);
    }

    [Fact]
    public void DefaultsMatchTheMac()
    {
        var d = AppData.FromJson("{}")!;
        Assert.True(d.Settings.MinimalClosed);
        Assert.False(d.Settings.HideFromScreenShare);
        var written = JsonNode.Parse(d.ToJson())!["settings"]!;
        Assert.True(written["minimalClosed"]!.GetValue<bool>());
        Assert.False(written["hideFromScreenShare"]!.GetValue<bool>());
    }

    /// <summary>A wrong type falls back to the default instead of failing the file.</summary>
    [Fact]
    public void WrongTypesFallBack()
    {
        var d = AppData.FromJson("""{"settings":{"minimalClosed":"no","hideFromScreenShare":1,"defaultsRevision":2}}""")!;
        Assert.True(d.Settings.MinimalClosed);
        Assert.False(d.Settings.HideFromScreenShare);
    }

    /// <summary>Like the Mac's decoder: settings from before 1.3.1 (no revision, or 1) get the minimal closed notch.</summary>
    [Fact]
    public void OldSettingsGetTheMinimalClosedNotch()
    {
        Assert.True(AppData.FromJson("""{"settings":{"minimalClosed":false}}""")!.Settings.MinimalClosed);
        Assert.True(AppData.FromJson("""{"settings":{"minimalClosed":false,"defaultsRevision":1}}""")!.Settings.MinimalClosed);
    }

    /// <summary>Old settings are migrated once: they're written back as the current revision, so turning the minimal
    /// closed island off sticks across launches (revision 1 stayed and re-forced it every time). A newer revision
    /// from a newer Mac is kept.</summary>
    [Fact]
    public void OldSettingsAreMigratedOnce()
    {
        var d = AppData.FromJson("""{"settings":{"minimalClosed":false,"defaultsRevision":1}}""")!;
        d.Settings.MinimalClosed = false;   // the user turns it off again
        var json = d.ToJson();
        Assert.Equal(NotchSettings.CurrentRevision, (int)JsonNode.Parse(json)!["settings"]!["defaultsRevision"]!);
        Assert.False(AppData.FromJson(json)!.Settings.MinimalClosed);
        var newer = AppData.FromJson("""{"settings":{"defaultsRevision":7}}""")!.ToJson();
        Assert.Equal(7, (int)JsonNode.Parse(newer)!["settings"]!["defaultsRevision"]!);
    }

    /// <summary>The Mac's older showMascot is only read (no face when off) and written back as it was.</summary>
    [Fact]
    public void ShowMascotIsKeptAsRead()
    {
        var d = AppData.FromJson("""{"settings":{"showMascot":false,"defaultsRevision":2}}""")!;
        Assert.Equal("off", d.Settings.ClosedFace);
        var written = JsonNode.Parse(d.ToJson())!["settings"]!;
        Assert.False(written["showMascot"]!.GetValue<bool>());
    }
}

public class MusicSettingTests
{
    /// <summary>The Mac's closedLyrics and musicSource come through to Windows and back, in the Mac's names.</summary>
    [Fact]
    public void ClosedLyricsAndMusicSourceRoundTrip()
    {
        var d = AppData.FromJson("""{"settings":{"closedLyrics":false,"musicSource":"spotify","showLyrics":true}}""")!;
        Assert.False(d.Settings.ClosedLyrics);
        Assert.Equal(MusicSource.Spotify, d.Settings.MusicSource);
        d.Settings.MusicSource = MusicSource.AppleMusic;
        var json = JsonNode.Parse(d.ToJson())!["settings"]!;
        Assert.Equal("appleMusic", json["musicSource"]!.GetValue<string>());
        Assert.False(json["closedLyrics"]!.GetValue<bool>());
        var again = AppData.FromJson(d.ToJson())!;
        Assert.False(again.Settings.ClosedLyrics);
        Assert.Equal("appleMusic", again.Settings.MusicSource);
    }

    [Fact]
    public void MissingOrUnknownMusicSettingsUseTheMacDefaults()
    {
        var d = AppData.FromJson("{}")!;
        Assert.True(d.Settings.ClosedLyrics);
        Assert.Equal("auto", d.Settings.MusicSource);
        // A source from a newer version, or a value of the wrong type.
        Assert.Equal("auto", AppData.FromJson("""{"settings":{"musicSource":"tidal"}}""")!.Settings.MusicSource);
        Assert.Equal("auto", AppData.FromJson("""{"settings":{"musicSource":3,"closedLyrics":"yes"}}""")!.Settings.MusicSource);
        Assert.True(AppData.FromJson("""{"settings":{"closedLyrics":"yes"}}""")!.Settings.ClosedLyrics);
    }

    /// <summary>musicAllowed is the Mac's Automation prompt; Windows needs no permission and leaves it as it was.</summary>
    [Theory]
    [InlineData("""{"settings":{"musicAllowed":false}}""", false)]
    [InlineData("""{"settings":{"musicAllowed":true}}""", true)]
    [InlineData("{}", null)]
    public void MusicAllowedIsKeptUntouched(string text, bool? want)
    {
        var json = JsonNode.Parse(AppData.FromJson(text)!.ToJson())!["settings"]!;
        Assert.Equal(want, json["musicAllowed"]?.GetValue<bool>());
    }
}

public class MusicSourceTests
{
    private const string SpotifyDesktop = "Spotify.exe";
    private const string SpotifyStore = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
    private const string AppleMusicApp = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";
    private const string ITunes = "iTunes.exe";
    private const string Chrome = "Chrome";
    private const string MediaPlayer = "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic";

    [Fact]
    public void KnowsThePlayersByTheirAppIds()
    {
        Assert.True(MusicSource.IsSpotify(SpotifyDesktop));
        Assert.True(MusicSource.IsSpotify(SpotifyStore));
        Assert.True(MusicSource.IsAppleMusic(AppleMusicApp));
        Assert.True(MusicSource.IsAppleMusic(ITunes));
        Assert.False(MusicSource.IsSpotify(Chrome));
        Assert.False(MusicSource.IsAppleMusic(MediaPlayer));
        Assert.True(MusicSource.Allows("auto", Chrome));
        Assert.True(MusicSource.Allows("something-new", Chrome));   // unknown = Auto
        Assert.False(MusicSource.Allows("spotify", AppleMusicApp));
        Assert.True(MusicSource.Allows("appleMusic", ITunes));
    }

    [Fact]
    public void AutoPrefersWhatsPlayingThenWindowsCurrentThenTheFirst()
    {
        MusicSource.Session S(string id, bool playing = false, bool current = false) => new(id, playing, current);
        Assert.Null(MusicSource.Pick("auto", []));
        Assert.Equal(1, MusicSource.Pick("auto", [S(Chrome, current: true), S(SpotifyDesktop, playing: true)]));
        Assert.Equal(0, MusicSource.Pick("auto", [S(Chrome, current: true), S(SpotifyDesktop)]));
        Assert.Equal(0, MusicSource.Pick("auto", [S(Chrome), S(SpotifyDesktop)]));
        Assert.Equal(1, MusicSource.Pick("auto", [S(Chrome, playing: true), S(SpotifyDesktop, playing: true, current: true)]));
    }

    [Fact]
    public void AChosenSourceFollowsOnlyThatPlayer()
    {
        MusicSource.Session S(string id, bool playing = false, bool current = false) => new(id, playing, current);
        // Spotify paused still beats a browser that's playing.
        Assert.Equal(1, MusicSource.Pick("spotify", [S(Chrome, playing: true, current: true), S(SpotifyStore)]));
        Assert.Null(MusicSource.Pick("spotify", [S(Chrome, playing: true), S(AppleMusicApp)]));
        Assert.Equal(2, MusicSource.Pick("appleMusic", [S(SpotifyDesktop, playing: true), S(ITunes), S(AppleMusicApp, playing: true)]));
    }
}

public class ClosedChorusTests
{
    private static readonly IReadOnlyList<LyricLine> Lines = Lrc.Parse("[00:10.00]First\n[00:12.00]\n[00:15.00]Second\n[00:15.00]Echo\n[00:20.00]Last");

    [Fact]
    public void ShowsTheLineBeingSungOrANote()
    {
        Assert.Equal("♪", ClosedChorus.Line(2, Lines));          // before the first line
        Assert.Equal("First", ClosedChorus.Line(9.9, Lines));    // a moment early, like Chorus beside Purr
        Assert.Equal("♪", ClosedChorus.Line(13, Lines));         // an empty line between verses
        Assert.Equal("Last", ClosedChorus.Line(300, Lines));
        Assert.Equal("♪", ClosedChorus.Line(5, []));
    }

    [Fact]
    public void WakesUpWhenTheNextLineStarts()
    {
        Assert.Equal(10 - 2 - ClosedChorus.Lead, ClosedChorus.UntilNextLine(2, Lines)!.Value, 6);
        Assert.Equal(15 - 13 - ClosedChorus.Lead, ClosedChorus.UntilNextLine(13, Lines)!.Value, 6);
        // Two lines on one time stamp: the next change is the one after both.
        Assert.Equal(20 - 16 - ClosedChorus.Lead, ClosedChorus.UntilNextLine(16, Lines)!.Value, 6);
        Assert.Null(ClosedChorus.UntilNextLine(25, Lines));
        Assert.Null(ClosedChorus.UntilNextLine(0, []));
    }

    [Fact]
    public void SingsOnlyWhileEverythingIsOn()
    {
        var on = new NotchSettings { ShowLyrics = true };
        var synced = new LyricsState(LyricsKind.Synced, Lines);
        Assert.True(on.ClosedLyrics);
        Assert.True(ClosedChorus.Shows(on, playing: true, synced));
        Assert.False(ClosedChorus.Shows(on, playing: false, synced));
        Assert.False(ClosedChorus.Shows(new NotchSettings { ShowLyrics = true, ClosedLyrics = false }, true, synced));
        Assert.False(ClosedChorus.Shows(new NotchSettings(), true, synced));
        Assert.False(ClosedChorus.Shows(on, true, new LyricsState(LyricsKind.Plain, Text: "words")));
        Assert.False(ClosedChorus.Shows(on, true, new LyricsState(LyricsKind.Synced, [])));
    }

    [Fact]
    public void LoadingNothingWhenIdleSaysNothing()
    {
        var client = new LyricsClient(new HttpClient());
        int changes = 0;
        client.Changed += () => changes++;
        client.Load(null);
        client.Load(null);
        Assert.Equal(0, changes);
        client.Preview(Lines, new SongInfo("t", "a", "b", 30));
        client.Load(null);
        Assert.Equal(2, changes);
        Assert.Equal(LyricsKind.Idle, client.State.Kind);
    }
}

[Collection("Lang")]
public class PeekLookTests
{
    /// <summary>The Mac's peeks keep their colour on Windows: a glyph and a tint for every kind.</summary>
    [Fact]
    public void EveryKindHasAGlyphAndTheMacsTint()
    {
        foreach (var kind in Enum.GetValues<PeekKind>())
        {
            var look = PeekLooks.Of(kind, 50);
            Assert.True(look.Glyph.Length == 1 && look.Glyph[0] >= '', $"{kind} has a Segoe icon glyph");
        }
        Assert.Equal(PeekTint.Teal, PeekLooks.Of(PeekKind.CapsOn).Tint);
        Assert.Equal(PeekTint.Neutral, PeekLooks.Of(PeekKind.CapsOff).Tint);
        Assert.Equal(PeekTint.Whiskers, PeekLooks.Of(PeekKind.Keyboard).Tint);
        Assert.Equal(PeekTint.Berry, PeekLooks.Of(PeekKind.Offline).Tint);
        Assert.Equal("Funk", PeekLooks.Of(PeekKind.Offline).Sound);
        Assert.Equal(PeekTint.Teal, PeekLooks.Of(PeekKind.HeadphonesOn).Tint);
        Assert.Equal(PeekTint.Berry, PeekLooks.Of(PeekKind.BatteryLow, 9).Tint);
        Assert.Equal(PeekTint.Gold, PeekLooks.Of(PeekKind.OnBattery, 60).Tint);
        Assert.True(PeekLooks.Of(PeekKind.FocusDone).ShowsLion);
        Assert.False(PeekLooks.Of(PeekKind.Meeting).ShowsLion);
        // Every tint is its own colour, and the room tints are the rooms' deep accents.
        var tints = Enum.GetValues<PeekTint>();
        Assert.Equal(tints.Length, tints.Select(PeekLooks.Rgb).Distinct().Count());
        Assert.Equal(0xFF9500u, PeekLooks.Rgb(PeekTint.Den));
        Assert.Equal(0x6FD6B4u, PeekLooks.Rgb(PeekTint.Teal));
    }

    [Theory]
    [InlineData(5, "")]
    [InlineData(20, "")]
    [InlineData(50, "")]
    [InlineData(80, "")]
    [InlineData(100, "")]
    public void BatteryGlyphFollowsTheMacsSteps(int percent, string glyph) => Assert.Equal(glyph, PeekLooks.Battery(percent));

    [Fact]
    public void ScoutAndTrackLooks()
    {
        var needs = PeekLooks.Scout(ScoutEvent.New("claude", ScoutKind.NeedsYou, "Claude needs you"));
        Assert.Equal(PeekTint.Den, needs.Tint);
        Assert.Equal("Tink", needs.Sound);
        var done = PeekLooks.Scout(ScoutEvent.New("codex", ScoutKind.Done, "Codex finished"));
        Assert.Equal(PeekTint.Scout, done.Tint);
        Assert.Equal("", done.Sound);
        Assert.Equal(PeekTint.Spotify, PeekLooks.Track("Spotify.exe").Tint);
        Assert.Equal(PeekTint.AppleMusic, PeekLooks.Track("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App").Tint);
        Assert.Equal(PeekTint.Purr, PeekLooks.Track("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic").Tint);
    }

    [Fact]
    public void ModelPeeksCarryTheirLook()
    {
        Lang.Choice = AppLanguage.En;
        var peeks = new List<PeekRequest>();
        var m = new NotchModel(new AppData());
        m.Peek += peeks.Add;
        var now = DateTime.UtcNow;
        m.Now = () => now;
        m.AddTask("call", now.AddMinutes(-1));
        for (int i = 0; i < 5; i++) m.Tick();   // due tasks are checked every fifth second
        m.UpdateBadges([new Badge("Claude", "Claude", 1, "1", now)]);
        m.UpdateBadges([new Badge("Claude", "Claude", 1, "1", now), new Badge("WhatsApp", "WhatsApp", 3, "3", now)]);
        var task = peeks.Single(p => p.Room == Room.Hunt);
        Assert.Equal(PeekTint.Hunt, task.Look?.Tint);
        Assert.True(task.Look?.ShowsLion);
        Assert.Equal(PeekTint.Scout, peeks.Single(p => p.Title == "Claude").Look?.Tint);   // an AI app's badge is Scout's colour
        var roar = peeks.Single(p => p.Title == "WhatsApp");
        Assert.Equal(PeekTint.Roar, roar.Look?.Tint);
        Assert.Equal("3", roar.Value);
    }

    [Fact]
    public void RingGeometry()
    {
        Assert.Equal(2.4, RingMath.LineWidth(20), 6);
        Assert.Equal(3.2, RingMath.LineWidth(32), 6);
        Assert.Equal(14.4, RingMath.Radius(32, 3.2), 6);
        var (s, e, large) = RingMath.Arc(0.25, 10, 10, 8);
        Assert.Equal((10.0, 2.0), (Math.Round(s.X, 6), Math.Round(s.Y, 6)));   // 12 o'clock
        Assert.Equal((18.0, 10.0), (Math.Round(e.X, 6), Math.Round(e.Y, 6)));  // 3 o'clock, clockwise
        Assert.False(large);
        (_, e, large) = RingMath.Arc(0.75, 10, 10, 8);
        Assert.Equal((2.0, 10.0), (Math.Round(e.X, 6), Math.Round(e.Y, 6)));
        Assert.True(large);
        (_, e, _) = RingMath.Arc(2, 10, 10, 8);   // clamped to a full turn
        Assert.Equal((10.0, 2.0), (Math.Round(e.X, 6), Math.Round(e.Y, 6)));
        Assert.Equal("80", RingMath.Label(0.8));
        Assert.Equal("100", RingMath.Label(1.4));
        Assert.Equal("0", RingMath.Label(-1));
    }

    /// <summary>Each Mac sound name is its own Windows sound, so a task due never sounds like a lost connection.</summary>
    [Fact]
    public void EachMacSoundIsDistinct()
    {
        string[] used = ["Glass", "Ping", "Tink", "Pop", "Bottle", "Purr", "Funk"];
        foreach (var name in used) Assert.NotEmpty(Chimes.Candidates(name));
        var firsts = Chimes.Names.Select(n => Chimes.Candidates(n)[0]).ToList();
        Assert.Equal(firsts.Count, firsts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Chimes.Names, n => Assert.All(Chimes.Candidates(n), f => Assert.EndsWith(".wav", f)));
        Assert.Empty(Chimes.Candidates("NoSuchSound"));
        Assert.Contains(Chimes.Fallback("NoSuchSound"), new[] { "Asterisk", "Beep", "Exclamation", "Hand", "Question" });
        Assert.NotEqual(Chimes.Fallback("Funk"), Chimes.Fallback("Glass"));
    }
}

[Collection("Lang")]
public class ParityUxTests
{
    [Fact]
    public void AFirstLaunchLeavesOneWelcomeNote()
    {
        Lang.Choice = AppLanguage.En;
        var d = new AppData();
        var m = new NotchModel(d);
        var note = Assert.Single(d.Notes);
        Assert.Equal("Welcome to Ezkiel Notch", note.Title);
        Assert.Contains("Ctrl+Alt+N", note.Body);
        Assert.Equal(note.Id, m.SelectedNoteId);
        Assert.True(d.Onboarded);
        Assert.True((bool)JsonNode.Parse(d.ToJson())!["onboarded"]!);   // the Mac's key

        // Deleted later: it doesn't come back.
        d.Notes.Clear();
        var again = AppData.FromJson(d.ToJson())!;
        new NotchModel(again);
        Assert.Empty(again.Notes);
    }

    [Fact]
    public void TheWelcomeNoteFollowsTheLanguageAndSkipsExistingUsers()
    {
        var id = AppData.FromJson("""{"settings":{"language":"id","hotkey":"controlOptionSpace"}}""")!;
        new NotchModel(id);
        Assert.Equal("Selamat datang di Ezkiel Notch", id.Notes.Single().Title);
        Assert.Contains("Ctrl+Alt+Space", id.Notes.Single().Body);
        Lang.Choice = AppLanguage.En;

        // An update from a version without the flag: someone with tasks gets no surprise note.
        var used = new AppData();
        used.Tasks.Add(TaskItem.New("x"));
        new NotchModel(used);
        Assert.Empty(used.Notes);
        Assert.True(used.Onboarded);

        // The Mac already welcomed them.
        var mac = AppData.FromJson("""{"onboarded":true}""")!;
        new NotchModel(mac);
        Assert.Empty(mac.Notes);
    }

    [Fact]
    public void QuietDuringFocusHoldsRoarAndFinishedAgents()
    {
        Lang.Choice = AppLanguage.En;
        var peeks = new List<PeekRequest>();
        var m = new NotchModel(new AppData());
        m.Peek += peeks.Add;
        m.FocusStartPause();
        Assert.True(m.FocusQuiet);
        var t = DateTime.UtcNow;
        m.UpdateBadges([new Badge("WhatsApp", "WhatsApp", 2, "2", t)]);
        m.ScoutReceived(ScoutEvent.New("claude", ScoutKind.Done, "Claude finished", session: "a"));
        Assert.Empty(peeks);
        // Something that needs you still gets through.
        m.ScoutReceived(ScoutEvent.New("claude", ScoutKind.NeedsYou, "Claude needs you", session: "b"));
        Assert.Single(peeks);

        // Off: everything peeks again.
        m.Settings.QuietDuringFocus = false;
        Assert.False(m.FocusQuiet);
        m.UpdateBadges([new Badge("Teams", "Teams", 1, "1", t)]);
        m.ScoutReceived(ScoutEvent.New("claude", ScoutKind.Done, "Claude finished", session: "c"));
        Assert.Equal(3, peeks.Count);
    }

    [Fact]
    public void ASecondLaunchAsksToOpenWithoutLookingLikeAnEvent()
    {
        Assert.True(ScoutWire.IsOpenCommand(ScoutWire.OpenCommand));
        Assert.True(ScoutWire.IsOpenCommand(""" { "command" : "open" } """));
        Assert.Null(ScoutWire.Decode(ScoutWire.OpenCommand));
        Assert.False(ScoutWire.IsOpenCommand(ScoutWire.Encode(ScoutEvent.New("script", ScoutKind.Info, "open"))));
        Assert.False(ScoutWire.IsOpenCommand("not json"));
    }

    [Fact]
    public void HotkeyLabelsMatchTheMacChoices()
    {
        Assert.Equal("Ctrl+Alt+N", NotchSettings.HotkeyLabel("controlOptionN"));
        Assert.Equal("Ctrl+Alt+Space", NotchSettings.HotkeyLabel("controlOptionSpace"));
        Assert.Equal("Ctrl+Alt+I", NotchSettings.HotkeyLabel("optionCommandI"));
        Assert.Equal("Ctrl+Alt+N", NotchSettings.HotkeyLabel("something else"));
    }
}

public class SysMonTests
{
    private static JsonNode Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sysmon-alerts.json")))!;

    private static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : null;

    [Fact]
    public void DefaultRulesMatchTheFixture()
    {
        var root = Fixture();
        var rules = root["rules"]!.AsArray().Select(r => new SysAlertRule(r!["id"]!.GetValue<string>(), r["above"]!.GetValue<bool>(),
            Num(r["on"])!.Value, Num(r["off"])!.Value, Num(r["sustain"])!.Value, Num(r["cooldown"])!.Value)).ToArray();
        Assert.Equal(SysAlerts.DefaultRules, rules);
        var alerts = new SysAlerts();
        Assert.Equal(Num(root["spacing"]), alerts.Spacing);
        Assert.Equal(Num(root["maxGap"]), alerts.MaxGap);
    }

    /// <summary>Every alert case in the fixture the Mac's tests read too.</summary>
    [Fact]
    public void AlertsFromTheFixture()
    {
        var failures = new List<string>();
        foreach (var c in Fixture()["alerts"]!.AsArray())
        {
            var samples = new List<(double T, string Id, double V)>();
            foreach (var s in c!["samples"]!.AsArray())
            {
                string id = s!["id"]!.GetValue<string>();
                double v = Num(s["v"])!.Value;
                if (Num(s["t"]) is { } t) samples.Add((t, id, v));
                else
                    for (double x = Num(s["from"])!.Value; x <= Num(s["to"])!.Value; x += Num(s["step"])!.Value) samples.Add((x, id, v));
            }
            var alerts = new SysAlerts();
            var fired = samples.Where(s => alerts.Feed(s.Id, s.V, s.T)).Select(s => $"{(int)s.T} {s.Id}").ToList();
            var want = c["fires"]!.AsArray().Select(f => $"{(int)Num(f!["t"])!.Value} {f["id"]!.GetValue<string>()}").ToList();
            if (!fired.SequenceEqual(want)) failures.Add($"{c["name"]}: got [{string.Join(", ", fired)}], want [{string.Join(", ", want)}]");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void CountersCpuMemoryAndSeries()
    {
        var root = Fixture();
        foreach (var c in root["memoryLoad"]!.AsArray())
        {
            MemoryPressure? p = c!["pressure"]?.GetValue<string>() switch
            {
                "normal" => MemoryPressure.Normal,
                "warning" => MemoryPressure.Warning,
                "critical" => MemoryPressure.Critical,
                _ => null,
            };
            Assert.Equal(Num(c["load"]), SysMemory.AlertLoad(Num(c["used"])!.Value, p));
        }
        foreach (var c in root["counterDelta"]!.AsArray())
        {
            var d = SysCounters.Delta((ulong)Num(c!["previous"])!.Value, (ulong)Num(c["current"])!.Value, (int)Num(c["bits"])!.Value);
            Assert.Equal(Num(c["delta"]), d is { } x ? (double?)x : null);
        }
        foreach (var c in root["rates"]!.AsArray())
        {
            static Dictionary<string, ulong> Counters(JsonNode? n) => n!.AsObject().ToDictionary(p => p.Key, p => (ulong)Num(p.Value)!.Value);
            var r = SysCounters.Rate(Counters(c!["previous"]), Counters(c["current"]), Num(c["seconds"])!.Value, (int)Num(c["bits"])!.Value);
            Assert.Equal(Num(c["rate"]), r);
        }
        foreach (var c in root["cpu"]!.AsArray())
        {
            var p = SysCpu.Percent((ulong)Num(c!["previousBusy"])!.Value, (ulong)Num(c["previousIdle"])!.Value,
                                   (ulong)Num(c["busy"])!.Value, (ulong)Num(c["idle"])!.Value);
            Assert.Equal(Num(c["percent"]), p);
        }
        var s = root["series"]!;
        var series = new SysSeries((int)Num(s["capacity"])!.Value);
        foreach (var v in s["push"]!.AsArray()) series.Append(Num(v)!.Value);
        Assert.Equal(s["values"]!.AsArray().Select(v => Num(v)!.Value), series.Values);
        foreach (var n in s["normalized"]!.AsArray())
            Assert.Equal(n!["values"]!.AsArray().Select(v => Num(v)!.Value), series.Normalized(Num(n["floor"])!.Value));
    }

    [Fact]
    public void FormatsFromTheFixture()
    {
        var format = Fixture()["format"]!.AsObject();
        var fns = new Dictionary<string, Func<double, string>>
        {
            ["rate"] = SysFormat.Rate,
            ["compactRate"] = SysFormat.CompactRate,
            ["memory"] = SysFormat.Memory,
            ["disk"] = SysFormat.Disk,
            ["percent"] = SysFormat.Percent,
            ["minutes"] = v => SysFormat.Minutes((int)v),
        };
        Assert.Equal(fns.Keys.OrderBy(k => k), format.Select(p => p.Key).OrderBy(k => k));
        var failures = new List<string>();
        foreach (var (key, fn) in fns)
            foreach (var pair in format[key]!.AsArray())
            {
                var got = fn(Num(pair![0])!.Value);
                var want = pair[1]!.GetValue<string>();
                if (got != want) failures.Add($"{key}({pair[0]}): got {got}, want {want}");
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>The Mac's system monitor settings come through to Windows and back; unknown pills fall back to off.</summary>
    [Fact]
    public void SettingsRoundTripAndDefaults()
    {
        var d = AppData.FromJson("""{"settings":{"sysmon":true,"sysmonPill":"network","sysmonCpuAlert":false,"sysmonDiskAlert":false}}""")!;
        Assert.True(d.Settings.Sysmon);
        Assert.Equal("network", d.Settings.SysmonPill);
        Assert.False(d.Settings.SysmonCpuAlert);
        Assert.True(d.Settings.SysmonMemoryAlert);
        var again = AppData.FromJson(d.ToJson())!;
        Assert.True(again.Settings.Sysmon);
        Assert.Equal("network", again.Settings.SysmonPill);
        Assert.False(again.Settings.SysmonDiskAlert);
        var fresh = AppData.FromJson("""{"settings":{"sysmonPill":"gpu"}}""")!;
        Assert.False(fresh.Settings.Sysmon);
        Assert.Equal("off", fresh.Settings.SysmonPill);
        var written = JsonNode.Parse(fresh.ToJson())!["settings"]!;
        Assert.False(written["sysmon"]!.GetValue<bool>());
        Assert.Equal("off", written["sysmonPill"]!.GetValue<string>());
    }
}
