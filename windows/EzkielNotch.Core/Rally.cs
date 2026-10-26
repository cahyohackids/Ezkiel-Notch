namespace EzkielNotch.Core;

/// <summary>Rally: keeps today's next meetings from the iCal address fresh (every 10 minutes, or on demand)
/// and works out the list every minute. Only that address is asked, only while Rally is on.</summary>
public sealed class RallyClient
{
    public IReadOnlyList<Meeting> Upcoming { get; private set; } = [];
    public enum Status { Off, Loading, Ok, Failed }
    public Status State { get; private set; } = Status.Off;
    public event Action? Changed;

    private readonly HttpClient _http;
    private string? _url;
    private string _ics = "";
    private DateTime _fetched = DateTime.MinValue;
    private bool _simulated;

    public RallyClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>webcal:// links work too (they are https underneath).</summary>
    public static string? Normalize(string? url)
    {
        var u = url?.Trim() ?? "";
        if (u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u[9..];
        return Uri.TryCreate(u, UriKind.Absolute, out var parsed) && (parsed.Scheme == "https" || parsed.Scheme == "http") ? u : null;
    }

    public void Configure(bool on, string url)
    {
        if (_simulated) return;
        var normal = on ? Normalize(url) : null;
        if (normal == _url && (State != Status.Off || normal is null)) return;
        _url = normal;
        _ics = "";
        _fetched = DateTime.MinValue;
        if (normal is null)
        {
            Set([], Status.Off);
            return;
        }
        _ = Fetch();
    }

    /// <summary>Once a minute: refetch when 10 minutes old, and recompute the list.</summary>
    public void Tick(DateTime nowUtc)
    {
        if (_simulated || _url is null) return;
        if (nowUtc - _fetched > TimeSpan.FromMinutes(10)) _ = Fetch();
        else Set(ICal.Upcoming(_ics, nowUtc), State);
    }

    public async Task Fetch()
    {
        var url = _url;
        if (url is null) return;
        _fetched = DateTime.UtcNow;
        if (State != Status.Ok) Set(Upcoming, Status.Loading);
        try
        {
            var text = await _http.GetStringAsync(url);
            if (url != _url) return;
            _ics = text;
            Set(ICal.Upcoming(text, DateTime.UtcNow), text.Contains("BEGIN:VCALENDAR") ? Status.Ok : Status.Failed);
        }
        catch (Exception)
        {
            if (url == _url) Set(Upcoming, Status.Failed);
        }
    }

    /// <summary>Self-test: made-up meetings, no network.</summary>
    public void Simulate(IReadOnlyList<Meeting> meetings)
    {
        _simulated = true;
        Set(meetings, Status.Ok);
    }

    private void Set(IReadOnlyList<Meeting> list, Status s)
    {
        bool same = s == State && list.SequenceEqual(Upcoming);
        Upcoming = list;
        State = s;
        if (!same) Changed?.Invoke();
    }
}
