import NotchCore
import SwiftUI

/// Ezkiel's lion eyes for the closed notch: amber irises with round pupils under one brow ridge, drawn as
/// a single face so they always move together. They blink now and then, open a little wider when you hover,
/// close for a nap, squint when happy, narrow during focus, glance side to side while an AI agent works and
/// glow amber when it needs you. Every change is a spring, so nothing jumps; between moves nothing redraws.
/// The other styles (sparkly anime eyes, a cat's, cute round ones, robot LEDs) share all of that.
struct EzkielEyes: View {
    enum State: Equatable { case awake, happy, sleeping, focused, working, waiting }

    var state: State
    var hovering: Bool
    var height: CGFloat = 10
    var style: EyeStyle = .lion

    /// 1 = open, 0 = shut; the blink multiplies the state's own openness.
    @SwiftUI.State private var lid: CGFloat = 1
    /// -1 … 1: where both pupils look.
    @SwiftUI.State private var look: CGFloat = 0
    /// 0 … 1: the anime eyes' star flaring now and then.
    @SwiftUI.State private var twinkle: CGFloat = 0

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
        canvas
            .frame(width: height * 3.2, height: height * 1.5)
            .animation(.spring(response: 0.42, dampingFraction: 0.82), value: state)
            .animation(.spring(response: 0.3, dampingFraction: 0.8), value: hovering)
            .task(id: TaskKey(state: state, style: style)) { await life() }
            .accessibilityHidden(true)
    }

    private struct TaskKey: Equatable {
        var state: State
        var style: EyeStyle
    }

    @ViewBuilder
    private var canvas: some View {
        let glow: CGFloat = state == .waiting ? 1 : 0, smile: CGFloat = state == .happy ? 1 : 0
        if style == .lion {
            LionEyesCanvas(open: openness * lid, look: look, glow: glow, smile: smile)
        } else {
            StyledEyesCanvas(style: style, open: openness * lid, look: look, glow: glow, smile: smile,
                             twinkle: twinkle, wide: hovering || state == .waiting ? 1 : 0)
        }
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
            if style == .anime { await sparkle() }
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
        if style == .anime {
            // Anime eyes blink slowly and softly, as in the clip they come from.
            withAnimation(.easeInOut(duration: 0.35)) { lid = 0.06 }
            try? await Task.sleep(nanoseconds: 470_000_000)
            withAnimation(.easeInOut(duration: 0.45)) { lid = 1 }
            try? await Task.sleep(nanoseconds: 450_000_000)
            return
        }
        withAnimation(.easeIn(duration: 0.08)) { lid = 0.06 }
        try? await Task.sleep(nanoseconds: 95_000_000)
        withAnimation(.spring(response: 0.22, dampingFraction: 0.78)) { lid = 1 }
    }

    /// The star in each pupil flares and turns a little, then settles.
    @MainActor
    private func sparkle() async {
        withAnimation(.easeOut(duration: 0.35)) { twinkle = 1 }
        try? await Task.sleep(nanoseconds: 380_000_000)
        withAnimation(.easeInOut(duration: 0.7)) { twinkle = 0 }
    }
}

/// The anime, cat, cute and robot eyes. Same lids, look, glow and smile as the lion's, so moods read the same.
private struct StyledEyesCanvas: View, Animatable {
    var style: EyeStyle
    var open: CGFloat
    var look: CGFloat
    var glow: CGFloat
    var smile: CGFloat
    var twinkle: CGFloat
    var wide: CGFloat

    var animatableData: AnimatablePair<AnimatablePair<AnimatablePair<CGFloat, CGFloat>, AnimatablePair<CGFloat, CGFloat>>,
                                       AnimatablePair<CGFloat, CGFloat>> {
        get { AnimatablePair(AnimatablePair(AnimatablePair(open, look), AnimatablePair(glow, smile)), AnimatablePair(twinkle, wide)) }
        set {
            open = newValue.first.first.first
            look = newValue.first.first.second
            glow = newValue.first.second.first
            smile = newValue.first.second.second
            twinkle = newValue.second.first
            wide = newValue.second.second
        }
    }

