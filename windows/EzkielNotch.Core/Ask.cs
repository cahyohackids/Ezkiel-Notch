using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EzkielNotch.Core;

/// <summary>
/// Ask: a quick question to the Ezkielyna AI gateway (OpenAI-compatible) from the open island. The request, the
/// event-stream reader, the error mapping, the model list and the text clean-up follow the Mac's Ask.swift, and
/// both are tested against Tests/fixtures/ai-sse.json. The user's own API key is passed in; it is never kept here.
/// </summary>
public static class Ask
{
    public const string BaseUrl = "https://api.ezkielyna.store/v1";
    /// <summary>Where a user gets a key.</summary>
    public const string KeyPage = "https://api.ezkielyna.store";
    public const string DefaultModel = "deepseek-v4.1-flash";
    /// <summary>Offered even before /models loads.</summary>
    public static readonly string[] SuggestedModels = ["deepseek-v4.1-flash", "deepseek-v4-pro-0813"];
    /// <summary>Earlier questions sent along, so "and in Bali?" makes sense.</summary>
    public const int ContextTurns = 3;
    /// <summary>Guards against runaway use of the user's balance (the same numbers as the Mac's Ask.swift):
    /// an answer stops at MaxAnswerTokens, a pasted question is cut at MaxQuestionLength, each earlier turn sent as
    /// context at MaxContextLength, and at most MaxQuestionsPerWindow questions go out in WindowSeconds.</summary>
    public const int MaxAnswerTokens = 1200;
    public const int MaxQuestionLength = 2000;
    public const int MaxContextLength = 1500;
    public const int MaxQuestionsPerWindow = 30;
    public const double WindowSeconds = 600;

    public static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    /// <summary>May another question go out now, given when the earlier ones did? (Drops the ones outside the window.)</summary>
    public static bool AllowSend(List<DateTime> sent, DateTime nowUtc)
    {
        sent.RemoveAll(t => (nowUtc - t).TotalSeconds >= WindowSeconds || t > nowUtc);
        return sent.Count < MaxQuestionsPerWindow;
    }
    /// <summary>Questions kept for this session (never saved).</summary>
    public const int HistoryLimit = 10;
    /// <summary>Seconds without a byte before giving up.</summary>
    public const double IdleTimeout = 30;

    public static string SystemPrompt(bool indonesian) => indonesian
        ? "Kamu Ezkiel, asisten cepat di dalam app Ezkiel Notch. Jawab singkat dan langsung ke inti: beberapa kalimat atau daftar pendek, tanpa basa-basi. Teks biasa, markdown seminimal mungkin. Balas dalam Bahasa Indonesia, kecuali pengguna menulis dalam bahasa lain."
        : "You are Ezkiel, the quick helper inside the Ezkiel Notch app. Answer short and to the point: a few sentences or a short list, no preamble. Plain text, minimal markdown. Reply in English unless the user writes in another language.";

    /// <summary>One question and its answer, kept in memory for the session.</summary>
    public sealed record Turn(string Question, string Answer);

    /// <summary>A request, ready for any HTTP client.</summary>
    public sealed record Request(string Url, string Method, IReadOnlyDictionary<string, string> Headers, string? Body);

    public static string CleanKey(string key) => key.Trim();

