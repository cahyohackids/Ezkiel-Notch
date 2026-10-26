using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

public enum ScoutKind { NeedsYou, Done, Working, Info, Ended }

/// <summary>Scout: one thing an AI agent (or any script) told the notch. Same fields and JSON as the Mac's
/// ScoutEvent, so data.json's scoutLog is shared. AppBundleId holds, on Windows, the app that ran the agent
/// (its exe path or app id).</summary>
public sealed record ScoutEvent(Guid Id, string Source, ScoutKind Kind, string Title, string Body, string? Cwd,
                                string? Session, string? AppBundleId, DateTime Date)
{
    public static ScoutEvent New(string source, ScoutKind kind, string title, string body = "", string? cwd = null,
                                 string? session = null, string? app = null, DateTime? date = null) =>
        new(Guid.NewGuid(), source, kind, title, body, cwd, session, app, date ?? DateTime.UtcNow);

    /// <summary>"ezkiel-notch" from "C:\Users\me\ezkiel-notch" (or a POSIX path).</summary>
    public string? Project
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Cwd)) return null;
            var name = Cwd.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault();
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }

    /// <summary>Anything can post to Scout, so every field is trimmed to what the notch can show.</summary>
    public ScoutEvent Clamped() => this with
    {
        Source = Cut(Source, 24),
        Title = Cut(Title, 80),
        Body = Cut(Body, 200),
        Cwd = Cwd is null ? null : Cut(Cwd, 300),
        Session = Session is null ? null : Cut(Session, 80),
        AppBundleId = AppBundleId is null ? null : Cut(AppBundleId, 260),
    };

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n];

    internal static string KindName(ScoutKind k) => k switch
    {
        ScoutKind.NeedsYou => "needsYou",
        ScoutKind.Done => "done",
        ScoutKind.Working => "working",
        ScoutKind.Ended => "ended",
        _ => "info",
    };

    internal static ScoutKind? KindFrom(string? s) => s switch
    {
        "needsYou" => ScoutKind.NeedsYou,
        "done" => ScoutKind.Done,
        "working" => ScoutKind.Working,
        "info" => ScoutKind.Info,
        "ended" => ScoutKind.Ended,
        _ => null,
    };

    internal static ScoutEvent? Read(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["source"]) is { } source && KindFrom(Json.String(o["kind"])) is { } kind
        && Json.String(o["title"]) is { } title && Json.String(o["body"]) is { } body && Json.Date(o["date"]) is { } date
            ? new ScoutEvent(id, source, kind, title, body, Json.String(o["cwd"]), Json.String(o["session"]), Json.String(o["appBundleID"]), date)
            : null;

    internal JsonObject Write()
    {
        var o = new JsonObject
        {
            ["id"] = Json.Id(Id), ["source"] = Source, ["kind"] = KindName(Kind), ["title"] = Title, ["body"] = Body, ["date"] = Json.Date(Date),
        };
        if (Cwd is not null) o["cwd"] = Cwd;
        if (Session is not null) o["session"] = Session;
        if (AppBundleId is not null) o["appBundleID"] = AppBundleId;
        return o;
    }
}