    var body: some View {
        Canvas { ctx, size in
            let w = size.width, h = size.height
            let centers = [CGPoint(x: w * 0.27, y: h * 0.56), CGPoint(x: w * 0.73, y: h * 0.56)]
            let o = max(0, min(1.15, open))
            for (i, c) in centers.enumerated() {
                let side: CGFloat = i == 0 ? -1 : 1
                switch style {
                case .anime: anime(ctx, c, side: side, o: o, h: h, w: w)
                case .cat: cat(ctx, c, side: side, o: o, h: h, w: w)
                case .cute: cute(ctx, c, o: o, h: h, w: w)
                case .robot, .lion: robot(ctx, c, o: o, h: h, w: w)
                }
            }
        }
    }

    // MARK: Anime: a big pink-to-plum iris, a twinkling white star, lashes, blush

    private static let animePink = Color(red: 1.0, green: 0.62, blue: 0.86)
    private static let animeMagenta = Color(red: 0.86, green: 0.2, blue: 0.66)
    private static let animePlum = Color(red: 0.33, green: 0.07, blue: 0.3)
    private static let lash = Color(red: 0.16, green: 0.05, blue: 0.12)

    private func anime(_ ctx: GraphicsContext, _ c: CGPoint, side: CGFloat, o: CGFloat, h: CGFloat, w: CGFloat) {
        let eyeW = w * 0.36, eyeH = h * 0.8
        let left = CGPoint(x: c.x - eyeW / 2, y: c.y + eyeH * 0.1)
        let right = CGPoint(x: c.x + eyeW / 2, y: c.y + eyeH * 0.1)
        let outer = side < 0 ? left : right
        let top = eyeH * 0.78 * o
        let bottom = eyeH * (0.42 * o + 0.04) * (1 - smile * 0.85)

        // Blush under each eye, warmer when happy.
        let blush = CGRect(x: c.x - eyeW * 0.45 + side * eyeW * 0.12, y: c.y + eyeH * 0.42, width: eyeW * 0.9, height: eyeH * 0.32)
        ctx.drawLayer { layer in
            layer.addFilter(.blur(radius: h * 0.08))
            layer.fill(Path(ellipseIn: blush), with: .color(Self.animePink.opacity(0.28 + 0.25 * smile)))
        }

        guard o > 0.12 else {
            // Shut: a soft downward curve with three little lashes.
            var line = Path()
            line.move(to: left)
            line.addQuadCurve(to: right, control: CGPoint(x: c.x, y: c.y + eyeH * 0.42))
            ctx.stroke(line, with: .color(Self.lash), style: StrokeStyle(lineWidth: max(1, h * 0.09), lineCap: .round))
            for t in [0.3, 0.55, 0.8] as [CGFloat] {
                let x = side < 0 ? c.x - eyeW / 2 + eyeW * (1 - t) : c.x - eyeW / 2 + eyeW * t
                let y = c.y + eyeH * 0.05 + eyeH * 0.37 * (1 - pow(2 * t - 1, 2)) * 0.5
                var l = Path()
                l.move(to: CGPoint(x: x, y: y))
                l.addLine(to: CGPoint(x: x + side * eyeW * 0.06, y: y + eyeH * 0.18))
                ctx.stroke(l, with: .color(Self.lash), style: StrokeStyle(lineWidth: max(0.7, h * 0.05), lineCap: .round))
            }
            return
        }

        var eye = Path()
        eye.move(to: left)
        eye.addCurve(to: right, control1: CGPoint(x: left.x + eyeW * 0.08, y: c.y - top), control2: CGPoint(x: right.x - eyeW * 0.08, y: c.y - top))
        eye.addQuadCurve(to: left, control: CGPoint(x: c.x, y: c.y + bottom * 1.35 - smile * eyeH * 0.4))
        eye.closeSubpath()

        let irisR = eyeH * 0.48
        let irisC = CGPoint(x: c.x + look * eyeW * 0.14, y: c.y + eyeH * 0.02)
        ctx.drawLayer { layer in
            layer.clip(to: eye)
            layer.fill(Path(eye.boundingRect.insetBy(dx: -2, dy: -2)), with: .color(.white.opacity(0.92)))
            let iris = Path(ellipseIn: CGRect(x: irisC.x - irisR * 0.9, y: irisC.y - irisR, width: irisR * 1.8, height: irisR * 2))
            // Darker at the top under the lashes, glowing pink at the bottom, as anime irises are.
            layer.fill(iris, with: .linearGradient(Gradient(colors: [Self.animePlum, Self.animeMagenta, Self.animePink]),
                                                   startPoint: CGPoint(x: irisC.x, y: irisC.y - irisR),
                                                   endPoint: CGPoint(x: irisC.x, y: irisC.y + irisR)))
            layer.stroke(iris, with: .color(Self.animePlum.opacity(0.9)), lineWidth: max(0.6, h * 0.04))
            let pupil = CGRect(x: irisC.x - irisR * 0.3, y: irisC.y - irisR * 0.38, width: irisR * 0.6, height: irisR * 0.76)
            layer.fill(Path(ellipseIn: pupil), with: .color(Self.animePlum))
            // The four-point star, flaring and turning when it twinkles, and a small round catchlight.
            let starR = irisR * (0.5 + 0.22 * twinkle)
            let star = Self.star(center: CGPoint(x: irisC.x - irisR * 0.18, y: irisC.y - irisR * 0.22), radius: starR,
                                 angle: .degrees(Double(twinkle) * 20))
            layer.fill(star, with: .color(.white))
            let dot = irisR * 0.13
            layer.fill(Path(ellipseIn: CGRect(x: irisC.x + irisR * 0.32 - dot, y: irisC.y + irisR * 0.42 - dot, width: dot * 2, height: dot * 2)),
                       with: .color(.white.opacity(0.85)))
        }
        if glow > 0.01 {
            ctx.drawLayer { layer in
                layer.addFilter(.blur(radius: h * 0.16))
                layer.stroke(eye, with: .color(Self.animePink.opacity(0.8 * glow)), lineWidth: h * 0.12)
            }
        }
        // The thick upper lash line, with two flicks at the outer corner; a fine line below.
        var upper = Path()
        upper.move(to: left)
        upper.addCurve(to: right, control1: CGPoint(x: left.x + eyeW * 0.08, y: c.y - top), control2: CGPoint(x: right.x - eyeW * 0.08, y: c.y - top))
        ctx.stroke(upper, with: .color(Self.lash), style: StrokeStyle(lineWidth: max(1.2, h * 0.14), lineCap: .round))
        for (dx, dy) in [(0.16, -0.32), (0.2, -0.08)] as [(CGFloat, CGFloat)] {
            var flick = Path()
            flick.move(to: CGPoint(x: outer.x - side * eyeW * 0.04, y: outer.y - eyeH * 0.1))
            flick.addQuadCurve(to: CGPoint(x: outer.x + side * eyeW * dx, y: outer.y + eyeH * dy * o),
                               control: CGPoint(x: outer.x + side * eyeW * dx * 0.4, y: outer.y - eyeH * 0.08))
            ctx.stroke(flick, with: .color(Self.lash), style: StrokeStyle(lineWidth: max(0.8, h * 0.07), lineCap: .round))
        }
        var lower = Path()
        lower.move(to: CGPoint(x: left.x + eyeW * 0.12, y: left.y + bottom * 0.5))
        lower.addQuadCurve(to: CGPoint(x: right.x - eyeW * 0.12, y: right.y + bottom * 0.5),
                           control: CGPoint(x: c.x, y: c.y + bottom * 1.3 - smile * eyeH * 0.4))
        ctx.stroke(lower, with: .color(Self.animeMagenta.opacity(0.55)), style: StrokeStyle(lineWidth: max(0.5, h * 0.035), lineCap: .round))

        // A tiny sparkle off the outer corner, brightest as the star twinkles.
        let sparkle = Self.star(center: CGPoint(x: outer.x + side * eyeW * 0.2, y: c.y - eyeH * 0.55), radius: h * (0.09 + 0.07 * twinkle))
        ctx.fill(sparkle, with: .color(.white.opacity(0.35 + 0.6 * twinkle)))
    }