    public static JsonArray Messages(string question, IReadOnlyList<Turn> history, bool indonesian)
    {
        var list = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(indonesian) } };
        foreach (var turn in history.Skip(Math.Max(0, history.Count - ContextTurns)))
        {
            if (turn.Answer.Length == 0) continue;
            list.Add(new JsonObject { ["role"] = "user", ["content"] = Cut(turn.Question, MaxContextLength) });
            list.Add(new JsonObject { ["role"] = "assistant", ["content"] = Cut(turn.Answer, MaxContextLength) });
        }
        list.Add(new JsonObject { ["role"] = "user", ["content"] = Cut(question.Trim(), MaxQuestionLength) });
        return list;
    }

    /// <summary>POST /chat/completions with stream: true.</summary>
    public static Request ChatRequest(string key, string model, string question, IReadOnlyList<Turn> history, bool indonesian)
    {
        var body = new JsonObject
        {
            ["model"] = string.IsNullOrEmpty(model) ? DefaultModel : model,
            ["stream"] = true,
            ["max_tokens"] = MaxAnswerTokens,
            ["messages"] = Messages(question, history, indonesian),
        };
        return new Request(BaseUrl + "/chat/completions", "POST", new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + CleanKey(key),
            ["Content-Type"] = "application/json",
            ["Accept"] = "text/event-stream",
        }, Json.Sorted(body)!.ToJsonString());
    }

    /// <summary>GET /models (also how "Test key" checks a key without spending anything).</summary>
    public static Request ModelsRequest(string key) => new(BaseUrl + "/models", "GET", new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer " + CleanKey(key),
        ["Accept"] = "application/json",
    }, null);

    private static readonly string[] NotChat = ["embed", "whisper", "tts", "dall-e", "moderation", "rerank"];

    /// <summary>The chat models in a /models reply, in its order, without duplicates and without embedding, speech,
    /// image or rerank models (they can't answer a question).</summary>
    public static List<string> ParseModels(string json)
    {
        var ids = new List<string>();
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (Exception) { return ids; }
        if (root is not JsonObject o || o["data"] is not JsonArray list) return ids;
        foreach (var item in list)
        {
            if (item is not JsonObject m || Json.String(m["id"]) is not { Length: > 0 } id || ids.Contains(id)) continue;
            var lower = id.ToLowerInvariant();
            if (NotChat.Any(lower.Contains)) continue;
            ids.Add(id);
        }
        return ids;
    }

    // MARK: Text

    /// <summary>Minimal markdown for a small box: headings lose their #, list bullets become •, code fences go.
    /// Inline **bold** and `code` stay (the box draws them).</summary>
    public static string Tidy(string text) => Clean(text, plain: false);

    /// <summary>Like Tidy, without the inline markers: for Copy, notes and task titles.</summary>
    public static string PlainText(string text) => Clean(text, plain: true);

    private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+");
    private static readonly Regex Bullet = new(@"^(\s*)[-*+]\s+");
    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*");
    private static readonly Regex Underline = new(@"__(.+?)__");
    private static readonly Regex Code = new(@"`([^`]+)`");

    private static string Clean(string text, bool plain)
    {
        var output = new List<string>();
        bool inFence = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Trim(' ', '\t').StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                output.Add(raw);
                continue;
            }
            var line = Heading.Replace(raw, "");
            line = Bullet.Replace(line, "$1• ");
            if (plain)
            {
                line = Bold.Replace(line, "$1");
                line = Underline.Replace(line, "$1");
                line = Code.Replace(line, "$1");
            }
            output.Add(line);
        }
        return string.Join("\n", output).Trim();
    }

    /// <summary>"Make task": the question when it's short (a time in it, like "jam 3", becomes the reminder), else the
    /// answer's first line. At most 80 characters. `now` is local time, like DuePhrase.</summary>
    public static (string Title, DateTime? Due) MakeTask(string question, string answer, DateTime now)
    {
        var q = question.Trim();
        if (q.Length <= 80 || answer.Trim().Length == 0)
        {
            if (DuePhrase.Parse(q, now) is { Title.Length: > 0 } r) return (Clip(r.Title), r.Due);
            return (Clip(q), null);
        }
        var first = PlainText(answer).Split('\n').Select(l => l.Trim(' ', '\t')).FirstOrDefault(l => l.Length > 0) ?? q;
        return (Clip(first), null);
    }

    /// <summary>"Save as note": the question as its title, the answer as plain text.</summary>
    public static (string Title, string Body) Note(string question, string answer) => (Clip(question.Trim()), PlainText(answer));

    internal static string Clip(string s, int limit = 80) => s.Length <= limit ? s : s[..(limit - 1)].Trim(' ', '\t') + "…";
}

public enum AskErrorKind { NoKey, InvalidKey, NoBalance, RateLimited, Offline, Timeout, Server, Other }

