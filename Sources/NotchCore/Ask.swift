import Foundation

/// Ask: a quick question to the Ezkielyna AI gateway (OpenAI-compatible) from the open notch.
/// Everything here is plain logic (no networking): the request, the event-stream reader, the error mapping,
/// the model list and the text clean-up. The Windows app's Ask.cs follows the same rules, and both are tested
/// against Tests/fixtures/ai-sse.json. The user's own API key is passed in; it is never stored here.
public enum Ask {
    public static let baseURL = "https://api.ezkielyna.store/v1"
    /// Where a user gets a key.
    public static let keyPage = "https://api.ezkielyna.store"
    public static let defaultModel = "deepseek-v4.1-flash"
    /// Offered even before /models loads.
    public static let suggestedModels = ["deepseek-v4.1-flash", "deepseek-v4-pro-0813"]
    /// Earlier questions sent along, so "and in Bali?" makes sense.
    public static let contextTurns = 3
    /// Questions kept for this session (never saved).
    public static let historyLimit = 10
    /// Seconds without a byte before giving up.
    public static let idleTimeout: Double = 30

    public static func systemPrompt(indonesian: Bool) -> String {
        indonesian
            ? "Kamu Ezkiel, asisten cepat di dalam app Ezkiel Notch. Jawab singkat dan langsung ke inti: beberapa kalimat atau daftar pendek, tanpa basa-basi. Teks biasa, markdown seminimal mungkin. Balas dalam Bahasa Indonesia, kecuali pengguna menulis dalam bahasa lain."
            : "You are Ezkiel, the quick helper inside the Ezkiel Notch app. Answer short and to the point: a few sentences or a short list, no preamble. Plain text, minimal markdown. Reply in English unless the user writes in another language."
    }

    /// One question and its answer, kept in memory for the session.
    public struct Turn: Equatable {
        public var question: String
        public var answer: String
        public init(question: String, answer: String) {
            self.question = question
            self.answer = answer
        }
    }

    /// A request, ready for any HTTP client (the app turns it into a URLRequest).
    public struct Request: Equatable {
        public var url: String
        public var method: String
        public var headers: [String: String]
        public var body: Data?
    }