    /// A four-point star with concave sides.
    static func star(center: CGPoint, radius r: CGFloat, angle: Angle = .zero) -> Path {
        var p = Path()
        for k in 0..<4 {
            let a = angle.radians + Double(k) * .pi / 2 - .pi / 2
            let tip = CGPoint(x: center.x + r * CGFloat(cos(a)), y: center.y + r * CGFloat(sin(a)))
            let mid = a + .pi / 4
            let pinch = CGPoint(x: center.x + r * 0.16 * CGFloat(cos(mid)), y: center.y + r * 0.16 * CGFloat(sin(mid)))
            let next = a + .pi / 2
            let nextTip = CGPoint(x: center.x + r * CGFloat(cos(next)), y: center.y + r * CGFloat(sin(next)))
            if k == 0 { p.move(to: tip) }
            p.addQuadCurve(to: nextTip, control: pinch)
        }
        p.closeSubpath()
        return p
    }

    // MARK: Cat: green-gold almond eyes, a slit pupil that widens when you come close

    private func cat(_ ctx: GraphicsContext, _ c: CGPoint, side: CGFloat, o: CGFloat, h: CGFloat, w: CGFloat) {
        let eyeW = w * 0.32, eyeH = h * 0.64
        let inner = CGPoint(x: c.x - side * eyeW / 2, y: c.y + eyeH * 0.12)
        let outer = CGPoint(x: c.x + side * eyeW / 2, y: c.y - eyeH * 0.18)
        let left = side < 0 ? outer : inner, right = side < 0 ? inner : outer
        let lime = Color(red: 0.74, green: 0.9, blue: 0.33), amber = Color(red: 0.9, green: 0.7, blue: 0.2)
        guard o > 0.12 else {
            var line = Path()
            line.move(to: left)
            line.addQuadCurve(to: right, control: CGPoint(x: c.x, y: c.y + eyeH * 0.3))
            ctx.stroke(line, with: .color(lime.opacity(0.85)), style: StrokeStyle(lineWidth: max(1, h * 0.08), lineCap: .round))
            return
        }
        let top = eyeH * 0.95 * o, bottom = eyeH * (0.6 * o + 0.06) * (1 - smile * 0.9)
        var almond = Path()
        almond.move(to: left)
        almond.addQuadCurve(to: right, control: CGPoint(x: c.x, y: c.y - top))
        almond.addQuadCurve(to: left, control: CGPoint(x: c.x, y: c.y + bottom - smile * eyeH * 0.5))
        almond.closeSubpath()
        ctx.drawLayer { layer in
            layer.clip(to: almond)
            let irisC = CGPoint(x: c.x + look * eyeW * 0.14, y: c.y - eyeH * 0.03)
            layer.fill(Path(almond.boundingRect.insetBy(dx: -2, dy: -2)),
                       with: .radialGradient(Gradient(colors: [amber, lime, Color(red: 0.3, green: 0.45, blue: 0.1)]),
                                             center: irisC, startRadius: 0, endRadius: eyeW * 0.6))
            let slitW = eyeH * (0.16 + 0.34 * wide), slitH = eyeH * 0.95
            layer.fill(Path(ellipseIn: CGRect(x: irisC.x - slitW / 2, y: irisC.y - slitH / 2, width: slitW, height: slitH)), with: .color(.black))
            let shine = eyeH * 0.12
            layer.fill(Path(ellipseIn: CGRect(x: irisC.x - eyeW * 0.2, y: irisC.y - eyeH * 0.32, width: shine * 2, height: shine * 2)),
                       with: .color(.white.opacity(0.8)))
        }
        if glow > 0.01 {
            ctx.drawLayer { layer in
                layer.addFilter(.blur(radius: h * 0.18))
                layer.stroke(almond, with: .color(lime.opacity(0.8 * glow)), lineWidth: h * 0.12)
            }
        }
        ctx.stroke(almond, with: .color(Color(red: 0.05, green: 0.08, blue: 0.02)), lineWidth: max(1, h * 0.08))
    }

