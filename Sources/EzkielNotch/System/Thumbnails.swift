import AppKit
import QuickLookThumbnailing

/// QuickLook thumbnails, cached per file and size so Stash and Pounce never ask twice.
enum Thumbnails {
    private static let cache = NSCache<NSString, NSImage>()

    static func image(for url: URL, side: CGFloat) async -> NSImage? {
        let key = "\(url.path)#\(Int(side))" as NSString
        if let hit = cache.object(forKey: key) { return hit }
        let request = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: side * 2, height: side * 2),
                                                   scale: NSScreen.main?.backingScaleFactor ?? 2,
                                                   representationTypes: .thumbnail)
        guard let rep = try? await QLThumbnailGenerator.shared.generateBestRepresentation(for: request) else { return nil }
        cache.setObject(rep.nsImage, forKey: key)
        return rep.nsImage
    }
}
