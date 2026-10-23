import AppKit
import NotchCore
import QuickLookThumbnailing
import SwiftUI

struct ShelfPane: View {
    @ObservedObject var model: AppModel
    @State private var bob = false

    var body: some View {
        let targeted = model.dropTargeted || model.fileDragNear
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                SectionLabel(text: "Shelf")
                Text(model.data.shelf.isEmpty ? "Keep it within reach" : "\(model.data.shelf.count) item\(model.data.shelf.count == 1 ? "" : "s") · drag out to share")
                    .font(Theme.rounded(10.5))
                    .foregroundStyle(Theme.mist)
                Spacer()
                if !model.data.shelf.isEmpty {
                    Button("Clear") { model.clearShelf() }
                        .buttonStyle(PressableStyle())
                        .font(Theme.rounded(10.5, .semibold))
                        .foregroundStyle(Theme.gold)
                }
                IconButton(icon: "plus", size: 26, filled: true, help: "Choose files…") { model.chooseShelfFiles() }
            }

            ZStack {
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(targeted ? Theme.gold.opacity(0.10) : Color.white.opacity(0.03))
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .strokeBorder(targeted ? Theme.gold.opacity(0.85) : Color.white.opacity(0.22),
                                  style: StrokeStyle(lineWidth: targeted ? 1.6 : 1.2, dash: [6, 5]))

                if model.data.shelf.isEmpty || targeted {
                    VStack(spacing: 8) {
                        Image(systemName: "arrow.down")
                            .font(.system(size: 22, weight: .bold))
                            .foregroundStyle(Theme.lionGradient)
                            .offset(y: targeted ? (bob ? 8 : 2) : 0)
                            .animation(targeted ? .easeInOut(duration: 0.6).repeatForever(autoreverses: true) : .default, value: bob)
                        Text(targeted ? "Release to keep it here" : "Drop files here")
                            .font(Theme.rounded(14, .bold))
                            .foregroundStyle(Theme.ink)
                            .id(targeted)
                            .transition(.opacity.combined(with: .offset(y: targeted ? 5 : -5)))
                        if !targeted {
                            Text("Drag from Finder onto the notch, or press + to choose files. Originals stay in place.")
                                .font(Theme.rounded(10.5))
                                .foregroundStyle(Theme.mist)
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(model.data.shelf.isEmpty ? Color.clear : Color.black.opacity(0.7))
                    .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
                    .zIndex(1)
                    .onAppear { bob = true }
                }

                if !model.data.shelf.isEmpty {
                    ScrollView(.horizontal, showsIndicators: false) {
                        LazyHStack(spacing: 8) {
                            ForEach(model.data.shelf) { record in
                                ShelfCard(record: record, url: model.url(for: record),
                                          remove: { model.removeFromShelf(record.id) })
                                    .transition(.fallIn)
                            }
                        }
                        .padding(.horizontal, 10)
                        .frame(maxHeight: .infinity)
                    }
                }
            }
            .scaleEffect(targeted ? 1.015 : 1)
            .animation(Theme.spring, value: targeted)
        }
    }
}

private struct ShelfCard: View {
    var record: ShelfRecord
    var url: URL
    var remove: () -> Void
    @State private var hovering = false
    @State private var thumbnail: NSImage?

    var body: some View {
        VStack(spacing: 5) {
            ZStack(alignment: .topTrailing) {
                Image(nsImage: thumbnail ?? NSWorkspace.shared.icon(forFile: url.path))
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .frame(width: 54, height: 54)
                    .shadow(color: .black.opacity(0.35), radius: 4, y: 2)
                    .scaleEffect(hovering ? 1.08 : 1)
                if hovering {
                    Button(action: remove) {
                        Image(systemName: "xmark")
                            .font(.system(size: 8, weight: .heavy))
                            .foregroundStyle(Theme.navy)
                            .frame(width: 17, height: 17)
                            .background(Circle().fill(Theme.cream))
                    }
                    .buttonStyle(PressableStyle())
                    .offset(x: 7, y: -5)
                    .transition(.scale.combined(with: .opacity))
                    .help("Remove from Shelf")
                }
            }
            Text(record.name)
                .font(Theme.rounded(10, .semibold))
                .foregroundStyle(Theme.ink)
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(width: 78)
            Text(sizeText)
                .font(Theme.rounded(9))
                .foregroundStyle(Theme.mist)
        }
        .padding(.vertical, 8)
        .frame(width: 88)
        .background(RoundedRectangle(cornerRadius: 13, style: .continuous).fill(Color.white.opacity(hovering ? 0.1 : 0.0)))
        .contentShape(Rectangle())
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
        .onTapGesture(count: 2) { NSWorkspace.shared.open(url) }
        .onDrag { NSItemProvider(contentsOf: url) ?? NSItemProvider() }
        .contextMenu {
            Button("Open") { NSWorkspace.shared.open(url) }
            Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([url]) }
            Button("Copy Path") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(url.path, forType: .string)
            }
            Divider()
            Button("Remove from Shelf", action: remove)
        }
        .help("\(url.path)\nDouble-click to open · drag to share")
        .task(id: url) { await loadThumbnail() }
    }

    private var sizeText: String {
        let values = try? url.resourceValues(forKeys: [.fileSizeKey, .isDirectoryKey])
        if values?.isDirectory == true { return "Folder" }
        guard let size = values?.fileSize else { return FileManager.default.fileExists(atPath: url.path) ? "" : "Missing" }
        return ByteCountFormatter.string(fromByteCount: Int64(size), countStyle: .file)
    }

    private func loadThumbnail() async {
        let request = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 108, height: 108),
                                                   scale: NSScreen.main?.backingScaleFactor ?? 2,
                                                   representationTypes: .thumbnail)
        if let rep = try? await QLThumbnailGenerator.shared.generateBestRepresentation(for: request) {
            thumbnail = rep.nsImage
        }
    }
}
