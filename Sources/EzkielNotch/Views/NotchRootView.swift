import AppKit
import NotchCore
import SwiftUI
import UniformTypeIdentifiers

/// Fills the whole (fixed-size) panel. Only the notch shape inside it animates; each presentation
/// lays its content out at its own final size, so nothing re-flows while the notch opens.
struct NotchRootView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var chrome: NotchChrome
    @ObservedObject var music: MusicController
    @ObservedObject var battery: BatteryMonitor
    @ObservedObject var lyrics: LyricsService
    @ObservedObject var calendar: CalendarService

    var body: some View {
        let size = model.bodySize
        let shape = model.shape
        let notch = NotchShape(bottomRadius: shape.radius, ear: shape.ear, belly: chrome.ripple)
        let expanded = model.presentation == .expanded

        ZStack(alignment: .top) {
            Color.clear

            ZStack(alignment: .top) {
                // Body and its shadow sit under the content: the shadow re-renders with the shape,
                // never with the live content on top.
                notch
                    .fill(Color.black)
                    .shadow(color: .black.opacity(expanded ? 0.42 : 0), radius: expanded ? 16 : 0, y: expanded ? 8 : 0)

                // A file dragged near: a faint Stash-coloured glow rises from the bottom.
                if model.fileDragNear || model.dropTargeted {
                    notch
                        .fill(LinearGradient(colors: [Accent.stash.deep.opacity(0), Accent.stash.deep.opacity(model.dropTargeted ? 0.28 : 0.16)],
                                             startPoint: .top, endPoint: .bottom))
                        .allowsHitTesting(false)
                        .transition(.opacity)
                }

                content
                    .frame(width: size.width + shape.ear * 2, height: size.height, alignment: .top)
                    .clipShape(notch)
            }
            .frame(width: size.width + shape.ear * 2, height: size.height)
            .onDrop(of: [UTType.fileURL], isTargeted: $model.dropTargeted) { providers in
                model.receiveDrop(providers)
            }
        }
        .frame(width: model.panelSize.width, height: model.panelSize.height, alignment: .top)
        .environment(\.colorScheme, .dark)
        .onChange(of: model.dropTargeted) { targeted in
            if targeted { model.expand(.stash) }
        }
    }

    @ViewBuilder
    private var content: some View {
        let bar = NotchMetrics.barHeight(model.geometry)
        switch model.presentation {
        case .compact:
            CompactView(model: model, chrome: chrome, music: music, battery: battery, lyrics: lyrics, calendar: calendar)
                .padding(.horizontal, NotchMetrics.compactShape.ear)
                .transition(.notchContent)
        case .peek:
            if let peek = model.peek {
                PeekView(model: model, music: music, peek: peek, bar: bar)
                    .id(peek.id)
                    .padding(.horizontal, NotchMetrics.peekShape.ear)
                    .frame(width: model.peekSize(peek).width + NotchMetrics.peekShape.ear * 2,
                           height: model.peekSize(peek).height, alignment: .top)
                    .transition(.notchContent)
            }
        case .expanded:
            ExpandedView(model: model, chrome: chrome, music: music)
                .padding(.horizontal, NotchMetrics.expandedShape.ear)
                .frame(width: NotchMetrics.expandedWidth + NotchMetrics.expandedShape.ear * 2,
                       height: NotchMetrics.expandedSize(model.geometry, pane: model.paneHeight).height, alignment: .top)
                .transition(.notchContent)
        }
    }
}

// MARK: - Compact

