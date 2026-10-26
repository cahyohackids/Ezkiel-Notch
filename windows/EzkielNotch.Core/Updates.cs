using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

/// <summary>"1.3.1" vs "v1.4.0" vs "windows-v0.3.0": numeric, missing parts count as 0 (the Mac's AppVersion).</summary>
public static class AppVersion
{
    public static int[] Parts(string version)
    {
        var start = version.IndexOfAny("0123456789".ToCharArray());
        if (start < 0) return [];
        return version[start..].Split('.').Select(p => int.TryParse(new string(p.TakeWhile(char.IsAsciiDigit).ToArray()), out var n) ? n : 0).ToArray();
    }

    public static bool IsNewer(string candidate, string current)
    {
        var a = Parts(candidate);
        var b = Parts(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}

/// <summary>A release that has a Windows build for this machine.</summary>
public sealed record WindowsRelease(string Version, string Page, string ZipUrl, string ZipName, string SumsUrl);

/// <summary>The checks the updater runs before it replaces anything (the Mac's UpdateVerify), plus picking the
/// newest release with a Windows zip from GitHub's release list.</summary>
public static class UpdateVerify
{
    /// <summary>sha256sum/shasum output → the checksum listed for `file` (lowercase hex), or null.</summary>
    public static string? Checksum(string file, string sums)
    {
        foreach (var line in sums.Split('\n', '\r'))
        {
            var parts = line.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var hash = parts[0].ToLowerInvariant();
            // "hash  name" (text) or "hash *name" (binary).
            var name = parts[1].Trim().TrimStart('*');
            if (name == file && hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash;
        }
        return null;
    }

    public static string Sha256(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    /// <summary>From GET /repos/…/releases: the newest non-draft, non-prerelease release that carries
    /// EzkielNotch-windows-&lt;rid&gt;.zip and SHA256SUMS.txt.</summary>
    public static WindowsRelease? Pick(string releasesJson, string rid)
    {
        try
        {
            if (JsonNode.Parse(releasesJson) is not JsonArray releases) return null;
            var zipName = $"EzkielNotch-windows-{rid}.zip";
            WindowsRelease? best = null;
            foreach (var r in releases.OfType<JsonObject>())
            {
                if (Json.Bool(r["draft"]) == true || Json.Bool(r["prerelease"]) == true) continue;
                var assets = (r["assets"] as JsonArray ?? []).OfType<JsonObject>().ToList();
                string? Url(string name) => assets.FirstOrDefault(a => Json.String(a["name"]) == name) is { } a ? Json.String(a["browser_download_url"]) : null;
                if (Url(zipName) is not { } zip || Url("SHA256SUMS.txt") is not { } sums) continue;
                var tag = Json.String(r["tag_name"]) ?? "";
                var candidate = new WindowsRelease(tag, Json.String(r["html_url"]) ?? "", zip, zipName, sums);
                if (best is null || AppVersion.IsNewer(tag, best.Version)) best = candidate;
            }
            return best;
        }
        catch (Exception) { return null; }
    }
}
