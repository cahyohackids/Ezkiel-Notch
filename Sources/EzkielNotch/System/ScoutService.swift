import AppKit
import NotchCore

/// Scout: what AI agents and scripts tell the notch.
///
/// The app's own executable doubles as the sender: Claude Code runs it as a hook
/// (`--claude-hook`, JSON on stdin), Codex as its notify program (`--codex-notify <json>`), and
/// anything else can call `--notify "title" "body"` or open `ezkielnotch://notify?title=…`.
/// The sender hands the event to the running app as a distributed notification (same user,
/// same Mac) and exits; it prints nothing, so it never adds to an agent's context.
@MainActor
final class ScoutService {
    nonisolated static let notification = Notification.Name("store.ezkielyna.notch.scout")

    var onEvent: ((ScoutEvent) -> Void)?
    private var observer: NSObjectProtocol?
    private var recent: [Date] = []

    func setEnabled(_ on: Bool) {
        guard on != (observer != nil) else { return }
        let center = DistributedNotificationCenter.default()
        if let observer { center.removeObserver(observer) }
        observer = nil
        guard on else { return }
        observer = center.addObserver(forName: Self.notification, object: nil, queue: .main) { [weak self] note in
            guard let text = note.object as? String, let event = ScoutWire.decode(text) else { return }
            MainActor.assumeIsolated { self?.receive(event) }
        }
    }

    func receive(_ event: ScoutEvent) {
        // Anything on this Mac can post; a runaway script shouldn't turn the notch into a strobe.
        let now = Date()
        recent = recent.filter { now.timeIntervalSince($0) < 10 } + [now]
        guard recent.count <= 12 else { return }
        onEvent?(event.clamped())
    }

    // MARK: Sender (runs in a short-lived process, before the app starts)

    /// Handles `--claude-hook`, `--codex-notify` and `--notify`. Returns false for a normal launch.
    nonisolated static func runSender(_ args: [String]) -> Bool {
        let app = callerApp()
        let event: ScoutEvent?
        if args.contains("--claude-hook") {
            event = ScoutParser.claudeHook(FileHandle.standardInput.readDataToEndOfFile(), appBundleID: app)
        } else if let i = args.firstIndex(of: "--codex-notify") {
            event = i + 1 < args.count ? ScoutParser.codexNotify(args[i + 1], appBundleID: app) : nil
        } else if let i = args.firstIndex(of: "--notify") {
            let rest = Array(args[(i + 1)...])
            var positional: [String] = []
            var source = "script", kind = ScoutEvent.Kind.info
            var j = 0
            while j < rest.count {
                switch rest[j] {
                case "--source" where j + 1 < rest.count: source = rest[j + 1]; j += 2
                case "--kind" where j + 1 < rest.count: kind = ScoutEvent.Kind(rawValue: rest[j + 1]) ?? .info; j += 2
                default: positional.append(rest[j]); j += 1
                }
            }
            guard let title = positional.first, !title.isEmpty else {
                FileHandle.standardError.write(Data("usage: EzkielNotch --notify \"title\" [\"body\"] [--source name] [--kind info|done|needsYou|working]\n".utf8))
                exit(2)
            }
            event = ScoutEvent(source: source, kind: kind, title: title, body: positional.dropFirst().first ?? "",
                               cwd: FileManager.default.currentDirectoryPath, appBundleID: app)
        } else {
            return false
        }
        if let event { post(event) }
        return true
    }

    nonisolated static func post(_ event: ScoutEvent) {
        guard let text = ScoutWire.encode(event.clamped()) else { return }
        DistributedNotificationCenter.default().postNotificationName(notification, object: text, userInfo: nil,
                                                                     deliverImmediately: true)
    }

    /// The app that ran the agent (Terminal, iTerm, VS Code, Claude, …), so opening the event brings it forward.
    nonisolated private static func callerApp() -> String? {
        let env = ProcessInfo.processInfo.environment
        if let id = env["__CFBundleIdentifier"], !id.isEmpty, id != "store.ezkielyna.notch" { return id }
        switch env["TERM_PROGRAM"] {
        case "Apple_Terminal": return "com.apple.Terminal"
        case "iTerm.app": return "com.googlecode.iterm2"
        case "vscode": return "com.microsoft.VSCode"
        case "WarpTerminal": return "dev.warp.Warp-Stable"
        case "ghostty": return "com.mitchellh.ghostty"
        default: return nil
        }
    }
}

/// Connects Claude Code and Codex to Scout. Only runs when the user presses Connect; the file
/// is backed up first (`<file>.ezkiel-backup`) and everything else in it is kept.
enum AgentLinks {
    enum Agent: String, CaseIterable, Identifiable {
        case claude, codex
        var id: String { rawValue }
        var title: String { self == .claude ? "Claude Code" : "Codex CLI" }
        var file: URL {
            let home = URL(fileURLWithPath: NSHomeDirectory())
            return self == .claude ? home.appendingPathComponent(".claude/settings.json") : home.appendingPathComponent(".codex/config.toml")
        }
        /// The agent is installed (its config folder exists).
        var installed: Bool { FileManager.default.fileExists(atPath: file.deletingLastPathComponent().path) }
    }

    static var executable: String { Bundle.main.executablePath ?? CommandLine.arguments[0] }

    static func state(_ agent: Agent, file: URL? = nil) -> ScoutSetup.LinkState {
        let url = file ?? agent.file
        switch agent {
        case .claude:
            return ScoutSetup.claudeState(try? Data(contentsOf: url), command: ScoutSetup.claudeCommand(executable: executable))
        case .codex:
            return ScoutSetup.codexState((try? String(contentsOf: url, encoding: .utf8)) ?? "", executable: executable)
        }
    }

    /// Returns nil on success, otherwise a short reason for a toast.
    @discardableResult
    static func set(_ agent: Agent, connected: Bool, file: URL? = nil) -> String? {
        let url = file ?? agent.file
        let existing = try? Data(contentsOf: url)
        do {
            let output: Data
            switch agent {
            case .claude:
                output = connected
                    ? try ScoutSetup.connectClaude(existing, command: ScoutSetup.claudeCommand(executable: executable))
                    : try ScoutSetup.disconnectClaude(existing)
            case .codex:
                let text = existing.flatMap { String(data: $0, encoding: .utf8) } ?? ""
                output = Data((connected ? try ScoutSetup.connectCodex(text, executable: executable) : ScoutSetup.disconnectCodex(text)).utf8)
            }
            guard output != existing else { return nil }
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            if let existing { try existing.write(to: url.appendingPathExtension("ezkiel-backup"), options: .atomic) }
            try output.write(to: url, options: .atomic)
            return nil
        } catch ScoutSetup.SetupError.unreadable {
            return "\(url.lastPathComponent) isn't valid JSON · left as it is"
        } catch ScoutSetup.SetupError.otherNotify {
            return "Codex already has a notify program · left as it is"
        } catch {
            return "Couldn't write \(url.lastPathComponent)"
        }
    }
}
