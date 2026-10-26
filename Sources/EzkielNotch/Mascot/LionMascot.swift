import AppKit
import SwiftUI

/// Ezkiel, the ezkielyna.store lion, drawn as vectors so it stays crisp at 16 pt
/// and can breathe, blink, nap and cheer. The animation clock only runs while
/// `animated` is true (TimelineView pauses otherwise), so an idle lion costs nothing.
struct LionMascot: View {
    var size: CGFloat
    var mood: LionMood = .awake
    var animated: Bool = true

    var body: some View {
        Group {
            if animated {
                TimelineView(.animation(minimumInterval: 1.0 / 30, paused: false)) { timeline in
                    canvas(t: timeline.date.timeIntervalSinceReferenceDate)
                }
            } else {
                canvas(t: 0)
            }
        }
        .frame(width: size, height: size)
        .accessibilityLabel("Ezkiel the lion")
    }

    private func canvas(t: Double) -> some View {
        Canvas { context, canvasSize in
            LionPainter.draw(in: &context, size: canvasSize, t: t, mood: mood, detailed: size >= 26)
        }
    }
}

enum LionPainter {
    // Flat palette: two mane tones, one face tone. No gradients.
    static let maneOuter = Color(hex: 0xD9672C)
    static let maneInner = Color(hex: 0xF0914A)
    static let face = Color(hex: 0xFFCF87)
    static let muzzle = Color(hex: 0xFFF0D4)
    static let ink = Color(hex: 0x1A1420)

