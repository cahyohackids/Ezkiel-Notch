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
        let notch = NotchShape(bottomRadius: shape.radius, ear: shape.ear, belly: chrome.ripple, topRadius: shape.top)
        let expanded = model.presentation == .expanded
        let island = model.isIsland
        // A notch casts a shadow only when open; a floating island casts a softer one all the time.
        let shadowOpacity = island ? (model.settings.islandShadow ? (expanded ? 0.45 : 0.32) : 0) : (expanded ? 0.42 : 0)
        let shadowRadius: CGFloat = island ? (expanded ? 18 : 8) : (expanded ? 16 : 0)

        ZStack(alignment: .top) {
            Color.clear

            ZStack(alignment: .top) {
                // The sparkly eyes' ambient light, under everything.
                if model.settings.closedFace == .eyes && model.settings.eyeStyle == .anime {
                    AmbientGlow(shape: notch, expanded: expanded)
                        .transition(.opacity)
                }

                // Body and its shadow sit under the content: the shadow re-renders with the shape,
                // never with the live content on top.
                IslandBody(shape: notch, island: island, glass: island && model.settings.islandGlass)
                    .shadow(color: .black.opacity(shadowOpacity), radius: shadowRadius, y: island ? (expanded ? 10 : 4) : (expanded ? 8 : 0))

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
            .padding(.top, model.topInset)
        }
        .frame(width: model.panelSize.width, height: model.panelSize.height, alignment: .top)
        .opacity(model.hiddenForFullscreen ? 0 : 1)
        .environment(\.locale, Lang.locale)
        .animation(Theme.soft, value: model.hiddenForFullscreen)
        .environment(\.colorScheme, .dark)
        .onChange(of: model.dropTargeted) { targeted in
            if targeted { model.expand(.stash) }
        }
    }

    @ViewBuilder
    private var content: some View {
        let bar = model.rowHeight
        let ear = model.shape.ear
        switch model.presentation {
        case .compact:
            CompactView(model: model, chrome: chrome, music: music, battery: battery, lyrics: lyrics, calendar: calendar)
                .padding(.horizontal, ear)
                .transition(.notchContent)
        case .peek:
            if let peek = model.peek {
                PeekView(model: model, music: music, peek: peek, bar: bar)
                    .id(peek.id)
                    .padding(.horizontal, ear)
                    .frame(width: model.peekSize(peek).width + ear * 2,
                           height: model.peekSize(peek).height, alignment: .top)
                    .transition(.notchContent)
            }
        case .expanded:
            ExpandedView(model: model, chrome: chrome, music: music)
                .padding(.horizontal, ear)
                .frame(width: model.expandedWidth + ear * 2, height: model.expandedSize.height, alignment: .top)
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
        let bar = model.rowHeight
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                if model.showsWings {
                    leftWing
                        .frame(width: NotchMetrics.wing + (chrome.hovering ? 6 : 0))
                        .overlay(alignment: .trailing) { ProwlDots(mic: model.prowl.micInUse, camera: model.prowl.cameraInUse) }
                        .transition(.opacity.combined(with: .scale(scale: 0.7)))
                }
                center
                    .frame(width: model.centerWidth)
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
        .help(L("Ezkiel Notch · hover or click to open · %@", model.settings.hotkey.label))
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
        } else if model.settings.closedFace == .lion {
            // Fixed size and detail; hover only scales it, so the drawing never swaps.
            LionMascot(size: 22, mood: model.displayedMood, animated: true, detailed: false)
                .scaleEffect(chrome.hovering ? 1.12 : 1)
                .transition(.scale.combined(with: .opacity))
        } else if model.settings.closedFace == .eyes && !model.centerIsFree {
            // A notched display: the camera owns the middle, so the eyes sit beside it.
            EzkielEyes(state: model.eyesState, hovering: chrome.hovering, height: 9, style: model.settings.eyeStyle)
                .transition(.scale.combined(with: .opacity))
        } else {
            Color.clear
        }
    }

    @ViewBuilder
    private var center: some View {
        if model.settings.closedFace == .eyes && model.centerIsFree {
            EzkielEyes(state: model.eyesState, hovering: chrome.hovering, height: model.isIsland ? 10 : 9, style: model.settings.eyeStyle)
        } else if !model.geometry.hasNotch && !model.isIsland {
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
                Text(L("in %dm", minutes))
                    .font(Theme.number(11, .semibold))
                    .foregroundStyle(Accent.rally.linear)
                    .lineLimit(1)
            }
            .help(meeting.title)
            .transition(.scale.combined(with: .opacity))
        } else if model.settings.closedFace != .eyes, let agent = model.liveAgents.first {
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
            .help(waiting ? L(agent.title) : "\(L(agent.title))\(agent.project.map { " · \($0)" } ?? "")")
            .transition(.scale.combined(with: .opacity))
        } else if let np = music.nowPlaying, np.isPlaying {
            EqualizerBars(playing: true, color: music.artworkAccent?.deep ?? np.player.tint, height: 12)
                .transition(.scale.combined(with: .opacity))
        } else if model.unreadTotal > 0 {
            CountPill(label: RoarPane.short(model.unreadTotal), accent: .roar)
                .scaleEffect(0.82)
                .help(L("%d unread", model.unreadTotal) + " · " + model.roarBadges.map(\.name).joined(separator: ", "))
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
                .help(L("%d ml of water today", model.waterToday))
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
        if model.peeksInline {
            if peek.isHUD { inlineHUD } else if peek.isRich { inlineRich } else { inlineCard }
        } else {
            if peek.isHUD { hud } else if peek.isRich { rich } else { card }
        }
    }

    // MARK: One row (island, or a display without a notch)

    private var inlineHUD: some View {
        HStack(spacing: 10) {
            Image(systemName: peek.icon)
                .font(.system(size: 13, weight: .semibold))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(peek.tint)
                .frame(width: 20)
            if let level = peek.level {
                LevelBar(level: level, accent: .whiskers)
            } else {
                Text(L(peek.title)).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink).lineLimit(1)
                Spacer(minLength: 0)
            }
            if let value = peek.value {
                Text(value).font(Theme.number(13)).foregroundStyle(Theme.ink).frame(minWidth: 40, alignment: .trailing)
            }
        }
        .padding(.horizontal, 16)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .contentShape(Rectangle())
        .onTapGesture { model.openPeek() }
    }

    private var inlineCard: some View {
        HStack(spacing: 10) {
            leading.frame(width: 28, height: 28).popIn(delay: 0.04)
            VStack(alignment: .leading, spacing: 0) {
                Text(L(peek.title)).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                if !peek.subtitle.isEmpty {
                    Text(L(peek.subtitle)).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                }
            }
            .lineLimit(1)
            .truncationMode(.tail)
            Spacer(minLength: 6)
            trailing.popIn(delay: 0.08)
        }
        .padding(.horizontal, 14)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .contentShape(Rectangle())
        .onTapGesture { model.openPeek() }
    }

    private var inlineRich: some View {
        HStack(spacing: 10) {
            if let file = peek.file {
                FileThumbnail(url: file, size: 40)
                    .onDrag { NSItemProvider(contentsOf: file) ?? NSItemProvider() }
                    .help(L("Drag it anywhere"))
            } else {
                Image(systemName: peek.icon)
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(peek.tint)
                    .frame(width: 40, height: 40)
                    .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(peek.tint.opacity(0.14)))
            }
            richText
            Spacer(minLength: 6)
            richActions
        }
        .padding(.horizontal, 12)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var richText: some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(L(peek.title)).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink).lineLimit(1)
            Text(L(peek.subtitle)).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                .lineLimit(1).truncationMode(.middle)
        }
    }

    @ViewBuilder
    private var richActions: some View {
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

    // MARK: Around the camera (notched display)

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
                richText
                Spacer(minLength: 6)
                richActions
            }
            .padding(.horizontal, 14)
            .frame(maxHeight: .infinity)
        }
    }

    /// The menu-bar row: left and right of the camera housing.
    private func wings<L: View, T: View>(@ViewBuilder leading: () -> L, @ViewBuilder trailing: () -> T) -> some View {
        HStack(spacing: 0) {
            leading().frame(maxWidth: .infinity)
            Color.clear.frame(width: model.centerWidth)
            trailing().frame(maxWidth: .infinity)
        }
        .frame(height: bar)
    }

    /// One line under the camera: title, then the detail in grey.
    private var caption: some View {
        HStack(spacing: 6) {
            Text(L(peek.title))
                .font(Theme.font(Theme.Size.body, .semibold))
                .foregroundStyle(Theme.ink)
                .layoutPriority(1)
            if !peek.subtitle.isEmpty {
                Text(L(peek.subtitle))
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
        } else if peek.progress != nil {
            // Battery: the icon here, the level as a ring on the other side.
            Image(systemName: peek.icon)
                .font(.system(size: 14, weight: .semibold))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(peek.tint)
        } else {
            Image(systemName: peek.icon)
                .font(.system(size: 14, weight: .semibold))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(peek.tint)
        }
    }

    @ViewBuilder
    private var trailing: some View {
        if let progress = peek.progress {
            ChargeRing(level: progress, tint: peek.tint, charging: peek.icon == "bolt.fill", size: model.peeksInline ? 32 : 28)
        } else if let value = peek.value {
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

/// Prowl: green while a camera is on, orange while a microphone is (the colours macOS uses).
struct ProwlDots: View {
    var mic: Bool
    var camera: Bool

    var body: some View {
        VStack(spacing: 3) {
            if camera { dot(Color(hex: 0x30D158)) }
            if mic { dot(Color(hex: 0xFF9F0A)) }
        }
        .padding(.trailing, 3)
        .animation(Theme.snappy, value: mic)
        .animation(Theme.snappy, value: camera)
        .help([camera ? L("Camera is on") : nil, mic ? L("Microphone is on") : nil].compactMap { $0 }.joined(separator: " · "))
        .accessibilityHidden(!mic && !camera)
        .accessibilityLabel([camera ? L("Camera is on") : nil, mic ? L("Microphone is on") : nil].compactMap { $0 }.joined(separator: ", "))
    }

    private func dot(_ color: Color) -> some View {
        Circle().fill(color).frame(width: 5, height: 5)
            .shadow(color: color.opacity(0.7), radius: 2)
            .transition(.scale.combined(with: .opacity))
    }
}

/// The notch's body: pure black for a notch; an island adds a hairline edge, or Liquid Glass when asked
/// (macOS 26), with a dark blur on older macOS. The dark tint keeps white text readable on any wallpaper.
/// The sparkly eyes' "ambient mode": their pink, magenta and violet spill softly around the notch and breathe,
/// like a video's colours glowing around its player. Still under Reduce Motion.
struct AmbientGlow: View {
    var shape: NotchShape
    var expanded: Bool
    @State private var breathe = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    static let colors = [Color(red: 1, green: 0.55, blue: 0.82), Color(red: 0.88, green: 0.24, blue: 0.68),
                         Color(red: 0.55, green: 0.3, blue: 0.95)]

    var body: some View {
        shape
            .fill(AngularGradient(colors: Self.colors + [Self.colors[0]], center: .center))
            .blur(radius: expanded ? 24 : 14)
            .opacity((expanded ? 0.55 : 1) * (breathe ? 1 : 0.6))
            .hueRotation(.degrees(breathe ? 14 : -8))
            .allowsHitTesting(false)
            .onAppear {
                guard !reduceMotion else { return }
                withAnimation(.easeInOut(duration: 3.4).repeatForever(autoreverses: true)) { breathe = true }
            }
    }
}

struct IslandBody: View {
    var shape: NotchShape
    var island: Bool
    var glass: Bool

    var body: some View {
        if glass {
            glassBody
                .overlay(shape.stroke(Color.white.opacity(0.14), lineWidth: 0.5))
        } else if island {
            shape.fill(Color.black)
                .overlay(shape.stroke(Color.white.opacity(0.09), lineWidth: 0.5))
        } else {
            shape.fill(Color.black)
        }
    }

    @ViewBuilder
    private var glassBody: some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            Color.clear.glassEffect(.regular.tint(Color.black.opacity(0.55)), in: shape)
        } else {
            blurBody
        }
        #else
        blurBody
        #endif
    }

    private var blurBody: some View {
        shape.fill(.ultraThinMaterial)
            .overlay(shape.fill(Color.black.opacity(0.55)))
    }
}

/// Battery level as a ring with the number inside. It fills up when it appears, and while charging a bright
/// arc keeps circling it. It animates only while the peek is on screen; with Reduce Motion it stays still.
struct ChargeRing: View {
    var level: Double
    var tint: Color
    var charging: Bool
    var size: CGFloat
    @State private var shown: Double = 0

    private var line: CGFloat { max(2.4, size * 0.1) }

    var body: some View {
        ZStack {
            Circle().stroke(tint.opacity(0.18), lineWidth: line)
            Circle()
                .trim(from: 0, to: shown)
                .stroke(AngularGradient(colors: [tint.opacity(0.75), tint], center: .center,
                                        startAngle: .degrees(0), endAngle: .degrees(360 * max(0.01, shown))),
                        style: StrokeStyle(lineWidth: line, lineCap: .round))
                .rotationEffect(.degrees(-90))
            if charging && !Theme.reduceMotion {
                TimelineView(.animation(minimumInterval: 1.0 / 30)) { tl in
                    let turn = tl.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 1.4) / 1.4
                    Circle()
                        .trim(from: 0, to: 0.22)
                        .stroke(AngularGradient(colors: [.white.opacity(0), .white.opacity(0.9)], center: .center,
                                                startAngle: .degrees(0), endAngle: .degrees(80)),
                                style: StrokeStyle(lineWidth: line, lineCap: .round))
                        .rotationEffect(.degrees(turn * 360 - 90))
                        .blendMode(.plusLighter)
                }
            }
            Text("\(Int((level * 100).rounded()))")
                .font(Theme.number(size * 0.34, .bold))
                .foregroundStyle(Theme.ink)
                .minimumScaleFactor(0.6)
        }
        .frame(width: size, height: size)
        .onAppear { withAnimation(Theme.reduceMotion ? nil : .easeOut(duration: 0.9)) { shown = level } }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(Int((level * 100).rounded()))%")
    }
}
