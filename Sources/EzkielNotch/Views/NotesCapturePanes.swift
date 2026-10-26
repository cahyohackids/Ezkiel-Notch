import AppKit
import NotchCore
import SwiftUI

struct NotesPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        HStack(spacing: 10) {
            VStack(spacing: 6) {
                HStack {
                    SectionLabel(text: "\(model.data.notes.count) notes")
                    Spacer()
                    IconButton(icon: "plus", size: 22, tint: Theme.gold, help: "New note") { model.addNote() }
                }
                ScrollView(.vertical, showsIndicators: false) {
                    LazyVStack(spacing: 4) {
                        ForEach(model.data.notes) { note in
                            NoteListRow(note: note, selected: note.id == model.selectedNoteID) {
                                withAnimation(Theme.snappy) { model.selectedNoteID = note.id }
                            }
                            .transition(.move(edge: .top).combined(with: .opacity))
                        }
                    }
                }
            }
            .frame(width: 136)

            if let id = model.selectedNoteID, model.data.notes.contains(where: { $0.id == id }) {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 6) {
                        TextField("", text: model.noteBinding(id, \.title),
                                  prompt: Text("Title").foregroundColor(Theme.mist))
                            .textFieldStyle(.plain)
                            .font(Theme.font(14, .semibold))
                            .foregroundStyle(Theme.ink)
                        Spacer(minLength: 0)
                        IconButton(icon: "trash", size: 24, tint: Theme.berry.opacity(0.9), help: "Delete note") {
                            model.deleteNote(id)
                        }
                    }
                    TextEditor(text: model.noteBinding(id, \.body))
                        .font(Theme.font(12))
                        .foregroundStyle(Theme.ink)
                        .scrollContentBackground(.hidden)
                        .scrollIndicators(.never)
                        .padding(6)
                        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Theme.surface))
                }
                .id(id)
                .transition(.opacity)
            } else {
                EmptyHint(title: "No notes", subtitle: "Notes stay on this Mac.")
                    .overlay(alignment: .bottom) {
                        PillButton(title: "New note", icon: "plus", prominent: true) { model.addNote() }
                    }
            }
        }
    }
}

private struct NoteListRow: View {
    var note: NoteItem
    var selected: Bool
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 2) {
                Text(note.displayTitle)
                    .font(Theme.font(11.5, .medium))
                    .foregroundStyle(selected ? Theme.gold : Theme.ink)
                    .lineLimit(1)
                Text(note.body.isEmpty ? " " : note.body.replacingOccurrences(of: "\n", with: " "))
                    .font(Theme.font(9.5))
                    .foregroundStyle(Theme.mist)
                    .lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(selected ? Color.white.opacity(0.1) : Color.white.opacity(hovering ? 0.06 : 0)))
        }
        .buttonStyle(PressableStyle(scale: 0.96))
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}

struct CapturePane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 1) {
                    Text("Clipboard").font(Theme.font(13, .semibold)).foregroundStyle(Theme.ink)
                    Text("Read only when you press Capture · keeps 30")
                        .font(Theme.font(10.5)).foregroundStyle(Theme.mist)
                }
                Spacer()
                PillButton(title: "Capture", icon: "square.and.arrow.down", prominent: true) {
                    model.captureClipboard()
                }
            }

            if model.data.clips.isEmpty {
                EmptyHint(title: "No clips yet", subtitle: "Copy some text, then press Capture.")
            } else {
                ScrollView(.vertical, showsIndicators: false) {
                    LazyVStack(spacing: 4) {
                        ForEach(model.data.clips) { clip in
                            ClipRow(clip: clip, copy: { model.copyClip(clip) }, remove: { model.removeClip(clip.id) })
                                .transition(.asymmetric(insertion: .move(edge: .top).combined(with: .opacity),
                                                        removal: .opacity))
                        }
                    }
                }
            }
        }
    }
}

private struct ClipRow: View {
    var clip: ClipItem
    var copy: () -> Void
    var remove: () -> Void
    @State private var hovering = false
    @State private var copied = false

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                Text(clip.text)
                    .font(Theme.font(11.5))
                    .foregroundStyle(Theme.ink.opacity(0.92))
                    .lineLimit(2)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(clip.captured.formatted(date: .abbreviated, time: .shortened))
                    .font(Theme.font(9.5))
                    .foregroundStyle(Theme.mist)
            }
            Button {
                copy()
                withAnimation(Theme.bouncy) { copied = true }
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    withAnimation(Theme.soft) { copied = false }
                }
            } label: {
                Label(copied ? "Copied" : "Copy", systemImage: copied ? "checkmark" : "doc.on.doc")
                    .font(Theme.font(10.5, .medium))
                    .foregroundStyle(copied ? Theme.teal : Theme.ink)
                    .padding(.horizontal, 9)
                    .frame(height: 22)
                    .background(Capsule().fill(Color.white.opacity(0.08)))
            }
            .buttonStyle(PressableStyle())
            IconButton(icon: "trash", size: 24, tint: Theme.mist, help: "Remove clip", action: remove)
                .opacity(hovering ? 1 : 0.4)
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.white.opacity(hovering ? 0.08 : 0.045)))
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}