struct CompactView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var chrome: NotchChrome
    @ObservedObject var music: MusicController
    @ObservedObject var battery: BatteryMonitor
    @ObservedObject var lyrics: LyricsService
    @ObservedObject var calendar: CalendarService

    var body: some View {
        let bar = NotchMetrics.barHeight(model.geometry)
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                if model.showsWings {
                    leftWing
                        .frame(width: NotchMetrics.wing + (chrome.hovering ? 6 : 0))
                        .transition(.opacity.combined(with: .scale(scale: 0.7)))
                }
                center
                    .frame(width: model.geometry.notchWidth)
                if model.showsWings {
                    rightWing
                        .frame(width: NotchMetrics.wing + (chrome.hovering ? 6 : 0))
                        .transition(.opacity.combined(with: .scale(scale: 0.7)))
                }
            }
            .frame(height: bar + (chrome.hovering ? 3 : 0))

            if model.showsClosedLyrics, let np = music.nowPlaying, let lines = lyrics.syncedLines {
                ClosedLyricLine(np: np, lines: lines)
                    .frame(height: NotchMetrics.lyricBand)
                    .transition(.opacity.combined(with: .offset(y: -6)))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .contentShape(Rectangle())
        .onTapGesture { model.expand() }
        .help("Ezkiel Notch · hover or click to open · \(model.settings.hotkey.label)")
        .animation(Theme.smooth, value: model.showsClosedLyrics)
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
            // Fixed size and detail; hover only scales it, so the drawing never swaps.
            LionMascot(size: 22, mood: model.displayedMood, animated: true, detailed: false)
                .scaleEffect(chrome.hovering ? 1.12 : 1)
                .transition(.scale.combined(with: .opacity))
        } else {
            Image(systemName: "sparkle").font(.system(size: 11, weight: .semibold)).foregroundStyle(Theme.gold)
        }
    }

    @ViewBuilder
    private var center: some View {
        if !model.geometry.hasNotch {
            // No hardware notch: show a clock in the middle.
            TimelineView(.everyMinute) { tl in
                Text(tl.date, format: .dateTime.hour().minute())
                    .font(Theme.number(13))
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
                    .font(Theme.number(11, .semibold))
                    .foregroundStyle(model.focus.mode == .focus ? AnyShapeStyle(Accent.den.linear) : AnyShapeStyle(Accent.rest.linear))
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
            }
            .transition(.scale.combined(with: .opacity))
        } else if let meeting = model.meetingSoon {
            TimelineView(.everyMinute) { tl in
                let minutes = max(1, Int((meeting.start.timeIntervalSince(tl.date) / 60).rounded(.up)))
                Text("in \(minutes)m")
                    .font(Theme.number(11, .semibold))
                    .foregroundStyle(Accent.rally.linear)
                    .lineLimit(1)
            }
            .help(meeting.title)
            .transition(.scale.combined(with: .opacity))
        } else if let agent = model.liveAgents.first {
            // Scout: an agent is working (calm pulse) or waiting for you (amber).
            let waiting = agent.kind == .needsYou
            HStack(spacing: 2) {
                Image(systemName: waiting ? "hand.raised.fill" : "sparkle")
                    .font(.system(size: 11, weight: .semibold))
                    .symbolRenderingMode(.hierarchical)
                    .symbolPulse(true)
                if model.agents.count > 1 {
                    Text("\(model.agents.count)").font(Theme.number(10.5, .semibold))
                }
            }
            .foregroundStyle(waiting ? Accent.den.linear : Accent.scout.linear)
            .help(waiting ? agent.title : "\(agent.title)\(agent.project.map { " · \($0)" } ?? "")")
            .transition(.scale.combined(with: .opacity))
        } else if let np = music.nowPlaying, np.isPlaying {
            EqualizerBars(playing: true, color: music.artworkAccent?.deep ?? np.player.tint, height: 12)
                .transition(.scale.combined(with: .opacity))
        } else if model.unreadTotal > 0 {
            CountPill(label: RoarPane.short(model.unreadTotal), accent: .roar)
                .scaleEffect(0.82)
                .help("\(model.unreadTotal) unread · \(model.roarBadges.map(\.name).joined(separator: ", "))")
                .transition(.scale.combined(with: .opacity))
        } else if let b = battery.state, b.isCharging {
            HStack(spacing: 2) {
                Image(systemName: "bolt.fill").font(.system(size: 8.5, weight: .semibold))
                Text("\(b.percent)%").font(Theme.number(10.5, .semibold))
            }
            .foregroundStyle(Theme.teal)
            .transition(.scale.combined(with: .opacity))
        } else {
            ProgressRing(progress: model.waterProgress, lineWidth: 2.4, accent: .oasis)
                .frame(width: 13, height: 13)
                .help("\(model.waterToday) ml of water today")
                .transition(.scale.combined(with: .opacity))
        }
    }
}

// MARK: - Peek