    public static func cleanKey(_ key: String) -> String {
        key.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    public static func messages(question: String, history: [Turn], indonesian: Bool) -> [[String: String]] {
        var list = [["role": "system", "content": systemPrompt(indonesian: indonesian)]]
        for turn in history.suffix(contextTurns) where !turn.answer.isEmpty {
            list.append(["role": "user", "content": turn.question])
            list.append(["role": "assistant", "content": turn.answer])
        }
        list.append(["role": "user", "content": question.trimmingCharacters(in: .whitespacesAndNewlines)])
        return list
    }

    /// POST /chat/completions with `stream: true`.
    public static func chatRequest(key: String, model: String, question: String, history: [Turn], indonesian: Bool) -> Request {
        let body: [String: Any] = [
            "model": model.isEmpty ? defaultModel : model,
            "stream": true,
            "messages": messages(question: question, history: history, indonesian: indonesian),
        ]
        return Request(url: baseURL + "/chat/completions", method: "POST",
                       headers: ["Authorization": "Bearer " + cleanKey(key), "Content-Type": "application/json",
                                 "Accept": "text/event-stream"],
                       body: try? JSONSerialization.data(withJSONObject: body, options: [.sortedKeys]))
    }

    /// GET /models (also how "Test key" checks a key without spending anything).
    public static func modelsRequest(key: String) -> Request {
        Request(url: baseURL + "/models", method: "GET",
                headers: ["Authorization": "Bearer " + cleanKey(key), "Accept": "application/json"], body: nil)
    }

    /// The chat models in a /models reply, in its order, without duplicates and without embedding, speech,
    /// image or rerank models (they can't answer a question).
    public static func parseModels(_ data: Data) -> [String] {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let list = root["data"] as? [Any] else { return [] }
        var ids: [String] = []
        for item in list {
            guard let id = (item as? [String: Any])?["id"] as? String, !id.isEmpty, !ids.contains(id) else { continue }
            let lower = id.lowercased()
            if ["embed", "whisper", "tts", "dall-e", "moderation", "rerank"].contains(where: lower.contains) { continue }
            ids.append(id)
        }
        return ids
    }

    // MARK: Text

    /// Minimal markdown for a small box: headings lose their #, list bullets become •, code fences go.
    /// Inline **bold** and `code` stay (the Mac draws them).
    public static func tidy(_ text: String) -> String { clean(text, plain: false) }

    /// Like `tidy`, without the inline markers: for Copy, notes and task titles.
    public static func plainText(_ text: String) -> String { clean(text, plain: true) }

    private static func rx(_ pattern: String) -> NSRegularExpression {
        // The patterns are constants; a typo should fail the tests, not ship.
        try! NSRegularExpression(pattern: pattern)
    }

    private static let heading = rx(#"^\s{0,3}#{1,6}\s+"#)
    private static let bullet = rx(#"^(\s*)[-*+]\s+"#)
    private static let bold = rx(#"\*\*(.+?)\*\*"#)
    private static let underline = rx(#"__(.+?)__"#)
    private static let code = rx(#"`([^`]+)`"#)

    private static func replace(_ e: NSRegularExpression, in s: String, with template: String) -> String {
        e.stringByReplacingMatches(in: s, range: NSRange(location: 0, length: (s as NSString).length), withTemplate: template)
    }

    private static func clean(_ text: String, plain: Bool) -> String {
        var out: [String] = []
        var inFence = false
        for raw in text.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n") {
            if raw.trimmingCharacters(in: .whitespaces).hasPrefix("```") {
                inFence.toggle()
                continue
            }
            if inFence {
                out.append(raw)
                continue
            }
            var line = replace(heading, in: raw, with: "")
            line = replace(bullet, in: line, with: "$1• ")
            if plain {
                line = replace(bold, in: line, with: "$1")
                line = replace(underline, in: line, with: "$1")
                line = replace(code, in: line, with: "$1")
            }
            out.append(line)
        }
        return out.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// "Make task": the question when it's short (a time in it, like "jam 3", becomes the reminder),
    /// else the answer's first line. At most 80 characters.
    public static func makeTask(question: String, answer: String, now: Date = Date(), calendar: Calendar = .current) -> (title: String, due: Date?) {
        let q = question.trimmingCharacters(in: .whitespacesAndNewlines)
        if q.count <= 80 || answer.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            if let r = DuePhrase.parse(q, now: now, calendar: calendar), !r.title.isEmpty { return (clip(r.title), r.due) }
            return (clip(q), nil)
        }
        let first = plainText(answer).components(separatedBy: "\n")
            .map { $0.trimmingCharacters(in: .whitespaces) }.first { !$0.isEmpty } ?? q
        return (clip(first), nil)
    }

    /// "Save as note": the question as its title, the answer as plain text.
    public static func note(question: String, answer: String) -> (title: String, body: String) {
        (clip(question.trimmingCharacters(in: .whitespacesAndNewlines)), plainText(answer))
    }

    static func clip(_ s: String, _ limit: Int = 80) -> String {
        s.count <= limit ? s : String(s.prefix(limit - 1)).trimmingCharacters(in: .whitespaces) + "…"
    }
}

/// What went wrong, in words a person understands. The UI shows `title` and `detail` through L().
public enum AskError: Error, Equatable {
    case noKey, invalidKey, noBalance, rateLimited, offline, timeout, server(Int), other(String)

    /// The fixture's name for it.
    public var code: String {
        switch self {
        case .noKey: return "noKey"
        case .invalidKey: return "invalidKey"
        case .noBalance: return "noBalance"
        case .rateLimited: return "rateLimited"
        case .offline: return "offline"
        case .timeout: return "timeout"
        case .server: return "server"
        case .other: return "other"
        }
    }

    public var title: String {
        switch self {
        case .noKey: return "Add your API key"
        case .invalidKey: return "That API key didn't work"
        case .noBalance: return "Your balance is empty"
        case .rateLimited: return "Too many questions at once"
        case .offline: return "You're offline"
        case .timeout: return "No answer in time"
        case .server: return "The AI service is having trouble"
        case .other: return "Couldn't get an answer"
        }
    }

    /// English; `other` carries the server's own words.
    public var detail: String {
        switch self {
        case .noKey: return "Paste your Ezkielyna key in Settings › Features"
        case .invalidKey: return "Check the key in Settings, or make a new one"
        case .noBalance: return "Top up at api.ezkielyna.store, then ask again"
        case .rateLimited: return "Wait a moment and ask again"
        case .offline: return "Check your connection and ask again"
        case .timeout: return "The service was too slow. Ask again"
        case .server: return "Try again in a minute"
        case .other(let message): return message
        }
    }

    /// The key or the account needs attention: the error shows a Settings button.
    public var needsSettings: Bool {
        switch self {
        case .noKey, .invalidKey, .noBalance: return true
        default: return false
        }
    }

    private static func mentions(_ text: String, _ words: [String]) -> Bool {
        let lower = text.lowercased()
        return words.contains { lower.contains($0) }
    }

    private static let balanceWords = ["balance", "insufficient", "quota", "credit", "saldo", "billing", "payment", "top up", "top-up"]
    private static let keyWords = ["invalid api key", "invalid_api_key", "incorrect api key", "invalid token", "unauthorized",
                                   "unauthenticated", "api key not", "no api key"]

    /// An HTTP status and the reply's body.
    public static func http(status: Int, body: String) -> AskError {
        let (message, all) = describe(body)
        return map(status: status, message: message, all: all)
    }

    /// An `{"error": …}` object that came inside the stream.
    static func streamed(_ error: Any) -> AskError {
        var status: Int?
        if let o = error as? [String: Any] {
            status = (o["code"] as? Int) ?? (o["status"] as? Int)
        }
        let (message, all) = describe(any: error)
        return map(status: status, message: message, all: all)
    }

    private static func map(status: Int?, message: String, all: String) -> AskError {
        let balance = mentions(all, balanceWords), key = mentions(all, keyWords)
        switch status {
        case 401: return .invalidKey
        case 402: return .noBalance
        case 403: return balance ? .noBalance : key ? .invalidKey : .other(message.isEmpty ? "HTTP 403" : message)
        case 429: return balance ? .noBalance : .rateLimited
        case 408, 504: return .timeout
        case let s? where s >= 500: return .server(s)
        default:
            if balance { return .noBalance }
            if key { return .invalidKey }
            return .other(message.isEmpty ? status.map { "HTTP \($0)" } ?? "Unknown error" : message)
        }
    }

    /// The readable message in an error body, and everything worth searching for keywords.
    private static func describe(_ body: String) -> (String, String) {
        let text = body.trimmingCharacters(in: .whitespacesAndNewlines)
        if let data = text.data(using: .utf8), let json = try? JSONSerialization.jsonObject(with: data) {
            if let o = json as? [String: Any] {
                if let e = o["error"] { return describe(any: e) }
                if let m = (o["message"] as? String) ?? (o["detail"] as? String) { return (m, m) }
            }
            return ("", text)
        }
        // HTML error pages say nothing useful.
        if text.hasPrefix("<") { return ("", text) }
        return (String(text.prefix(200)), text)
    }

    private static func describe(any error: Any) -> (String, String) {
        if let s = error as? String { return (s, s) }
        guard let o = error as? [String: Any] else { return ("", "") }
        let message = o["message"] as? String ?? ""
        let extra = [o["type"], o["code"]].compactMap { $0 as? String }.joined(separator: " ")
        return (message, message + " " + extra)
    }
}

/// Reads the event stream of /chat/completions one line at a time.
public struct AskStream: Equatable {
    /// The answer so far.
    public private(set) var text = ""
    /// `[DONE]` came, or an error ended it.
    public private(set) var finished = false
    /// The model sent its thinking (reasoning_content) before the answer.
    public private(set) var reasoning = false
    public private(set) var error: AskError?

    public init() {}

    /// Ends the stream with an error that didn't come in it (HTTP status, offline, timeout).
    public mutating func fail(_ error: AskError) {
        self.error = error
        finished = true
    }

    /// Returns the new piece of the answer, if this line had one.
    @discardableResult
    public mutating func feed(_ rawLine: String) -> String? {
        guard !finished else { return nil }
        var line = rawLine
        while line.hasSuffix("\r") || line.hasSuffix("\n") { line.removeLast() }
        guard line.hasPrefix("data:") else { return nil }   // comments, event:, id:, retry:, blank lines
        var payload = String(line.dropFirst(5))
        if payload.hasPrefix(" ") { payload.removeFirst() }
        if payload.trimmingCharacters(in: .whitespaces) == "[DONE]" {
            finished = true
            return nil
        }
        guard let data = payload.data(using: .utf8),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return nil }
        if let e = o["error"] {
            error = AskError.streamed(e)
            finished = true
            return nil
        }
        guard let choice = (o["choices"] as? [Any])?.first as? [String: Any] else { return nil }
        let part = (choice["delta"] as? [String: Any]) ?? (choice["message"] as? [String: Any]) ?? [:]
        if part["reasoning_content"] is String || part["reasoning"] is String { reasoning = true }
        guard let piece = part["content"] as? String, !piece.isEmpty else { return nil }
        text += piece
        return piece
    }
}
