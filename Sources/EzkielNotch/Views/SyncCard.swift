import AppKit
import NotchCore
import SwiftUI

/// Settings › Features: sync tasks, notes, water and focus with other Macs and PCs through a cloud folder.
struct SyncCard: View {
    @ObservedObject var sync: SyncController
    @State private var name = ""

    private var isError: Bool { sync.config.enabled && !sync.syncing && sync.config.lastError != nil }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                Image(systemName: "arrow.triangle.2.circlepath")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(sync.config.enabled ? Accent.oasis.deep : Theme.mist)
                    .frame(width: 18)
                VStack(alignment: .leading, spacing: 1) {
                    Text(L("Sync")).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                    Text(sync.status).font(Theme.font(Theme.Size.caption))
                        .foregroundStyle(isError ? Accent.hunt.light : Theme.mist)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 6)
                if sync.config.enabled {
                    PillButton(title: sync.syncing ? "Syncing…" : "Sync now", icon: "arrow.clockwise") { sync.syncNow() }
                        .disabled(sync.syncing)
                } else if sync.preview == nil {
                    PillButton(title: "Pick folder…", icon: "folder", prominent: true, accent: .oasis) { sync.chooseFolder() }
                }
            }

            if let preview = sync.preview {
                VStack(alignment: .leading, spacing: 3) {
                    Text(Self.shortPath(preview.folder)).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.ink)
                        .lineLimit(1).truncationMode(.middle)
                    ForEach(preview.lines, id: \.self) { line in
                        Text(line).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    HStack(spacing: 6) {
                        PillButton(title: "Start syncing", icon: "checkmark", prominent: true, accent: .oasis) { sync.confirmPreview() }
                        TextButton(title: L("Cancel"), tint: Theme.mist) { sync.cancelPreview() }
                        Spacer(minLength: 0)
                    }
                    .padding(.top, 3)
                }
                .padding(.leading, 28)
                .transition(.opacity)
            } else if sync.config.enabled {
                VStack(alignment: .leading, spacing: 5) {
                    HStack(spacing: 6) {
                        Image(systemName: "folder").font(.system(size: 10)).foregroundStyle(Theme.mist)
                        Text(Self.shortPath(sync.config.folder)).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.ink)
                            .lineLimit(1).truncationMode(.middle)
                        Spacer(minLength: 4)
                        TextButton(title: L("Change"), tint: Theme.mist) { sync.chooseFolder() }
                    }
                    HStack(spacing: 6) {
                        Text(L("This Mac")).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist)
                        NotchField(placeholder: "Device name", text: $name) { sync.setDeviceName(name) }
                            .frame(width: 150)
                        Spacer(minLength: 0)
                    }
                    if sync.config.devices.isEmpty {
                        Text(L("No other devices yet · pick the same folder on your other Mac or PC"))
                            .font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    ForEach(sync.config.devices) { device in
                        HStack(spacing: 6) {
                            Image(systemName: device.platform == "windows" ? "pc" : "laptopcomputer")
                                .font(.system(size: 10)).foregroundStyle(Theme.mist).frame(width: 14)
                            Text(device.name.isEmpty ? L("another device") : device.name)
                                .font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.ink).lineLimit(1)
                            Text(Self.seen(device)).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist).lineLimit(1)
                            Spacer(minLength: 0)
                        }
                    }
                    HStack {
                        TextButton(title: L("Stop syncing on this device"), tint: Accent.hunt.light) { sync.stop() }
                        Spacer(minLength: 0)
                    }
                }
                .padding(.leading, 28)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .frame(minHeight: 36)
        .cardSurface()
        .animation(Theme.snappy, value: sync.preview)
        .animation(Theme.snappy, value: sync.config.enabled)
        .onAppear { name = sync.config.deviceName }
        .onChange(of: sync.config.deviceName) { name = $0 }
    }

    static func shortPath(_ path: String) -> String {
        let home = NSHomeDirectory()
        var p = path.hasPrefix(home) ? "~" + path.dropFirst(home.count) : path
        p = p.replacingOccurrences(of: "~/Library/Mobile Documents/com~apple~CloudDocs", with: "iCloud Drive")
        p = p.replacingOccurrences(of: "~/Library/CloudStorage/", with: "")
        return p
    }

    static func seen(_ device: SyncDevice) -> String {
        if device.ignored { return L("not synced · update it or open it again") }
        guard let at = device.written else { return "" }
        let f = RelativeDateTimeFormatter()
        f.locale = Lang.locale
        f.unitsStyle = .short
        return L("seen %@", f.localizedString(for: at, relativeTo: Date()))
    }
}