struct PeekView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var music: MusicController
    var peek: Peek
    var bar: CGFloat

    var body: some View {
        if peek.isHUD { hud } else if peek.isRich { rich } else { card }
    }

    /// Overlay: icon and value in the wings, then the level bar (volume) or a label.
    private var hud: some View {
        VStack(spacing: 0) {
            wings {
                Image(systemName: peek.icon)
                    .font(.system(size: 13, weight: .semibold))
                    .symbolRenderingMode(.hierarchical)
                    .foregroundStyle(peek.tint)
            } trailing: {
                if let value = peek.value {
                    Text(value).font(Theme.number(13)).foregroundStyle(Theme.ink)
                }
            }
            if let level = peek.level {
                LevelBar(level: level, accent: .whiskers)
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

    /// Pounce (file) and Rally (meeting): a second row with a thumbnail or icon and buttons.
    private var rich: some View {
        VStack(spacing: 0) {
            wings {
                Image(systemName: peek.icon)
                    .font(.system(size: 13, weight: .semibold))
                    .symbolRenderingMode(.hierarchical)
                    .foregroundStyle(peek.tint)
            } trailing: {
                if peek.link != nil {
                    Image(systemName: "video.fill").font(.system(size: 12, weight: .semibold)).foregroundStyle(peek.tint)
                }
            }
            HStack(spacing: 10) {
                if let file = peek.file {
                    FileThumbnail(url: file, size: 38)
                        .onDrag { NSItemProvider(contentsOf: file) ?? NSItemProvider() }
                        .help("Drag it anywhere")
                }
                VStack(alignment: .leading, spacing: 1) {
                    Text(peek.title).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink).lineLimit(1)
                    Text(peek.subtitle).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                        .lineLimit(1).truncationMode(.middle)
                }
                Spacer(minLength: 6)
                if let file = peek.file {
                    HStack(spacing: 6) {
                        IconButton(icon: "doc.on.doc", label: "Copy", size: 26) { model.copyFile(file) }
                        IconButton(icon: "tray.and.arrow.down", label: "Add to Stash", size: 26) { model.stashFile(file) }
                        IconButton(icon: "magnifyingglass", label: "Show in Finder", size: 26) { model.reveal(file) }
                    }
                } else if let link = peek.link {
                    PillButton(title: "Join", icon: "video.fill", prominent: true, accent: .rally) { model.join(link) }
                }
            }
            .padding(.horizontal, 14)
            .frame(maxHeight: .infinity)
        }
    }

    /// The menu-bar row: left and right of the camera housing.
    private func wings<L: View, T: View>(@ViewBuilder leading: () -> L, @ViewBuilder trailing: () -> T) -> some View {
        HStack(spacing: 0) {
            leading().frame(maxWidth: .infinity)
            Color.clear.frame(width: model.geometry.notchWidth)
            trailing().frame(maxWidth: .infinity)
        }
        .frame(height: bar)
    }

    /// One line under the camera: title, then the detail in grey.
    private var caption: some View {
        HStack(spacing: 6) {
            Text(peek.title)
                .font(Theme.font(Theme.Size.body, .semibold))
                .foregroundStyle(Theme.ink)
                .layoutPriority(1)
            if !peek.subtitle.isEmpty {
                Text(peek.subtitle)
                    .font(Theme.font(Theme.Size.secondary))
                    .foregroundStyle(Theme.mist)
            }
        }
        .lineLimit(1)
        .truncationMode(.tail)
        .padding(.horizontal, 16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .padding(.top, 2)
    }

    @ViewBuilder
    private var leading: some View {
        if peek.artwork, let art = music.artwork {
            Image(nsImage: art)
                .resizable()
                .aspectRatio(contentMode: .fill)
                .frame(width: 22, height: 22)
                .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
        } else if peek.appIcon, let app = peek.app {
            AppIconView(bundleID: app, size: 22)
        } else if peek.showsLion {
            LionMascot(size: 26, mood: peek.mood, detailed: true)
        } else if let progress = peek.progress {
            ZStack {
                ProgressRing(progress: progress, lineWidth: 2.4, accent: Accent(light: peek.tint.opacity(0.7), deep: peek.tint))
                Image(systemName: "bolt.fill").font(.system(size: 8.5, weight: .bold)).foregroundStyle(peek.tint)
                    .opacity(peek.icon == "bolt.fill" ? 1 : 0)
            }
            .frame(width: 22, height: 22)
        } else {
            Image(systemName: peek.icon)
                .font(.system(size: 14, weight: .semibold))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(peek.tint)
        }
    }

    @ViewBuilder
    private var trailing: some View {
        if let value = peek.value {
            Text(value).font(Theme.number(15)).foregroundStyle(peek.tint)
        } else if peek.artwork {
            EqualizerBars(playing: true, color: music.artworkAccent?.deep ?? peek.tint, height: 12)
        } else if peek.showsLion {
            Image(systemName: peek.icon)
                .font(.system(size: 13, weight: .semibold))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(peek.tint)
        }
    }
}

/// QuickLook thumbnail with the file icon as a placeholder.
struct FileThumbnail: View {
    var url: URL
    var size: CGFloat
    @State private var image: NSImage?

    var body: some View {
        Image(nsImage: image ?? NSWorkspace.shared.icon(forFile: url.path))
            .resizable()
            .interpolation(.high)
            .aspectRatio(contentMode: image == nil ? .fit : .fill)
            .frame(width: size, height: size)
            .clipShape(RoundedRectangle(cornerRadius: 7, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(Theme.hairline, lineWidth: 0.5))
            .task(id: url) { image = await Thumbnails.image(for: url, side: size) }
    }
}

// MARK: - Closed-notch sing-along (Chorus)

/// The line being sung, under the closed notch. Each new line rises in.
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
                    .foregroundStyle(Theme.ink)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
                    .padding(.horizontal, 16)
                    .id(index ?? -1)
                    .transition(.asymmetric(insertion: .opacity.combined(with: .offset(y: 8)),
                                            removal: .opacity.combined(with: .offset(y: -6))))
            }
            .animation(Theme.smooth, value: index)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .clipped()
    }
}

extension Accent {
    /// Den's break mode.
    static let rest = Accent(light: Color(hex: 0xA6F0D8), deep: Color(hex: 0x30C99A))
}
