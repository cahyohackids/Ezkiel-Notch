import AppKit
import SwiftUI

// MARK: - Transitions

/// Fade + slight scale + offset, used for content that appears inside the notch. No large blurs:
/// they force an offscreen pass over the whole pane on every frame.
private struct Arrive: ViewModifier {
    var opacity: Double
    var scale: CGFloat
    var x: CGFloat
    var y: CGFloat
    func body(content: Content) -> some View {
        content
            .opacity(opacity)
            .scaleEffect(scale, anchor: .top)
            .offset(x: x, y: y)
    }
}

extension AnyTransition {
    static var notchContent: AnyTransition {
        .asymmetric(
            insertion: .modifier(active: Arrive(opacity: 0, scale: 0.98, x: 0, y: -6),
                                 identity: Arrive(opacity: 1, scale: 1, x: 0, y: 0))
                .animation(Theme.smooth.delay(0.06)),
            removal: .opacity.animation(.easeOut(duration: 0.1))
        )
    }

    static func tabSlide(forward: Bool) -> AnyTransition {
        let dx: CGFloat = forward ? 8 : -8
        return .asymmetric(
            insertion: .modifier(active: Arrive(opacity: 0, scale: 1, x: dx, y: 0), identity: Arrive(opacity: 1, scale: 1, x: 0, y: 0)),
            removal: .modifier(active: Arrive(opacity: 0, scale: 1, x: -dx, y: 0), identity: Arrive(opacity: 1, scale: 1, x: 0, y: 0))
        )
    }

    /// Shelf cards drop into place with a small overshoot.
    static var fallIn: AnyTransition {
        .asymmetric(
            insertion: .modifier(active: Arrive(opacity: 0, scale: 0.8, x: 0, y: -16), identity: Arrive(opacity: 1, scale: 1, x: 0, y: 0))
                .animation(Theme.bouncy),
            removal: .scale(scale: 0.7).combined(with: .opacity).animation(.easeIn(duration: 0.15))
        )
    }
}

// MARK: - Surfaces

extension View {
    /// The one card look: continuous corners, `surface` fill, lifted on hover.
    func cardSurface(hover: Bool = false, radius: CGFloat = Theme.Radius.control, raised: Bool = false) -> some View {
        background(RoundedRectangle(cornerRadius: radius, style: .continuous)
            .fill(Color.white.opacity((raised ? 0.08 : 0.05) + (hover ? Theme.hoverLift : 0))))
    }

    /// Fades the top and bottom edges of a scroll view so cut-off content reads as "more".
    func scrollEdgeFade(_ size: CGFloat = 10) -> some View {
        mask(VStack(spacing: 0) {
            LinearGradient(colors: [.clear, .black], startPoint: .top, endPoint: .bottom).frame(height: size)
            Color.black
            LinearGradient(colors: [.black, .clear], startPoint: .top, endPoint: .bottom).frame(height: size)
        })
    }
}

// MARK: - Buttons

/// Press feedback for everything tappable in the notch.
struct PressableStyle: ButtonStyle {
    var scale: CGFloat = 0.94
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? scale : 1)
            .opacity(configuration.isPressed ? 0.85 : 1)
            .animation(Theme.snappy, value: configuration.isPressed)
            .contentShape(Rectangle())
    }
}

/// Apple-style filled capsule: a light-to-deep gradient and a hairline highlight on top.
private struct AccentFill: View {
    var accent: Accent
    var hovering: Bool
    var body: some View {
        Capsule()
            .fill(accent.linear)
            .overlay(Capsule().fill(Color.white.opacity(hovering ? 0.08 : 0)))
            .overlay(Capsule().strokeBorder(LinearGradient(colors: [.white.opacity(0.45), .white.opacity(0)],
                                                           startPoint: .top, endPoint: .center), lineWidth: 0.5))
    }
}

/// Rounded pill button. `prominent` fills it with the accent.
struct PillButton: View {
    var title: String
    var icon: String? = nil
    var prominent: Bool = false
    var accent: Accent = .gold
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 5) {
                if let icon {
                    Image(systemName: icon).font(.system(size: 11, weight: .semibold))
                }
                Text(L(title)).font(Theme.font(Theme.Size.body, .medium)).lineLimit(1)
            }
            .padding(.horizontal, 12)
            .frame(height: 28)
            .foregroundStyle(prominent ? Theme.onAccent : Theme.ink)
            .background {
                if prominent {
                    AccentFill(accent: accent, hovering: hovering)
                } else {
                    Capsule().fill(Color.white.opacity(0.08 + (hovering ? Theme.hoverLift : 0)))
                }
            }
        }
        .buttonStyle(PressableStyle())
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

