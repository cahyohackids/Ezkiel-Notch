using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

/// <summary>Tolerant readers and Mac-compatible writers for data.json values.</summary>
internal static class Json
{
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        // Keep Indonesian text, emoji and quotes readable in the file instead of \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue(out bool b) ? b : null;
    public static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : null;
    public static double? Double(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : null;
    public static string? String(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    public static Guid? Guid(JsonNode? n) => String(n) is { } s && System.Guid.TryParse(s, out var g) ? g : null;

    /// <summary>ISO 8601 ("2026-10-26T08:00:00Z", or with an offset). Returned in UTC.</summary>
    public static DateTime? Date(JsonNode? n) =>
        String(n) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime : null;

    /// <summary>The Mac reads dates with ISO8601DateFormatter's default options: whole seconds, UTC "Z".</summary>
    public static string Date(DateTime d)
    {
        var utc = d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Swift writes UUIDs in upper case.</summary>
    public static string Id(Guid g) => g.ToString("D").ToUpperInvariant();

    public static List<T> List<T>(JsonNode? n, Func<JsonObject, T?> read) where T : class
    {
        var list = new List<T>();
        if (n is not JsonArray a) return list;
        foreach (var item in a)
        {
            if (item is not JsonObject o) continue;
            try
            {
                if (read(o) is { } value) list.Add(value);
            }
            catch (Exception)
            {
                // A damaged entry is skipped, never the list.
            }
        }
        return list;
    }

    /// <summary>A copy with every object's keys in ordinal order (Swift's .sortedKeys).</summary>
    public static JsonNode? Sorted(JsonNode? n) => n switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
                                        .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        null => null,
        _ => n.DeepClone(),
    };
}