    static func draw(in ctx: inout GraphicsContext, size: CGSize, t: Double, mood: LionMood, detailed: Bool) {
        let s = min(size.width, size.height) / 100
        ctx.translateBy(x: (size.width - 100 * s) / 2, y: (size.height - 100 * s) / 2)
        ctx.scaleBy(x: s, y: s)

        let sleeping = mood == .sleeping
        let happy = mood == .happy
        let focused = mood == .focused

        // Motion: slow breathing, a hop and a head tilt when happy.
        let breath = sleeping ? 1 + 0.03 * sin(t * 1.4) : 1 + 0.015 * sin(t * 2.0)
        var bob = sleeping ? 1.2 * sin(t * 1.4) : 0.6 * sin(t * 1.7)
        var tilt = 0.0
        if happy {
            bob = -3 * abs(sin(t * 6.5))
            tilt = 6 * sin(t * 6.5)
        }

        var head = ctx
        head.translateBy(x: 50, y: 54 + bob)
        head.rotate(by: .degrees(tilt))
        head.translateBy(x: -50, y: -54)

        // Mane: scalloped ring with a slight swirl, plus an inner ring offset by half a tuft.
        var mane = head
        mane.translateBy(x: 50, y: 52)
        mane.scaleBy(x: breath, y: breath)
        if detailed {
            mane.fill(scallops(count: 12, outer: 48, inner: 39, swirl: 0.05, offset: 0), with: .color(maneOuter))
            mane.fill(scallops(count: 12, outer: 41.5, inner: 33, swirl: 0.05, offset: 0.5), with: .color(maneInner))
        } else {
            mane.fill(scallops(count: 9, outer: 48, inner: 38, swirl: 0, offset: 0), with: .color(maneOuter))
        }

        // Ears
        let earDrop: Double = sleeping ? 2 : 0
        for x in [29.0, 71.0] {
            head.fill(Path(ellipseIn: CGRect(x: x - 9, y: 22 + earDrop, width: 18, height: 18)), with: .color(face))
            head.fill(Path(ellipseIn: CGRect(x: x - 4.5, y: 26.5 + earDrop, width: 9, height: 9)), with: .color(maneOuter))
        }

        // Face: broad cheeks, tapered chin.
        var faceShape = Path()
        faceShape.move(to: CGPoint(x: 50, y: 26))
        faceShape.addCurve(to: CGPoint(x: 78, y: 53), control1: CGPoint(x: 67, y: 26), control2: CGPoint(x: 78, y: 37))
        faceShape.addCurve(to: CGPoint(x: 50, y: 83), control1: CGPoint(x: 78, y: 71), control2: CGPoint(x: 64, y: 83))
        faceShape.addCurve(to: CGPoint(x: 22, y: 53), control1: CGPoint(x: 36, y: 83), control2: CGPoint(x: 22, y: 71))
        faceShape.addCurve(to: CGPoint(x: 50, y: 26), control1: CGPoint(x: 22, y: 37), control2: CGPoint(x: 33, y: 26))
        head.fill(faceShape, with: .color(face))

        // Eyes. Small sizes get bigger eyes so they still read at 20 pt.
        let eyeX = [39.5, 60.5]
        let eyeY = 50.0
        let ew = detailed ? 7.6 : 9.5
        let eh = detailed ? 9.6 : 11.5
        if sleeping {
            for x in eyeX {
                var lid = Path()
                lid.move(to: CGPoint(x: x - 5, y: eyeY))
                lid.addQuadCurve(to: CGPoint(x: x + 5, y: eyeY), control: CGPoint(x: x, y: eyeY + 4.5))
                head.stroke(lid, with: .color(ink), style: StrokeStyle(lineWidth: detailed ? 2.4 : 3.2, lineCap: .round))
            }
        } else if happy {
            for x in eyeX {
                var arc = Path()
                arc.move(to: CGPoint(x: x - 5, y: eyeY + 2))
                arc.addQuadCurve(to: CGPoint(x: x + 5, y: eyeY + 2), control: CGPoint(x: x, y: eyeY - 5.5))
                head.stroke(arc, with: .color(ink), style: StrokeStyle(lineWidth: detailed ? 2.6 : 3.2, lineCap: .round))
            }
        } else {
            let cycle = t.truncatingRemainder(dividingBy: 4.6)
            let blinking = cycle > 4.45 || (Int(t / 4.6) % 3 == 0 && cycle > 4.1 && cycle < 4.22)
            let openness = blinking ? 0.12 : (focused ? 0.72 : 1)
            let look = sin(t * 0.37) * (detailed ? 1.2 : 0.6)
            for x in eyeX {
                let h = eh * openness
                head.fill(Path(ellipseIn: CGRect(x: x - ew / 2 + look, y: eyeY - h / 2, width: ew, height: h)), with: .color(ink))
                if !blinking && detailed {
                    head.fill(Path(ellipseIn: CGRect(x: x + 0.4 + look, y: eyeY - 3.6, width: 2.6, height: 2.6)), with: .color(.white))
                }
            }
        }

        // Brows: they carry most of the expression.
        if detailed {
            // dir points from the inner end (near the nose) to the outer end.
            let innerY = focused ? 42.5 : (sleeping ? 41.5 : 39.6)
            let outerY = focused ? 40.2 : 41.2
            for (x, dir) in [(eyeX[0], -1.0), (eyeX[1], 1.0)] {
                var brow = Path()
                brow.move(to: CGPoint(x: x - 3.5 * dir, y: innerY))
                brow.addLine(to: CGPoint(x: x + 4.5 * dir, y: outerY))
                head.stroke(brow, with: .color(maneOuter), style: StrokeStyle(lineWidth: 2.6, lineCap: .round))
            }
        }

        // Muzzle
        head.fill(Path(ellipseIn: CGRect(x: 37.5, y: 58.5, width: 14, height: 12)), with: .color(muzzle))
        head.fill(Path(ellipseIn: CGRect(x: 48.5, y: 58.5, width: 14, height: 12)), with: .color(muzzle))

        // Nose
        var nose = Path()
        nose.move(to: CGPoint(x: 45, y: 58))
        nose.addQuadCurve(to: CGPoint(x: 55, y: 58), control: CGPoint(x: 50, y: 55.5))
        nose.addQuadCurve(to: CGPoint(x: 50, y: 63.5), control: CGPoint(x: 55, y: 61))
        nose.addQuadCurve(to: CGPoint(x: 45, y: 58), control: CGPoint(x: 45, y: 61))
        head.fill(nose, with: .color(ink))

        // Mouth
        if happy {
            var mouth = Path()
            mouth.move(to: CGPoint(x: 44.5, y: 65.5))
            mouth.addQuadCurve(to: CGPoint(x: 55.5, y: 65.5), control: CGPoint(x: 50, y: 75))
            mouth.closeSubpath()
            head.fill(mouth, with: .color(Theme.rust))
        } else if detailed {
            var mouth = Path()
            mouth.move(to: CGPoint(x: 50, y: 63.5))
            mouth.addLine(to: CGPoint(x: 50, y: 66))
            mouth.move(to: CGPoint(x: 45.5, y: 66))
            mouth.addQuadCurve(to: CGPoint(x: 50, y: 66), control: CGPoint(x: 47.5, y: 68.5))
            mouth.addQuadCurve(to: CGPoint(x: 54.5, y: 66), control: CGPoint(x: 52.5, y: 68.5))
            head.stroke(mouth, with: .color(ink), style: StrokeStyle(lineWidth: 1.6, lineCap: .round, lineJoin: .round))
        }

        // Blush only when happy.
        if happy && detailed {
            for x in [30.5, 69.5] {
                head.fill(Path(ellipseIn: CGRect(x: x - 4, y: 57, width: 8, height: 5)), with: .color(Theme.blush.opacity(0.6)))
            }
        }

        // One drifting "z" while napping.
        if sleeping {
            let phase = (t * 0.4).truncatingRemainder(dividingBy: 1)
            var z = ctx
            z.opacity = sin(phase * .pi)
            z.draw(Text("z").font(.system(size: 16, weight: .bold)).foregroundColor(Theme.cream),
                   at: CGPoint(x: 82 + phase * 8, y: 26 - phase * 18))
        }
    }