    // MARK: Cute: big shiny black eyes and a blush

    private func cute(_ ctx: GraphicsContext, _ c: CGPoint, o: CGFloat, h: CGFloat, w: CGFloat) {
        let r = h * 0.4
        let blush = CGRect(x: c.x - r * 1.1, y: c.y + r * 0.75, width: r * 2.2, height: r * 0.7)
        ctx.drawLayer { layer in
            layer.addFilter(.blur(radius: h * 0.07))
            layer.fill(Path(ellipseIn: blush), with: .color(Color(red: 1, green: 0.5, blue: 0.6).opacity(0.3 + 0.25 * smile)))
        }
        let cream = Color(red: 0.98, green: 0.95, blue: 0.9)
        if smile > 0.5 || o <= 0.12 {
            // Happy: ^ ^; asleep: ‿ ‿
            var arc = Path()
            let up = smile > 0.5
            arc.move(to: CGPoint(x: c.x - r * 0.8, y: c.y + (up ? r * 0.2 : -r * 0.1)))
            arc.addQuadCurve(to: CGPoint(x: c.x + r * 0.8, y: c.y + (up ? r * 0.2 : -r * 0.1)),
                             control: CGPoint(x: c.x, y: c.y + (up ? -r * 0.9 : r * 0.6)))
            ctx.stroke(arc, with: .color(cream), style: StrokeStyle(lineWidth: max(1.1, h * 0.1), lineCap: .round))
            return
        }
        let cx = c.x + look * r * 0.3
        let eye = CGRect(x: cx - r, y: c.y - r * o, width: r * 2, height: r * 2 * o)
        ctx.fill(Path(ellipseIn: eye), with: .color(Color(red: 0.06, green: 0.05, blue: 0.08)))
        ctx.stroke(Path(ellipseIn: eye), with: .color(cream.opacity(0.85)), lineWidth: max(0.7, h * 0.05))
        if glow > 0.01 {
            ctx.drawLayer { layer in
                layer.addFilter(.blur(radius: h * 0.16))
                layer.stroke(Path(ellipseIn: eye), with: .color(Color(red: 1, green: 0.75, blue: 0.4).opacity(0.8 * glow)), lineWidth: h * 0.12)
            }
        }
        let big = r * 0.36, small = r * 0.16
        ctx.fill(Path(ellipseIn: CGRect(x: cx - r * 0.5, y: c.y - r * 0.6 * o, width: big * 2, height: big * 2 * o)), with: .color(.white))
        ctx.fill(Path(ellipseIn: CGRect(x: cx + r * 0.3, y: c.y + r * 0.25 * o, width: small * 2, height: small * 2 * o)),
                 with: .color(.white.opacity(0.85)))
    }

