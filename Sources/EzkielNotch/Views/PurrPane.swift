import AppKit
import NotchCore
import SwiftUI

/// Purr: what's playing in Spotify or Music, and Chorus (synced lyrics) beside it.
struct PurrPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    @ObservedObject private var lyrics: LyricsService
    @State private var scrubbing: Double?
    @State private var showOutputs = false

    init(model: AppModel, music: MusicController) {
        self.model = model
        self.music = music
        self.lyrics = model.lyrics
    }

    private var accent: Accent { music.artworkAccent ?? .purr }

    var body: some View {
        Group {
            if let np = music.nowPlaying {
                if model.settings.showLyrics { split(np) } else { player(np) }
            } else {
                emptyState
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .overlay {
            if showOutputs {
                OutputPicker(outputs: model.outputs, accent: accent) { withAnimation(Theme.snappy) { showOutputs = false } }
                    .transition(.opacity.combined(with: .scale(scale: 0.97)))
            }
        }
    }

    /// Where the sound goes: AirPods, the MacBook, a speaker…
    private var outputButton: some View {
        IconButton(icon: "airplayaudio", label: L("Sound output"), size: 26, tint: Theme.mist) {
            model.outputs.refresh()
            withAnimation(Theme.snappy) { showOutputs = true }
        }
    }

    // MARK: Player

    private func player(_ np: NowPlaying) -> some View {
        HStack(alignment: .top, spacing: Theme.Space.l) {
            artwork(np).frame(width: 96, height: 96)
            VStack(alignment: .leading, spacing: 6) {
                titleBlock(np, titleSize: Theme.Size.title)
                progress(np)
                HStack(spacing: 10) {
                    transport(np, play: 34)
                    Spacer(minLength: 0)
                    outputButton
                    lyricsButton
                    openButton(np)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    // MARK: Player + Chorus

    private func split(_ np: NowPlaying) -> some View {
        HStack(alignment: .top, spacing: Theme.Space.m) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 9) {
                    artwork(np).frame(width: 48, height: 48)
                    titleBlock(np, titleSize: Theme.Size.body)
                }
                progress(np)
                HStack(spacing: 6) {
                    transport(np, play: 30)
                    Spacer(minLength: 0)
                    outputButton
                    lyricsButton
                }
            }
            .frame(width: 176)

            ChorusPanel(state: lyrics.state, np: np, accent: accent,
                        onSeek: { music.seek(to: $0) }, onRetry: { model.lyrics.retry(np) })
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .cardSurface()
        }
    }

    // MARK: Pieces

    private func titleBlock(_ np: NowPlaying, titleSize: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            HStack(spacing: 5) {
                Text(np.player.name).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.mist)
                EqualizerBars(playing: np.isPlaying, color: accent.deep, height: 8)
            }
            Text(np.title)
                .font(Theme.font(titleSize, .semibold))
                .foregroundStyle(Theme.ink)
                .lineLimit(1)
                .id("t" + np.trackKey)
                .transition(.opacity)
            Text([np.artist, np.album].filter { !$0.isEmpty }.joined(separator: " · "))
                .font(Theme.font(Theme.Size.secondary))
                .foregroundStyle(Theme.mist)
                .lineLimit(1)
        }
        .animation(Theme.smooth, value: np.trackKey)
    }

    private func artwork(_ np: NowPlaying) -> some View {
        ZStack {
            if let art = music.artwork {
                Image(nsImage: art).resizable().aspectRatio(contentMode: .fill).transition(.opacity)
            } else {
                LinearGradient(colors: [accent.light.opacity(0.6), accent.deep.opacity(0.35)], startPoint: .topLeading, endPoint: .bottomTrailing)
                Image(systemName: "music.note").font(.system(size: 26, weight: .medium)).foregroundStyle(.white.opacity(0.85))
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.control, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: Theme.Radius.control, style: .continuous).strokeBorder(Theme.hairline, lineWidth: 0.5))
        // Apple Music-style glow in the artwork's own colour.
        .shadow(color: accent.deep.opacity(np.isPlaying ? 0.45 : 0.15), radius: 14, y: 4)
        .scaleEffect(np.isPlaying ? 1 : 0.94)
        .animation(Theme.bouncy, value: np.isPlaying)
    }

    private func progress(_ np: NowPlaying) -> some View {
        TimelineView(.animation(minimumInterval: 1.0 / 10, paused: !np.isPlaying || scrubbing != nil)) { tl in
            let position = scrubbing ?? np.position(at: tl.date)
            let fraction = np.duration > 0 ? min(1, position / np.duration) : 0
            VStack(spacing: 3) {
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Capsule().fill(Color.white.opacity(0.14))
                        Capsule().fill(LinearGradient(colors: [accent.light, accent.deep], startPoint: .leading, endPoint: .trailing))
                            .frame(width: max(4, geo.size.width * fraction))
                        Circle().fill(Theme.ink)
                            .frame(width: scrubbing != nil ? 11 : 0, height: scrubbing != nil ? 11 : 0)
                            .offset(x: max(0, geo.size.width * fraction - 5.5))
                    }
                    .frame(height: 4)
                    .frame(maxHeight: .infinity)
                    .contentShape(Rectangle())
                    .gesture(DragGesture(minimumDistance: 0)
                        .onChanged { v in
                            guard np.duration > 0 else { return }
                            scrubbing = max(0, min(np.duration, Double(v.location.x / geo.size.width) * np.duration))
                        }
                        .onEnded { _ in
                            if let s = scrubbing { music.seek(to: s) }
                            scrubbing = nil
                        })
                }
                .frame(height: 12)
                HStack {
                    Text(Self.time(position))
                    Spacer()
                    // Live streams report no length: no countdown then.
                    if np.duration > 0 { Text("-" + Self.time(max(0, np.duration - position))) }
                }
                .font(Theme.number(Theme.Size.caption, .medium))
                .foregroundStyle(Theme.mist)
            }
        }
    }

    private func transport(_ np: NowPlaying, play: CGFloat) -> some View {
        HStack(spacing: 4) {
            TransportButton(icon: "backward.fill", label: "Previous track") { music.previous() }
            Button { music.playPause() } label: {
                Image(systemName: np.isPlaying ? "pause.fill" : "play.fill")
                    .font(.system(size: play * 0.38, weight: .semibold))
                    .foregroundStyle(Theme.onAccent)
                    .frame(width: play, height: play)
                    .background(Circle().fill(accent.linear))
                    .contentTransition(.opacity)
            }
            .buttonStyle(PressableStyle(scale: 0.86))
            .help(np.isPlaying ? "Pause" : "Play")
            .accessibilityLabel(np.isPlaying ? "Pause" : "Play")
            TransportButton(icon: "forward.fill", label: "Next track") { music.next() }
        }
    }

    private var lyricsButton: some View {
        IconButton(icon: "quote.bubble", label: model.settings.showLyrics ? "Hide Chorus (lyrics)" : "Show Chorus: synced lyrics from LRCLIB",
                   size: 26, tint: Theme.mist, filled: model.settings.showLyrics, accent: accent) {
            model.toggleLyrics()
        }
    }

    private func openButton(_ np: NowPlaying) -> some View {
        IconButton(icon: "arrow.up.forward.app", label: L("Open %@", np.player.name), size: 26, tint: Theme.mist) {
            music.open(np.player)
        }
    }

    private static func time(_ seconds: Double) -> String {
        guard seconds.isFinite else { return "0:00" }
        let s = Int(seconds)
        return String(format: "%d:%02d", s / 60, s % 60)
    }

    // MARK: Empty / permission

    @ViewBuilder
    private var emptyState: some View {
        switch music.status {
        case .denied:
            HStack(spacing: Theme.Space.m) {
                LionMascot(size: 44, mood: .awake, animated: false)
                VStack(alignment: .leading, spacing: 6) {
                    Text(L("Allow music access")).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                    Text(L("Privacy & Security → Automation → Ezkiel Notch → Spotify / Music."))
                        .font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist).fixedSize(horizontal: false, vertical: true)
                    PillButton(title: "Open Automation settings", icon: "lock.open", prominent: true, accent: .purr) {
                        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!)
                    }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        default:
            HStack(spacing: Theme.Space.m) {
                LionMascot(size: 44, mood: model.displayedMood == .sleeping ? .sleeping : .awake, animated: false)
                VStack(alignment: .leading, spacing: 6) {
                    Text(music.status == .noPlayer ? L("Open Spotify or Music") : L("Nothing playing"))
                        .font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                    HStack(spacing: 6) {
                        PillButton(title: "Spotify", prominent: true, accent: Accent(light: Color(hex: 0x7CF0A6), deep: Theme.spotify)) {
                            music.open(.spotify)
                        }
                        PillButton(title: "Music") { music.open(.appleMusic) }
                        IconButton(icon: "arrow.clockwise", label: "Refresh", size: 26, tint: Theme.mist) { music.refresh() }
                        outputButton
                    }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

private struct TransportButton: View {
    var icon: String
    var label: String
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 12.5, weight: .semibold))
                .foregroundStyle(Theme.ink)
                .frame(width: 28, height: 28)
                .background(Circle().fill(Color.white.opacity(hovering ? 0.08 : 0)))
        }
        .buttonStyle(PressableStyle(scale: 0.84))
        .help(label)
        .accessibilityLabel(label)
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

// MARK: - Chorus (lyrics)

private struct ChorusPanel: View {
    var state: LyricsService.State
    var np: NowPlaying
    var accent: Accent
    var onSeek: (Double) -> Void
    var onRetry: () -> Void

    var body: some View {
        switch state {
        case .synced(let lines):
            // The timeline only works out the current line; the list itself redraws only when it changes.
            TimelineView(.periodic(from: .now, by: 0.2)) { tl in
                SyncedLyrics(lines: lines, current: LRC.index(at: np.position(at: tl.date) + 0.15, in: lines),
                             accent: accent, onSeek: onSeek)
                    .equatable()
            }
        case .plain(let text):
            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 6) {
                    Text(L("Not synced")).font(Theme.font(Theme.Size.caption, .medium)).foregroundStyle(Theme.faint)
                    Text(text)
                        .font(Theme.font(Theme.Size.body))
                        .foregroundStyle(Theme.ink.opacity(0.85))
                        .lineSpacing(3)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollEdgeFade()
        case .loading:
            VStack(spacing: 8) {
                EqualizerBars(playing: true, color: accent.deep, height: 14)
                Text(L("Finding lyrics…")).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
            }
        case .idle:
            Color.clear
        case .instrumental:
            message("Instrumental", "Just the music", icon: "music.quarternote.3", retry: false)
        case .notFound:
            message("No lyrics for this song", "LRCLIB doesn't have it yet", icon: "text.magnifyingglass", retry: true)
        case .failed:
            message("Couldn't load lyrics", "Check your connection", icon: "wifi.exclamationmark", retry: true)
        }
    }

    private func message(_ title: String, _ subtitle: String, icon: String, retry: Bool) -> some View {
        VStack(spacing: 4) {
            Image(systemName: icon).font(.system(size: 17, weight: .medium)).foregroundStyle(accent.deep)
            Text(title).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
            Text(subtitle).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist)
            if retry { TextButton(title: "Try again", tint: accent.light, action: onRetry) }
        }
    }
}

/// Lines light up as they're sung; the list glides to keep the current line centred.
private struct SyncedLyrics: View, Equatable {
    var lines: [LyricLine]
    var current: Int?
    var accent: Accent
    var onSeek: (Double) -> Void

    static func == (a: SyncedLyrics, b: SyncedLyrics) -> Bool {
        a.current == b.current && a.lines == b.lines && a.accent == b.accent
    }

    var body: some View {
        GeometryReader { geo in
            ScrollViewReader { proxy in
                ScrollView(.vertical, showsIndicators: false) {
                    VStack(alignment: .leading, spacing: 8) {
                        // Half the viewport above and below: the first and last lines can sit in the middle too.
                        Color.clear.frame(height: max(0, geo.size.height / 2 - 12))
                        ForEach(lines.indices, id: \.self) { i in line(i).id(i) }
                        Color.clear.frame(height: max(0, geo.size.height / 2 - 12))
                    }
                    .padding(.horizontal, 12)
                }
                .scrollEdgeFade(24)
                .onAppear {
                    DispatchQueue.main.async { proxy.scrollTo(current ?? 0, anchor: .center) }
                }
                .onChange(of: current) { i in
                    withAnimation(Theme.smooth) { proxy.scrollTo(i ?? 0, anchor: .center) }
                }
            }
        }
    }

    private func line(_ i: Int) -> some View {
        let isCurrent = i == current
        let past = i < (current ?? 0)
        let text = lines[i].text.isEmpty ? "♪" : lines[i].text
        return Button { onSeek(lines[i].time) } label: {
            Text(text)
                .font(Theme.font(13.5, .semibold))
                .foregroundStyle(isCurrent ? AnyShapeStyle(LinearGradient(colors: [Theme.ink, accent.light], startPoint: .leading, endPoint: .trailing))
                                 : AnyShapeStyle(Theme.ink.opacity(past ? 0.28 : 0.42)))
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
        }
        .buttonStyle(PressableStyle(scale: 0.98))
        .help("Jump here")
        .animation(Theme.smooth, value: isCurrent)
    }
}

/// The sound outputs as chips; the current one is filled.
private struct OutputPicker: View {
    @ObservedObject var outputs: AudioOutputs
    var accent: Accent
    var close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Space.s) {
            HStack {
                Text(L("Play sound on")).font(Theme.font(Theme.Size.secondary, .semibold)).foregroundStyle(Theme.ink)
                Spacer(minLength: 0)
                IconButton(icon: "xmark", label: L("Close"), size: 24, tint: Theme.mist, action: close)
            }
            ScrollView(.vertical, showsIndicators: false) {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 130), spacing: 6)], spacing: 6) {
                    ForEach(outputs.devices) { device in
                        OutputTile(device: device, accent: accent) {
                            outputs.select(device)
                            close()
                        }
                    }
                }
            }
            .scrollEdgeFade(4)
        }
        .padding(10)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(RoundedRectangle(cornerRadius: Theme.Radius.card, style: .continuous).fill(Color(white: 0.09)))
        .overlay(RoundedRectangle(cornerRadius: Theme.Radius.card, style: .continuous).strokeBorder(Theme.hairline, lineWidth: 0.5))
    }
}

private struct OutputTile: View {
    var device: AudioOutputs.Device
    var accent: Accent
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                Image(systemName: device.icon)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(device.isDefault ? Theme.onAccent : Theme.ink)
                    .frame(width: 26, height: 26)
                    .background(Circle().fill(device.isDefault ? AnyShapeStyle(accent.linear) : AnyShapeStyle(Color.white.opacity(0.08))))
                Text(device.name).font(Theme.font(Theme.Size.secondary, .medium)).foregroundStyle(Theme.ink).lineLimit(1)
                Spacer(minLength: 0)
                if device.isDefault {
                    Image(systemName: "checkmark").font(.system(size: 10, weight: .bold)).foregroundStyle(accent.light)
                }
            }
            .padding(.horizontal, 8)
            .frame(height: 36)
            .cardSurface(hover: hovering)
        }
        .buttonStyle(PressableStyle(scale: 0.97))
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
        .accessibilityLabel(device.name)
        .accessibilityAddTraits(device.isDefault ? .isSelected : [])
    }
}
