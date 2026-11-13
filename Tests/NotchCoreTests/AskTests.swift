import XCTest
@testable import NotchCore

/// Ask, against the fixture the Windows tests read too. No network: only the logic around it.
final class AskTests: XCTestCase {
    private func fixture() throws -> [String: Any] {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appendingPathComponent("../fixtures/ai-sse.json").standardized
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
    }

    func testSystemPrompts() throws {
        let system = try XCTUnwrap(try fixture()["system"] as? [String: String])
        XCTAssertEqual(Ask.systemPrompt(indonesian: false), system["en"])
        XCTAssertEqual(Ask.systemPrompt(indonesian: true), system["id"])
    }

    func testChatRequest() throws {
        let r = try XCTUnwrap(try fixture()["request"] as? [String: Any])
        let history = (r["history"] as? [[String: String]] ?? []).map { Ask.Turn(question: $0["question"]!, answer: $0["answer"]!) }
        let req = Ask.chatRequest(key: r["key"] as! String, model: r["model"] as! String, question: r["question"] as! String,
                                  history: history, indonesian: r["indonesian"] as! Bool)
        XCTAssertEqual(req.url, r["url"] as? String)
        XCTAssertEqual(req.method, r["method"] as? String)
        XCTAssertEqual(req.headers, r["headers"] as? [String: String])
        // Compared as canonical JSON (sorted keys), the same on macOS and Linux.
        func canonical(_ o: Any) throws -> String {
            String(decoding: try JSONSerialization.data(withJSONObject: o, options: [.sortedKeys]), as: UTF8.self)
        }
        let body = try JSONSerialization.jsonObject(with: try XCTUnwrap(req.body))
        XCTAssertEqual(try canonical(body), try canonical(try XCTUnwrap(r["body"])))

        let models = Ask.modelsRequest(key: r["key"] as! String)
        XCTAssertEqual(models.url, r["modelsUrl"] as? String)
        XCTAssertEqual(models.method, "GET")
        XCTAssertEqual(models.headers["Authorization"], "Bearer sk-test-not-a-real-key")
        XCTAssertNil(models.body)
    }

    func testEmptyModelFallsBackToTheDefault() throws {
        let req = Ask.chatRequest(key: "k", model: "", question: "hi", history: [], indonesian: false)
        let body = try XCTUnwrap(JSONSerialization.jsonObject(with: try XCTUnwrap(req.body)) as? [String: Any])
        XCTAssertEqual(body["model"] as? String, Ask.defaultModel)
    }

    func testLimitsMatchTheFixtureAndHold() throws {
        let l = try XCTUnwrap(try fixture()["limits"] as? [String: Any])
        XCTAssertEqual(l["maxAnswerTokens"] as? Int, Ask.maxAnswerTokens)
        XCTAssertEqual(l["maxQuestionLength"] as? Int, Ask.maxQuestionLength)
        XCTAssertEqual(l["maxContextLength"] as? Int, Ask.maxContextLength)
        XCTAssertEqual(l["maxQuestionsPerWindow"] as? Int, Ask.maxQuestionsPerWindow)
        XCTAssertEqual((l["windowSeconds"] as? NSNumber)?.doubleValue, Ask.windowSeconds)

        let req = Ask.chatRequest(key: "k", model: "", question: String(repeating: "q", count: 5000),
                                  history: [Ask.Turn(question: String(repeating: "a", count: 4000), answer: String(repeating: "b", count: 4000))],
                                  indonesian: false)
        let body = try XCTUnwrap(JSONSerialization.jsonObject(with: try XCTUnwrap(req.body)) as? [String: Any])
        XCTAssertEqual(body["max_tokens"] as? Int, Ask.maxAnswerTokens)
        let messages = try XCTUnwrap(body["messages"] as? [[String: String]])
        XCTAssertEqual(messages[1]["content"]?.count, Ask.maxContextLength)
        XCTAssertEqual(messages[2]["content"]?.count, Ask.maxContextLength)
        XCTAssertEqual(messages.last?["content"]?.count, Ask.maxQuestionLength)
        XCTAssertLessThan(try XCTUnwrap(req.body).count, 10_000)

        let now = Date(timeIntervalSince1970: 1_794_571_200)
        var sent = (0..<Ask.maxQuestionsPerWindow).map { now.addingTimeInterval(-Double($0)) }
        XCTAssertFalse(Ask.allowSend(&sent, now: now))
        XCTAssertTrue(Ask.allowSend(&sent, now: now.addingTimeInterval(Ask.windowSeconds)))
        var none: [Date] = []
        XCTAssertTrue(Ask.allowSend(&none, now: now))
    }

