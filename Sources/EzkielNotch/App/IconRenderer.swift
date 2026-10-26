import AppKit
import SwiftUI

@MainActor
enum IconRenderer {
    /// Writes every PNG an .iconset needs. `iconutil -c icns` turns it into AppIcon.icns.
    static func renderIconset(to path: String) -> Bool {
        let dir = URL(fileURLWithPath: path, isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        for points in [16, 32, 128, 256, 512] {
            for scale in [1, 2] {
                let pixels = points * scale
                let suffix = scale == 2 ? "@2x" : ""
                let file = dir.appendingPathComponent("icon_\(points)x\(points)\(suffix).png")
                guard let data = png(LionIconArtwork(size: CGFloat(pixels)), size: CGFloat(pixels)) else { return false }
                do { try data.write(to: file) } catch { return false }
            }
        }
        return true
    }

    /// Writes the review sheet, plus one transparent 512 px PNG per mood next to it (lion-awake.png, ...).
    static func renderMascotSheet(to path: String) -> Bool {
        let renderer = ImageRenderer(content: LionMascotSheet())
        renderer.scale = 2
        guard let cg = renderer.cgImage,
              let data = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]),
              (try? data.write(to: URL(fileURLWithPath: path))) != nil else { return false }
        let dir = URL(fileURLWithPath: path).deletingLastPathComponent()
        let moods: [(String, LionMood)] = [("awake", .awake), ("happy", .happy), ("focused", .focused), ("sleeping", .sleeping)]
        for (name, mood) in moods {
            guard let png = png(LionMascot(size: 512, mood: mood, animated: false), size: 512) else { return false }
            guard (try? png.write(to: dir.appendingPathComponent("lion-\(name).png"))) != nil else { return false }
        }
        return true
    }

    /// Menu-bar lion: a template image, tinted by macOS like its own menu-bar icons.
    static func statusImage(points: CGFloat = 18) -> NSImage {
        let renderer = ImageRenderer(content: LionTemplateGlyph(size: points))
        renderer.scale = 2
        let image = renderer.nsImage ?? NSImage(systemSymbolName: "pawprint.fill", accessibilityDescription: nil) ?? NSImage()
        image.size = NSSize(width: points, height: points)
        image.isTemplate = true
        return image
    }

    private static func png<V: View>(_ view: V, size: CGFloat) -> Data? {
        let renderer = ImageRenderer(content: view.frame(width: size, height: size))
        renderer.scale = 1
        renderer.isOpaque = false
        guard let cg = renderer.cgImage else { return nil }
        let rep = NSBitmapImageRep(cgImage: cg)
        return rep.representation(using: .png, properties: [:])
    }
}
