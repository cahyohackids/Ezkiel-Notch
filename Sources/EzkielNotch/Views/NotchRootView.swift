import AppKit
import NotchCore
import SwiftUI
import UniformTypeIdentifiers

/// Fills the whole (fixed-size) panel. Only the notch shape inside it animates.
struct NotchRootView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    @ObservedObject var battery: BatteryMonitor

    var body: some View {
        let size = model.bodySize
        let shape = model.shape
        let notch = NotchShape(bottomRadius: shape.radius, ear: shape.ear, belly: model.ripple)
        let expanded = model.presentation == .expanded

        ZStack(alignment: .top) {
            Color.clear

            ZStack(alignment: .top) {
                // Body
                notch.fill(Color.black)

                // Warm glow when a file is dragged near, or when a peek is up.
                notch
                    .fill(LinearGradient(colors: [Theme.orange.opacity(0.0), Theme.orange.opacity(glowOpacity)],
                                         startPoint: .top, endPoint: .bottom))
                    .allowsHitTesting(false)


                content
                    .padding(.horizontal, shape.ear)
                    .frame(width: size.width + shape.ear * 2, height: size.height, alignment: .top)
                    .clipShape(notch)
            }
            .frame(width: size.width + shape.ear * 2, height: size.height)
            .compositingGroup()
            .shadow(color: .black.opacity(expanded ? 0.45 : 0.0), radius: expanded ? 18 : 0, y: expanded ? 8 : 0)
            .onDrop(of: [UTType.fileURL], isTargeted: $model.dropTargeted) { providers in
                model.receiveDrop(providers)
            }
        }
        .frame(width: model.panelSize.width, height: model.panelSize.height, alignment: .top)
        .animation(Theme.liquid, value: model.showsClosedLyrics)
        .environment(\.colorScheme, .dark)
        .onChange(of: model.dropTargeted) { targeted in
            if targeted { model.expand(.shelf) }
        }
    }

    private var glowOpacity: Double {
        if model.dropTargeted { return 0.22 }
        if model.fileDragNear { return 0.12 }
        return 0
    }

    @ViewBuilder
    private var content: some View {
        switch model.presentation {
        case .compact:
            CompactView(model: model, music: music, battery: battery)
                .transition(.notchContent)
        case .peek:
            if let peek = model.peek {
                PeekView(model: model, peek: peek)
                    .transition(.notchContent)
            }
        case .expanded:
            ExpandedView(model: model, music: music)
                .transition(.notchContent)
        }
    }
}

// MARK: - Compact