    func testStreams() throws {
        for s in try XCTUnwrap(try fixture()["streams"] as? [[String: Any]]) {
            let name = s["name"] as? String ?? "?"
            var stream = AskStream()
            var pieces = ""
            for line in s["lines"] as? [String] ?? [] { pieces += stream.feed(line) ?? "" }
            XCTAssertEqual(stream.text, s["text"] as? String, name)
            XCTAssertEqual(pieces, stream.text, "\(name): the pieces add up to the text")
            XCTAssertEqual(stream.finished, s["finished"] as? Bool, name)
            XCTAssertEqual(stream.reasoning, s["reasoning"] as? Bool, name)
            XCTAssertEqual(stream.error?.code, s["error"] as? String, name)
            if let message = s["message"] as? String { XCTAssertEqual(stream.error?.detail, message, name) }
        }
    }

    func testErrors() throws {
        for e in try XCTUnwrap(try fixture()["errors"] as? [[String: Any]]) {
            let status = e["status"] as! Int, body = e["body"] as! String
            let got = AskError.http(status: status, body: body)
            XCTAssertEqual(got.code, e["error"] as? String, "\(status) \(body)")
            if let message = e["message"] as? String { XCTAssertEqual(got.detail, message, "\(status) \(body)") }
        }
        XCTAssertTrue(AskError.noBalance.needsSettings)
        XCTAssertFalse(AskError.offline.needsSettings)
    }

    func testModels() throws {
        let m = try XCTUnwrap(try fixture()["models"] as? [String: Any])
        XCTAssertEqual(Ask.parseModels(Data((m["body"] as! String).utf8)), m["ids"] as? [String])
        for bad in m["bad"] as? [String] ?? [] { XCTAssertEqual(Ask.parseModels(Data(bad.utf8)), [], bad) }
    }

    func testText() throws {
        for t in try XCTUnwrap(try fixture()["text"] as? [[String: String]]) {
            XCTAssertEqual(Ask.tidy(t["markdown"]!), t["tidy"], t["markdown"]!)
            XCTAssertEqual(Ask.plainText(t["markdown"]!), t["plain"], t["markdown"]!)
        }
        let note = Ask.note(question: "  Apa itu SSE? ", answer: "**Server-Sent Events**: satu arah.")
        XCTAssertEqual(note.title, "Apa itu SSE?")
        XCTAssertEqual(note.body, "Server-Sent Events: satu arah.")
    }

    func testMakeTask() throws {
        let tasks = try XCTUnwrap(try fixture()["tasks"] as? [String: Any])
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "Asia/Jakarta")!
        let f = DateFormatter()
        f.calendar = cal
        f.timeZone = cal.timeZone
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm"
        let now = try XCTUnwrap(f.date(from: tasks["now"] as? String ?? ""))
        for c in tasks["cases"] as? [[String: Any]] ?? [] {
            let r = Ask.makeTask(question: c["question"] as! String, answer: c["answer"] as! String, now: now, calendar: cal)
            XCTAssertEqual(r.title, c["title"] as? String)
            XCTAssertEqual(r.due.map { f.string(from: $0) }, c["due"] as? String, r.title)
        }
    }
}

final class AskSettingsTests: XCTestCase {
    func testAskSettingsRoundTripWithoutTheKey() throws {
        let json = #"{"settings":{"ask":false,"askModel":"qwen3-max"}}"#
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let d = try decoder.decode(AppData.self, from: Data(json.utf8))
        XCTAssertFalse(d.settings.ask)
        XCTAssertEqual(d.settings.askModel, "qwen3-max")
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let again = try decoder.decode(AppData.self, from: try encoder.encode(d))
        XCTAssertEqual(again.settings.askModel, "qwen3-max")
        let fresh = try decoder.decode(AppData.self, from: Data("{}".utf8))
        XCTAssertTrue(fresh.settings.ask)
        XCTAssertEqual(fresh.settings.askModel, Ask.defaultModel)
        XCTAssertEqual(try decoder.decode(AppData.self, from: Data(#"{"settings":{"askModel":""}}"#.utf8)).settings.askModel, Ask.defaultModel)
    }
}
