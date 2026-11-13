import NotchCore
import SwiftUI

/// The closed notch's left wing while a meeting, a quick timer or a download is what it shows: that thing's icon.
/// A download with a known size draws a thin ring around its arrow; several downloads add their count.
struct LiveWingIcon: View {
    @ObservedObject var model: AppModel

    var body: some View {
        switch model.closedLive {
        case .meeting:
            glyph("calendar", Accent.rally)
        case .timer:
            glyph("timer", Accent.den)
                .overlay(alignment: .bottomTrailing) { count(model.data.timers.count, Accent.den) }
        case .download:
            if let lead = model.leadDownload {
                ZStack {
                    if let fraction = lead.download.fraction {
                        ProgressRing(progress: fraction, lineWidth: 2, accent: .stash)
                            .frame(width: 17, height: 17)
                            .animation(Theme.smooth, value: fraction)
                        Image(systemName: "arrow.down")
                            .font(.system(size: 8, weight: .bold))
                            .foregroundStyle(Accent.stash.light)
                    } else {
                        Image(systemName: "arrow.down.circle")
                            .font(.system(size: 13, weight: .semibold))
                            .symbolRenderingMode(.hierarchical)
                            .foregroundStyle(Accent.stash.linear)
                    }
                }
                .overlay(alignment: .bottomTrailing) { count(lead.count, Accent.stash) }
                .accessibilityLabel(L("Downloading %@", lead.download.finalName))
            }
        default:
            Color.clear
        }
    }

    private func glyph(_ name: String, _ accent: Accent) -> some View {
        Image(systemName: name)
            .font(.system(size: 12, weight: .semibold))
            .symbolRenderingMode(.hierarchical)
            .foregroundStyle(accent.linear)
    }

    @ViewBuilder
    private func count(_ n: Int, _ accent: Accent) -> some View {
        if n > 1 {
            Text("\(n)")
                .font(Theme.number(7.5, .bold))
                .foregroundStyle(Theme.onAccent)
                .padding(.horizontal, 2.5)
                .frame(minWidth: 10, minHeight: 10)
                .background(Capsule().fill(accent.light))
                .offset(x: 6, y: 4)
        }
    }
}

/// The right wing of a download: the percent when the final size is known (Safari), else the size so far with the
/// speed under it (Chrome, Edge and Firefox don't say how big the file will be).
struct LiveDownloadValue: View {
    var download: ActiveDownload

    var body: some View {
        if let fraction = download.fraction {
            Text("\(Int((fraction * 100).rounded(.down)))%")
                .font(Theme.number(11, .semibold))
                .foregroundStyle(Accent.stash.linear)
                .lineLimit(1)
                .contentTransition(.numericText())
        } else {
            VStack(spacing: -1) {
                Text(DownloadTracker.bytes(download.bytes))
                    .font(Theme.number(9.5, .semibold))
                    .foregroundStyle(Theme.ink)
                if let speed = download.bytesPerSecond {
                    Text(DownloadTracker.speed(speed))
                        .font(Theme.number(7.5, .medium))
                        .foregroundStyle(Theme.mist)
                }
            }
            .lineLimit(1)
            .minimumScaleFactor(0.7)
        }
    }
}