/// Round icon button. The glyph size and weight are fixed so neighbours always match.
struct IconButton: View {
    var icon: String
    var label: String
    var size: CGFloat = 26
    var tint: Color = Theme.ink
    var filled: Bool = false
    var accent: Accent = .gold
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: size >= 28 ? 12.5 : 11.5, weight: .semibold))
                .symbolRenderingMode(.hierarchical)
                .foregroundStyle(filled ? Theme.onAccent : tint)
                .frame(width: size, height: size)
                .background {
                    if filled {
                        Circle().fill(accent.linear)
                    } else {
                        Circle().fill(Color.white.opacity((hovering ? 0.12 : 0.07)))
                    }
                }
        }
        .buttonStyle(PressableStyle(scale: 0.88))
        .help(L(label))
        .accessibilityLabel(L(label))
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

/// Capsule chip for presets and segmented choices (no pop-up menus inside the notch).
struct Chip: View {
    var title: String
    var selected: Bool
    var accent: Accent = .gold
    var disabled: Bool = false
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(L(title))
                .font(Theme.font(Theme.Size.secondary, .medium))
                .foregroundStyle(selected ? Theme.onAccent : Theme.ink.opacity(disabled ? 0.35 : 0.85))
                .padding(.horizontal, 10)
                .frame(height: 24)
                .background {
                    if selected {
                        AccentFill(accent: accent, hovering: false)
                    } else {
                        Capsule().fill(Color.white.opacity(0.06 + (hovering && !disabled ? Theme.hoverLift : 0)))
                    }
                }
        }
        .buttonStyle(PressableStyle())
        .disabled(disabled)
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
        .animation(Theme.snappy, value: selected)
    }
}

/// A full-width settings row with a switch. VoiceOver reads it as a toggle.
struct ToggleRow: View {
    var title: String
    var subtitle: String? = nil
    var icon: String
    var accent: Accent = .gold
    @Binding var isOn: Bool
    @State private var hovering = false

    var body: some View {
        Button {
            withAnimation(Theme.snappy) { isOn.toggle() }
        } label: {
            HStack(spacing: 10) {
                Image(systemName: icon)
                    .font(.system(size: 12, weight: .medium))
                    .symbolRenderingMode(.hierarchical)
                    .foregroundStyle(isOn ? accent.deep : Theme.mist)
                    .frame(width: 18)
                VStack(alignment: .leading, spacing: 1) {
                    Text(L(title)).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink)
                    if let subtitle {
                        Text(L(subtitle)).font(Theme.font(Theme.Size.caption)).foregroundStyle(Theme.mist)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                Spacer(minLength: 6)
                NotchSwitch(isOn: isOn, accent: accent)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .frame(minHeight: 36)
            .cardSurface(hover: hovering)
        }
        .buttonStyle(PressableStyle(scale: 0.98))
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(L(title))
        .accessibilityValue(isOn ? L("On") : L("Off"))
        .accessibilityAddTraits(.isButton)
        .accessibilityHint(subtitle.map(L) ?? "")
    }
}

struct NotchSwitch: View {
    var isOn: Bool
    var accent: Accent = .gold
    var body: some View {
        ZStack(alignment: isOn ? .trailing : .leading) {
            Capsule().fill(isOn ? AnyShapeStyle(accent.linear) : AnyShapeStyle(Color.white.opacity(0.16)))
            Circle().fill(.white).padding(2).shadow(color: .black.opacity(0.25), radius: 1, y: 0.5)
        }
        .frame(width: 30, height: 18)
        .animation(Theme.snappy, value: isOn)
    }
}

// MARK: - Live indicators

/// Five bars that bounce while something plays. 15 fps, and fully paused when not playing.
struct EqualizerBars: View {
    var playing: Bool
    var color: Color = Theme.gold
    var height: CGFloat = 12
    private static let phases: [Double] = [0.0, 0.3, 0.6, 0.15, 0.45]

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 15, paused: !playing)) { tl in
            let t = tl.date.timeIntervalSinceReferenceDate
            HStack(alignment: .center, spacing: 2) {
                ForEach(0..<5, id: \.self) { i in
                    let v = playing ? 0.3 + 0.7 * abs(sin((t * 1.1 + Self.phases[i]) * .pi)) : 0.25
                    Capsule().fill(color).frame(width: 2.4, height: max(2.5, height * v))
                }
            }
            .frame(height: height)
        }
        .accessibilityHidden(true)
    }
}

/// Fitness-style ring: a faint track and a gradient stroke in one hue.
struct ProgressRing: View {
    var progress: Double
    var lineWidth: CGFloat = 6
    var accent: Accent = .gold

