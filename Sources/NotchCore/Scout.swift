import Foundation

/// Scout: one thing an AI agent (or any script) told the notch.
public struct ScoutEvent: Codable, Identifiable, Equatable {
    public enum Kind: String, Codable {
        /// The agent stopped and is waiting for you (permission, a question, idle).
        case needsYou
        /// The agent finished its turn.
        case done
        /// The agent started working on a prompt.
        case working
        /// Anything else (scripts, cron jobs, bots).
        case info
    }

    public var id: UUID
    public var source: String
    public var kind: Kind
    public var title: String
    public var body: String
    /// Project folder the agent ran in, when known.
    public var cwd: String?
    /// Agent session, so "working" and "done" for the same run pair up.
    public var session: String?
    /// App to bring forward when the event is opened (the terminal or editor that ran the agent).
    public var appBundleID: String?
    public var date: Date

    public init(id: UUID = UUID(), source: String, kind: Kind, title: String, body: String = "",
                cwd: String? = nil, session: String? = nil, appBundleID: String? = nil, date: Date = Date()) {
        self.id = id
        self.source = source
        self.kind = kind
        self.title = title
        self.body = body
        self.cwd = cwd
        self.session = session
        self.appBundleID = appBundleID
        self.date = date
    }

    /// "ezkiel-notch" from "/Users/me/Ezkielyna/ezkiel-notch".
    public var project: String? {
        guard let cwd, !cwd.isEmpty else { return nil }
        return (cwd as NSString).lastPathComponent
    }
}

/// Turns what agents send into Scout events. Pure, so it is unit-tested.
public enum ScoutParser {
    /// Claude Code hook input (stdin JSON): Notification, Stop, SubagentStop, UserPromptSubmit.
    public static func claudeHook(_ data: Data, appBundleID: String? = nil, now: Date = Date()) -> ScoutEvent? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let event = json["hook_event_name"] as? String else { return nil }
        let cwd = json["cwd"] as? String
        let session = json["session_id"] as? String
        func make(_ kind: ScoutEvent.Kind, _ title: String, _ body: String) -> ScoutEvent {
            ScoutEvent(source: "claude", kind: kind, title: title, body: body, cwd: cwd, session: session,
                       appBundleID: appBundleID, date: now)
        }
        switch event {
        case "Notification":
            let message = (json["message"] as? String) ?? "Claude is waiting for you"
            return make(.needsYou, "Claude needs you", message)
        case "Stop":
            return make(.done, "Claude finished", "Your turn")
        case "SubagentStop":
            return make(.info, "A subagent finished", "")
        case "UserPromptSubmit":
            let prompt = (json["prompt"] as? String).map { String($0.prefix(140)) } ?? ""
            return make(.working, "Claude is working", prompt)
        default:
            return nil
        }
    }

    /// Codex CLI `notify` program argument: {"type":"agent-turn-complete", "last-assistant-message": ..., ...}.
    public static func codexNotify(_ argument: String, appBundleID: String? = nil, now: Date = Date()) -> ScoutEvent? {
        guard let data = argument.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let type = json["type"] as? String else { return nil }
        guard type == "agent-turn-complete" else {
            return ScoutEvent(source: "codex", kind: .info, title: "Codex", body: type, appBundleID: appBundleID, date: now)
        }
        let last = (json["last-assistant-message"] as? String) ?? ""
        let cwd = json["cwd"] as? String
        let session = (json["thread-id"] as? String) ?? (json["turn-id"] as? String)
        return ScoutEvent(source: "codex", kind: .done, title: "Codex finished",
                          body: String(last.trimmingCharacters(in: .whitespacesAndNewlines).prefix(200)),
                          cwd: cwd, session: session, appBundleID: appBundleID, date: now)
    }

    /// `ezkielnotch://notify?title=…&body=…&source=…&kind=…` (also what the CLI sends).
    public static func url(_ url: URL, now: Date = Date()) -> ScoutEvent? {
        guard url.scheme?.lowercased() == "ezkielnotch",
              url.host?.lowercased() == "notify",
              let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems else { return nil }
        func value(_ name: String) -> String? { items.first { $0.name == name }?.value }
        guard let title = value("title"), !title.isEmpty else { return nil }
        return ScoutEvent(source: value("source") ?? "script",
                          kind: value("kind").flatMap(ScoutEvent.Kind.init(rawValue:)) ?? .info,
                          title: String(title.prefix(80)), body: String((value("body") ?? "").prefix(200)),
                          cwd: value("cwd"), session: value("session"), appBundleID: value("app"), date: now)
    }
}

