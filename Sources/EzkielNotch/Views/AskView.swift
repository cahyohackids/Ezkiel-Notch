import AppKit
import NotchCore
import SwiftUI

/// Ask: a one-line question box over the page of the open notch, and the answer as it streams.
/// Esc or × puts it away; what was asked stays for the session.
struct AskOverlay: View {
    @ObservedObject var model: AppModel
    @ObservedObject var ask: AskModel
    @FocusState private var fieldFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 6) {
                HStack(spacing: 6) {
                    Image(systemName: "sparkles")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(Accent.gold.linear)
                    TextField("", text: $ask.draft, prompt: Text(L("Ask anything…")).foregroundColor(Theme.faint))
                        .textFieldStyle(.plain)
                        .font(Theme.font(Theme.Size.body))
                        .foregroundStyle(Theme.ink)
                        .focused($fieldFocused)
                        .onSubmit { model.askSend() }
                }
                .padding(.horizontal, 10)
                .frame(height: 28)
                .cardSurface(raised: true)
                if ask.busy {
                    IconButton(icon: "stop.fill", label: "Stop the answer", size: 28, tint: Theme.ink) { ask.stop() }
                } else {
                    IconButton(icon: "arrow.up", label: "Ask (Return)", size: 28, filled: true, accent: .gold) { model.askSend() }
                }
                IconButton(icon: "xmark", label: "Close Ask (Esc)", size: 24, tint: Theme.mist) { model.setAsk(false) }
            }
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        // The notch's own black, so the page underneath doesn't show through.
        .background(Color.black)
        .onAppear {
            _ = ask.currentKey()
            DispatchQueue.main.async { fieldFocused = true }
        }
        .onChange(of: ask.focusRequest) { _ in fieldFocused = true }
        .animation(Theme.snappy, value: ask.phase)
    }

    @ViewBuilder
    private var content: some View {
        if let error = ask.error, ask.answer.isEmpty {
            AskErrorCard(model: model, ask: ask, error: error)
        } else if ask.question.isEmpty {
            intro
        } else {
            answer
        }
    }

    // MARK: Before the first question

    @ViewBuilder
    private var intro: some View {
        if !ask.hasKey {
            AskErrorCard(model: model, ask: ask, error: .noKey)
        } else {
            VStack(alignment: .leading, spacing: 6) {
                Text(L("Short answers, right here. Return asks, Esc closes."))
                    .font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                HStack(spacing: 4) {
                    Image(systemName: "cpu").font(.system(size: 10, weight: .medium)).foregroundStyle(Theme.faint)
                    Text(model.settings.askModel).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.faint).lineLimit(1)
                    Text("· " + L("only your question is sent")).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.faint).lineLimit(1)
                }
                if !ask.history.isEmpty {
                    Text(L("Earlier")).font(Theme.font(Theme.Size.caption, .semibold)).foregroundStyle(Theme.faint).padding(.top, 4)
                    ForEach(Array(ask.history.suffix(3).reversed().enumerated()), id: \.offset) { _, turn in
                        Button { ask.show(turn) } label: {
                            HStack(spacing: 6) {
                                Image(systemName: "clock.arrow.circlepath").font(.system(size: 10, weight: .medium)).foregroundStyle(Theme.mist)
                                Text(turn.question).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.ink).lineLimit(1)
                                Spacer(minLength: 0)
                            }
                            .padding(.horizontal, 8)
                            .frame(height: 24)
                            .cardSurface()
                        }
                        .buttonStyle(PressableStyle(scale: 0.98))
                    }
                }
            }
        }
    }

    // MARK: The answer

    private var answer: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(ask.question)
                .font(Theme.font(Theme.Size.secondary, .semibold))
                .foregroundStyle(Theme.mist)
                .lineLimit(1)
            ScrollViewReader { proxy in
                ScrollView(.vertical, showsIndicators: false) {
                    VStack(alignment: .leading, spacing: 0) {
                        if ask.answer.isEmpty {
                            HStack(spacing: 6) {
                                ProgressView().controlSize(.small).scaleEffect(0.7)
                                Text(L(ask.phase == .thinking ? "Thinking…" : "Asking…"))
                                    .font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                            }
                        } else {
                            Text(Self.rendered(ask.answer))
                                .font(Theme.font(Theme.Size.body))
                                .foregroundStyle(Theme.ink)
                                .textSelection(.enabled)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                        if let error = ask.error {
                            // The answer stopped part-way (the balance ran out, the connection dropped).
                            HStack(spacing: 6) {
                                Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 10)).foregroundStyle(Accent.hunt.light)
                                Text(L(error.title)).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Accent.hunt.light)
                                if error.needsSettings { TextButton(title: "Settings") { model.openAskSettings() } }
                            }
                            .padding(.top, 4)
                        } else if ask.stopped {
                            Text(L("Stopped")).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.faint).padding(.top, 4)
                        }
                        Color.clear.frame(height: 1).id("end")
                    }
                    .padding(.vertical, 2)
                }
                .scrollEdgeFade(6)
                .onChange(of: ask.answer) { _ in
                    if ask.busy { proxy.scrollTo("end", anchor: .bottom) }
                }
            }
            actions
        }
    }

    private var actions: some View {
        HStack(spacing: 5) {
            if ask.busy {
                Text(L(ask.phase == .thinking ? "Thinking…" : ask.phase == .streaming ? "Writing…" : "Asking…"))
                    .font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist)
                Spacer(minLength: 0)
                IconButton(icon: "stop.fill", label: "Stop the answer", size: 22, tint: Theme.ink) { ask.stop() }
            } else {
                AskAction(icon: "doc.on.doc", title: "Copy") { model.askCopy() }
                AskAction(icon: "checklist", title: "Make task") { model.askMakeTask() }
                AskAction(icon: "note.text.badge.plus", title: "Save as note") { model.askSaveNote() }
                Spacer(minLength: 0)
                TextButton(title: "New question") { model.askNew() }
            }
        }
        .frame(height: 24)
        .disabled(ask.answer.isEmpty && !ask.busy)
    }

    /// Headings and bullets tidied, **bold** and `code` drawn.
    static func rendered(_ answer: String) -> AttributedString {
        let tidy = Ask.tidy(answer)
        let options = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        return (try? AttributedString(markdown: tidy, options: options)) ?? AttributedString(tidy)
    }
}

