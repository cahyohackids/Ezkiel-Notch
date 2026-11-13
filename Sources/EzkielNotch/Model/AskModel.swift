import AppKit
import Foundation
import NotchCore
import Security

/// Where Ask's API key lives: the Keychain. Never data.json, never the log.
protocol AskKeyStore: AnyObject {
    func read() -> String?
    @discardableResult func save(_ key: String) -> Bool
    func remove()
}

/// The user's own key for api.ezkielyna.store, as a generic password in the login keychain.
final class KeychainAskKeys: AskKeyStore {
    private let query: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: "store.ezkielyna.notch.ask",
        kSecAttrAccount as String: "api.ezkielyna.store",
    ]

    func read() -> String? {
        var q = query
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var out: CFTypeRef?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    func save(_ key: String) -> Bool {
        let data = Data(key.utf8)
        let status = SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        guard status == errSecItemNotFound else { return status == errSecSuccess }
        var add = query
        add[kSecValueData as String] = data
        add[kSecAttrLabel as String] = "Ezkiel Notch · Ask API key"
        return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
    }

    func remove() {
        _ = SecItemDelete(query as CFDictionary)
    }
}

/// The self-test's key store: memory only, so it never reads or writes the user's Keychain.
final class MemoryAskKeys: AskKeyStore {
    private var key: String?
    func read() -> String? { key }
    func save(_ key: String) -> Bool { self.key = key; return true }
    func remove() { key = nil }
}

/// How Ask reaches the gateway. The self-test swaps in a fake, so it never touches the network.
protocol AskTransport: Sendable {
    /// The status, the content type and the body line by line.
    func lines(_ request: URLRequest) async throws -> (status: Int, contentType: String, lines: AsyncThrowingStream<String, Error>)
    func data(_ request: URLRequest) async throws -> (status: Int, body: Data)
}