extension ScoutEvent {
    /// Anything can post to Scout, so every field is trimmed to what the notch can show.
    public func clamped() -> ScoutEvent {
        var e = self
        e.source = String(source.prefix(24))
        e.title = String(title.prefix(80))
        e.body = String(body.prefix(200))
        e.cwd = cwd.map { String($0.prefix(300)) }
        e.session = session.map { String($0.prefix(80)) }
        e.appBundleID = appBundleID.map { String($0.prefix(120)) }
        return e
    }
}

/// How a Scout event travels from the sender (`EzkielNotch --claude-hook`, …) to the running app.
public enum ScoutWire {
    public static func encode(_ event: ScoutEvent) -> String? {
        (try? JSONEncoder().encode(event)).flatMap { String(data: $0, encoding: .utf8) }
    }

    public static func decode(_ text: String) -> ScoutEvent? {
        guard text.utf8.count < 8_192, let data = text.data(using: .utf8) else { return nil }
        return (try? JSONDecoder().decode(ScoutEvent.self, from: data))?.clamped()
    }
}

/// Dock badge text → unread count: "8" → 8, "99+" → 99, "•" or "New" → 1, "" or "0" → nil.
public enum BadgeLabel {
    public static func count(_ label: String?) -> Int? {
        guard let raw = label?.trimmingCharacters(in: .whitespacesAndNewlines), !raw.isEmpty else { return nil }
        let digits = raw.filter { $0.isASCII && $0.isNumber }
        if digits.isEmpty { return 1 }
        guard let n = Int(digits), n > 0 else { return nil }
        return n
    }
}

/// Connecting agents to Scout: edits Claude Code's settings.json and Codex's config.toml while
/// keeping everything else in them. Pure (data in, data out), so it is unit-tested.
public enum ScoutSetup {
    public enum LinkState: Equatable {
        case off
        case on
        /// Connected to a copy of the app that has moved (or an older build): reconnect.
        case stale
    }

    public enum SetupError: Error, Equatable {
        /// The file isn't valid JSON (or its "hooks" isn't an object); we never overwrite it.
        case unreadable
        /// Codex already runs another notify program; we don't replace it.
        case otherNotify(String)
    }

    public static let claudeEvents = ["Notification", "Stop", "UserPromptSubmit"]

    // MARK: Claude Code

    public static func claudeCommand(executable: String) -> String {
        "'\(executable.replacingOccurrences(of: "'", with: #"'\''"#))' --claude-hook"
    }

    private static func isOurs(_ command: String) -> Bool {
        command.contains("--claude-hook") && command.contains("EzkielNotch")
    }

    public static func connectClaude(_ existing: Data?, command: String) throws -> Data {
        var root = try jsonObject(existing)
        var hooks = try hooksObject(root)
        for event in claudeEvents {
            var groups = withoutOurs(hooks[event]).groups
            groups.append(["hooks": [["type": "command", "command": command, "timeout": 10] as [String: Any]]])
            hooks[event] = groups
        }
        root["hooks"] = hooks
        return try serialize(root)
    }

    public static func disconnectClaude(_ existing: Data?) throws -> Data {
        var root = try jsonObject(existing)
        var hooks = try hooksObject(root)
        for (event, value) in hooks {
            let result = withoutOurs(value)
            guard result.removed else { continue }
            hooks[event] = result.groups.isEmpty ? nil : result.groups
        }
        root["hooks"] = hooks.isEmpty ? nil : hooks
        return try serialize(root)
    }

