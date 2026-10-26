import AppKit
import NotchCore
import SwiftUI

/// Scratch: notes, and Catch (text you capture from the clipboard).
struct ScratchPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Space.s) {
            HStack(spacing: 6) {
                ForEach(ScratchMode.allCases) { mode in
                    Chip(title: mode.title, selected: model.scratchMode == mode, accent: .scratch) {
                        withAnimation(Theme.snappy) { model.scratchMode = mode }
                    }
                }
                Spacer(minLength: 0)
                if model.scratchMode == .notes {
                    IconButton(icon: "square.and.pencil", label: "New note", size: 26) { model.addNote() }
                } else {
                    PillButton(title: "Capture", icon: "doc.on.clipboard", prominent: true, accent: .scratch) {
                        model.captureClipboard()
                    }
                    .help("Reads the clipboard only when you press it")
                }
            }

            Group {
                if model.scratchMode == .notes { NotesView(model: model) } else { CatchView(model: model) }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .transition(.opacity)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
    }
}

// MARK: - Notes

private struct NotesView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        if model.data.notes.isEmpty {
            EmptyHint(title: "No notes", subtitle: "Notes stay on this Mac.")
        } else {
            HStack(spacing: Theme.Space.s) {
                ScrollView(.vertical, showsIndicators: false) {
                    LazyVStack(spacing: 2) {
                        ForEach(model.data.notes) { note in
                            NoteListRow(note: note, selected: note.id == model.selectedNoteID) {
                                withAnimation(Theme.snappy) { model.selectedNoteID = note.id }
                            }
                        }
                    }
                }
                .scrollEdgeFade(6)
                .frame(width: 124)

                if let id = model.selectedNoteID, let note = model.data.notes.first(where: { $0.id == id }) {
                    NoteEditor(model: model, note: note)
                        .id(id)
                }
            }
        }
    }
}

/// Keeps its own copy while you type and writes back after a pause, so typing never
/// re-renders the whole notch or re-compares all saved data per keystroke.
private struct NoteEditor: View {
    @ObservedObject var model: AppModel
    let note: NoteItem
    @State private var title = ""
    @State private var body_ = ""
    @State private var pending: Task<Void, Never>?
    @FocusState private var titleFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                TextField("", text: $title, prompt: Text("Title").foregroundColor(Theme.faint))
                    .textFieldStyle(.plain)
                    .font(Theme.font(Theme.Size.title, .semibold))
                    .foregroundStyle(Theme.ink)
                    .focused($titleFocused)
                Spacer(minLength: 0)
                IconButton(icon: "trash", label: "Delete note", size: 24, tint: Theme.mist) {
                    flush()
                    model.deleteNote(note.id)
                }
            }
            TextEditor(text: $body_)
                .font(Theme.font(Theme.Size.body))
                .foregroundStyle(Theme.ink)
                .scrollContentBackground(.hidden)
                .scrollIndicators(.never)
                // NSTextView insets its text by ~5pt; pull it back so body and title share one edge.
                .padding(.horizontal, -5)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .cardSurface()
        .onAppear {
            title = note.title
            body_ = note.body
            if model.noteToFocus == note.id {
                model.noteToFocus = nil
                DispatchQueue.main.async { titleFocused = true }
            }
        }
        .onChange(of: title) { _ in schedule() }
        .onChange(of: body_) { _ in schedule() }
        .onDisappear { flush() }
    }

    private func schedule() {
        pending?.cancel()
        pending = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 300_000_000)
            guard !Task.isCancelled else { return }
            model.updateNote(note.id, title: title, body: body_)
        }
    }

    private func flush() {
        pending?.cancel()
        model.updateNote(note.id, title: title, body: body_)
    }
}

private struct NoteListRow: View {
    var note: NoteItem
    var selected: Bool
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 1) {
                Text(note.displayTitle)
                    .font(Theme.font(Theme.Size.secondary, .medium))
                    .foregroundStyle(selected ? Theme.ink : Theme.ink.opacity(0.85))
                    .lineLimit(1)
                Text(note.updated.formatted(.relative(presentation: .named)))
                    .font(Theme.font(Theme.Size.caption))
                    .foregroundStyle(Theme.faint)
                    .lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 8)
            .padding(.vertical, 5)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.control - 2, style: .continuous)
                .fill(selected ? Accent.scratch.deep.opacity(0.16) : Color.white.opacity(hovering ? 0.05 : 0)))
        }
        .buttonStyle(PressableStyle(scale: 0.97))
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}

// MARK: - Catch

private struct CatchView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        if model.data.clips.isEmpty {
            EmptyHint(title: "Nothing caught yet", subtitle: "Copy some text, then press Capture. Keeps 30 clips.")
        } else {
            ScrollView(.vertical, showsIndicators: false) {
                LazyVStack(spacing: 4) {
                    ForEach(model.data.clips) { clip in
                        ClipRow(clip: clip, copy: { model.copyClip(clip) }, remove: { model.removeClip(clip.id) })
                            .transition(.asymmetric(insertion: .move(edge: .top).combined(with: .opacity), removal: .opacity))
                    }
                }
                .padding(.vertical, 2)
            }
            .scrollEdgeFade(6)
        }
    }
}

private struct ClipRow: View {
    var clip: ClipItem
    var copy: () -> Void
    var remove: () -> Void
    @State private var hovering = false

    var body: some View {
        HStack(alignment: .center, spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                Text(clip.text)
                    .font(Theme.font(Theme.Size.secondary))
                    .foregroundStyle(Theme.ink)
                    .lineLimit(2)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(clip.captured.formatted(.relative(presentation: .named)))
                    .font(Theme.font(Theme.Size.caption))
                    .foregroundStyle(Theme.faint)
            }
            IconButton(icon: "doc.on.doc", label: "Copy", size: 24, action: copy)
            IconButton(icon: "xmark", label: "Remove clip", size: 24, tint: Theme.mist, action: remove)
                .opacity(hovering ? 1 : 0)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .cardSurface(hover: hovering)
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}