/// A small labelled action under the answer.
private struct AskAction: View {
    var icon: String
    var title: String
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 4) {
                Image(systemName: icon).font(.system(size: 10, weight: .semibold))
                Text(L(title)).font(Theme.font(Theme.Size.caption, .medium)).lineLimit(1)
            }
            .foregroundStyle(Theme.ink)
            .padding(.horizontal, 8)
            .frame(height: 24)
            .background(Capsule().fill(Color.white.opacity(0.07 + (hovering ? Theme.hoverLift : 0))))
        }
        .buttonStyle(PressableStyle())
        .help(L(title))
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

/// No key, a wrong key, no balance, offline, a timeout: what happened, in plain words, and what to do.
private struct AskErrorCard: View {
    @ObservedObject var model: AppModel
    @ObservedObject var ask: AskModel
    var error: AskError

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: icon)
                .font(.system(size: 16, weight: .medium))
                .foregroundStyle(error == .noKey ? AnyShapeStyle(Accent.gold.linear) : AnyShapeStyle(Accent.hunt.light))
                .frame(width: 22)
            VStack(alignment: .leading, spacing: 3) {
                Text(L(error.title)).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                Text(detail).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                    .fixedSize(horizontal: false, vertical: true)
                    .lineLimit(3)
                HStack(spacing: 6) {
                    PillButton(title: "Settings", icon: "gearshape", prominent: error.needsSettings) { model.openAskSettings() }
                    if error == .noKey || error == .noBalance {
                        TextButton(title: error == .noKey ? "Get a key" : "Top up") {
                            NSWorkspace.shared.open(URL(string: Ask.keyPage)!)
                        }
                    }
                    if !ask.question.isEmpty, error != .noKey {
                        TextButton(title: "Try again", tint: Theme.ink) { model.askRetry() }
                    }
                }
                .padding(.top, 4)
            }
            Spacer(minLength: 0)
        }
        .padding(10)
        .cardSurface()
    }

    private var icon: String {
        switch error {
        case .noKey: return "key.fill"
        case .invalidKey: return "key.slash"
        case .noBalance: return "creditcard"
        case .offline: return "wifi.slash"
        case .timeout: return "hourglass"
        default: return "exclamationmark.triangle"
        }
    }

    private var detail: String {
        if case .other(let message) = error { return message }
        return L(error.detail)
    }
}

/// Settings › Features: the switch, the user's own API key (Keychain), the model and "Test key".
struct AskSettingsCard: View {
    @ObservedObject var model: AppModel
    @ObservedObject var ask: AskModel
    @State private var draft = ""
    @State private var reveal = false

