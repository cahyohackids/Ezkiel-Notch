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
    static func draw(in ctx: inout GraphicsContext, size: CGSize, t: Double, mood: LionMood, detailed: Bool) {
        let s = min(size.width, size.height) / 100
        ctx.translateBy(x: (size.width - 100 * s) / 2, y: (size.height - 100 * s) / 2)
        ctx.scaleBy(x: s, y: s)

        let sleeping = mood == .sleeping
        let happy = mood == .happy

        // Motion
        let breath = sleeping ? 1 + 0.035 * sin(t * 1.4) : 1 + 0.022 * sin(t * 2.2)
        var bob = sleeping ? 1.6 * sin(t * 1.4) : 1.0 * sin(t * 1.7)
        var tilt = 0.0
        if happy {
            bob = -3.2 * abs(sin(t * 7))
            tilt = 7 * sin(t * 7)
        } else if mood == .focused {
            tilt = 1.5 * sin(t * 0.8)
        }

        var head = ctx
        head.translateBy(x: 50, y: 54 + bob)
        head.rotate(by: .degrees(tilt))
        head.translateBy(x: -50, y: -54)

        // Mane: two rings of soft petals, slowly rippling.
        var mane = head
        mane.translateBy(x: 50, y: 52)
        mane.scaleBy(x: breath, y: breath)
        let outer = 14
        for i in 0..<outer {
            let a = Double(i) / Double(outer) * 2 * .pi + (sleeping ? 0 : 0.03 * sin(t * 1.3 + Double(i)))
            var petal = mane
            petal.rotate(by: .radians(a))
            let rect = CGRect(x: -11.5, y: -46, width: 23, height: 30)
            petal.fill(Path(ellipseIn: rect), with: .color(i % 2 == 0 ? Theme.ember : Theme.orange))
        }
        if detailed {
            for i in 0..<outer {
                let a = (Double(i) + 0.5) / Double(outer) * 2 * .pi
                var petal = mane
                petal.rotate(by: .radians(a))
                petal.fill(Path(ellipseIn: CGRect(x: -9, y: -39, width: 18, height: 24)),
                           with: .color(i % 2 == 0 ? Theme.orange : Color(hex: 0xF4AE5E)))
            }
        }

        // Ears
        let earDrop: Double = sleeping ? 2.5 : 0
        for x in [30.0, 70.0] {
            head.fill(Path(ellipseIn: CGRect(x: x - 9, y: 24 + earDrop, width: 18, height: 17)), with: .color(Theme.gold))
            head.fill(Path(ellipseIn: CGRect(x: x - 5, y: 28 + earDrop, width: 10, height: 9)), with: .color(Theme.ember.opacity(0.85)))
        }

        // Face
        let face = Path(ellipseIn: CGRect(x: 23, y: 28, width: 54, height: 52))
        head.fill(face, with: .radialGradient(
            Gradient(colors: [Color(hex: 0xFFE2AE), Theme.gold, Color(hex: 0xF7B865)]),
            center: CGPoint(x: 46, y: 46), startRadius: 2, endRadius: 34))

        // Brows
        if detailed {
            let browY: Double = mood == .focused ? 41 : 39
            for (x, dir) in [(39.0, -1.0), (61.0, 1.0)] {
                var brow = Path()
                brow.move(to: CGPoint(x: x - 5, y: browY + (mood == .focused ? -dir * 1.2 : 0.8)))
                brow.addQuadCurve(to: CGPoint(x: x + 5, y: browY + (mood == .focused ? dir * 1.2 : 0.8)),
                                  control: CGPoint(x: x, y: browY - 2))
                head.stroke(brow, with: .color(Theme.ember.opacity(0.7)), style: StrokeStyle(lineWidth: 1.8, lineCap: .round))
            }
        }

        // Eyes
        let navy = Theme.navy3
        if sleeping {
            for x in [39.0, 61.0] {
                var lid = Path()
                lid.move(to: CGPoint(x: x - 5, y: 50))
                lid.addQuadCurve(to: CGPoint(x: x + 5, y: 50), control: CGPoint(x: x, y: 54.5))
                head.stroke(lid, with: .color(navy), style: StrokeStyle(lineWidth: 2.4, lineCap: .round))
            }
        } else if happy {
            for x in [39.0, 61.0] {
                var arc = Path()
                arc.move(to: CGPoint(x: x - 5, y: 52))
                arc.addQuadCurve(to: CGPoint(x: x + 5, y: 52), control: CGPoint(x: x, y: 45))
                head.stroke(arc, with: .color(navy), style: StrokeStyle(lineWidth: 2.6, lineCap: .round))
            }
        } else {
            // Blink every ~4.2 s, sometimes double.
            let cycle = t.truncatingRemainder(dividingBy: 4.2)
            let blinking = cycle > 4.02 || (Int(t / 4.2) % 3 == 0 && cycle > 3.7 && cycle < 3.82)
            let openness: Double = blinking ? 0.12 : (mood == .focused ? 0.75 : 1)
            // Eyes drift slightly, as if looking around.
            let look = sin(t * 0.37) * 1.2
            for x in [39.0, 61.0] {
                let h = 9.5 * openness
                head.fill(Path(ellipseIn: CGRect(x: x - 3.6 + look, y: 50.5 - h / 2, width: 7.2, height: h)), with: .color(navy))
                if !blinking && detailed {
                    head.fill(Path(ellipseIn: CGRect(x: x - 1.6 + look, y: 47.4, width: 2.6, height: 2.6)), with: .color(.white.opacity(0.95)))
                }
            }
        }

        // Cheeks
        let blush = happy ? 0.75 : 0.42
        for x in [31.5, 68.5] {
            head.fill(Path(ellipseIn: CGRect(x: x - 4.5, y: 58, width: 9, height: 6)), with: .color(Theme.blush.opacity(blush)))
        }

        // Muzzle
        head.fill(Path(ellipseIn: CGRect(x: 36, y: 59, width: 15, height: 13)), with: .color(Theme.cream))
        head.fill(Path(ellipseIn: CGRect(x: 49, y: 59, width: 15, height: 13)), with: .color(Theme.cream))
        head.fill(Path(ellipseIn: CGRect(x: 44, y: 68, width: 12, height: 8)), with: .color(Color(hex: 0xEAD7B2)))

        // Nose
        var nose = Path()
        nose.move(to: CGPoint(x: 45, y: 58.5))
        nose.addQuadCurve(to: CGPoint(x: 55, y: 58.5), control: CGPoint(x: 50, y: 56))
        nose.addQuadCurve(to: CGPoint(x: 50, y: 64), control: CGPoint(x: 55, y: 61.5))
        nose.addQuadCurve(to: CGPoint(x: 45, y: 58.5), control: CGPoint(x: 45, y: 61.5))
        head.fill(nose, with: .color(navy))

        // Mouth
        if happy {
            var mouth = Path()
            mouth.move(to: CGPoint(x: 45, y: 66))
            mouth.addQuadCurve(to: CGPoint(x: 55, y: 66), control: CGPoint(x: 50, y: 74))
            mouth.closeSubpath()
            head.fill(mouth, with: .color(Theme.rust))
        } else {
            var mouth = Path()
            mouth.move(to: CGPoint(x: 50, y: 64))
            mouth.addLine(to: CGPoint(x: 50, y: 66.5))
            mouth.move(to: CGPoint(x: 45.5, y: 67))
            mouth.addQuadCurve(to: CGPoint(x: 50, y: 66.5), control: CGPoint(x: 48, y: 69))
            mouth.addQuadCurve(to: CGPoint(x: 54.5, y: 67), control: CGPoint(x: 52, y: 69))
            head.stroke(mouth, with: .color(navy.opacity(0.85)), style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
        }

        // Whisker dots
        if detailed {
            for (x, y) in [(40.0, 64.0), (42.5, 67.0), (60.0, 64.0), (57.5, 67.0)] {
                head.fill(Path(ellipseIn: CGRect(x: x - 0.8, y: y - 0.8, width: 1.6, height: 1.6)), with: .color(Theme.rust.opacity(0.6)))
            }
        }

        // Zzz while napping
        if sleeping {
            for k in 0..<3 {
                let phase = (t * 0.45 + Double(k) / 3).truncatingRemainder(dividingBy: 1)
                var z = ctx
                z.opacity = sin(phase * .pi)
                let fontSize = 10 + phase * 8
                z.draw(Text("z").font(.system(size: fontSize, weight: .heavy, design: .rounded)).foregroundColor(Theme.gold),
                       at: CGPoint(x: 80 + phase * 12, y: 30 - phase * 26))
            }
        }

        // Sparkles when happy
        if happy && detailed {
            for k in 0..<3 {
                let phase = (t * 1.2 + Double(k) / 3).truncatingRemainder(dividingBy: 1)
                let angle = Double(k) * 2.1 + 0.4
                let r = 44 + phase * 8
                let p = CGPoint(x: 50 + cos(angle) * r, y: 50 + sin(angle) * r * 0.9)
                var spark = ctx
                spark.opacity = sin(phase * .pi)
                spark.fill(sparkle(at: p, radius: 4.5 * (1 - phase * 0.4)), with: .color(Theme.gold))
            }
        }
    }

    private static func sparkle(at p: CGPoint, radius r: Double) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: p.x, y: p.y - r))
        path.addQuadCurve(to: CGPoint(x: p.x + r, y: p.y), control: CGPoint(x: p.x, y: p.y))
        path.addQuadCurve(to: CGPoint(x: p.x, y: p.y + r), control: CGPoint(x: p.x, y: p.y))
        path.addQuadCurve(to: CGPoint(x: p.x - r, y: p.y), control: CGPoint(x: p.x, y: p.y))
        path.addQuadCurve(to: CGPoint(x: p.x, y: p.y - r), control: CGPoint(x: p.x, y: p.y))
        path.closeSubpath()
        return path
    }
}

/// App icon artwork: the lion on a navy squircle with a warm glow.
struct LionIconArtwork: View {
    var size: CGFloat
    var body: some View {
        let inset = size * 0.1
        ZStack {
            RoundedRectangle(cornerRadius: (size - inset * 2) * 0.225, style: .continuous)
                .fill(LinearGradient(colors: [Theme.navy3, Theme.navy, Theme.void], startPoint: .top, endPoint: .bottom))
                .overlay(
                    RadialGradient(colors: [Theme.orange.opacity(0.45), .clear], center: .center,
                                   startRadius: 0, endRadius: size * 0.42)
                )
                .overlay(
                    RoundedRectangle(cornerRadius: (size - inset * 2) * 0.225, style: .continuous)
                        .strokeBorder(LinearGradient(colors: [Theme.gold.opacity(0.7), Theme.ember.opacity(0.2)],
                                                     startPoint: .top, endPoint: .bottom), lineWidth: max(1, size * 0.008))
                )
                .padding(inset)
            LionMascot(size: size * 0.66, mood: .awake, animated: false)
                .offset(y: size * 0.01)
        }
        .frame(width: size, height: size)
    }
}