struct URLSessionAskTransport: AskTransport {
    private let session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        // An idle limit (no byte for this long), not a limit on the whole answer.
        config.timeoutIntervalForRequest = Ask.idleTimeout
        config.urlCache = nil
        return URLSession(configuration: config)
    }()

    func lines(_ request: URLRequest) async throws -> (status: Int, contentType: String, lines: AsyncThrowingStream<String, Error>) {
        let (bytes, response) = try await session.bytes(for: request)
        let http = response as? HTTPURLResponse
        let stream = AsyncThrowingStream<String, Error> { continuation in
            let task = Task {
                do {
                    for try await line in bytes.lines { continuation.yield(line) }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
        return (http?.statusCode ?? 0, http?.mimeType ?? "", stream)
    }

    func data(_ request: URLRequest) async throws -> (status: Int, body: Data) {
        let (data, response) = try await session.data(for: request)
        return ((response as? HTTPURLResponse)?.statusCode ?? 0, data)
    }
}

/// Ask's state: one question at a time, its answer as it streams, and this session's earlier questions
/// (in memory only, never saved).
@MainActor
final class AskModel: ObservableObject {
    enum Phase: Equatable { case idle, waiting, thinking, streaming, done, failed }
    enum KeyState: Equatable { case unknown, checking, ok, failed(AskError) }

    @Published var draft = ""
    @Published private(set) var question = ""
    @Published private(set) var answer = ""
    @Published private(set) var phase: Phase = .idle {
        didSet { if busy != (oldValue == .waiting || oldValue == .thinking || oldValue == .streaming) { onBusyChange(busy) } }
    }
    @Published private(set) var error: AskError?
    /// Stop was pressed before the answer was complete.
    @Published private(set) var stopped = false
    @Published private(set) var history: [Ask.Turn] = []
    /// Models from /models, once a key was tested (empty: the suggested two).
    @Published private(set) var models: [String] = []
    @Published private(set) var keyState: KeyState = .unknown
    /// Known once the key was looked up (the Keychain is only read when Ask or its settings are used).
    @Published private(set) var hasKey = false
    /// Bumped to put the caret in the ask field.
    @Published var focusRequest = 0

    var transport: AskTransport = URLSessionAskTransport()
    var keys: AskKeyStore = KeychainAskKeys()
    /// Holds the notch open while an answer streams.
    var onBusyChange: (Bool) -> Void = { _ in }

    private var keyLoaded = false
    private var keyValue: String?
    /// Where this conversation starts in the history ("New question" starts a fresh one).
    private var contextStart = 0
    private var task: Task<Void, Never>?
    private var run = 0

    var busy: Bool { phase == .waiting || phase == .thinking || phase == .streaming }

    // MARK: Key

    func currentKey() -> String? {
        if !keyLoaded {
            keyValue = keys.read()
            keyLoaded = true
            hasKey = !(keyValue ?? "").isEmpty
        }
        return keyValue
    }

    /// The key with only its last 4 characters showing.
    var maskedKey: String {
        guard let key = currentKey(), !key.isEmpty else { return "" }
        return String(repeating: "•", count: 8) + key.suffix(4)
    }

    @discardableResult
    func saveKey(_ raw: String) -> Bool {
        let key = Ask.cleanKey(raw)
        guard !key.isEmpty, keys.save(key) else { return false }
        keyValue = key
        keyLoaded = true
        hasKey = true
        keyState = .unknown
        // "Add your key" (or "That key didn't work") with nothing answered yet: the question goes back in the box.
        if error?.needsSettings == true, !busy, answer.isEmpty {
            if draft.isEmpty { draft = question }
            clear()
        }
        return true
    }

    func removeKey() {
        keys.remove()
        keyValue = nil
        keyLoaded = true
        hasKey = false
        keyState = .unknown
        models = []
    }

    /// The self-test's fresh start: another key store, nothing remembered.
    func reset(keys: AskKeyStore, transport: AskTransport) {
        stop()
        self.keys = keys
        self.transport = transport
        keyLoaded = false
        keyValue = nil
        hasKey = false
        keyState = .unknown
        models = []
        history = []
        contextStart = 0
        clear()
    }

    // MARK: Asking

    /// When questions went out (for Ask.allowSend); the clock can be swapped in tests.
    private var sent: [Date] = []
    var now: () -> Date = { Date() }

    func send(model: String, indonesian: Bool) {
        let q = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty else { return }
        if busy && q == question { return }   // Return pressed again on the same question: keep the one running
        stop()
        question = q
        answer = ""
        error = nil
        stopped = false
        draft = ""
        guard let key = currentKey(), !key.isEmpty else {
            error = .noKey
            phase = .failed
            return
        }
        guard Ask.allowSend(&sent, now: now()) else {
            error = .rateLimited
            phase = .failed
            return
        }
        sent.append(now())
        phase = .waiting
        run += 1
        let id = run
        let context = Array(history[min(contextStart, history.count)...])
        let request = Self.urlRequest(Ask.chatRequest(key: key, model: model, question: q, history: context, indonesian: indonesian))
        let transport = self.transport
        task = Task { [weak self] in
            var stream = AskStream()
            do {
                let (status, type, lines) = try await transport.lines(request)
                if !(200..<300).contains(status) {
                    var body = ""
                    for try await line in lines {
                        body += line + "\n"
                        if body.utf8.count > 65_536 { break }
                    }
                    stream.fail(AskError.http(status: status, body: body))
                } else if type == "application/json" {
                    // A server that ignored stream: true sends the whole answer as one JSON.
                    var body = ""
                    for try await line in lines { body += line + " " }
                    stream.feed("data: " + body)
                } else {
                    for try await line in lines {
                        let wasReasoning = stream.reasoning
                        if stream.feed(line) != nil || stream.reasoning != wasReasoning { self?.display(id, stream) }
                        if stream.finished { break }
                    }
                }
            } catch {
                if Task.isCancelled || (error as? URLError)?.code == .cancelled { return }
                stream.fail(Self.map(error))
            }
            self?.finish(id, stream)
        }
    }

    private func display(_ id: Int, _ stream: AskStream) {
        guard id == run, busy else { return }
        answer = stream.text
        phase = stream.text.isEmpty ? (stream.reasoning ? .thinking : .waiting) : .streaming
    }

    private func finish(_ id: Int, _ stream: AskStream) {
        guard id == run, busy else { return }
        task = nil
        answer = stream.text
        error = stream.error
        phase = stream.error == nil ? .done : .failed
        remember()
    }

    /// Stops the answer where it is (what came so far stays).
    func stop() {
        guard busy else { return }
        run += 1
        task?.cancel()
        task = nil
        stopped = true
        phase = .done
        remember()
    }

    /// "New question": an empty box and a fresh conversation (the session's list stays).
    func clear() {
        stop()
        question = ""
        answer = ""
        error = nil
        stopped = false
        phase = .idle
        contextStart = history.count
        focusRequest += 1
    }

    /// Shows an earlier question and its answer again.
    func show(_ turn: Ask.Turn) {
        stop()
        question = turn.question
        answer = turn.answer
        error = nil
        stopped = false
        phase = .done
    }

    /// Asks the last question again (after an error).
    func retry(model: String, indonesian: Bool) {
        guard !question.isEmpty else { return }
        draft = question
        send(model: model, indonesian: indonesian)
    }

    private func remember() {
        guard !answer.isEmpty else { return }
        history.append(Ask.Turn(question: question, answer: answer))
        if history.count > Ask.historyLimit {
            history.removeFirst()
            contextStart = max(0, contextStart - 1)
        }
    }

    // MARK: Settings

    /// "Test key": asks /models with the key (nothing is spent) and keeps the list.
    func testKey() {
        guard let key = currentKey(), !key.isEmpty else {
            keyState = .failed(.noKey)
            return
        }
        keyState = .checking
        let request = Self.urlRequest(Ask.modelsRequest(key: key))
        let transport = self.transport
        Task { [weak self] in
            let result: KeyState
            var found: [String] = []
            do {
                let (status, body) = try await transport.data(request)
                if (200..<300).contains(status) {
                    found = Ask.parseModels(body)
                    result = .ok
                } else {
                    result = .failed(AskError.http(status: status, body: String(decoding: body, as: UTF8.self)))
                }
            } catch {
                result = .failed(Self.map(error))
            }
            guard let self else { return }
            self.keyState = result
            if !found.isEmpty { self.models = found }
        }
    }

    /// The models to offer: the suggested two first, then the rest from /models.
    func modelChoices(current: String) -> [String] {
        var list = Ask.suggestedModels
        for m in models where !list.contains(m) { list.append(m) }
        if !current.isEmpty, !list.contains(current) { list.append(current) }
        return list
    }

    // MARK: Plumbing

    static func urlRequest(_ r: Ask.Request) -> URLRequest {
        var request = URLRequest(url: URL(string: r.url)!)
        request.httpMethod = r.method
        for (name, value) in r.headers { request.setValue(value, forHTTPHeaderField: name) }
        request.httpBody = r.body
        return request
    }

    static func map(_ error: Error) -> AskError {
        guard let e = error as? URLError else { return .other(error.localizedDescription) }
        switch e.code {
        case .timedOut: return .timeout
        case .notConnectedToInternet, .networkConnectionLost, .cannotFindHost, .cannotConnectToHost, .dnsLookupFailed,
             .internationalRoamingOff, .dataNotAllowed, .callIsActive:
            return .offline
        default: return .other(e.localizedDescription)
        }
    }
}
