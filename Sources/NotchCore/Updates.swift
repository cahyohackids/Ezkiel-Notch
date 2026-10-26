import CryptoKit
import Foundation

/// The checks the in-app updater runs before it replaces anything. Pure, so they're unit-tested.
public enum UpdateVerify {
    /// `shasum -a 256` output → the checksum listed for `file` (lowercase hex), or nil.
    public static func checksum(for file: String, in sums: String) -> String? {
        for line in sums.split(whereSeparator: \.isNewline) {
            let parts = line.split(maxSplits: 1, whereSeparator: { $0 == " " || $0 == "\t" })
            guard parts.count == 2 else { continue }
            let hash = parts[0].lowercased()
            // shasum writes "hash  name" (text) or "hash *name" (binary).
            let name = parts[1].trimmingCharacters(in: .whitespaces).drop(while: { $0 == "*" })
            if String(name) == file, hash.count == 64, hash.allSatisfy(\.isHexDigit) { return hash }
        }
        return nil
    }

    /// SHA-256 of a file, read in 1 MB chunks.
    public static func sha256(of url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: 1 << 20), !chunk.isEmpty {
            hasher.update(data: chunk)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    /// The app inside the download is ours, the version the release promised, and newer than this one.
    public static func acceptable(bundleID: String?, version: String?, expected: String, current: String,
                                  ownBundleID: String = "store.ezkielyna.notch") -> Bool {
        guard bundleID == ownBundleID, let version else { return false }
        return AppVersion.parts(version) == AppVersion.parts(expected) && AppVersion.isNewer(version, than: current)
    }
}