/// <summary>Turns what agents send into Scout events (the Mac's ScoutParser).</summary>
public static class ScoutParser
{
    /// <summary>Claude Code hook input (stdin JSON): Notification, Stop, SubagentStop, UserPromptSubmit, SessionEnd.</summary>
    public static ScoutEvent? ClaudeHook(string json, string? app = null, DateTime? now = null)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(json) as JsonObject; } catch (Exception) { return null; }
        if (o is null || Json.String(o["hook_event_name"]) is not { } name) return null;
        var cwd = Json.String(o["cwd"]);
        var session = Json.String(o["session_id"]);
        ScoutEvent Make(ScoutKind kind, string title, string body) => ScoutEvent.New("claude", kind, title, body, cwd, session, app, now);
        return name switch
        {
            "Notification" => Make(ScoutKind.NeedsYou, "Claude needs you", Json.String(o["message"]) ?? "Claude is waiting for you"),
            "Stop" => Make(ScoutKind.Done, "Claude finished", "Your turn"),
            "SubagentStop" => Make(ScoutKind.Info, "A subagent finished", ""),
            "SessionEnd" => Make(ScoutKind.Ended, "Claude session ended", ""),
            "UserPromptSubmit" => Make(ScoutKind.Working, "Claude is working", Cut(Json.String(o["prompt"]) ?? "", 140)),
            _ => null,
        };
    }

    /// <summary>Codex CLI notify argument: {"type":"agent-turn-complete", "last-assistant-message": …}.</summary>
    public static ScoutEvent? CodexNotify(string argument, string? app = null, DateTime? now = null)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(argument) as JsonObject; } catch (Exception) { return null; }
        if (o is null || Json.String(o["type"]) is not { } type) return null;
        if (type != "agent-turn-complete") return ScoutEvent.New("codex", ScoutKind.Info, "Codex", type, app: app, date: now);
        var last = (Json.String(o["last-assistant-message"]) ?? "").Trim();
        return ScoutEvent.New("codex", ScoutKind.Done, "Codex finished", Cut(last, 200), Json.String(o["cwd"]),
                              Json.String(o["thread-id"]) ?? Json.String(o["turn-id"]), app, now);
    }

    /// <summary>ezkielnotch://notify?title=…&amp;body=…&amp;source=…&amp;kind=…</summary>
    public static ScoutEvent? Url(string url, DateTime? now = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !u.Scheme.Equals("ezkielnotch", StringComparison.OrdinalIgnoreCase)
            || !u.Host.Equals("notify", StringComparison.OrdinalIgnoreCase)) return null;
        var items = new Dictionary<string, string>();
        foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            var k = Uri.UnescapeDataString((eq < 0 ? part : part[..eq]).Replace('+', ' '));
            var v = eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            items.TryAdd(k, v);
        }
        if (!items.TryGetValue("title", out var title) || title.Length == 0) return null;
        // No app from links: any web page can open this URL, and its peek must not launch apps.
        return ScoutEvent.New(items.GetValueOrDefault("source") ?? "link",
                              ScoutEvent.KindFrom(items.GetValueOrDefault("kind")) ?? ScoutKind.Info,
                              Cut(title, 80), Cut(items.GetValueOrDefault("body") ?? "", 200),
                              items.GetValueOrDefault("cwd"), items.GetValueOrDefault("session"), null, now);
    }

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>How an event travels from the sender process to the running app (one JSON line).</summary>
public static class ScoutWire
{
    public static string Encode(ScoutEvent e) => e.Clamped().Write().ToJsonString();

    public static ScoutEvent? Decode(string text)
    {
        if (text.Length > 8192) return null;
        try { return JsonNode.Parse(text) is JsonObject o ? ScoutEvent.Read(o)?.Clamped() : null; }
        catch (Exception) { return null; }
    }
}

/// <summary>Badge text → unread count: "8" → 8, "99+" → 99, "•" or "New" → 1, "" or "0" → null.</summary>
public static class BadgeLabel
{
    public static int? Count(string? label)
    {
        var raw = label?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0) return 1;
        return int.TryParse(digits, out var n) && n > 0 ? n : null;
    }
}

/// <summary>One app with something unread (Roar).</summary>
public sealed record Badge(string Id, string Name, int Count, string Label, DateTime Since);

public enum LinkState { Off, On, Stale }

/// <summary>Connecting agents to Scout: edits Claude Code's settings.json and Codex's config.toml while keeping
/// everything else in them (the Mac's ScoutSetup, with Windows commands).</summary>
public static class ScoutSetup
{
    public sealed class SetupException(string message) : Exception(message);

    public static readonly string[] ClaudeEvents = ["Notification", "Stop", "UserPromptSubmit", "SessionEnd"];

    /// <summary>Claude Code runs hooks with Git Bash on Windows. Guarded: if the app is moved or deleted the hook
    /// does nothing (and still exits 0) instead of failing every prompt.</summary>
    public static string ClaudeCommand(string executable)
    {
        var p = "'" + executable.Replace('\\', '/').Replace("'", @"'\''") + "'";
        return $"[ -f {p} ] && {p} --claude-hook; true";
    }

    private static bool IsOurs(string command) => command.Contains("--claude-hook") && command.Contains("EzkielNotch", StringComparison.OrdinalIgnoreCase);

