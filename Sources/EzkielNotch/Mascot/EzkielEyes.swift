import SwiftUI

/// Ezkiel's lion eyes for the closed notch: amber irises with round pupils under one brow ridge, drawn as
/// a single face so they always move together. They blink now and then, open a little wider when you hover,
/// close for a nap, squint when happy, narrow during focus, glance side to side while an AI agent works and
/// glow amber when it needs you. Every change is a spring, so nothing jumps; between moves nothing redraws.
struct EzkielEyes: View {
    enum State: Equatable { case awake, happy, sleeping, focused, working, waiting }

    var state: State
    var hovering: Bool
    var height: CGFloat = 10

    /// 1 = open, 0 = shut; the blink multiplies the state's own openness.
    @SwiftUI.State private var lid: CGFloat = 1
    /// -1 … 1: where both pupils look.
    @SwiftUI.State private var look: CGFloat = 0

    private var openness: CGFloat {
        switch state {
        case .sleeping: return 0.04
        case .happy: return 0.5
        case .focused: return 0.72
        case .waiting: return 1.08
        case .awake, .working: return hovering ? 1.08 : 1
        }
    }

    var body: some View {
        LionEyesCanvas(open: openness * lid, look: look,
                       glow: state == .waiting ? 1 : 0, smile: state == .happy ? 1 : 0)
            .frame(width: height * 3.2, height: height * 1.5)
            .animation(.spring(response: 0.42, dampingFraction: 0.82), value: state)
            .animation(.spring(response: 0.3, dampingFraction: 0.8), value: hovering)
            .task(id: state) { await life() }
            .accessibilityHidden(true)
    }

    /// Blinks (both eyes at once), and glances: slow side to side while an agent works, now and then otherwise.
    @MainActor
    private func life() async {
        withAnimation(.spring(response: 0.5, dampingFraction: 0.85)) {
            lid = 1
            look = 0
        }
        guard state != .sleeping else { return }
        var beats = 0
        while !Task.isCancelled {
            try? await Task.sleep(nanoseconds: UInt64(Double.random(in: 2.4...4.8) * 1_000_000_000))
            if Task.isCancelled { return }
            beats += 1
            if state == .working {
                withAnimation(.easeInOut(duration: 0.9)) { look = look > 0 ? -0.75 : 0.75 }
            } else if beats % 3 == 0 {
                withAnimation(.easeInOut(duration: 0.7)) { look = CGFloat.random(in: -0.5...0.5) }
            }
            guard state != .happy else { continue }
            await blink()
            if Double.random(in: 0...1) < 0.2 {
                try? await Task.sleep(nanoseconds: 140_000_000)
                await blink()
            }
        }
    }

    @MainActor
    private func blink() async {
        withAnimation(.easeIn(duration: 0.08)) { lid = 0.06 }
        try? await Task.sleep(nanoseconds: 95_000_000)
        withAnimation(.spring(response: 0.22, dampingFraction: 0.78)) { lid = 1 }
    }
}

/// The drawing. Animatable, so springs move the lids and pupils smoothly frame by frame.
private struct LionEyesCanvas: View, Animatable {
    var open: CGFloat
    var look: CGFloat
    var glow: CGFloat
    var smile: CGFloat

    var animatableData: AnimatablePair<AnimatablePair<CGFloat, CGFloat>, AnimatablePair<CGFloat, CGFloat>> {
        get { AnimatablePair(AnimatablePair(open, look), AnimatablePair(glow, smile)) }
        set {
            open = newValue.first.first
            look = newValue.first.second
            glow = newValue.second.first
            smile = newValue.second.second
        }
    }