/// <summary>What went wrong, in words a person understands. The UI shows Title and Detail through L().</summary>
public sealed record AskError(AskErrorKind Kind, string Message = "", int Status = 0)
{
    public static readonly AskError NoKey = new(AskErrorKind.NoKey);
    public static readonly AskError InvalidKey = new(AskErrorKind.InvalidKey);
    public static readonly AskError NoBalance = new(AskErrorKind.NoBalance);
    public static readonly AskError RateLimited = new(AskErrorKind.RateLimited);
    public static readonly AskError Offline = new(AskErrorKind.Offline);
    public static readonly AskError Timeout = new(AskErrorKind.Timeout);

    /// <summary>The fixture's name for it (the Mac's case name).</summary>
    public string Code => Kind.ToString() is var k ? char.ToLowerInvariant(k[0]) + k[1..] : "";

    public string Title => Kind switch
    {
        AskErrorKind.NoKey => "Add your API key",
        AskErrorKind.InvalidKey => "That API key didn't work",
        AskErrorKind.NoBalance => "Your balance is empty",
        AskErrorKind.RateLimited => "Too many questions at once",
        AskErrorKind.Offline => "You're offline",
        AskErrorKind.Timeout => "No answer in time",
        AskErrorKind.Server => "The AI service is having trouble",
        _ => "Couldn't get an answer",
    };

    /// <summary>English; Other carries the server's own words.</summary>
    public string Detail => Kind switch
    {
        AskErrorKind.NoKey => "Paste your Ezkielyna key in Settings › Features",
        AskErrorKind.InvalidKey => "Wrong, expired or out of balance: check the key in Settings, or top up",
        AskErrorKind.NoBalance => "Top up at api.ezkielyna.store, then ask again",
        AskErrorKind.RateLimited => "Wait a moment and ask again",
        AskErrorKind.Offline => "Check your connection and ask again",
        AskErrorKind.Timeout => "The service was too slow. Ask again",
        AskErrorKind.Server => "Try again in a minute",
        _ => Message,
    };

    /// <summary>The key or the account needs attention: the error shows a Settings button.</summary>
    public bool NeedsSettings => Kind is AskErrorKind.NoKey or AskErrorKind.InvalidKey or AskErrorKind.NoBalance;

    private static readonly string[] BalanceWords = ["balance", "insufficient", "quota", "credit", "saldo", "billing", "payment", "top up", "top-up"];
    private static readonly string[] KeyWords = ["invalid api key", "invalid_api_key", "incorrect api key", "invalid token", "unauthorized",
                                                 "unauthenticated", "api key not", "no api key"];

    private static bool Mentions(string text, string[] words)
    {
        var lower = text.ToLowerInvariant();
        return words.Any(lower.Contains);
    }

    /// <summary>An HTTP status and the reply's body.</summary>
    public static AskError Http(int status, string body)
    {
        var (message, all) = Describe(body);
        return Map(status, message, all);
    }

    /// <summary>An {"error": …} object that came inside the stream.</summary>
    internal static AskError Streamed(JsonNode? error)
    {
        int? status = error is JsonObject o ? Json.Int(o["code"]) ?? Json.Int(o["status"]) : null;
        var (message, all) = DescribeNode(error);
        return Map(status, message, all);
    }

    private static AskError Map(int? status, string message, string all)
    {
        bool balance = Mentions(all, BalanceWords), key = Mentions(all, KeyWords);
        return status switch
        {
            401 => InvalidKey,
            402 => NoBalance,
            403 => balance ? NoBalance : key ? InvalidKey : new(AskErrorKind.Other, message.Length > 0 ? message : "HTTP 403", 403),
            429 => balance ? NoBalance : RateLimited,
            408 or 504 => Timeout,
            >= 500 => new(AskErrorKind.Server, "", status.Value),
            _ when balance => NoBalance,
            _ when key => InvalidKey,
            _ => new(AskErrorKind.Other, message.Length > 0 ? message : status is { } s ? $"HTTP {s}" : "Unknown error", status ?? 0),
        };
    }

