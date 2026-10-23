import AppKit
import NotchCore
import SwiftUI

struct MusicPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    @State private var scrubbing: Double?

    var body: some View {
        if let np = music.nowPlaying {
            player(np)
        } else {
            emptyState
        }
    }

    // MARK: Player

    private func player(_ np: NowPlaying) -> some View {
        let tint = np.player == .spotify ? Color(hex: 0x1ED760) : Theme.berry
        return HStack(spacing: 18) {
            artwork(np, tint: tint)
                .frame(width: 132, height: 132)

            VStack(alignment: .leading, spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Text(np.player.name.uppercased())
                            .font(Theme.rounded(8.5, .heavy))
                            .tracking(1.3)
                            .foregroundStyle(tint)
                            .padding(.horizontal, 6)
                            .frame(height: 16)
                            .background(Capsule().fill(tint.opacity(0.14)))
                        EqualizerBars(playing: np.isPlaying, color: tint, height: 10)
                    }
                    Text(np.title)
                        .font(Theme.rounded(17, .bold))
                        .foregroundStyle(Theme.ink)
                        .lineLimit(1)
                        .id("t" + np.trackKey)
                        .transition(.opacity.combined(with: .offset(y: 6)))
                    Text([np.artist, np.album].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(Theme.rounded(11.5))
                        .foregroundStyle(Theme.mist)
                        .lineLimit(1)
                }

                progress(np)

                HStack(spacing: 18) {
                    transportButton("backward.fill", size: 34) { music.previous() }
                    Button { music.playPause() } label: {
                        Image(systemName: np.isPlaying ? "pause.fill" : "play.fill")
                            .font(.system(size: 17, weight: .bold))
                            .foregroundStyle(Theme.navy)
                            .frame(width: 44, height: 44)
                            .background(Circle().fill(Theme.lionGradient))
                            .shadow(color: Theme.orange.opacity(0.5), radius: 10)
                            .contentTransition(.opacity)
                    }
                    .buttonStyle(PressableStyle(scale: 0.86))
                    .help(np.isPlaying ? "Pause" : "Play")
                    transportButton("forward.fill", size: 34) { music.next() }
                    Spacer()
                    IconButton(icon: "arrow.up.forward.app", size: 28, tint: Theme.mist, help: "Open \(np.player.name)") {
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
        .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).strokeBorder(Color.white.opacity(0.12), lineWidth: 1))
        .shadow(color: tint.opacity(np.isPlaying ? 0.35 : 0.1), radius: 16, y: 6)
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
                        Capsule().fill(Theme.lionGradient)
                            .frame(width: max(4, geo.size.width * fraction))
                        Circle().fill(Theme.cream)
                            .frame(width: scrubbing != nil ? 12 : 8, height: scrubbing != nil ? 12 : 8)
                            .offset(x: max(0, geo.size.width * fraction - 4))
                            .shadow(color: .black.opacity(0.4), radius: 2)
                    }
                    .frame(height: 5)
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
                .font(.system(size: 9.5, weight: .semibold, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(Theme.mist)
            }
        }
    }

    private func transportButton(_ icon: String, size: CGFloat, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 15, weight: .bold))
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
                LionMascot(size: 64, mood: .awake)
                VStack(alignment: .leading, spacing: 8) {
                    Text("Allow music access").font(Theme.rounded(15, .bold)).foregroundStyle(Theme.ink)
                    Text("System Settings → Privacy & Security → Automation → Ezkiel Notch → turn on Spotify / Music.")
                        .font(Theme.rounded(11)).foregroundStyle(Theme.mist).fixedSize(horizontal: false, vertical: true)
                    PillButton(title: "Open Automation settings", icon: "lock.open", prominent: true) {
                        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!)
                    }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        default:
            HStack(spacing: 16) {
                LionMascot(size: 64, mood: model.displayedMood == .sleeping ? .sleeping : .awake)
                VStack(alignment: .leading, spacing: 8) {
                    Text(music.status == .noPlayer ? "Open Spotify or Apple Music" : "Waiting for a track")
                        .font(Theme.rounded(15, .bold)).foregroundStyle(Theme.ink)
                    Text("Play something and it shows up here, with artwork, seek and controls.")
                        .font(Theme.rounded(11)).foregroundStyle(Theme.mist)
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