    var body: some View {
        // Inset by half the stroke so the ring stays inside its frame (never clipped by a pane edge).
        ZStack {
            Circle().inset(by: lineWidth / 2).stroke(accent.deep.opacity(0.18), lineWidth: lineWidth)
            Circle().inset(by: lineWidth / 2)
                .trim(from: 0, to: max(0.001, min(1, progress)))
                .stroke(accent.ring, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
    }
}

/// Level bar for overlays; springs to its value.
struct LevelBar: View {
    var level: Double
    var accent: Accent = .gold

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.white.opacity(0.14))
                Capsule().fill(LinearGradient(colors: [accent.light, accent.deep], startPoint: .leading, endPoint: .trailing))
                    .frame(width: max(5, geo.size.width * max(0, min(1, level))))
            }
        }
        .frame(height: 5)
        .animation(.spring(response: 0.32, dampingFraction: 0.7), value: level)
    }
}

// MARK: - Text and structure

/// The one pane header: a small title, optional detail, actions on the right.
struct PaneHeader<Trailing: View>: View {
    var title: String
    var detail: String? = nil
    @ViewBuilder var trailing: () -> Trailing

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(L(title)).font(Theme.font(Theme.Size.secondary, .semibold)).foregroundStyle(Theme.ink)
            if let detail {
                Text(L(detail)).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist).lineLimit(1)
            }
            Spacer(minLength: 4)
            trailing()
        }
        .frame(height: 26)
    }
}

extension PaneHeader where Trailing == EmptyView {
    init(title: String, detail: String? = nil) {
        self.init(title: title, detail: detail) { EmptyView() }
    }
}

/// Small text button with a padded hit area.
struct TextButton: View {
    var title: String
    var tint: Color = Theme.gold
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(L(title))
                .font(Theme.font(Theme.Size.secondary, .medium))
                .foregroundStyle(tint)
                .padding(.horizontal, 6)
                .frame(minHeight: 24)
                .background(Capsule().fill(Color.white.opacity(hovering ? 0.08 : 0)))
        }
        .buttonStyle(PressableStyle())
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

/// Empty state with a still lion.
struct EmptyHint: View {
    var title: String
    var subtitle: String
    var mood: LionMood = .awake
    var body: some View {
        HStack(spacing: 12) {
            LionMascot(size: 40, mood: mood, animated: false)
            VStack(alignment: .leading, spacing: 2) {
                Text(L(title)).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
                Text(L(subtitle)).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// Text field styled for the dark notch.
struct NotchField: View {
    var placeholder: String
    @Binding var text: String
    var onSubmit: () -> Void

    var body: some View {
        TextField("", text: $text, prompt: Text(L(placeholder)).foregroundColor(Theme.faint))
            .textFieldStyle(.plain)
            .font(Theme.font(Theme.Size.body))
            .foregroundStyle(Theme.ink)
            .padding(.horizontal, 10)
            .frame(height: 28)
            .cardSurface(raised: true)
            .onSubmit(onSubmit)
    }
}

/// The floating message at the bottom of the open notch, with an optional action.
struct ToastBar: View {
    var toast: Toast
    var dismiss: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            Text(L(toast.text))
                .font(Theme.font(Theme.Size.secondary, .medium))
                .foregroundStyle(Theme.ink)
                .lineLimit(1)
            if let title = toast.actionTitle, let action = toast.action {
                Button {
                    action()
                    dismiss()
                } label: {
                    Text(L(title)).font(Theme.font(Theme.Size.secondary, .semibold)).foregroundStyle(Theme.gold)
                        .padding(.horizontal, 6).frame(minHeight: 22)
                }
                .buttonStyle(PressableStyle())
            }
        }
        .padding(.horizontal, 14)
        .frame(height: 30)
        .background(Capsule().fill(Color(white: 0.16)))
        .overlay(Capsule().strokeBorder(Theme.hairline, lineWidth: 0.5))
        .shadow(color: .black.opacity(0.5), radius: 10, y: 4)
    }
}

// MARK: - Motion helpers

/// Springs a view in from small the first time it appears.
struct PopIn: ViewModifier {
    var delay: Double = 0
    @State private var shown = false

    func body(content: Content) -> some View {
        content
            .scaleEffect(shown ? 1 : 0.5)
            .opacity(shown ? 1 : 0)
            .onAppear {
                withAnimation(Theme.bouncy.delay(delay)) { shown = true }
            }
    }
}

extension View {
    func popIn(delay: Double = 0) -> some View { modifier(PopIn(delay: delay)) }

    /// SF Symbol bounce on `value` change where macOS supports it (14+).
    @ViewBuilder
    func symbolBounce<V: Equatable>(value: V) -> some View {
        if #available(macOS 14.0, *) {
            symbolEffect(.bounce, value: value)
        } else {
            self
        }
    }
}

extension Accent {
    /// Ezkielyna gold for the app's own prominent controls.
    static let gold = Accent(light: Color(hex: 0xFFE3B5), deep: Color(hex: 0xFFC46B))
}
