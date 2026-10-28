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
            // Anime eyes are big; the rest share the lion's frame.
            .frame(width: height * (style == .anime ? 4.2 : 3.2), height: height * (style == .anime ? 2.3 : 1.5))
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

    // MARK: Anime: the clip's eyes. A round iris, black under the lid, violet, then glowing hot pink at the
    // bottom; a big white ✕ star with a pink halo; glitter; a heavy black lash line; a pink glow underneath.

    private static let animePink = Color(red: 1.0, green: 0.4, blue: 0.6)
    private static let animeRose = Color(red: 0.95, green: 0.22, blue: 0.45)
    private static let animeMagenta = Color(red: 0.86, green: 0.16, blue: 0.56)
    private static let animeViolet = Color(red: 0.42, green: 0.14, blue: 0.62)
    private static let animeNight = Color(red: 0.07, green: 0.02, blue: 0.1)
    /// Lashes a shade lighter than black, as the clip's catch the light (pure black would vanish in the notch).
    private static let lash = Color(red: 0.13, green: 0.08, blue: 0.15)
    /// Where the glitter sits in the iris (fractions of its radius), and how big each speck is.
    private static let glitter: [(x: CGFloat, y: CGFloat, r: CGFloat)] = [
        (-0.55, -0.4, 0.05), (0.48, -0.55, 0.045), (0.66, 0.08, 0.04), (-0.4, 0.58, 0.035),
        (0.3, 0.64, 0.045), (-0.7, 0.12, 0.03), (0.08, -0.72, 0.04), (-0.18, -0.5, 0.03),
        (0.58, 0.42, 0.03), (-0.62, -0.08, 0.025),
    ]

    private func anime(_ ctx: GraphicsContext, _ c: CGPoint, side: CGFloat, o: CGFloat, h: CGFloat, w: CGFloat) {
        // Drawn in the eye's own frame: u runs from the inner corner (-) to the outer one (+), v downward,
        // so the same drawing mirrors for the other eye.
        let W = w * 0.36, H = h * 0.62
        let centre = CGPoint(x: c.x, y: c.y + h * 0.02)
        func p(_ u: CGFloat, _ v: CGFloat) -> CGPoint { CGPoint(x: centre.x + side * u * W, y: centre.y + v * H) }
        let lift = o   // how open: the upper lid comes down as it falls
        let lower = 1 - smile * 0.8

        // The pink glow under each eye.
        ctx.drawLayer { layer in
            layer.addFilter(.blur(radius: h * 0.09))
            let a = p(-0.45, 0.45), b = p(0.55, 0.95)
            layer.fill(Path(ellipseIn: CGRect(x: min(a.x, b.x), y: a.y, width: abs(b.x - a.x), height: b.y - a.y)),
                       with: .color(Self.animeMagenta.opacity(0.5 + 0.2 * smile)))
        }

        let inner = p(-0.5, 0.12), outer = p(0.5, 0.06)
        guard o > 0.12 else {
            // Shut: the lash band curves down to a point, lashes pointing down.
            var band = Path()
            band.move(to: inner)
            band.addCurve(to: p(0.62, 0.14), control1: p(-0.2, 0.62), control2: p(0.3, 0.6))
            band.addCurve(to: inner, control1: p(0.3, 0.42), control2: p(-0.2, 0.44))
            ctx.fill(band, with: .color(Self.lash))
            for u in [-0.1, 0.12, 0.34] as [CGFloat] {
                var l = Path()
                l.move(to: p(u, 0.5))
                l.addLine(to: p(u + 0.06, 0.78))
                ctx.stroke(l, with: .color(Self.lash), style: StrokeStyle(lineWidth: max(0.6, h * 0.04), lineCap: .round))
            }
            return
        }

        // The opening: a sharp inner corner, the upper lid high and wide, the lower lid a gentle curve.
        let upperC1 = p(-0.3, 0.12 - 0.58 * lift), upperC2 = p(0.3, 0.06 - 0.64 * lift)
        let lowerC1 = p(0.3, 0.06 + 0.46 * lift * lower), lowerC2 = p(-0.25, 0.12 + 0.4 * lift * lower)
        var eye = Path()
        eye.move(to: inner)
        eye.addCurve(to: outer, control1: upperC1, control2: upperC2)
        eye.addCurve(to: inner, control1: lowerC1, control2: lowerC2)
        eye.closeSubpath()

        // A tall oval iris; its top hides under the lid.
        let irisC = p(0.04 + look * 0.12 * side, 0.08)
        let rx = W * 0.31, ry = H * 0.48
        let iris = Path(ellipseIn: CGRect(x: irisC.x - rx, y: irisC.y - ry, width: rx * 2, height: ry * 2))
        let glowC = CGPoint(x: irisC.x, y: irisC.y + ry * 0.35)
        ctx.drawLayer { layer in
            layer.clip(to: eye)
            // Lavender whites in the lid's shadow, darker under it.
            layer.fill(Path(eye.boundingRect.insetBy(dx: -2, dy: -2)),
                       with: .linearGradient(Gradient(colors: [Color(red: 0.42, green: 0.36, blue: 0.5), Color(red: 0.8, green: 0.75, blue: 0.88)]),
                                             startPoint: p(0, -0.5), endPoint: p(0, 0.5)))
            // The iris glows past its edge at the bottom.
            layer.fill(Path(ellipseIn: CGRect(x: glowC.x - rx * 1.6, y: glowC.y - ry * 0.6, width: rx * 3.2, height: ry * 1.4)),
                       with: .radialGradient(Gradient(colors: [Self.animePink.opacity(0.75), Self.animePink.opacity(0)]),
                                             center: glowC, startRadius: 0, endRadius: rx * 1.6))
            layer.fill(iris, with: .color(Self.animeNight))
            layer.fill(iris, with: .radialGradient(Gradient(stops: [
                .init(color: Self.animePink, location: 0),
                .init(color: Self.animeRose, location: 0.4),
                .init(color: Self.animeMagenta, location: 0.65),
                .init(color: Self.animeViolet, location: 0.85),
                .init(color: Self.animeViolet.opacity(0), location: 1),
            ]), center: glowC, startRadius: 0, endRadius: ry * 1.1))
            layer.fill(iris, with: .linearGradient(Gradient(colors: [Self.animeNight.opacity(0.95), Self.animeNight.opacity(0)]),
                                                   startPoint: CGPoint(x: irisC.x, y: irisC.y - ry),
                                                   endPoint: CGPoint(x: irisC.x, y: irisC.y + ry * 0.1)))
            layer.stroke(iris, with: .color(Self.animeNight), lineWidth: max(0.5, h * 0.03))
            for (i, g) in Self.glitter.enumerated() {
                let phase = i.isMultiple(of: 2) ? twinkle : 1 - twinkle
                let r = rx * g.r * 1.2 * (0.8 + 0.5 * phase)
                let q = CGPoint(x: irisC.x + g.x * rx, y: irisC.y + g.y * ry)
                layer.fill(Path(ellipseIn: CGRect(x: q.x - r, y: q.y - r, width: r * 2, height: r * 2)),
                           with: .color(.white.opacity(0.45 + 0.5 * phase)))
            }
            // The big ✕ star with its pink halo, flaring and turning as it twinkles.
            let starC = CGPoint(x: irisC.x, y: irisC.y + ry * 0.12)
            let starR = rx * (0.78 + 0.22 * twinkle)
            let angle = Angle.degrees(45 + Double(twinkle) * 12)
            layer.drawLayer { halo in
                halo.addFilter(.blur(radius: h * 0.07))
                halo.fill(Self.star(center: starC, radius: starR * 1.15, angle: angle, pinch: 0.3), with: .color(Color(red: 1, green: 0.7, blue: 0.85)))
            }
            layer.fill(Self.star(center: starC, radius: starR, angle: angle, pinch: 0.12), with: .color(.white))
        }
        if glow > 0.01 {
            ctx.drawLayer { layer in
                layer.addFilter(.blur(radius: h * 0.14))
                layer.stroke(eye, with: .color(Self.animePink.opacity(0.8 * glow)), lineWidth: h * 0.1)
            }
        }

        // The lash band: thin at the inner corner, thick over the eye, ending in a flick past the outer corner,
        // with a few spiky lashes on top.
        var band = Path()
        band.move(to: inner)
        band.addCurve(to: outer, control1: upperC1, control2: upperC2)
        band.addQuadCurve(to: p(0.74, 0.16), control: p(0.66, 0.0))          // the flick
        band.addQuadCurve(to: p(0.6, -0.2 * lift - 0.02), control: p(0.7, -0.08))
        band.addCurve(to: p(-0.54, 0.04), control1: p(0.32, 0.02 - 0.84 * lift), control2: p(-0.32, 0.02 - 0.72 * lift))
        band.closeSubpath()
        ctx.fill(band, with: .color(Self.lash))
        // Short lashes sweeping out from the outer half of the band.
        for (u, len) in [(0.3, 0.18), (0.45, 0.24), (0.58, 0.2)] as [(CGFloat, CGFloat)] {
            let base = 0.06 - (1 - pow(u / 0.62, 2)) * 0.62 * lift
            var spike = Path()
            spike.move(to: p(u - 0.04, base + 0.03))
            spike.addQuadCurve(to: p(u + 0.12, base - len * lift), control: p(u + 0.02, base - len * 0.5 * lift))
            spike.addQuadCurve(to: p(u + 0.05, base + 0.05), control: p(u + 0.07, base - len * 0.35 * lift))
            spike.closeSubpath()
            ctx.fill(spike, with: .color(Self.lash))
        }
        // The lower lid: a fine line along the outer two thirds of it.
        var lowerLid = Path()
        lowerLid.move(to: outer)
        lowerLid.addCurve(to: inner, control1: lowerC1, control2: lowerC2)
        ctx.stroke(lowerLid.trimmedPath(from: 0, to: 0.68), with: .color(Self.lash.opacity(0.9)),
                   style: StrokeStyle(lineWidth: max(0.5, h * 0.035), lineCap: .round))
    }

    /// A four-point star with concave sides. `pinch` is how far the waist between two points sits from the
    /// middle (a fraction of the radius): small for the clip's thin ✕, larger for a plump sparkle.
    static func star(center: CGPoint, radius r: CGFloat, angle: Angle = .zero, pinch: CGFloat = 0.3) -> Path {
        func at(_ a: Double, _ d: CGFloat) -> CGPoint { CGPoint(x: center.x + d * CGFloat(cos(a)), y: center.y + d * CGFloat(sin(a))) }
        var p = Path()
        for k in 0..<4 {
            let a = angle.radians + Double(k) * .pi / 2 - .pi / 2
            let tip = at(a, r), waist = at(a + .pi / 4, r * pinch), next = at(a + .pi / 2, r)
            if k == 0 { p.move(to: tip) }
            // Each side bows in toward the middle, so the points come out long and sharp.
            p.addQuadCurve(to: waist, control: at(a + .pi / 10, r * (pinch + 0.12)))
            p.addQuadCurve(to: next, control: at(a + .pi / 2 - .pi / 10, r * (pinch + 0.12)))
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
