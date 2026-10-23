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
        let notch = NotchShape(bottomRadius: shape.radius, ear: shape.ear)
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

                // Hairline rim in lion gold.
                notch
                    .stroke(LinearGradient(colors: [Theme.gold.opacity(0), Theme.gold.opacity(expanded ? 0.28 : 0.12),
                                                    Theme.ember.opacity(expanded ? 0.35 : 0.1)],
                                           startPoint: .top, endPoint: .bottom), lineWidth: 1)
                    .opacity(model.presentation == .compact && !model.hovering ? 0 : 1)
                    .allowsHitTesting(false)

                content
                    .padding(.horizontal, shape.ear)
                    .frame(width: size.width + shape.ear * 2, height: size.height, alignment: .top)
                    .clipShape(notch)
            }
            .frame(width: size.width + shape.ear * 2, height: size.height)
            .compositingGroup()
            .shadow(color: .black.opacity(expanded ? 0.55 : 0.0), radius: expanded ? 22 : 0, y: expanded ? 10 : 0)
            .shadow(color: Theme.orange.opacity(model.fileDragNear || model.dropTargeted ? 0.5 : 0), radius: 24)
            .onDrop(of: [UTType.fileURL], isTargeted: $model.dropTargeted) { providers in
                model.receiveDrop(providers)
            }
        }
        .frame(width: model.panelSize.width, height: model.panelSize.height, alignment: .top)
        .environment(\.colorScheme, .dark)
        .onChange(of: model.dropTargeted) { targeted in
            if targeted { model.expand(.shelf) }
        }
    }

    private var glowOpacity: Double {
        if model.dropTargeted { return 0.35 }
        if model.fileDragNear { return 0.22 }
        if model.presentation == .peek { return 0.10 }
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
        .contentShape(Rectangle())
        .onTapGesture { model.expand() }
        .help("Ezkiel Notch · hover or click to open · ⌥⌘I")
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
            // No hardware notch: show a clock like Nusa's closed notch.
            TimelineView(.everyMinute) { tl in
                Text(tl.date, format: .dateTime.hour().minute())
                    .font(Theme.rounded(13, .bold))
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
            GlowRing(progress: model.waterProgress, lineWidth: 2.6,
                     colors: [Color(hex: 0x9FE7FF), Color(hex: 0x4FA3E8), Color(hex: 0x9FE7FF)],
                     glow: Color(hex: 0x4FA3E8))
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
        let bar = NotchMetrics.barHeight(model.geometry)
        VStack(spacing: 0) {
            Color.clear.frame(height: bar)
            HStack(spacing: 12) {
                ZStack {
                    if peek.showsLion {
                        LionMascot(size: 38, mood: peek.mood)
                    } else if let progress = peek.progress {
                        GlowRing(progress: progress, lineWidth: 3.2, colors: [peek.tint, peek.tint.opacity(0.7), peek.tint],
                                 glow: peek.tint)
                            .frame(width: 32, height: 32)
                        Image(systemName: peek.icon).font(.system(size: 12, weight: .bold)).foregroundStyle(peek.tint)
                    } else {
                        Circle().fill(peek.tint.opacity(0.18)).frame(width: 34, height: 34)
                        Image(systemName: peek.icon).font(.system(size: 14, weight: .bold)).foregroundStyle(peek.tint)
                    }
                }
                .frame(width: 40, height: 40)

                VStack(alignment: .leading, spacing: 2) {
                    Text(peek.title)
                        .font(Theme.rounded(13.5, .bold))
                        .foregroundStyle(Theme.ink)
                        .lineLimit(1)
                    Text(peek.subtitle)
                        .font(Theme.rounded(11))
                        .foregroundStyle(Theme.mist)
                        .lineLimit(1)
                }
                Spacer(minLength: 6)
                if peek.showsLion {
                    Image(systemName: peek.icon)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundStyle(peek.tint)
                        .frame(width: 30, height: 30)
                        .background(Circle().fill(peek.tint.opacity(0.16)))
                }
            }
            .padding(.horizontal, 18)
            .frame(maxHeight: .infinity)
        }
        .contentShape(Rectangle())
        .onTapGesture { model.openPeek() }
    }
}
