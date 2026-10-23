import AppKit
import SwiftUI

// MARK: - Transitions

/// Blur + fade + slight scale, used for content that appears inside the notch.
private struct BlurFade: ViewModifier {
    var blur: CGFloat
    var opacity: Double
    var scale: CGFloat
    var x: CGFloat
    func body(content: Content) -> some View {
        content
            .blur(radius: blur)
            .opacity(opacity)
            .scaleEffect(scale, anchor: .top)
            .offset(x: x)
    }
}

extension AnyTransition {
    static var notchContent: AnyTransition {
        .asymmetric(
            insertion: .modifier(active: BlurFade(blur: 8, opacity: 0, scale: 0.94, x: 0),
                                 identity: BlurFade(blur: 0, opacity: 1, scale: 1, x: 0))
                .animation(Theme.spring.delay(0.09)),
            removal: .modifier(active: BlurFade(blur: 6, opacity: 0, scale: 0.97, x: 0),
                               identity: BlurFade(blur: 0, opacity: 1, scale: 1, x: 0))
                .animation(.easeOut(duration: 0.12))
        )
    }

    static func tabSlide(forward: Bool) -> AnyTransition {
        let dx: CGFloat = forward ? 28 : -28
        return .asymmetric(
            insertion: .modifier(active: BlurFade(blur: 6, opacity: 0, scale: 1, x: dx),
                                 identity: BlurFade(blur: 0, opacity: 1, scale: 1, x: 0)),
            removal: .modifier(active: BlurFade(blur: 6, opacity: 0, scale: 1, x: -dx * 0.6),
                               identity: BlurFade(blur: 0, opacity: 1, scale: 1, x: 0))
        )
    }

    /// Shelf cards "fall" into place with an overshoot, like Nusa's shelf.
    static var fallIn: AnyTransition {
        .asymmetric(
            insertion: .modifier(active: FallModifier(progress: 0), identity: FallModifier(progress: 1))
                .animation(.spring(response: 0.55, dampingFraction: 0.55)),
            removal: .scale(scale: 0.6).combined(with: .opacity).animation(.easeIn(duration: 0.18))
        )
    }
}

private struct FallModifier: ViewModifier {
    var progress: CGFloat
    func body(content: Content) -> some View {
        content
            .offset(y: (1 - progress) * -34)
            .scaleEffect(0.7 + 0.3 * progress)
            .opacity(Double(min(1, progress * 2.5)))
    }
}

// MARK: - Buttons

/// Press-scale + hover-highlight style for every tappable thing in the notch.
struct PressableStyle: ButtonStyle {
    var scale: CGFloat = 0.92
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? scale : 1)
            .brightness(configuration.isPressed ? -0.06 : 0)
            .animation(Theme.snappy, value: configuration.isPressed)
            .contentShape(Rectangle())
    }
}

/// Rounded pill button. `prominent` uses the lion gradient.
struct PillButton: View {
    var title: String
    var icon: String? = nil
    var prominent: Bool = false
    var tint: Color = Theme.gold
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                if let icon {
                    Image(systemName: icon).font(.system(size: 11, weight: .bold))
                }
                Text(title).font(Theme.rounded(12, .semibold)).lineLimit(1)
            }
            .padding(.horizontal, 13)
            .frame(height: 30)
            .foregroundStyle(prominent ? Theme.navy : Theme.ink)
            .background {
                Capsule().fill(prominent ? AnyShapeStyle(LinearGradient(colors: [tint, tint.opacity(0.82)], startPoint: .top, endPoint: .bottom))
                                         : AnyShapeStyle(Color.white.opacity(hovering ? 0.14 : 0.08)))
            }
            .overlay(Capsule().strokeBorder(Color.white.opacity(prominent ? 0.25 : 0.06), lineWidth: 0.6))
            .shadow(color: prominent ? tint.opacity(hovering ? 0.55 : 0.3) : .clear, radius: hovering ? 10 : 6)
        }
        .buttonStyle(PressableStyle())
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
    }
}

/// Small round icon button.
struct IconButton: View {
    var icon: String
    var size: CGFloat = 28
    var tint: Color = Theme.ink
    var filled: Bool = false
    var help: String = ""
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: size * 0.42, weight: .bold))
                .foregroundStyle(filled ? Theme.navy : tint)
                .frame(width: size, height: size)
                .background(Circle().fill(filled ? AnyShapeStyle(Theme.lionGradient) : AnyShapeStyle(Color.white.opacity(hovering ? 0.16 : 0.08))))
                .scaleEffect(hovering ? 1.06 : 1)
        }
        .buttonStyle(PressableStyle(scale: 0.86))
        .help(help)
        .onHover { h in withAnimation(Theme.snappy) { hovering = h } }
    }
}