    // MARK: Robot: two neon bars that collapse to blink and sweep while an agent works

    private func robot(_ ctx: GraphicsContext, _ c: CGPoint, o: CGFloat, h: CGFloat, w: CGFloat) {
        let neon = glow > 0.5 ? Color(red: 1, green: 0.72, blue: 0.25) : Color(red: 0.24, green: 0.95, blue: 1)
        let barW = w * 0.26, barH = max(h * 0.1, h * 0.62 * o)
        let x = c.x + look * w * 0.035
        var bar: Path
        if smile > 0.5 {
            // Happy: ^ ^ chevrons.
            bar = Path()
            bar.move(to: CGPoint(x: x - barW / 2, y: c.y + h * 0.14))
            bar.addLine(to: CGPoint(x: x, y: c.y - h * 0.16))
            bar.addLine(to: CGPoint(x: x + barW / 2, y: c.y + h * 0.14))
            ctx.drawLayer { layer in
                layer.addFilter(.blur(radius: h * 0.12))
                layer.stroke(bar, with: .color(neon.opacity(0.9)), style: StrokeStyle(lineWidth: h * 0.18, lineCap: .round, lineJoin: .round))
            }
            ctx.stroke(bar, with: .color(neon), style: StrokeStyle(lineWidth: max(1.2, h * 0.11), lineCap: .round, lineJoin: .round))
            return
        }
        let rect = CGRect(x: x - barW / 2, y: c.y - barH / 2, width: barW, height: barH)
        bar = Path(roundedRect: rect, cornerRadius: min(barH, barW) / 2.4, style: .continuous)
        ctx.drawLayer { layer in
            layer.addFilter(.blur(radius: h * 0.14))
            layer.fill(bar, with: .color(neon.opacity(0.75)))
        }
        ctx.fill(bar, with: .color(neon))
        // Scan lines across the LED, and a brighter band that follows the look (it sweeps while an agent works).
        ctx.drawLayer { layer in
            layer.clip(to: bar)
            let band = rect.minX + rect.width * (0.5 + look * 0.5) - rect.width * 0.15
            layer.fill(Path(CGRect(x: band, y: rect.minY, width: rect.width * 0.3, height: rect.height)), with: .color(.white.opacity(0.55)))
            var y = rect.minY + h * 0.06
            while y < rect.maxY {
                layer.fill(Path(CGRect(x: rect.minX, y: y, width: rect.width, height: max(0.4, h * 0.03))), with: .color(.black.opacity(0.25)))
                y += h * 0.12
            }
        }
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