    /// <summary>The readable message in an error body, and everything worth searching for keywords.</summary>
    private static (string Message, string All) Describe(string body)
    {
        var text = body.Trim();
        JsonNode? json = null;
        try { if (text.Length > 0) json = JsonNode.Parse(text); }
        catch (Exception) { json = null; }
        if (json is not null)
        {
            if (json is JsonObject o)
            {
                if (o.ContainsKey("error")) return DescribeNode(o["error"]);
                if ((Json.String(o["message"]) ?? Json.String(o["detail"])) is { } m) return (m, m);
            }
            return ("", text);
        }
        // HTML error pages say nothing useful.
        if (text.StartsWith('<')) return ("", text);
        return (text.Length > 200 ? text[..200] : text, text);
    }

    private static (string Message, string All) DescribeNode(JsonNode? error)
    {
        if (Json.String(error) is { } s) return (s, s);
        if (error is not JsonObject o) return ("", "");
        var message = Json.String(o["message"]) ?? "";
        var extra = string.Join(" ", new[] { Json.String(o["type"]), Json.String(o["code"]) }.OfType<string>());
        return (message, message + " " + extra);
    }
}

/// <summary>Reads the event stream of /chat/completions one line at a time.</summary>
public sealed class AskStream
{
    /// <summary>The answer so far.</summary>
    public string Text { get; private set; } = "";
    /// <summary>[DONE] came, or an error ended it.</summary>
    public bool Finished { get; private set; }
    /// <summary>The model sent its thinking (reasoning_content) before the answer.</summary>
    public bool Reasoning { get; private set; }
    public AskError? Error { get; private set; }

    /// <summary>Ends the stream with an error that didn't come in it (HTTP status, offline, timeout).</summary>
    public void Fail(AskError error)
    {
        Error = error;
        Finished = true;
    }

    /// <summary>Returns the new piece of the answer, if this line had one.</summary>
    public string? Feed(string rawLine)
    {
        if (Finished) return null;
        var line = rawLine.TrimEnd('\r', '\n');
        if (!line.StartsWith("data:", StringComparison.Ordinal)) return null;   // comments, event:, id:, retry:, blank lines
        var payload = line[5..];
        if (payload.StartsWith(' ')) payload = payload[1..];
        if (payload.Trim() == "[DONE]")
        {
            Finished = true;
            return null;
        }
        JsonNode? node;
        try { node = JsonNode.Parse(payload); }
        catch (Exception) { return null; }
        if (node is not JsonObject o) return null;
        if (o.ContainsKey("error"))
        {
            Fail(AskError.Streamed(o["error"]));
            return null;
        }
        if (o["choices"] is not JsonArray { Count: > 0 } choices || choices[0] is not JsonObject choice) return null;
        var part = choice["delta"] as JsonObject ?? choice["message"] as JsonObject;
        if (part is null) return null;
        if (Json.String(part["reasoning_content"]) is not null || Json.String(part["reasoning"]) is not null) Reasoning = true;
        if (Json.String(part["content"]) is not { Length: > 0 } piece) return null;
        Text += piece;
        return piece;
    }
}

/// <summary>Talks to the gateway. The handler is injectable, so tests and the self-test never reach the network.</summary>
public sealed class AskClient
{
    private readonly HttpClient _http;
    public TimeSpan Idle { get; init; } = TimeSpan.FromSeconds(Ask.IdleTimeout);

