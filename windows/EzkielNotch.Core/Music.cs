namespace EzkielNotch.Core;

/// <summary>
/// Purr source, in the Mac's names ("auto", "spotify", "appleMusic"; data.json keeps them as they are).
/// The Mac asks Spotify and Music directly; Windows reads every app in its media controls, so here the choice
/// picks which of those sessions Purr follows: Auto takes any player, the others only that app.
/// </summary>
public static class MusicSource
{
    public const string Auto = "auto";
    public const string Spotify = "spotify";
    public const string AppleMusic = "appleMusic";
    public static readonly string[] All = [Auto, Spotify, AppleMusic];

    /// <summary>A source from a newer version (or a damaged file) falls back to Auto, like the Mac's decoder.</summary>
    public static string Normalize(string? source) => source is not null && All.Contains(source) ? source : Auto;

    /// <summary>The desktop app ("Spotify.exe") and the Store one ("SpotifyAB.SpotifyMusic_…!Spotify").</summary>
    public static bool IsSpotify(string appId) => appId.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    /// <summary>Apple Music for Windows ("AppleInc.AppleMusicWin_…!App") and iTunes, which plays the same library.</summary>
    public static bool IsAppleMusic(string appId) =>
        appId.Contains("applemusic", StringComparison.OrdinalIgnoreCase) || appId.Contains("itunes", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether Purr may follow this app (its AppUserModelId) with this source.</summary>
    public static bool Allows(string source, string appId) => Normalize(source) switch
    {
        Spotify => IsSpotify(appId),
        AppleMusic => IsAppleMusic(appId),
        _ => true,
    };

    /// <summary>One of Windows' media sessions, as <see cref="Pick"/> sees it. Current: the one Windows itself
    /// would show (the volume flyout's player).</summary>
    public readonly record struct Session(string AppId, bool Playing, bool Current);

    /// <summary>The session Purr follows, or null when the source allows none. Like the Mac: one that's playing
    /// wins, then the one Windows calls current (the last one used), then the first.</summary>
    public static int? Pick(string source, IReadOnlyList<Session> sessions)
    {
        int? best = null;
        int bestScore = -1;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (!Allows(source, s.AppId)) continue;
            int score = (s.Playing ? 2 : 0) + (s.Current ? 1 : 0);
            if (score > bestScore)
            {
                best = i;
                bestScore = score;
            }
        }
        return best;
    }
}

/// <summary>Chorus under the closed island (the Mac's showsClosedLyrics and ClosedLyricLine).</summary>
public static class ClosedChorus
{
    /// <summary>The Mac's NotchMetrics.lyricBand: the closed island grows this much taller.</summary>
    public const double Band = 24;
    /// <summary>…and at least this wide.</summary>
    public const double Width = 340;
    /// <summary>A line shows a little before its time stamp, as in Chorus beside Purr.</summary>
    public const double Lead = 0.15;

    /// <summary>Lyrics are on, singing under the closed island is on, a song is playing and its lyrics are synced.</summary>
    public static bool Shows(NotchSettings settings, bool playing, LyricsState lyrics) =>
        settings.ShowLyrics && settings.ClosedLyrics && playing && lyrics.Kind == LyricsKind.Synced && lyrics.Lines is { Count: > 0 };

    /// <summary>The text under the closed island at this point of the song: the line being sung, or ♪ before the
    /// first line and on the empty lines between verses.</summary>
    public static string Line(double position, IReadOnlyList<LyricLine> lines) =>
        Lrc.Index(position + Lead, lines) is int i && lines[i].Text.Length > 0 ? lines[i].Text : "♪";

    /// <summary>Seconds of song until the next line starts (null after the last one), so the island wakes up exactly
    /// then instead of polling.</summary>
    public static double? UntilNextLine(double position, IReadOnlyList<LyricLine> lines)
    {
        double t = position + Lead;
        int next = Lrc.Index(t, lines) is int i ? i + 1 : 0;
        // Lines that share a time stamp (or an earlier one, after an offset) are already behind.
        while (next < lines.Count && lines[next].Time <= t) next++;
        return next < lines.Count ? lines[next].Time - t : null;
    }
}
