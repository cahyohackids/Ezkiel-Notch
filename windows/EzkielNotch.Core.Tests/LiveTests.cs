using System.Text.Json.Nodes;
using EzkielNotch.Core;
using Xunit;

namespace EzkielNotch.Core.Tests;

public class TimerPhraseTests
{
    private static JsonNode Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timer-phrases.json")))!;

    /// <summary>Every case in the fixture the Mac's tests read too.</summary>
    [Fact]
    public void ReadsTheSharedFixture()
    {
        var root = Fixture();
        var failures = new List<string>();
        var cases = root["cases"]!.AsArray();
        Assert.True(cases.Count > 20);
        foreach (var c in cases)
        {
            var text = c!["text"]!.GetValue<string>();
            var r = TimerPhrase.Parse(text);
            var got = r is null ? "(none)" : $"{r.Label} · {r.Seconds}";
            var seconds = c["seconds"]?.GetValue<int>();
            var want = seconds is null ? "(none)" : $"{c["label"]!.GetValue<string>()} · {seconds}";
            if (got != want) failures.Add($"\"{text}\": got {got}, want {want}");
        }
        foreach (var c in root["commands"]!.AsArray())
        {
            var text = c!["text"]!.GetValue<string>();
            bool want = c["timer"]!.GetValue<bool>();
            if ((TimerPhrase.Command(text) is not null) != want) failures.Add($"command \"{text}\": want {want}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

public class QuickTimerTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SoonestAndDue()
    {
        var tea = QuickTimer.Start("teh", 600, Now);
        var soup = QuickTimer.Start("sup", 120, Now);
        var done = new QuickTimer(Guid.NewGuid(), "done", 60, Now.AddSeconds(-30));
        var old = new QuickTimer(Guid.NewGuid(), "old", 60, Now.AddHours(-2));
        Assert.Equal("sup", QuickTimers.Soonest([tea, soup, done], Now)!.Label);
        var (running, ring, stale) = QuickTimers.Due([tea, done, old], Now);
        Assert.Equal(["teh"], running.Select(t => t.Label));
        Assert.Equal(["done"], ring.Select(t => t.Label));
        Assert.Equal(["old"], stale.Select(t => t.Label));
        Assert.Equal(TimeSpan.FromMinutes(10), tea.Remaining(Now));
        Assert.Equal(TimeSpan.Zero, done.Remaining(Now));
    }

    [Fact]
    public void Short()
    {
        Assert.Equal("10m", QuickTimers.Short(600));
        Assert.Equal("1h30m", QuickTimers.Short(5400));
        Assert.Equal("2h", QuickTimers.Short(7200));
        Assert.Equal("45s", QuickTimers.Short(45));
        Assert.Equal("2m30s", QuickTimers.Short(150));
    }

    /// <summary>Timers survive a restart in the Mac's format; a damaged one costs only itself.</summary>
    [Fact]
    public void TimersRoundTripTolerantly()
    {
        var d = AppData.FromJson("""
            {"timers":[{"id":"6F9619FF-8B86-D011-B42D-00C04FC964FF","label":"teh","seconds":600,"ends":"2026-11-13T10:00:00Z"},
                       {"label":"broken"}],
             "settings":{"timerSound":"Hero","liveDownloads":true}}
            """)!;
        var t = Assert.Single(d.Timers);
        Assert.Equal("teh", t.Label);
        Assert.Equal(600, t.Seconds);
        Assert.Equal(new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc), t.Ends);
        Assert.Equal("Hero", d.Settings.TimerSound);
        Assert.True(d.Settings.LiveDownloads);
        var json = d.ToJson();
        Assert.Contains("\"ends\": \"2026-11-13T10:00:00Z\"", json);
        Assert.Contains("\"id\": \"6F9619FF-8B86-D011-B42D-00C04FC964FF\"", json);
        var again = AppData.FromJson(json)!;
        Assert.Equal(d.Timers, again.Timers);
        var empty = AppData.FromJson("{}")!;
        Assert.Empty(empty.Timers);
        Assert.False(empty.Settings.LiveDownloads);
        Assert.Equal("Glass", empty.Settings.TimerSound);
        Assert.Equal("Glass", AppData.FromJson("""{"settings":{"timerSound":"Klaxon"}}""")!.Settings.TimerSound);
    }

    [Fact]
    public void ModelRingsOnceAndSnoozes()
    {
        var now = Now;
        var m = new NotchModel(new AppData(), launch: now) { Now = () => now };
        var rang = new List<QuickTimer>();
        m.TimerDone += rang.Add;
        Assert.NotNull(m.StartTimer("10m teh"));
        Assert.Null(m.StartTimer("teh"));
        m.StartTimer("sup", 120);
        Assert.Equal("sup", m.SoonestTimer!.Label);
        now = now.AddSeconds(121);
        m.Tick();
        Assert.Equal(["sup"], rang.Select(t => t.Label));
        Assert.Equal(["teh"], m.Timers.Select(t => t.Label));
        m.Tick();
        Assert.Single(rang);
        var again = m.SnoozeTimer(rang[0])!;
        Assert.Equal(now.AddMinutes(5), again.Ends);
        Assert.Equal("sup", m.SoonestTimer!.Label);
        m.CancelTimer(again.Id);
        Assert.Equal("teh", m.SoonestTimer!.Label);
    }
}

public class DownloadTrackerTests
{
    private static readonly DateTime T0 = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FinalNames()
    {
        Assert.Equal("movie.mp4", DownloadTracker.FinalName("movie.mp4.crdownload"));
        Assert.Equal("Setup.exe", DownloadTracker.FinalName("Setup.exe.part"));
        Assert.Equal("photos.zip", DownloadTracker.FinalName("photos.zip.download"));
        Assert.Equal("Unconfirmed 123", DownloadTracker.FinalName("Unconfirmed 123.crdownload"));
        Assert.Null(DownloadTracker.FinalName("notes.txt"));
        Assert.Null(DownloadTracker.FinalName(".crdownload"));
        Assert.Null(DownloadTracker.FinalName("archive"));
    }

    [Fact]
    public void SpeedProgressAndFinish()
    {
        var t = new DownloadTracker();
        var finished = t.Update([new("a.zip.crdownload", "a.zip", 1_000_000), new("b.dmg.download", "b.dmg", 500, 1000)], new HashSet<string>(), T0);
        Assert.Empty(finished);
        Assert.Null(t.Active.Single(a => a.FinalName == "a.zip").BytesPerSecond);
        Assert.Null(t.Active.Single(a => a.FinalName == "a.zip").Fraction);
        Assert.Equal(0.5, t.Active.Single(a => a.FinalName == "b.dmg").Fraction);

        t.Update([new("a.zip.crdownload", "a.zip", 3_000_000), new("b.dmg.download", "b.dmg", 900, 1000)], new HashSet<string>(), T0.AddSeconds(1));
        Assert.Equal(2_000_000, t.Active.Single(a => a.FinalName == "a.zip").BytesPerSecond);
        Assert.Equal("a.zip", DownloadTracker.Lead(t.Active)!.Value.Download.FinalName);
        Assert.Equal(2, DownloadTracker.Lead(t.Active)!.Value.Count);

        // a.zip lands; b.dmg vanishes without a file (cancelled): only a.zip finished.
        finished = t.Update([], new HashSet<string> { "a.zip", "other.pdf" }, T0.AddSeconds(2));
        Assert.Equal(["a.zip"], finished);
        Assert.Empty(t.Active);
        Assert.Null(DownloadTracker.Lead(t.Active));
    }

    /// <summary>Chrome first writes "Unconfirmed 123.crdownload", then renames it: that is not a finished download.</summary>
    [Fact]
    public void RenameIsNotFinish()
    {
        var t = new DownloadTracker();
        t.Update([new("Unconfirmed 1.crdownload", "Unconfirmed 1", 10)], new HashSet<string>(), T0);
        var finished = t.Update([new("song.mp3.crdownload", "song.mp3", 20)], new HashSet<string>(), T0.AddSeconds(1));
        Assert.Empty(finished);
        Assert.Equal(["song.mp3"], t.Active.Select(a => a.FinalName));
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("512 B", DownloadTracker.Bytes(512));
        Assert.Equal("820 KB", DownloadTracker.Bytes(820_000));
        Assert.Equal("12.4 MB", DownloadTracker.Bytes(12_400_000));
        Assert.Equal("512 MB", DownloadTracker.Bytes(512_000_000));
        Assert.Equal("1.25 GB", DownloadTracker.Bytes(1_250_000_000));
        Assert.Equal("1.2 MB/s", DownloadTracker.Speed(1_200_000));
    }
}

public class LivePillTests
{
    [Fact]
    public void Order()
    {
        Assert.Equal(LiveKind.Timer, LivePill.Pick([LiveKind.Music, LiveKind.Timer, LiveKind.Download]));
        Assert.Equal(LiveKind.Focus, LivePill.Pick([LiveKind.Music, LiveKind.Meeting, LiveKind.Focus]));
        Assert.Equal(LiveKind.Download, LivePill.Pick([LiveKind.Download, LiveKind.Agent, LiveKind.Unread]));
        Assert.Equal(LiveKind.Charging, LivePill.Pick([LiveKind.Water, LiveKind.Charging]));
        Assert.Null(LivePill.Pick([]));
    }

    [Fact]
    public void MeetingMinutes()
    {
        var now = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(7, LivePill.MeetingMinutes(now.AddSeconds(400), now));
        Assert.Equal(10, LivePill.MeetingMinutes(now.AddSeconds(600), now));
        Assert.Equal(1, LivePill.MeetingMinutes(now.AddSeconds(20), now));
        Assert.Null(LivePill.MeetingMinutes(now.AddSeconds(601), now));
        Assert.Null(LivePill.MeetingMinutes(now.AddSeconds(-5), now));
    }
}