    public AskClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        // The idle timer below covers a stream that stalls; the whole answer may take longer.
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }

    private static HttpRequestMessage Build(Ask.Request r)
    {
        var msg = new HttpRequestMessage(new HttpMethod(r.Method), r.Url);
        foreach (var (name, value) in r.Headers)
            if (name != "Content-Type") msg.Headers.TryAddWithoutValidation(name, value);
        if (r.Body is not null) msg.Content = new StringContent(r.Body, Encoding.UTF8, "application/json");
        return msg;
    }

    /// <summary>Streams the answer: the same AskStream after every change, then once more at the end. An HTTP error,
    /// a lost connection or a stall ends it with Error set. Cancelling `ct` (Stop) throws OperationCanceledException.</summary>
    public async IAsyncEnumerable<AskStream> Stream(Ask.Request request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var s = new AskStream();
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(Idle);
        var (response, error) = await Guard(() => _http.SendAsync(Build(request), HttpCompletionOption.ResponseHeadersRead, idle.Token), ct);
        if (response is null)
        {
            s.Fail(error ?? AskError.Offline);
            yield return s;
            yield break;
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var (body, _) = await Guard(() => response.Content.ReadAsStringAsync(idle.Token), ct);
                s.Fail(AskError.Http((int)response.StatusCode, body ?? ""));
                yield return s;
                yield break;
            }
            // A server that ignored stream: true sends the whole answer as one JSON.
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                var (body, e) = await Guard(() => response.Content.ReadAsStringAsync(idle.Token), ct);
                if (body is null) s.Fail(e ?? AskError.Offline);
                else s.Feed("data: " + body.Replace('\r', ' ').Replace('\n', ' '));
                yield return s;
                yield break;
            }
            var (stream, openError) = await Guard(() => response.Content.ReadAsStreamAsync(idle.Token), ct);
            if (stream is null)
            {
                s.Fail(openError ?? AskError.Offline);
                yield return s;
                yield break;
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                idle.CancelAfter(Idle);
                var (line, readError) = await Guard(async () => await reader.ReadLineAsync(idle.Token), ct);
                if (readError is not null)
                {
                    s.Fail(readError);
                    yield return s;
                    yield break;
                }
                if (line is null) break;
                bool wasReasoning = s.Reasoning;
                if (s.Feed(line) is not null || s.Reasoning != wasReasoning || s.Finished) yield return s;
                if (s.Finished) yield break;
            }
        }
        yield return s;
    }

    /// <summary>The chat models this key can use. A 401 here is how "Test key" knows a key is wrong.</summary>
    public async Task<(List<string> Models, AskError? Error)> Models(string key, CancellationToken ct = default)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(TimeSpan.FromSeconds(15));
        var (response, error) = await Guard(() => _http.SendAsync(Build(Ask.ModelsRequest(key)), idle.Token), ct);
        if (response is null) return ([], error ?? AskError.Offline);
        using (response)
        {
            var (body, readError) = await Guard(() => response.Content.ReadAsStringAsync(idle.Token), ct);
            if (!response.IsSuccessStatusCode) return ([], AskError.Http((int)response.StatusCode, body ?? ""));
            if (body is null) return ([], readError ?? AskError.Offline);
            return (Ask.ParseModels(body), null);
        }
    }

    /// <summary>Runs one network step; a stall is a timeout, a lost connection is offline, Stop is rethrown.</summary>
    private static async Task<(T? Value, AskError? Error)> Guard<T>(Func<Task<T>> work, CancellationToken user)
    {
        try { return (await work(), null); }
        catch (OperationCanceledException) when (user.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return (default, AskError.Timeout); }
        catch (HttpRequestException) { return (default, AskError.Offline); }
        catch (IOException) { return (default, AskError.Offline); }
    }
}

/// <summary>
/// The ask box's state: one question at a time, its answer as it streams, and this session's earlier questions
/// (in memory only, never saved). The window shows it; Changed fires after every change, on the caller's thread.
/// </summary>
public sealed class AskSession(AskClient client)
{
    public enum State { Idle, Waiting, Thinking, Streaming, Done, Failed }

    public State Phase { get; private set; } = State.Idle;
    public string Question { get; private set; } = "";
    public string Answer { get; private set; } = "";
    public AskError? Error { get; private set; }
    /// <summary>Stop was pressed before the answer was complete.</summary>
    public bool Stopped { get; private set; }
    public bool Busy => Phase is State.Waiting or State.Thinking or State.Streaming;
    public IReadOnlyList<Ask.Turn> History => _history;
    public event Action? Changed;

    /// <summary>Models from /models, once a key was tested or used (empty: the suggested two).</summary>
    public IReadOnlyList<string> Models { get; private set; } = [];
    public enum KeyState { Unknown, Checking, Ok, Failed }
    public KeyState KeyCheck { get; private set; } = KeyState.Unknown;
    public AskError? KeyError { get; private set; }

    private readonly List<Ask.Turn> _history = [];
    /// <summary>Where this conversation starts in the history ("New question" starts a fresh one).</summary>
    private int _contextStart;
    private CancellationTokenSource? _cts;