    var body: some View {
        ToggleRow(title: "Ask", subtitle: shortcutTaken ? L("%@ is taken by another app", AppModel.askHotkeyLabel)
                                                        : L("Quick AI answers in the notch · %@", AppModel.askHotkeyLabel),
                  icon: "sparkles", isOn: $model.settings.ask)
        if model.settings.ask {
            keyRow
            modelRow
        }
    }

    private var keyRow: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 10) {
                Image(systemName: "key").font(.system(size: 12, weight: .medium)).foregroundStyle(Theme.mist).frame(width: 18)
                VStack(alignment: .leading, spacing: 1) {
                    Text(L("Ezkielyna API key")).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                    Text(status).font(Theme.font(Theme.Size.caption)).foregroundStyle(statusTint).lineLimit(1)
                }
                Spacer(minLength: 6)
                if ask.hasKey {
                    TextButton(title: reveal ? "Hide key" : "Show key", tint: Theme.mist) { reveal.toggle() }
                    TextButton(title: "Remove", tint: Accent.hunt.light) {
                        ask.removeKey()
                        reveal = false
                    }
                    PillButton(title: ask.keyState == .checking ? "Testing…" : "Test key", prominent: false) { ask.testKey() }
                } else {
                    TextButton(title: "Get a key") { NSWorkspace.shared.open(URL(string: Ask.keyPage)!) }
                }
            }
            if ask.hasKey {
                Text(reveal ? (ask.currentKey() ?? "") : ask.maskedKey)
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(Theme.ink.opacity(0.8))
                    .textSelection(.enabled)
                    .lineLimit(1)
                    .truncationMode(.middle)
                    .padding(.leading, 28)
            } else {
                HStack(spacing: 6) {
                    Group {
                        if reveal {
                            TextField("", text: $draft, prompt: Text(L("Paste your key")).foregroundColor(Theme.faint))
                        } else {
                            SecureField("", text: $draft, prompt: Text(L("Paste your key")).foregroundColor(Theme.faint))
                        }
                    }
                    .textFieldStyle(.plain)
                    .font(.system(size: 11.5, design: .monospaced))
                    .foregroundStyle(Theme.ink)
                    .onSubmit(save)
                    .padding(.horizontal, 10)
                    .frame(height: 26)
                    .cardSurface(raised: true)
                    IconButton(icon: reveal ? "eye.slash" : "eye", label: reveal ? "Hide key" : "Show key", size: 24, tint: Theme.mist) { reveal.toggle() }
                    PillButton(title: "Save key", prominent: !draft.isEmpty, action: save)
                }
                .padding(.leading, 28)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .cardSurface()
        .onAppear { _ = ask.currentKey() }
    }

    private var modelRow: some View {
        HStack(spacing: 8) {
            Image(systemName: "cpu").font(.system(size: 12, weight: .medium)).foregroundStyle(Theme.mist).frame(width: 18)
            Text(L("AI model")).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink).frame(width: 92, alignment: .leading)
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 5) {
                    ForEach(ask.modelChoices(current: model.settings.askModel), id: \.self) { m in
                        Chip(title: m, selected: model.settings.askModel == m) { model.settings.askModel = m }
                    }
                }
                .padding(.trailing, 8)
            }
        }
        .padding(.horizontal, 10)
        .frame(height: 36)
        .cardSurface()
    }

    /// Another app already has ⌃⌥A (the header button and the menus still open Ask).
    private var shortcutTaken: Bool {
        model.settings.ask && (NSApp.delegate as? AppDelegate)?.askHotKeyRegistered == false
    }

    private func save() {
        guard ask.saveKey(draft) else { return }
        draft = ""
        reveal = false
        ask.testKey()
    }

    private var status: String {
        switch ask.keyState {
        case .checking: return L("Testing…")
        case .ok: return ask.models.isEmpty ? L("Key works") : L("Key works · %d models", ask.models.count)
        case .failed(let e): return L(e.title)
        case .unknown:
            return ask.hasKey ? L("Saved in your Keychain · only sent to api.ezkielyna.store") : L("Your own key from api.ezkielyna.store")
        }
    }

    private var statusTint: Color {
        switch ask.keyState {
        case .ok: return Accent.roar.light
        case .failed: return Accent.hunt.light
        default: return Theme.mist
        }
    }
}
