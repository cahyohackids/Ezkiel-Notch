using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EzkielNotch.Core;
using Xunit;

namespace EzkielNotch.Core.Tests;

/// <summary>Ask, against the fixture the Mac's tests read too, and the client with a fake transport (never the network).</summary>
[Collection("Lang")]
public class AskTests
{
    private static JsonNode Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ai-sse.json")))!;

    [Fact]
    public void SystemPrompts()
    {
        var f = Fixture();
        Assert.Equal(f["system"]!["en"]!.GetValue<string>(), Ask.SystemPrompt(false));
        Assert.Equal(f["system"]!["id"]!.GetValue<string>(), Ask.SystemPrompt(true));
    }

    [Fact]
    public void ChatRequest()
    {
        var r = Fixture()["request"]!;
        var history = r["history"]!.AsArray().Select(t => new Ask.Turn(t!["question"]!.GetValue<string>(), t["answer"]!.GetValue<string>())).ToList();
        var req = Ask.ChatRequest(r["key"]!.GetValue<string>(), r["model"]!.GetValue<string>(), r["question"]!.GetValue<string>(),
                                  history, r["indonesian"]!.GetValue<bool>());
        Assert.Equal(r["url"]!.GetValue<string>(), req.Url);
        Assert.Equal(r["method"]!.GetValue<string>(), req.Method);
        var headers = r["headers"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
        Assert.Equal(headers.OrderBy(p => p.Key), req.Headers.OrderBy(p => p.Key));
        Assert.True(JsonNode.DeepEquals(r["body"], JsonNode.Parse(req.Body!)), req.Body);

        var models = Ask.ModelsRequest(r["key"]!.GetValue<string>());
        Assert.Equal(r["modelsUrl"]!.GetValue<string>(), models.Url);
        Assert.Equal("GET", models.Method);
        Assert.Equal("Bearer sk-test-not-a-real-key", models.Headers["Authorization"]);
        Assert.Null(models.Body);
    }

    [Fact]
    public void EmptyModelFallsBackToTheDefault()
    {
        var req = Ask.ChatRequest("k", "", "hi", [], false);
        Assert.Equal(Ask.DefaultModel, JsonNode.Parse(req.Body!)!["model"]!.GetValue<string>());
    }

    [Fact]
    public void LimitsMatchTheFixtureAndHold()
    {
        var l = Fixture()["limits"]!;
        Assert.Equal(l["maxAnswerTokens"]!.GetValue<int>(), Ask.MaxAnswerTokens);
        Assert.Equal(l["maxQuestionLength"]!.GetValue<int>(), Ask.MaxQuestionLength);
        Assert.Equal(l["maxContextLength"]!.GetValue<int>(), Ask.MaxContextLength);
        Assert.Equal(l["maxQuestionsPerWindow"]!.GetValue<int>(), Ask.MaxQuestionsPerWindow);
        Assert.Equal(l["windowSeconds"]!.GetValue<double>(), Ask.WindowSeconds);

        var long1 = new string('q', 5000);
        var req = Ask.ChatRequest("k", "", long1, [new Ask.Turn(new string('a', 4000), new string('b', 4000))], false);
        var body = JsonNode.Parse(req.Body!)!;
        Assert.Equal(Ask.MaxAnswerTokens, body["max_tokens"]!.GetValue<int>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal(Ask.MaxContextLength, messages[1]!["content"]!.GetValue<string>().Length);
        Assert.Equal(Ask.MaxContextLength, messages[2]!["content"]!.GetValue<string>().Length);
        Assert.Equal(Ask.MaxQuestionLength, messages[^1]!["content"]!.GetValue<string>().Length);
        Assert.True(req.Body!.Length < 10_000, $"body {req.Body.Length} chars");

        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var sent = Enumerable.Range(0, Ask.MaxQuestionsPerWindow).Select(i => now.AddSeconds(-i)).ToList();
        Assert.False(Ask.AllowSend(sent, now));
        Assert.True(Ask.AllowSend(sent, now.AddSeconds(Ask.WindowSeconds)));   // the window moved on
        Assert.True(Ask.AllowSend([], now));
    }

    [Fact]
    public void Streams()
    {
        var failures = new List<string>();
        foreach (var c in Fixture()["streams"]!.AsArray())
        {
            var name = c!["name"]!.GetValue<string>();
            var s = new AskStream();
            var pieces = new StringBuilder();
            foreach (var line in c["lines"]!.AsArray()) pieces.Append(s.Feed(line!.GetValue<string>()));
            void Expect(object? want, object? got, string what)
            {
                if (!Equals(want, got)) failures.Add($"{name}: {what} {got ?? "null"} ≠ {want ?? "null"}");
            }
            Expect(c["text"]!.GetValue<string>(), s.Text, "text");
            Expect(s.Text, pieces.ToString(), "pieces");
            Expect(c["finished"]!.GetValue<bool>(), s.Finished, "finished");
            Expect(c["reasoning"]!.GetValue<bool>(), s.Reasoning, "reasoning");
            Expect(c["error"]?.GetValue<string>(), s.Error?.Code, "error");
            if (c["message"] is { } message) Expect(message.GetValue<string>(), s.Error?.Detail, "message");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Errors()
    {
        var failures = new List<string>();
        foreach (var c in Fixture()["errors"]!.AsArray())
        {
            int status = c!["status"]!.GetValue<int>();
            var body = c["body"]!.GetValue<string>();
            var got = AskError.Http(status, body);
            if (got.Code != c["error"]!.GetValue<string>()) failures.Add($"{status} {body}: {got.Code}");
            if (c["message"] is { } m && got.Detail != m.GetValue<string>()) failures.Add($"{status} {body}: \"{got.Detail}\"");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(AskError.NoBalance.NeedsSettings);
        Assert.False(AskError.Offline.NeedsSettings);
    }

    [Fact]
    public void Models()
    {
        var m = Fixture()["models"]!;
        Assert.Equal(m["ids"]!.AsArray().Select(i => i!.GetValue<string>()), Ask.ParseModels(m["body"]!.GetValue<string>()));
        foreach (var bad in m["bad"]!.AsArray()) Assert.Empty(Ask.ParseModels(bad!.GetValue<string>()));
    }

    [Fact]
    public void Text()
    {
        foreach (var t in Fixture()["text"]!.AsArray())
        {
            var md = t!["markdown"]!.GetValue<string>();
            Assert.Equal(t["tidy"]!.GetValue<string>(), Ask.Tidy(md));
            Assert.Equal(t["plain"]!.GetValue<string>(), Ask.PlainText(md));
        }
        var note = Ask.Note("  Apa itu SSE? ", "**Server-Sent Events**: satu arah.");
        Assert.Equal("Apa itu SSE?", note.Title);
        Assert.Equal("Server-Sent Events: satu arah.", note.Body);
    }

    [Fact]
    public void MakeTask()
    {
        var tasks = Fixture()["tasks"]!;
        var now = DateTime.Parse(tasks["now"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        foreach (var c in tasks["cases"]!.AsArray())
        {
            var r = Ask.MakeTask(c!["question"]!.GetValue<string>(), c["answer"]!.GetValue<string>(), now);
            Assert.Equal(c["title"]!.GetValue<string>(), r.Title);
            Assert.Equal(c["due"]?.GetValue<string>(), r.Due?.ToString("yyyy-MM-ddTHH:mm"));
        }
    }

    // MARK: The client, with a fake transport

    /// <summary>Answers every request from a function; records what was asked.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            return answer(request);
        }
    }

    private static HttpResponseMessage Sse(IEnumerable<string> lines) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Join("\n", lines) + "\n", Encoding.UTF8, "text/event-stream"),
    };

    private static IEnumerable<string> FixtureLines(int stream) =>
        Fixture()["streams"]![stream]!["lines"]!.AsArray().Select(l => l!.GetValue<string>());

    [Fact]
    public async Task SessionStreamsAnAnswerAndRemembersIt()
    {
        var handler = new FakeHandler(_ => Sse(FixtureLines(0)));
        var session = new AskSession(new AskClient(handler));
        var phases = new List<AskSession.State>();
        session.Changed += () => phases.Add(session.Phase);
        await session.Send("  Ibu kota Jepang? ", "sk-test", "deepseek-v4.1-flash", indonesian: true);
        Assert.Equal("Ibu kota Jepang adalah Tokyo.", session.Answer);
        Assert.Equal(AskSession.State.Done, session.Phase);
        Assert.Contains(AskSession.State.Streaming, phases);
        Assert.Equal(AskSession.State.Waiting, phases[0]);
        Assert.Single(session.History);
        var req = handler.Requests.Single();
        Assert.Equal("https://api.ezkielyna.store/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", req.Headers.Authorization!.ToString());
        Assert.Contains("text/event-stream", req.Headers.Accept.ToString());

        // A follow-up carries the first turn; "New question" starts fresh.
        await session.Send("dan Korea?", "sk-test", "", indonesian: true);
        Assert.Equal(4, JsonNode.Parse(handler.Bodies[1]!)!["messages"]!.AsArray().Count);
        session.Clear();
        Assert.Equal(AskSession.State.Idle, session.Phase);
        await session.Send("lagi", "sk-test", "", indonesian: true);
        Assert.Equal(2, JsonNode.Parse(handler.Bodies[2]!)!["messages"]!.AsArray().Count);
        Assert.Equal(3, session.History.Count);
    }

    [Fact]
    public async Task SessionNeedsAKeyAndNeverCallsWithout()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("no network in tests"));
        var session = new AskSession(new AskClient(handler));
        await session.Send("hi", "  ", "", false);
        Assert.Equal(AskSession.State.Failed, session.Phase);
        Assert.Equal(AskErrorKind.NoKey, session.Error!.Kind);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(401, "{\"error\":{\"message\":\"Invalid API key\"}}", AskErrorKind.InvalidKey)]
    [InlineData(402, "", AskErrorKind.NoBalance)]
    [InlineData(403, "{\"error\":{\"message\":\"Saldo habis\"}}", AskErrorKind.NoBalance)]
    [InlineData(502, "<html>bad gateway</html>", AskErrorKind.Server)]
    public async Task SessionMapsHttpErrors(int status, string body, AskErrorKind kind)
    {
        var session = new AskSession(new AskClient(new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        await session.Send("hi", "sk-test", "", false);
        Assert.Equal(AskSession.State.Failed, session.Phase);
        Assert.Equal(kind, session.Error!.Kind);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task OfflineAndTimeout()
    {
        var offline = new AskSession(new AskClient(new FakeHandler(_ => throw new HttpRequestException("no route"))));
        await offline.Send("hi", "sk-test", "", false);
        Assert.Equal(AskErrorKind.Offline, offline.Error!.Kind);

        // A stream that never sends a byte: the idle timer gives up.
        var stalled = new AskSession(new AskClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream()) { Headers = { { "Content-Type", "text/event-stream" } } },
        }))
        { Idle = TimeSpan.FromMilliseconds(200) });
        await stalled.Send("hi", "sk-test", "", false);
        Assert.Equal(AskErrorKind.Timeout, stalled.Error!.Kind);
    }

