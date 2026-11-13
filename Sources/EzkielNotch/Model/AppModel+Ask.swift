import AppKit
import NotchCore
import SwiftUI

/// Ask: a quick question to api.ezkielyna.store with the user's own key, answered in the open notch.
extension AppModel {
    /// The shortcut that opens the ask box (⌃⌥N, ⌃⌥Space and ⌥⌘I are the open/close choices).
    static let askHotkeyLabel = "⌃⌥A"

    func setAsk(_ on: Bool) {
        noteInteraction()
        if on, mirrorOn { setMirror(false) }
        // The key is looked up once, before the box shows (so it doesn't flash "add your key").
        if on { _ = ask.currentKey() }
        withAnimation(Theme.smooth) { askOn = on }
        if on {
            expand()
            ask.focusRequest += 1
        }
    }

    /// The shortcut: opens the box (with the caret in it), or closes the notch if the box is already up.
    func askFromKeyboard() {
        if askOn, presentation == .expanded {
            collapse()
        } else {
            setAsk(true)
        }
    }

    func askSend() {
        ask.send(model: settings.askModel, indonesian: Lang.isIndonesian)
    }

    func askRetry() {
        ask.retry(model: settings.askModel, indonesian: Lang.isIndonesian)
    }

    func askNew() {
        ask.clear()
    }

    func askCopy() {
        guard !ask.answer.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(Ask.plainText(ask.answer), forType: .string)
        showToast("Copied")
    }

    /// A Hunt task: the question (a time in it becomes the reminder), or the answer's first line.
    func askMakeTask() {
        guard !ask.question.isEmpty else { return }
        let t = Ask.makeTask(question: ask.question, answer: ask.answer)
        addTask(t.title, due: t.due)
        let text = t.due.map { L("Added to Hunt · %@", $0.formatted(date: .omitted, time: .shortened)) } ?? L("Added to Hunt")
        showToast(text, action: "Open Hunt") { [weak self] in self?.select(.hunt) }
    }

    /// A Scratch note: the question as its title, the answer as its text.
    func askSaveNote() {
        guard !ask.answer.isEmpty else { return }
        let n = Ask.note(question: ask.question, answer: ask.answer)
        let note = NoteItem(title: n.title, body: n.body)
        withAnimation(Theme.smooth) {
            data.notes.insert(note, at: 0)
            selectedNoteID = note.id
        }
        showToast("Saved to Scratch", action: "Open Scratch") { [weak self] in
            self?.scratchMode = .notes
            self?.select(.scratch)
        }
    }

    /// The key, the model and "Test key" live in Settings › Features.
    func openAskSettings() {
        setAsk(false)
        settingsSection = .features
        expand(.settings)
    }
}