    /// A ring of `count` rounded tufts. `swirl` leans each tuft (radians); `offset` rotates by tufts.
    private static func scallops(count: Int, outer: Double, inner: Double, swirl: Double, offset: Double) -> Path {
        var p = Path()
        let step = 2 * Double.pi / Double(count)
        func point(_ r: Double, _ a: Double) -> CGPoint { CGPoint(x: r * cos(a), y: r * sin(a)) }
        for i in 0...count {
            let a = (Double(i) + offset) * step - .pi / 2
            let valley = point(inner, a)
            if i == 0 {
                p.move(to: valley)
                continue
            }
            let prev = a - step
            p.addCurve(to: valley,
                       control1: point(outer * 1.08, prev + step * 0.2 + swirl),
                       control2: point(outer * 1.08, prev + step * 0.8 + swirl))
        }
        p.closeSubpath()
        return p
    }
}

/// App icon artwork: the lion on a flat navy squircle.
struct LionIconArtwork: View {
    var size: CGFloat
    var body: some View {
        let inset = size * 0.1
        ZStack {
            RoundedRectangle(cornerRadius: (size - inset * 2) * 0.225, style: .continuous)
                .fill(Theme.navy2)
                .padding(inset)
            LionMascot(size: size * 0.7, mood: .awake, animated: false)
        }
        .frame(width: size, height: size)
    }
}

/// `--render-mascot <file.png>`: every mood at large and notch size, for design review.
struct LionMascotSheet: View {
    var body: some View {
        let moods: [LionMood] = [.awake, .happy, .focused, .sleeping]
        VStack(spacing: 20) {
            HStack(spacing: 24) {
                ForEach(moods.indices, id: \.self) { i in LionMascot(size: 140, mood: moods[i], animated: false) }
            }
            HStack(spacing: 24) {
                ForEach(moods.indices, id: \.self) { i in
                    HStack(spacing: 12) {
                        LionMascot(size: 22, mood: moods[i], animated: false)
                        LionMascot(size: 38, mood: moods[i], animated: false)
                    }
                    .frame(width: 140)
                }
            }
        }
        .padding(28)
        .background(Color.black)
    }
}
