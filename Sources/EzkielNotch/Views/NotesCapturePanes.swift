import AppKit
import NotchCore
import SwiftUI

struct NotesPane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        HStack(spacing: 10) {
            VStack(spacing: 6) {
                HStack {
                    SectionLabel(text: "Notes")
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
            .frame(width: 150)

            if let id = model.selectedNoteID, model.data.notes.contains(where: { $0.id == id }) {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 6) {
                        TextField("", text: model.noteBinding(id, \.title),
                                  prompt: Text("Title").foregroundColor(Theme.mist))
                            .textFieldStyle(.plain)
                            .font(Theme.rounded(15, .bold))
                            .foregroundStyle(Theme.ink)
                        Spacer(minLength: 0)
                        IconButton(icon: "trash", size: 24, tint: Theme.berry.opacity(0.9), help: "Delete note") {
                            model.deleteNote(id)
                        }
                    }
                    TextEditor(text: model.noteBinding(id, \.body))
                        .font(Theme.rounded(12))
                        .foregroundStyle(Theme.ink)
                        .scrollContentBackground(.hidden)
                        .scrollIndicators(.never)
                        .padding(6)
                        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Theme.surface))
                        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Theme.hairline, lineWidth: 1))
                    Text("Saved on this Mac automatically")
                        .font(Theme.rounded(9.5))
                        .foregroundStyle(Theme.mist.opacity(0.8))
                }
                .id(id)
                .transition(.opacity)
            } else {
                EmptyHint(title: "Create a note to get started.", subtitle: "Ideas, links, snippets: they stay on this Mac.")
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
                    .font(Theme.rounded(11.5, .semibold))
                    .foregroundStyle(selected ? Theme.gold : Theme.ink)
                    .lineLimit(1)
                Text(note.body.isEmpty ? "No details yet" : note.body.replacingOccurrences(of: "\n", with: " "))
                    .font(Theme.rounded(9.5))
                    .foregroundStyle(Theme.mist)
                    .lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 9)
            .padding(.vertical, 7)
            .background(RoundedRectangle(cornerRadius: 10, style: .continuous)
                .fill(selected ? Theme.gold.opacity(0.12) : Color.white.opacity(hovering ? 0.08 : 0.045)))
        }
        .buttonStyle(PressableStyle(scale: 0.96))
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}

struct CapturePane: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack(spacing: 12) {
                Image(systemName: "hand.tap.fill")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(Theme.lionGradient)
                    .frame(width: 34, height: 34)
                    .background(Circle().fill(Theme.gold.opacity(0.12)))
                VStack(alignment: .leading, spacing: 2) {
                    Text("Capture current text").font(Theme.rounded(13, .bold)).foregroundStyle(Theme.ink)
                    Text("Ezkiel reads the clipboard only when you press Capture.")
                        .font(Theme.rounded(10.5)).foregroundStyle(Theme.mist)
                }
                Spacer()
                PillButton(title: "Capture", icon: "square.and.arrow.down", prominent: true) {
                    model.captureClipboard()
                }
            }
            .padding(10)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Theme.surface))

            if model.data.clips.isEmpty {
                EmptyHint(title: "Captured clips will appear here.", subtitle: "Copy text anywhere, then press Capture. Up to 30 clips.")
            } else {
                ScrollView(.vertical, showsIndicators: false) {
                    LazyVStack(spacing: 5) {
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
                    .font(Theme.rounded(11.5))
                    .foregroundStyle(Theme.ink.opacity(0.92))
                    .lineLimit(2)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(clip.captured.formatted(date: .abbreviated, time: .shortened))
                    .font(Theme.rounded(9.5))
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
                    .font(Theme.rounded(10, .semibold))
                    .foregroundStyle(copied ? Theme.teal : Theme.gold)
                    .padding(.horizontal, 9)
                    .frame(height: 24)
                    .background(Capsule().fill((copied ? Theme.teal : Theme.gold).opacity(0.13)))
            }
            .buttonStyle(PressableStyle())
            IconButton(icon: "trash", size: 24, tint: Theme.mist, help: "Remove clip", action: remove)
                .opacity(hovering ? 1 : 0.4)
        }
        .padding(9)
        .background(RoundedRectangle(cornerRadius: 11, style: .continuous).fill(Color.white.opacity(hovering ? 0.08 : 0.05)))
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}
