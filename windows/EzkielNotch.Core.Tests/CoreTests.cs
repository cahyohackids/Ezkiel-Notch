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