    public static func claudeState(_ existing: Data?, command: String) -> LinkState {
        guard let root = try? jsonObject(existing), let hooks = try? hooksObject(root) else { return .off }
        let commands = claudeEvents.map { event in
            ((hooks[event] as? [[String: Any]]) ?? []).flatMap { ($0["hooks"] as? [[String: Any]]) ?? [] }
                .compactMap { $0["command"] as? String }
        }
        if commands.allSatisfy({ $0.contains(command) }) { return .on }
        return commands.joined().contains(where: isOurs) ? .stale : .off
    }

    private static func jsonObject(_ data: Data?) throws -> [String: Any] {
        guard let data, !data.allSatisfy({ $0 == 0x20 || $0 == 0x0A || $0 == 0x0D || $0 == 0x09 }) else { return [:] }
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw SetupError.unreadable }
        return object
    }

    private static func hooksObject(_ root: [String: Any]) throws -> [String: Any] {
        guard let value = root["hooks"] else { return [:] }
        guard let hooks = value as? [String: Any] else { throw SetupError.unreadable }
        return hooks
    }

    /// The event's hook groups minus our commands; groups left empty by that are dropped.
    private static func withoutOurs(_ value: Any?) -> (groups: [[String: Any]], removed: Bool) {
        var removed = false
        let groups = ((value as? [[String: Any]]) ?? []).compactMap { group -> [String: Any]? in
            guard let hooks = group["hooks"] as? [[String: Any]] else { return group }
            let kept = hooks.filter { !isOurs(($0["command"] as? String) ?? "") }
            guard kept.count != hooks.count else { return group }
            removed = true
            if kept.isEmpty { return nil }
            var g = group
            g["hooks"] = kept
            return g
        }
        return (groups, removed)
    }

    private static func serialize(_ root: [String: Any]) throws -> Data {
        var data = try JSONSerialization.data(withJSONObject: root, options: [.prettyPrinted, .withoutEscapingSlashes, .sortedKeys])
        data.append(0x0A)
        return data
    }

    // MARK: Codex

    public static func codexLine(executable: String) -> String {
        let escaped = executable.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        return "notify = [\"\(escaped)\", \"--codex-notify\"]"
    }

    /// Lines before the first `[table]`: only there is `notify` a top-level key.
    private static func topLevelEnd(_ lines: [String]) -> Int {
        lines.firstIndex { $0.trimmingCharacters(in: .whitespaces).hasPrefix("[") } ?? lines.count
    }

    private static func isNotifyLine(_ line: String) -> Bool {
        let t = line.trimmingCharacters(in: .whitespaces)
        guard t.hasPrefix("notify") else { return false }
        return t.dropFirst("notify".count).trimmingCharacters(in: .whitespaces).hasPrefix("=")
    }

    public static func connectCodex(_ existing: String, executable: String) throws -> String {
        var lines = existing.isEmpty ? [] : existing.components(separatedBy: "\n")
        let line = codexLine(executable: executable)
        if let i = lines[..<topLevelEnd(lines)].firstIndex(where: isNotifyLine) {
            guard lines[i].contains("--codex-notify") else { throw SetupError.otherNotify(lines[i]) }
            lines[i] = line
        } else {
            // Top-level keys have to come before the first table.
            lines.insert(line, at: 0)
        }
        var text = lines.joined(separator: "\n")
        if !text.hasSuffix("\n") { text += "\n" }
        return text
    }

    public static func disconnectCodex(_ existing: String) -> String {
        var lines = existing.components(separatedBy: "\n")
        let end = topLevelEnd(lines)
        lines = lines.enumerated().filter { i, l in !(i < end && isNotifyLine(l) && l.contains("--codex-notify")) }.map(\.element)
        return lines.joined(separator: "\n")
    }

    public static func codexState(_ existing: String, executable: String) -> LinkState {
        let lines = existing.components(separatedBy: "\n")
        guard let line = lines[..<topLevelEnd(lines)].first(where: isNotifyLine), line.contains("--codex-notify") else { return .off }
        return line.trimmingCharacters(in: .whitespaces) == codexLine(executable: executable) ? .on : .stale
    }
}
