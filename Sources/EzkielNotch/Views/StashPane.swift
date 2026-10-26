import AppKit
import NotchCore
import SwiftUI

/// Stash: files you drop on the notch, ready to drag out, AirDrop or share. Pounce adds new
/// screenshots and downloads here with one click.
struct StashPane: View {
    @ObservedObject var model: AppModel
    @State private var bob = false

    var body: some View {
        let items = model.stashItems
        let targeted = model.dropTargeted || model.fileDragNear
        VStack(alignment: .leading, spacing: Theme.Space.s) {
            PaneHeader(title: "Stash", detail: items.isEmpty ? nil : "\(items.count)") {
                HStack(spacing: 6) {
                    if !items.isEmpty {
                        if let airDrop = AppModel.airDrop {
                            AirDropButton(service: airDrop, label: "AirDrop all") {
                                model.airDrop(items.filter(\.exists).map(\.url))
                            }
                        }
                        IconButton(icon: "square.and.arrow.up", label: "Share…", size: 26, tint: Theme.mist) {
                            model.share(items.filter(\.exists).map(\.url))
                        }
                        IconButton(icon: "trash", label: "Clear Stash (you can undo)", size: 26, tint: Theme.mist) { model.clearShelf() }
                    }
                    IconButton(icon: "plus", label: "Choose files…", size: 26, filled: true, accent: .stash) { model.chooseShelfFiles() }
                }
            }

            ZStack {
                RoundedRectangle(cornerRadius: Theme.Radius.card, style: .continuous)
                    .fill(targeted ? Accent.stash.deep.opacity(0.12) : Color.white.opacity(0.03))
                RoundedRectangle(cornerRadius: Theme.Radius.card, style: .continuous)
                    .strokeBorder(targeted ? Accent.stash.light.opacity(0.8) : Color.white.opacity(0.14),
                                  style: StrokeStyle(lineWidth: 1, dash: [5, 4]))

                if items.isEmpty || targeted {
                    VStack(spacing: 4) {
                        Image(systemName: "arrow.down")
                            .font(.system(size: 16, weight: .semibold))
                            .foregroundStyle(Accent.stash.linear)
                            .offset(y: bob ? 5 : 0)
                        Text(targeted ? "Release to stash" : "Drop files here")
                            .font(Theme.font(Theme.Size.body, .semibold))
                            .foregroundStyle(Theme.ink)
                        if !targeted {
                            Text("Originals stay where they are")
                                .font(Theme.font(Theme.Size.caption))
                                .foregroundStyle(Theme.mist)
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(items.isEmpty ? Color.clear : Color.black.opacity(0.72))
                    .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.card, style: .continuous))
                    .zIndex(1)
                }

                if !items.isEmpty {
                    ScrollView(.horizontal, showsIndicators: false) {
                        LazyHStack(spacing: 6) {
                            ForEach(items) { item in
                                StashCard(item: item, model: model)
                                    .transition(.fallIn)
                            }
                        }
                        .padding(.horizontal, 8)
                        .frame(maxHeight: .infinity)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .onChange(of: targeted) { t in
            // Bounce only while something hovers over the shelf, and stop cleanly after.
            if t {
                withAnimation(.easeInOut(duration: 0.55).repeatForever(autoreverses: true)) { bob = true }
            } else {
                withAnimation(Theme.snappy) { bob = false }
            }
        }
    }
}

private struct StashCard: View {
    var item: StashItem
    var model: AppModel
    @State private var hovering = false

    var body: some View {
        VStack(spacing: 4) {
            ZStack {
                FileThumbnail(url: item.url, size: 40)
                    .opacity(item.exists ? 1 : 0.4)
                    .scaleEffect(hovering ? 1.05 : 1)
            }
            .frame(width: 44, height: 44)
            .overlay(alignment: .topTrailing) {
                if hovering {
                    Button { model.removeFromShelf(item.id) } label: {
                        Image(systemName: "xmark")
                            .font(.system(size: 8, weight: .bold))
                            .foregroundStyle(Theme.onAccent)
                            .frame(width: 16, height: 16)
                            .background(Circle().fill(Theme.ink))
                            .frame(width: 24, height: 24)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(PressableStyle())
                    .offset(x: 10, y: -9)
                    .help("Remove from Stash")
                    .accessibilityLabel("Remove \(item.name) from Stash")
                    .transition(.scale.combined(with: .opacity))
                }
            }
            .overlay(alignment: .bottomLeading) {
                if hovering, item.exists, let airDrop = AppModel.airDrop {
                    AirDropButton(service: airDrop, label: "AirDrop \(item.name)", size: 20) { model.airDrop([item.url]) }
                        .offset(x: -9, y: 7)
                        .transition(.scale.combined(with: .opacity))
                }
            }
            Text(item.name)
                .font(Theme.font(Theme.Size.caption, .medium))
                .foregroundStyle(Theme.ink)
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(width: 66)
            Text(item.detail)
                .font(Theme.font(9))
                .foregroundStyle(item.exists ? Theme.mist : Accent.hunt.light)
        }
        .padding(.vertical, 6)
        .frame(width: 74)
        .background(RoundedRectangle(cornerRadius: Theme.Radius.control, style: .continuous)
            .fill(Color.white.opacity(hovering ? 0.07 : 0)))
        .contentShape(Rectangle())
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
        .onTapGesture(count: 2) { if item.exists { NSWorkspace.shared.open(item.url) } }
        .onDrag {
            // Missing files can't be dragged; our own drag-out must not light up the notch.
            guard item.exists, let provider = NSItemProvider(contentsOf: item.url) else { return NSItemProvider() }
            model.isDraggingOut = true
            return provider
        }
        .contextMenu {
            Button("Open") { NSWorkspace.shared.open(item.url) }.disabled(!item.exists)
            Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([item.url]) }.disabled(!item.exists)
            Button("AirDrop") { model.airDrop([item.url]) }.disabled(!item.exists)
            Button("Copy Path") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(item.url.path, forType: .string)
            }
            Divider()
            Button("Remove from Stash") { model.removeFromShelf(item.id) }
        }
        .help(item.exists ? "\(item.url.path)\nDouble-click to open · drag to share" : "File missing: \(item.url.path)")
    }
}

/// AirDrop with Apple's own AirDrop icon (from the sharing service).
private struct AirDropButton: View {
    var service: NSSharingService
    var label: String
    var size: CGFloat = 26
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(nsImage: service.image)
                .resizable()
                .aspectRatio(contentMode: .fit)
                .frame(width: size * 0.62, height: size * 0.62)
                .frame(width: size, height: size)
                .background(Circle().fill(Color.white.opacity(hovering ? 0.16 : 0.09)))
        }
        .buttonStyle(PressableStyle(scale: 0.88))
        .help(label)
        .accessibilityLabel(label)
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