    public AskClient Client => client;
    /// <summary>When questions went out (for Ask.AllowSend); the clock can be swapped in tests.</summary>
    private readonly List<DateTime> _sent = [];
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public async Task Send(string question, string? key, string model, bool indonesian)
    {
        var q = question.Trim();
        if (q.Length == 0) return;
        if (Busy && q == Question) return;   // Return pressed again on the same question: keep the one running
        Stop();
        Question = q;
        Answer = "";
        Error = null;
        Stopped = false;
        if (string.IsNullOrWhiteSpace(key))
        {
            Phase = State.Failed;
            Error = AskError.NoKey;
            Changed?.Invoke();
            return;
        }
        if (!Ask.AllowSend(_sent, Now()))
        {
            Phase = State.Failed;
            Error = AskError.RateLimited;
            Changed?.Invoke();
            return;
        }
        _sent.Add(Now());
        Phase = State.Waiting;
        Changed?.Invoke();
        var cts = _cts = new CancellationTokenSource();
        var context = _history.Skip(_contextStart).ToList();
        var request = Ask.ChatRequest(key, model, q, context, indonesian);
        try
        {
            await foreach (var s in client.Stream(request, cts.Token))
            {
                if (cts != _cts) return;
                Answer = s.Text;
                Phase = s.Text.Length > 0 ? State.Streaming : s.Reasoning ? State.Thinking : State.Waiting;
                if (s.Error is { } e)
                {
                    Error = e;
                    Phase = State.Failed;
                }
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            return;   // Stop already settled it
        }
        if (cts != _cts) return;
        _cts = null;
        if (Phase != State.Failed) Phase = State.Done;
        Remember();
        Changed?.Invoke();
    }

    /// <summary>Stops the answer where it is (what came so far stays).</summary>
    public void Stop()
    {
        if (_cts is null) return;
        var cts = _cts;
        _cts = null;
        cts.Cancel();
        if (!Busy) return;
        Stopped = true;
        Phase = State.Done;
        Remember();
        Changed?.Invoke();
    }

    /// <summary>"New question": an empty box and a fresh conversation (the session's list stays).</summary>
    public void Clear()
    {
        Stop();
        Question = "";
        Answer = "";
        Error = null;
        Stopped = false;
        Phase = State.Idle;
        _contextStart = _history.Count;
        Changed?.Invoke();
    }

    /// <summary>Shows an earlier question and its answer again.</summary>
    public void Show(Ask.Turn turn)
    {
        Stop();
        Question = turn.Question;
        Answer = turn.Answer;
        Error = null;
        Stopped = false;
        Phase = State.Done;
        Changed?.Invoke();
    }

    private void Remember()
    {
        if (Answer.Length == 0) return;
        _history.Add(new Ask.Turn(Question, Answer));
        if (_history.Count > Ask.HistoryLimit)
        {
            _history.RemoveAt(0);
            _contextStart = Math.Max(0, _contextStart - 1);
        }
    }

    /// <summary>Settings' "Test key": asks /models with the key (nothing is spent) and keeps the list.</summary>
    public async Task<AskError?> TestKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            KeyCheck = KeyState.Failed;
            KeyError = AskError.NoKey;
            Changed?.Invoke();
            return KeyError;
        }
        KeyCheck = KeyState.Checking;
        KeyError = null;
        Changed?.Invoke();
        var (models, error) = await client.Models(key);
        KeyCheck = error is null ? KeyState.Ok : KeyState.Failed;
        KeyError = error;
        if (models.Count > 0) Models = models;
        Changed?.Invoke();
        return error;
    }

    /// <summary>The key changed or was removed: forget what was checked.</summary>
    public void ForgetKeyCheck()
    {
        KeyCheck = KeyState.Unknown;
        KeyError = null;
        Changed?.Invoke();
    }

    /// <summary>The models to offer: the suggested two first, then the rest from /models.</summary>
    public IReadOnlyList<string> ModelChoices(string current)
    {
        var list = Ask.SuggestedModels.ToList();
        foreach (var m in Models) if (!list.Contains(m)) list.Add(m);
        if (current.Length > 0 && !list.Contains(current)) list.Add(current);
        return list;
    }
}