    var body: some View {
        Canvas { ctx, size in
            let w = size.width, h = size.height
            let eyeW = w * 0.34
            let eyeH = h * 0.62
            let midY = h * 0.6
            let centers = [CGPoint(x: w * 0.27, y: midY), CGPoint(x: w * 0.73, y: midY)]
            let gold = Color(red: 0.96, green: 0.70, blue: 0.24)
            let deep = Color(red: 0.72, green: 0.42, blue: 0.10)
            let rim = Color(red: 0.42, green: 0.24, blue: 0.06)

            // Brow ridge and nose bridge: one line that joins the eyes into a face.
            var brow = Path()
            brow.move(to: CGPoint(x: centers[0].x - eyeW * 0.62, y: midY - eyeH * 0.78))
            brow.addQuadCurve(to: CGPoint(x: w / 2, y: midY - eyeH * 0.35),
                              control: CGPoint(x: centers[0].x + eyeW * 0.1, y: midY - eyeH * 1.0))
            brow.addQuadCurve(to: CGPoint(x: centers[1].x + eyeW * 0.62, y: midY - eyeH * 0.78),
                              control: CGPoint(x: centers[1].x - eyeW * 0.1, y: midY - eyeH * 1.0))
            ctx.stroke(brow, with: .color(gold.opacity(0.22 + 0.2 * glow)), style: StrokeStyle(lineWidth: max(0.9, h * 0.06), lineCap: .round))
            // A short nose bridge, fading down from where the brows meet.
            var bridge = Path()
            bridge.move(to: CGPoint(x: w / 2, y: midY - eyeH * 0.35))
            bridge.addLine(to: CGPoint(x: w / 2, y: midY + eyeH * 0.05))
            ctx.stroke(bridge, with: .linearGradient(Gradient(colors: [gold.opacity(0.22 + 0.2 * glow), gold.opacity(0)]),
                                                     startPoint: CGPoint(x: w / 2, y: midY - eyeH * 0.35),
                                                     endPoint: CGPoint(x: w / 2, y: midY + eyeH * 0.05)),
                       style: StrokeStyle(lineWidth: max(0.9, h * 0.06), lineCap: .round))

            for (i, c) in centers.enumerated() {
                let side: CGFloat = i == 0 ? -1 : 1
                // Lion eyes slant: the outer corner sits a little higher than the inner one.
                let inner = CGPoint(x: c.x - side * eyeW / 2, y: c.y + eyeH * 0.08)
                let outer = CGPoint(x: c.x + side * eyeW / 2, y: c.y - eyeH * 0.1)
                let left = i == 0 ? outer : inner
                let right = i == 0 ? inner : outer
                let o = max(0, min(1.15, open))
                let top = eyeH * 0.95 * o
                let bottom = eyeH * (0.55 * o + 0.06) * (1 - smile * 0.9)

                var almond = Path()
                almond.move(to: left)
                almond.addQuadCurve(to: right, control: CGPoint(x: c.x, y: c.y - top))
                almond.addQuadCurve(to: left, control: CGPoint(x: c.x, y: c.y + bottom - smile * eyeH * 0.5))
                almond.closeSubpath()

                if o > 0.12 {
                    ctx.drawLayer { layer in
                        layer.clip(to: almond)
                        let irisR = eyeH * 0.74
                        let irisC = CGPoint(x: c.x + look * eyeW * 0.16, y: c.y - eyeH * 0.05)
                        let iris = Path(ellipseIn: CGRect(x: irisC.x - irisR, y: irisC.y - irisR, width: irisR * 2, height: irisR * 2))
                        // Lions show almost no white: the whole eye is amber, darker at the rim.
                        layer.fill(Path(almond.boundingRect.insetBy(dx: -2, dy: -2)), with: .color(rim))
                        layer.fill(iris, with: .radialGradient(Gradient(colors: [gold, deep]), center: irisC,
                                                                startRadius: 0, endRadius: irisR))
                        let pupilR = irisR * 0.28
                        layer.fill(Path(ellipseIn: CGRect(x: irisC.x - pupilR, y: irisC.y - pupilR, width: pupilR * 2, height: pupilR * 2)),
                                   with: .color(.black))
                        let shine = irisR * 0.17
                        layer.fill(Path(ellipseIn: CGRect(x: irisC.x - irisR * 0.42, y: irisC.y - irisR * 0.5, width: shine * 2, height: shine * 2)),
                                   with: .color(.white.opacity(0.85)))
                    }
                    if glow > 0.01 {
                        ctx.drawLayer { layer in
                            layer.addFilter(.blur(radius: h * 0.18))
                            layer.stroke(almond, with: .color(gold.opacity(0.8 * glow)), lineWidth: h * 0.12)
                        }
                    }
                    // Dark lining, as lions have.
                    ctx.stroke(almond, with: .color(Color(red: 0.12, green: 0.06, blue: 0.02)), lineWidth: max(1, h * 0.09))
                } else {
                    // Shut: just the lid line, curving down a little.
                    var line = Path()
                    line.move(to: left)
                    line.addQuadCurve(to: right, control: CGPoint(x: c.x, y: c.y + eyeH * 0.25))
                    ctx.stroke(line, with: .color(gold.opacity(0.85)), style: StrokeStyle(lineWidth: max(1, h * 0.08), lineCap: .round))
                }
            }
        }
    }
}
