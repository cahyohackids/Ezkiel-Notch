using System.Globalization;
using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

/// <summary>Weather glance from Open-Meteo (free, no account, no key), a port of the Mac's WeatherService. Uses the
/// city typed in Settings, so no location permission; only that city's coordinates are sent. Off until a city is set.</summary>
public sealed class WeatherClient
{
    public sealed record Now(double Temperature, int Code, double High, double Low, int? RainChance, bool IsDay, DateTime Fetched);
    public sealed record Place(string Name, string Detail, double Latitude, double Longitude);

    public Now? Current { get; private set; }
    public IReadOnlyList<Place> Results { get; private set; } = [];
    public bool Searching { get; private set; }
    public event Action? Changed;

    private readonly HttpClient _http;
    private (double Lat, double Lon)? _place;
    private bool _loading, _simulated;

    public WeatherClient(HttpClient? http = null) => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    public void Configure(double? latitude, double? longitude)
    {
        (double, double)? next = latitude is { } la && longitude is { } lo ? (la, lo) : null;
        if (_simulated || next == _place) return;
        _place = next;
        Current = null;
        Changed?.Invoke();
        _ = Refresh(force: true);
    }

    /// <summary>Every 30 minutes is plenty for a glance.</summary>
    public async Task Refresh(bool force = false)
    {
        if (_place is not { } p || _loading || _simulated) return;
        if (!force && Current is { } c && DateTime.UtcNow - c.Fetched < TimeSpan.FromMinutes(30)) return;
        _loading = true;
        try
        {
            var url = "https://api.open-meteo.com/v1/forecast?" + string.Join("&",
                "latitude=" + p.Lat.ToString("0.000", CultureInfo.InvariantCulture),
                "longitude=" + p.Lon.ToString("0.000", CultureInfo.InvariantCulture),
                "current=temperature_2m,weather_code,is_day",
                "daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max",
                "forecast_days=1", "timezone=auto");
            if (ParseForecast(await _http.GetStringAsync(url)) is { } now)
            {
                Current = now;
                Changed?.Invoke();
            }
        }
        catch (Exception) { }
        finally { _loading = false; }
    }

    public async Task Search(string text, bool indonesian)
    {
        var q = text.Trim();
        if (q.Length < 2)
        {
            Results = [];
            Changed?.Invoke();
            return;
        }
        Searching = true;
        Changed?.Invoke();
        try
        {
            var json = await _http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(q)}&count=5&language={(indonesian ? "id" : "en")}");
            Results = ParsePlaces(json);
        }
        catch (Exception)
        {
            Results = [];
        }
        Searching = false;
        Changed?.Invoke();
    }

    public void ClearResults()
    {
        Results = [];
        Changed?.Invoke();
    }

    /// <summary>Self-test snapshots.</summary>
    public void Simulate(Now? value)
    {
        _simulated = value is not null;
        Current = value;
        Changed?.Invoke();
    }

    public static Now? ParseForecast(string json)
    {
        try
        {
            var o = JsonNode.Parse(json)!.AsObject();
            var cur = o["current"]!.AsObject();
            var daily = o["daily"]!.AsObject();
            double temp = cur["temperature_2m"]!.GetValue<double>();
            int code = cur["weather_code"]!.GetValue<int>();
            double high = daily["temperature_2m_max"]![0]!.GetValue<double>();
            double low = daily["temperature_2m_min"]![0]!.GetValue<double>();
            int? rain = daily["precipitation_probability_max"]?[0] is JsonValue r && r.TryGetValue(out int rv) ? rv : null;
            bool day = cur["is_day"] is JsonValue d && d.TryGetValue(out int dv) ? dv == 1 : true;
            return new Now(temp, code, high, low, rain, day, DateTime.UtcNow);
        }
        catch (Exception) { return null; }
    }

    public static List<Place> ParsePlaces(string json)
    {
        try
        {
            if (JsonNode.Parse(json)?["results"] is not JsonArray items) return [];
            return items.OfType<JsonObject>().Select(r =>
            {
                var name = Json.String(r["name"]);
                var lat = Json.Double(r["latitude"]);
                var lon = Json.Double(r["longitude"]);
                if (name is null || lat is null || lon is null) return null;
                var detail = string.Join(", ", new[] { Json.String(r["admin1"]), Json.String(r["country"]) }.Where(x => !string.IsNullOrEmpty(x)));
                return new Place(name, detail, lat.Value, lon.Value);
            }).OfType<Place>().ToList();
        }
        catch (Exception) { return []; }
    }

    /// <summary>WMO weather code → a short word (the Mac's words, so the Indonesian table covers them).</summary>
    public static string Word(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 => "Partly cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 or >= 80 and <= 82 => "Rain",
        >= 71 and <= 77 or 85 or 86 => "Snow",
        >= 95 and <= 99 => "Thunderstorm",
        _ => "Cloudy",
    };

    public static double Display(double celsius, bool fahrenheit) => fahrenheit ? celsius * 9 / 5 + 32 : celsius;
}