    [Fact]
    public async Task StopKeepsWhatCameSoFar()
    {
        var stream = new StallingStream("data: {\"choices\":[{\"delta\":{\"content\":\"Half\"}}]}\n");
        var session = new AskSession(new AskClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream) { Headers = { { "Content-Type", "text/event-stream" } } },
        })));
        var done = new TaskCompletionSource();
        session.Changed += () => { if (session.Answer == "Half") done.TrySetResult(); };
        var sending = session.Send("hi", "sk-test", "", false);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Stop();
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AskSession.State.Done, session.Phase);
        Assert.True(session.Stopped);
        Assert.Equal("Half", session.Answer);
        Assert.Single(session.History);
    }

    [Fact]
    public async Task ServerThatIgnoresStreaming()
    {
        var session = new AskSession(new AskClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\n \"choices\": [{\"message\": {\"content\": \"Whole\\nanswer\"}}]\n}", Encoding.UTF8, "application/json"),
        })));
        await session.Send("hi", "sk-test", "", false);
        Assert.Equal("Whole\nanswer", session.Answer);
        Assert.Equal(AskSession.State.Done, session.Phase);
    }

    [Fact]
    public async Task TestKeyLoadsModels()
    {
        var body = Fixture()["models"]!["body"]!.GetValue<string>();
        var ok = new AskSession(new AskClient(new FakeHandler(r =>
            r.Headers.Authorization?.Parameter == "good"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"message\":\"Invalid API key\"}}") })));
        Assert.Null(await ok.TestKey(" good "));
        Assert.Equal(AskSession.KeyState.Ok, ok.KeyCheck);
        Assert.Equal(new[] { "deepseek-v4.1-flash", "deepseek-v4-pro-0813", "qwen3-max" }, ok.ModelChoices("deepseek-v4.1-flash"));
        Assert.Equal(AskErrorKind.InvalidKey, (await ok.TestKey("bad"))!.Kind);
        Assert.Equal(AskSession.KeyState.Failed, ok.KeyCheck);
        Assert.Equal(AskErrorKind.NoKey, (await ok.TestKey(""))!.Kind);
        Assert.Contains("custom-model", ok.ModelChoices("custom-model"));
    }

    [Fact]
    public void AskSettingsRoundTripAndTheKeyNeverLandsInTheFile()
    {
        var d = AppData.FromJson("""{"settings":{"ask":false,"askModel":"qwen3-max","futureKey":1}}""")!;
        Assert.False(d.Settings.Ask);
        Assert.Equal("qwen3-max", d.Settings.AskModel);
        var json = d.ToJson();
        var again = AppData.FromJson(json)!;
        Assert.False(again.Settings.Ask);
        Assert.Equal("qwen3-max", again.Settings.AskModel);
        Assert.Contains("futureKey", json);
        Assert.DoesNotContain("sk-", json);
        var fresh = AppData.FromJson("{}")!;
        Assert.True(fresh.Settings.Ask);
        Assert.Equal(Ask.DefaultModel, fresh.Settings.AskModel);
    }

    /// <summary>Sends `first` (if any), then waits forever until cancelled.</summary>
    private sealed class StallingStream(string first = "") : Stream
    {
        private byte[] _pending = Encoding.UTF8.GetBytes(first);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pending.Length > 0)
            {
                int n = Math.Min(buffer.Length, _pending.Length);
                _pending.AsMemory(0, n).CopyTo(buffer);
                _pending = _pending[n..];
                return n;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    }
}
