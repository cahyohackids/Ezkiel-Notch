import AppKit
import NotchCore
import SwiftUI

struct MusicPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    @ObservedObject private var lyrics: LyricsService
    @State private var scrubbing: Double?

    init(model: AppModel, music: MusicController) {
        self.model = model
        self.music = music
        self.lyrics = model.lyrics
    }

    var body: some View {
        if let np = music.nowPlaying {
            if model.settings.showLyrics {
                splitPlayer(np)
                    .transition(.opacity.combined(with: .scale(scale: 0.97)))
            } else {
                player(np)
                    .transition(.opacity.combined(with: .scale(scale: 0.97)))
            }
        } else {
            emptyState
        }
    }

    // MARK: Player + lyrics (the notch splits in two)

    private func splitPlayer(_ np: NowPlaying) -> some View {
        let tint = np.player == .spotify ? Color(hex: 0x1ED760) : Theme.berry
        return HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 9) {
                    artwork(np, tint: tint)
                        .frame(width: 48, height: 48)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(np.title)
                            .font(Theme.font(12.5, .semibold))
                            .foregroundStyle(Theme.ink)
                            .lineLimit(2)
                        Text(np.artist)
                            .font(Theme.font(10.5))
                            .foregroundStyle(Theme.mist)
                            .lineLimit(1)
                    }
                }
                progress(np)
                HStack(spacing: 6) {
                    transportButton("backward.fill", size: 28) { music.previous() }
                    playButton(np, size: 32)
                    transportButton("forward.fill", size: 28) { music.next() }
                    Spacer(minLength: 0)
                    lyricsButton
                }
            }
            .frame(width: 178)

            LyricsPanel(state: lyrics.state, np: np, onSeek: { music.seek(to: $0) },
                        onRetry: { model.lyrics.retry(np) })
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.white.opacity(0.04)))
        }
        .padding(.horizontal, 2)
        .frame(maxHeight: .infinity)
    }

    private var lyricsButton: some View {
        IconButton(icon: "quote.bubble", size: 26, tint: model.settings.showLyrics ? Theme.navy : Theme.mist,
                   filled: model.settings.showLyrics,
                   help: model.settings.showLyrics ? "Hide lyrics" : "Show synced lyrics (from LRCLIB)") {
            model.toggleLyrics()
        }
    }

    private func playButton(_ np: NowPlaying, size: CGFloat) -> some View {
        Button { music.playPause() } label: {
            Image(systemName: np.isPlaying ? "pause.fill" : "play.fill")
                .font(.system(size: size * 0.38, weight: .bold))
                .foregroundStyle(Theme.navy)
                .frame(width: size, height: size)
                .background(Circle().fill(Theme.cream))
                .contentTransition(.opacity)
        }
        .buttonStyle(PressableStyle(scale: 0.86))
        .help(np.isPlaying ? "Pause" : "Play")
    }

    // MARK: Player

    private func player(_ np: NowPlaying) -> some View {
        let tint = np.player == .spotify ? Color(hex: 0x1ED760) : Theme.berry
        return HStack(spacing: 16) {
            artwork(np, tint: tint)
                .frame(width: 104, height: 104)

            VStack(alignment: .leading, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 6) {
                        Text(np.player.name)
                            .font(Theme.font(10.5, .medium))
                            .foregroundStyle(Theme.mist)
                        EqualizerBars(playing: np.isPlaying, color: tint, height: 9)
                    }
                    Text(np.title)
                        .font(Theme.font(15, .semibold))
                        .foregroundStyle(Theme.ink)
                        .lineLimit(1)
                        .id("t" + np.trackKey)
                        .transition(.opacity.combined(with: .offset(y: 6)))
                    Text([np.artist, np.album].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(Theme.font(11.5))
                        .foregroundStyle(Theme.mist)
                        .lineLimit(1)
                }

                progress(np)

                HStack(spacing: 12) {
                    transportButton("backward.fill", size: 30) { music.previous() }
                    playButton(np, size: 36)
                    transportButton("forward.fill", size: 30) { music.next() }
                    Spacer()
                    lyricsButton
                    IconButton(icon: "arrow.up.forward.app", size: 26, tint: Theme.mist, help: "Open \(np.player.name)") {
                        music.open(np.player)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 4)
        .frame(maxHeight: .infinity)
        .animation(Theme.spring, value: np.trackKey)
    }

    private func artwork(_ np: NowPlaying, tint: Color) -> some View {
        ZStack {
            if let art = music.artwork {
                Image(nsImage: art)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
                    .transition(.opacity)
            } else {
                LinearGradient(colors: [tint.opacity(0.7), Theme.navy3], startPoint: .topLeading, endPoint: .bottomTrailing)
                Image(systemName: "music.note").font(.system(size: 36, weight: .semibold)).foregroundStyle(.white.opacity(0.85))
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        .scaleEffect(np.isPlaying ? 1 : 0.94)
        .animation(Theme.bouncy, value: np.isPlaying)
        .id(np.trackKey)
    }

    private func progress(_ np: NowPlaying) -> some View {
        TimelineView(.animation(minimumInterval: 1.0 / 15, paused: !np.isPlaying || scrubbing != nil)) { tl in
            let position = scrubbing ?? np.position(at: tl.date)
            let fraction = np.duration > 0 ? min(1, position / np.duration) : 0
            VStack(spacing: 4) {
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Capsule().fill(Color.white.opacity(0.14))
                        Capsule().fill(Theme.cream)
                            .frame(width: max(4, geo.size.width * fraction))
                        Circle().fill(Theme.cream)
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
                .frame(height: 14)
                HStack {
                    Text(Self.time(position))
                    Spacer()
                    Text("-" + Self.time(max(0, np.duration - position)))
                }
                .font(Theme.font(9.5))
                .monospacedDigit()
                .foregroundStyle(Theme.mist)
            }
        }
    }

    private func transportButton(_ icon: String, size: CGFloat, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(Theme.ink)
                .frame(width: size, height: size)
                .contentShape(Circle())
        }
        .buttonStyle(PressableStyle(scale: 0.82))
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
            HStack(spacing: 16) {
                LionMascot(size: 52, mood: .awake)
                VStack(alignment: .leading, spacing: 8) {
                    Text("Allow music access").font(Theme.font(14, .semibold)).foregroundStyle(Theme.ink)
                    Text("System Settings → Privacy & Security → Automation → Ezkiel Notch → turn on Spotify / Music.")
                        .font(Theme.font(11)).foregroundStyle(Theme.mist).fixedSize(horizontal: false, vertical: true)
                    PillButton(title: "Open Automation settings", icon: "lock.open", prominent: true) {
                        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!)
                    }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        default:
            HStack(spacing: 16) {
                LionMascot(size: 52, mood: model.displayedMood == .sleeping ? .sleeping : .awake)
                VStack(alignment: .leading, spacing: 8) {
                    Text(music.status == .noPlayer ? "Open Spotify or Apple Music" : "Waiting for a track")
                        .font(Theme.font(14, .semibold)).foregroundStyle(Theme.ink)
                    Text("Whatever plays there shows up here.")
                        .font(Theme.font(11)).foregroundStyle(Theme.mist)
                    HStack(spacing: 8) {
                        PillButton(title: "Spotify", icon: "music.note", prominent: true, tint: Color(hex: 0x1ED760)) {
                            music.open(.spotify)
                        }
                        PillButton(title: "Apple Music", icon: "music.note") { music.open(.appleMusic) }
                        IconButton(icon: "arrow.clockwise", size: 30, tint: Theme.mist, help: "Refresh") { music.refresh() }
                    }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

// MARK: - Lyrics panel

private struct LyricsPanel: View {
    var state: LyricsService.State
    var np: NowPlaying
    var onSeek: (Double) -> Void
    var onRetry: () -> Void

    var body: some View {
        switch state {
        case .synced(let lines):
            TimelineView(.periodic(from: .now, by: 0.2)) { tl in
                SyncedLyrics(lines: lines, current: LRC.index(at: np.position(at: tl.date) + 0.15, in: lines), onSeek: onSeek)
            }
        case .plain(let text):
            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 8) {
                    SectionLabel(text: "Lyrics · not synced")
                    Text(text)
                        .font(Theme.font(12.5, .medium))
                        .foregroundStyle(Theme.ink.opacity(0.85))
                        .lineSpacing(4)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(14)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        case .loading, .idle:
            VStack(spacing: 10) {
                EqualizerBars(playing: true, color: Theme.gold, height: 16)
                Text("Finding lyrics…").font(Theme.font(11.5, .semibold)).foregroundStyle(Theme.mist)
            }
        case .instrumental:
            message("Instrumental", "Just enjoy the music ♪", icon: "music.quarternote.3", retry: false)
        case .notFound:
            message("No lyrics for this song", "LRCLIB doesn't have it yet", icon: "text.magnifyingglass", retry: true)
        case .failed:
            message("Couldn't load lyrics", "Check your connection", icon: "wifi.exclamationmark", retry: true)
        }
    }

    private func message(_ title: String, _ subtitle: String, icon: String, retry: Bool) -> some View {
        VStack(spacing: 6) {
            Image(systemName: icon).font(.system(size: 20, weight: .semibold)).foregroundStyle(Theme.gold).popIn()
            Text(title).font(Theme.font(13, .bold)).foregroundStyle(Theme.ink)
            Text(subtitle).font(Theme.font(10.5)).foregroundStyle(Theme.mist)
            if retry {
                PillButton(title: "Try again", icon: "arrow.clockwise", action: onRetry).padding(.top, 4)
            }
        }
    }
}

/// Lines light up as they're sung; the list glides to keep the current line centred.
private struct SyncedLyrics: View {
    var lines: [LyricLine]
    var current: Int?
    var onSeek: (Double) -> Void

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 9) {
                    Color.clear.frame(height: 60)
                    ForEach(lines.indices, id: \.self) { i in
                        line(i).id(i)
                    }
                    Color.clear.frame(height: 90)
                }
                .padding(.horizontal, 12)
            }
            .mask(LinearGradient(stops: [.init(color: .clear, location: 0), .init(color: .black, location: 0.2),
                                         .init(color: .black, location: 0.8), .init(color: .clear, location: 1)],
                                 startPoint: .top, endPoint: .bottom))
            .onAppear { proxy.scrollTo(current ?? 0, anchor: .center) }
            .onChange(of: current) { i in
                withAnimation(Theme.liquid) { proxy.scrollTo(i ?? 0, anchor: .center) }
            }
        }
    }

    private func line(_ i: Int) -> some View {
        let isCurrent = i == current
        let distance = abs(i - (current ?? -1))
        let text = lines[i].text.isEmpty ? "♪" : lines[i].text
        return Button { onSeek(lines[i].time) } label: {
            Text(text)
                .font(Theme.font(isCurrent ? 15 : 13, .semibold))
                .foregroundStyle(isCurrent ? Theme.cream : Theme.ink.opacity(i < (current ?? 0) ? 0.28 : (distance == 1 ? 0.55 : 0.38)))
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
                .blur(radius: isCurrent ? 0 : min(1.2, Double(distance) * 0.25))
                .scaleEffect(isCurrent ? 1 : 0.97, anchor: .leading)
                .contentShape(Rectangle())
        }
        .buttonStyle(PressableStyle(scale: 0.97))
        .help("Jump here")
        .animation(Theme.liquid, value: isCurrent)
    }
}
