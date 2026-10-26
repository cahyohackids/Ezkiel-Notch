import AppKit
import SwiftUI

/// The notch silhouette: a body hanging from the top edge with rounded bottom
/// corners and two concave "ears" that melt into the menu bar (like a real notch).
/// The ears live inside `rect`, so the body is `rect.width - 2 * ear` wide.
/// `belly` bows the bottom edge (+ down, − up) for the water-drop settle.
struct NotchShape: Shape {
    var bottomRadius: CGFloat
    var ear: CGFloat
    var belly: CGFloat = 0

    var animatableData: AnimatablePair<AnimatablePair<CGFloat, CGFloat>, CGFloat> {
        get { AnimatablePair(AnimatablePair(bottomRadius, ear), belly) }
        set {
            bottomRadius = newValue.first.first
            ear = newValue.first.second
            belly = newValue.second
        }
    }

    func path(in rect: CGRect) -> Path {
        let e = max(0, min(ear, rect.width / 4, rect.height / 2))
        let left = rect.minX + e
        let right = rect.maxX - e
        let r = max(0, min(bottomRadius, (right - left) / 2, rect.height - e))
        // Small bow only; a deep one would look like a dent, not a settle.
        let b = max(-r / 2, min(r / 2, belly))

        var p = Path()
        p.move(to: CGPoint(x: rect.minX, y: rect.minY))
        // left ear (concave)
        p.addQuadCurve(to: CGPoint(x: left, y: rect.minY + e), control: CGPoint(x: left, y: rect.minY))
        p.addLine(to: CGPoint(x: left, y: rect.maxY - r))
        p.addArc(tangent1End: CGPoint(x: left, y: rect.maxY), tangent2End: CGPoint(x: left + r, y: rect.maxY), radius: r)
        // Bottom edge: two cubics that leave the corners and reach the middle horizontally,
        // so the bow never kinks at the corner arcs.
        let start = left + r
        let end = right - r
        let mid = (start + end) / 2
        let reach = (mid - start) * 0.55
        p.addCurve(to: CGPoint(x: mid, y: rect.maxY + b),
                   control1: CGPoint(x: start + reach, y: rect.maxY),
                   control2: CGPoint(x: mid - reach, y: rect.maxY + b))
        p.addCurve(to: CGPoint(x: end, y: rect.maxY),
                   control1: CGPoint(x: mid + reach, y: rect.maxY + b),
                   control2: CGPoint(x: end - reach, y: rect.maxY))
        p.addArc(tangent1End: CGPoint(x: right, y: rect.maxY), tangent2End: CGPoint(x: right, y: rect.maxY - r), radius: r)
        p.addLine(to: CGPoint(x: right, y: rect.minY + e))
        // right ear (concave)
        p.addQuadCurve(to: CGPoint(x: rect.maxX, y: rect.minY), control: CGPoint(x: right, y: rect.minY))
        p.closeSubpath()
        return p
    }
}