    public static string ConnectClaude(string? existing, string command)
    {
        var root = Object(existing);
        var hooks = Hooks(root);
        foreach (var e in ClaudeEvents)
        {
            var groups = WithoutOurs(hooks[e]).Groups;
            groups.Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = 10 }) });
            hooks[e] = groups;
        }
        root["hooks"] = hooks;
        return Serialize(root);
    }

    public static string DisconnectClaude(string? existing)
    {
        var root = Object(existing);
        var hooks = Hooks(root);
        foreach (var (key, value) in hooks.ToList())
        {
            var r = WithoutOurs(value);
            if (!r.Removed) continue;
            if (r.Groups.Count == 0) hooks.Remove(key); else hooks[key] = r.Groups;
        }
        if (hooks.Count == 0) root.Remove("hooks"); else root["hooks"] = hooks;
        return Serialize(root);
    }

    public static LinkState ClaudeState(string? existing, string command)
    {
        JsonObject hooks;
        try { hooks = Hooks(Object(existing)); } catch (SetupException) { return LinkState.Off; }
        var commands = ClaudeEvents.Select(e => (hooks[e] as JsonArray ?? [])
            .OfType<JsonObject>().SelectMany(g => (g["hooks"] as JsonArray ?? []).OfType<JsonObject>())
            .Select(h => Json.String(h["command"]) ?? "").ToList()).ToList();
        if (commands.All(c => c.Contains(command))) return LinkState.On;
        return commands.SelectMany(c => c).Any(IsOurs) ? LinkState.Stale : LinkState.Off;
    }

    private static JsonObject Object(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new SetupException("unreadable");
        }
        catch (JsonException) { throw new SetupException("unreadable"); }
    }

    private static JsonObject Hooks(JsonObject root) => root["hooks"] switch
    {
        null => [],
        JsonObject h => (JsonObject)h.DeepClone(),
        _ => throw new SetupException("unreadable"),
    };

    private static (JsonArray Groups, bool Removed) WithoutOurs(JsonNode? value)
    {
        bool removed = false;
        var result = new JsonArray();
        foreach (var node in value as JsonArray ?? [])
        {
            if (node is not JsonObject group || group["hooks"] is not JsonArray hooks)
            {
                result.Add(node?.DeepClone());
                continue;
            }
            var kept = hooks.Where(h => !IsOurs(Json.String((h as JsonObject)?["command"]) ?? "")).Select(h => h?.DeepClone()).ToArray();
            if (kept.Length == hooks.Count)
            {
                result.Add(group.DeepClone());
                continue;
            }
            removed = true;
            if (kept.Length == 0) continue;
            var g = (JsonObject)group.DeepClone();
            g["hooks"] = new JsonArray(kept);
            result.Add(g);
        }
        return (result, removed);
    }

    private static string Serialize(JsonObject root) => Json.Sorted(root)!.ToJsonString(Json.Pretty) + "\n";

    // MARK: Codex

    /// <summary>Codex runs the program with the event JSON appended.</summary>
    public static string CodexLine(string executable)
    {
        static string Toml(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return $"notify = [{Toml(executable.Replace('\\', '/'))}, {Toml("--codex-notify")}]";
    }

    private static int TopLevelEnd(List<string> lines)
    {
        int i = lines.FindIndex(l => l.TrimStart().StartsWith('['));
        return i < 0 ? lines.Count : i;
    }

    private static bool IsNotifyLine(string line)
    {
        var t = line.Trim();
        return t.StartsWith("notify") && t["notify".Length..].TrimStart().StartsWith('=');
    }

    public static string ConnectCodex(string existing, string executable)
    {
        var lines = existing.Length == 0 ? [] : existing.Replace("\r\n", "\n").Split('\n').ToList();
        var line = CodexLine(executable);
        int end = TopLevelEnd(lines);
        int i = lines.FindIndex(0, end, IsNotifyLine);
        if (i >= 0)
        {
            if (!lines[i].Contains("--codex-notify")) throw new SetupException(lines[i]);
            lines[i] = line;
        }
        else lines.Insert(0, line);   // top-level keys have to come before the first table
        var text = string.Join("\n", lines);
        return text.EndsWith('\n') ? text : text + "\n";
    }

    public static string DisconnectCodex(string existing)
    {
        var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
        int end = TopLevelEnd(lines);
        return string.Join("\n", lines.Where((l, i) => !(i < end && IsNotifyLine(l) && l.Contains("--codex-notify"))));
    }

    public static LinkState CodexState(string existing, string executable)
    {
        var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
        int end = TopLevelEnd(lines);
        var line = lines.Take(end).FirstOrDefault(IsNotifyLine);
        if (line is null || !line.Contains("--codex-notify")) return LinkState.Off;
        return line.Trim() == CodexLine(executable) ? LinkState.On : LinkState.Stale;
    }
}