/// Capsule chip used for presets and segmented choices (no popup menus inside the notch).
struct Chip: View {
    var title: String
    var selected: Bool
    var tint: Color = Theme.gold
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(Theme.rounded(11.5, .semibold))
                .foregroundStyle(selected ? tint : Theme.ink.opacity(0.75))
                .padding(.horizontal, 11)
                .frame(height: 26)
                .background(Capsule().fill(selected ? tint.opacity(0.16) : Color.white.opacity(hovering ? 0.12 : 0.06)))
                .overlay(Capsule().strokeBorder(selected ? tint.opacity(0.55) : .clear, lineWidth: 1))
        }
        .buttonStyle(PressableStyle())
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
        .animation(Theme.snappy, value: selected)
    }
}

/// Toggle row with a custom animated switch.
struct ToggleRow: View {
    var title: String
    var subtitle: String? = nil
    var icon: String
    @Binding var isOn: Bool

    var body: some View {
        Button {
            withAnimation(Theme.snappy) { isOn.toggle() }
        } label: {
            HStack(spacing: 9) {
                Image(systemName: icon)
                    .font(.system(size: 11, weight: .bold))
                    .foregroundStyle(isOn ? Theme.gold : Theme.mist)
                    .frame(width: 24, height: 24)
                    .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(0.07)))
                VStack(alignment: .leading, spacing: 1) {
                    Text(title).font(Theme.rounded(12, .semibold)).foregroundStyle(Theme.ink)
                    if let subtitle {
                        Text(subtitle).font(Theme.rounded(9.5)).foregroundStyle(Theme.mist).lineLimit(1)
                    }
                }
                Spacer(minLength: 4)
                NotchSwitch(isOn: isOn)
            }
            .padding(.horizontal, 9)
            .frame(height: 38)
            .background(RoundedRectangle(cornerRadius: 11).fill(Theme.surface))
        }
        .buttonStyle(PressableStyle(scale: 0.97))
    }
}

struct NotchSwitch: View {
    var isOn: Bool
    var body: some View {
        ZStack(alignment: isOn ? .trailing : .leading) {
            Capsule().fill(isOn ? AnyShapeStyle(Theme.lionGradient) : AnyShapeStyle(Color.white.opacity(0.16)))
            Circle().fill(.white).padding(2).shadow(color: .black.opacity(0.3), radius: 1, y: 1)
        }
        .frame(width: 30, height: 18)
        .animation(Theme.snappy, value: isOn)
    }
}

// MARK: - Live indicators

/// Five bouncing bars (Nusa `eq` keyframes).
struct EqualizerBars: View {
    var playing: Bool
    var color: Color = Theme.gold
    var height: CGFloat = 12

    var body: some View {
        TimelineView(.animation(minimumInterval: 1 / 30, paused: !playing)) { tl in
            let t = tl.date.timeIntervalSinceReferenceDate
            HStack(alignment: .center, spacing: 2) {
                ForEach(0..<5, id: \.self) { i in
                    let phase = [0.0, 0.3, 0.6, 0.15, 0.45][i]
                    let v = playing ? 0.3 + 0.7 * abs(sin((t * 1.1 + phase) * .pi)) : 0.25
                    Capsule().fill(color).frame(width: 2.4, height: max(2.5, height * v))
                }
            }
            .frame(height: height)
        }
    }
}

/// Circular progress ring with a glowing lion gradient.
struct GlowRing: View {
    var progress: Double
    var lineWidth: CGFloat = 8
    var colors: [Color] = [Theme.gold, Theme.orange, Theme.ember, Theme.gold]
    var glow: Color = Theme.orange

    var body: some View {
        ZStack {
            Circle().stroke(Color.white.opacity(0.08), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: max(0.001, progress))
                .stroke(AngularGradient(colors: colors, center: .center), style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .shadow(color: glow.opacity(0.55), radius: lineWidth * 0.9)
        }
    }
}

/// Section label in the gold small-caps style.
struct SectionLabel: View {
    var text: String
    var body: some View {
        Text(text.uppercased())
            .font(Theme.rounded(9.5, .heavy))
            .tracking(1.4)
            .foregroundStyle(Theme.gold.opacity(0.85))
    }
}

/// Empty state with the lion.
struct EmptyHint: View {
    var title: String
    var subtitle: String
    var mood: LionMood = .awake
    var body: some View {
        HStack(spacing: 14) {
            LionMascot(size: 54, mood: mood)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(Theme.rounded(14, .bold)).foregroundStyle(Theme.ink)
                Text(subtitle).font(Theme.rounded(11)).foregroundStyle(Theme.mist).fixedSize(horizontal: false, vertical: true)
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
        TextField("", text: $text, prompt: Text(placeholder).foregroundColor(Theme.mist))
            .textFieldStyle(.plain)
            .font(Theme.rounded(12.5, .medium))
            .foregroundStyle(Theme.ink)
            .padding(.horizontal, 11)
            .frame(height: 30)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.07)))
            .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Theme.hairline, lineWidth: 1))
            .onSubmit(onSubmit)
    }
}