struct CompactView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    @ObservedObject var battery: BatteryMonitor

    var body: some View {
        let bar = NotchMetrics.barHeight(model.geometry)
        VStack(spacing: 0) {
            topRow(bar: bar)
            if model.showsClosedLyrics, let np = music.nowPlaying, let lines = model.lyrics.syncedLines {
                ClosedLyricLine(np: np, lines: lines)
                    .frame(height: NotchMetrics.lyricBand)
                    .transition(.opacity.combined(with: .offset(y: -8)))
            }
        }
        .frame(maxWidth: .infinity)
        .contentShape(Rectangle())
        .onTapGesture { model.expand() }
        .help("Ezkiel Notch · hover or click to open · ⌥⌘I")
    }

    private func topRow(bar: CGFloat) -> some View {
        HStack(spacing: 0) {
            if model.showsWings {
                leftWing
                    .frame(width: NotchMetrics.wing + (model.hovering ? 7 : 0))
                    .transition(.opacity.combined(with: .scale(scale: 0.6)))
            }
            center
                .frame(width: model.geometry.notchWidth)
            if model.showsWings {
                rightWing
                    .frame(width: NotchMetrics.wing + (model.hovering ? 7 : 0))
                    .transition(.opacity.combined(with: .scale(scale: 0.6)))
            }
        }
        .frame(height: bar + (model.hovering ? 4 : 0))
        .frame(maxWidth: .infinity)
    }

    @ViewBuilder
    private var leftWing: some View {
        if let np = music.nowPlaying, np.isPlaying, let art = music.artwork {
            Image(nsImage: art)
                .resizable()
                .aspectRatio(contentMode: .fill)
                .frame(width: 20, height: 20)
                .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
                .transition(.scale.combined(with: .opacity))
        } else if model.settings.showMascot {
            LionMascot(size: model.hovering ? 26 : 22, mood: model.displayedMood, animated: true)
                .transition(.scale.combined(with: .opacity))
        } else {
            Image(systemName: "sparkle").font(.system(size: 11, weight: .bold)).foregroundStyle(Theme.gold)
        }
    }

    @ViewBuilder
    private var center: some View {
        if !model.geometry.hasNotch {
            // No hardware notch: show a clock in the closed notch.
            TimelineView(.everyMinute) { tl in
                Text(tl.date, format: .dateTime.hour().minute())
                    .font(Theme.font(13, .bold))
                    .monospacedDigit()
                    .foregroundStyle(Theme.ink)
            }
        } else {
            Color.clear
        }
    }

    @ViewBuilder
    private var rightWing: some View {
        if model.focus.isRunning {
            TimelineView(.periodic(from: .now, by: 1)) { tl in
                Text(FocusClock.format(model.focus.remaining(at: tl.date)))
                    .font(.system(size: 11, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(model.focus.mode == .focus ? Theme.gold : Theme.teal)
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
            }
            .transition(.scale.combined(with: .opacity))
        } else if let np = music.nowPlaying, np.isPlaying {
            EqualizerBars(playing: true, color: np.player == .spotify ? Color(hex: 0x1ED760) : Theme.gold, height: 12)
                .transition(.scale.combined(with: .opacity))
        } else if let b = battery.state, b.isCharging {
            HStack(spacing: 2) {
                Image(systemName: "bolt.fill").font(.system(size: 8, weight: .bold))
                Text("\(b.percent)").font(.system(size: 10.5, weight: .bold, design: .rounded)).monospacedDigit()
            }
            .foregroundStyle(Theme.teal)
            .transition(.scale.combined(with: .opacity))
        } else {
            ProgressRing(progress: model.waterProgress, lineWidth: 2.4, color: Theme.water)
                .frame(width: 13, height: 13)
                .help("\(model.waterToday) ml of water today")
                .transition(.scale.combined(with: .opacity))
        }
    }
}

// MARK: - Peek

struct PeekView: View {
    @ObservedObject var model: AppModel
    var peek: Peek

    var body: some View {
        if peek.isHUD { hud } else { card }
    }

    /// Overlay: icon and value in the wings, then the level bar (volume) or a label.
    private var hud: some View {
        VStack(spacing: 0) {
            wings {
                Image(systemName: peek.icon)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(peek.tint)
            } trailing: {
                if let value = peek.value {
                    Text(value)
                        .font(Theme.font(13, .semibold))
                        .monospacedDigit()
                        .foregroundStyle(Theme.ink)
                }
            }
            if let level = peek.level {
                LevelBar(level: level, tint: peek.tint)
                    .padding(.horizontal, 22)
                    .frame(maxHeight: .infinity, alignment: .top)
            } else {
                caption
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { model.openPeek() }
    }

    private var card: some View {
        VStack(spacing: 0) {
            wings {
                leading.popIn(delay: 0.04)
            } trailing: {
                trailing.popIn(delay: 0.08)
            }
            caption
        }
        .contentShape(Rectangle())
        .onTapGesture { model.openPeek() }
    }

    /// The menu-bar row: left and right of the camera housing.
    private func wings<L: View, T: View>(@ViewBuilder leading: () -> L, @ViewBuilder trailing: () -> T) -> some View {
        HStack(spacing: 0) {
            leading().frame(maxWidth: .infinity)
            Color.clear.frame(width: model.geometry.notchWidth)
            trailing().frame(maxWidth: .infinity)
        }
        .frame(height: NotchMetrics.barHeight(model.geometry))
    }

    /// One line under the camera: title, then the detail in grey.
    private var caption: some View {
        HStack(spacing: 6) {
            Text(peek.title)
                .font(Theme.font(12, .semibold))
                .foregroundStyle(Theme.ink)
            if !peek.subtitle.isEmpty {
                Text(peek.subtitle)
                    .font(Theme.font(11.5))
                    .foregroundStyle(Theme.mist)
            }
        }
        .lineLimit(1)
        .padding(.horizontal, 16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .padding(.top, 2)
    }

    @ViewBuilder
    private var leading: some View {
        if peek.artwork, let art = model.music.artwork {
            Image(nsImage: art)
                .resizable()
                .aspectRatio(contentMode: .fill)
                .frame(width: 22, height: 22)
                .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
        } else if peek.showsLion {
            LionMascot(size: 26, mood: peek.mood)
        } else if let progress = peek.progress {
            ZStack {
                ProgressRing(progress: progress, lineWidth: 2.4, color: peek.tint)
                Image(systemName: peek.icon).font(.system(size: 8.5, weight: .bold)).foregroundStyle(peek.tint)
            }
            .frame(width: 22, height: 22)
        } else {
            Image(systemName: peek.icon)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(peek.tint)
        }
    }

    @ViewBuilder
    private var trailing: some View {
        if let value = peek.value {
            Text(value)
                .font(Theme.font(15, .semibold))
                .monospacedDigit()
                .foregroundStyle(peek.tint)
        } else if peek.artwork {
            EqualizerBars(playing: true, color: peek.tint, height: 12)
        } else if peek.showsLion {
            Image(systemName: peek.icon)
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(peek.tint)
        }
    }
}

// MARK: - Closed-notch sing-along

/// The line being sung, under the closed notch. Each new line rises in like a bubble.
struct ClosedLyricLine: View {
    var np: NowPlaying
    var lines: [LyricLine]

    var body: some View {
        TimelineView(.periodic(from: .now, by: 0.25)) { tl in
            let index = LRC.index(at: np.position(at: tl.date) + 0.15, in: lines)
            let text = index.map { lines[$0].text } ?? ""
            ZStack {
                Text(text.isEmpty ? "♪" : text)
                    .font(Theme.font(12, .medium))
                    .foregroundStyle(Theme.cream)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
                    .padding(.horizontal, 16)
                    .id(index ?? -1)
                    .transition(.asymmetric(
                        insertion: .modifier(active: LyricRise(progress: 0), identity: LyricRise(progress: 1)),
                        removal: .opacity.combined(with: .offset(y: -6))))
            }
            .animation(Theme.liquid, value: index)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .clipped()
    }
}

private struct LyricRise: ViewModifier {
    var progress: CGFloat
    func body(content: Content) -> some View {
        content
            .offset(y: (1 - progress) * 10)
            .blur(radius: (1 - progress) * 4)
            .opacity(Double(progress))
    }
}
