import Foundation

/// A JSON value. Sync merges raw JSON, so fields written by a newer version survive the round trip.
public enum SyncJSON: Equatable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([SyncJSON])
    case object([String: SyncJSON])

    public subscript(key: String) -> SyncJSON? {
        if case .object(let o) = self { return o[key] }
        return nil
    }

    public var string: String? { if case .string(let s) = self { return s } else { return nil } }
    public var array: [SyncJSON]? { if case .array(let a) = self { return a } else { return nil } }
    public var object: [String: SyncJSON]? { if case .object(let o) = self { return o } else { return nil } }
    public var bool: Bool? { if case .bool(let b) = self { return b } else { return nil } }
    public var int: Int? {
        if case .number(let d) = self, d == d.rounded(), abs(d) < 9e15 { return Int(d) }
        return nil
    }

    public static func parse(_ data: Data) -> SyncJSON? { try? JSONDecoder().decode(SyncJSON.self, from: data) }

    /// Sorted keys, like data.json.
    public func encoded(pretty: Bool = false) -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = pretty ? [.sortedKeys, .prettyPrinted, .withoutEscapingSlashes] : [.sortedKeys, .withoutEscapingSlashes]
        return (try? encoder.encode(self)) ?? Data("null".utf8)
    }

    /// A model value as JSON, with data.json's date format.
    public static func from<T: Encodable>(_ value: T) -> SyncJSON? {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(value) else { return nil }
        return parse(data)
    }

    /// This JSON as a model value, with data.json's date format.
    public func decode<T: Decodable>(_ type: T.Type) -> T? {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return try? decoder.decode(type, from: encoded())
    }
}

extension SyncJSON: Codable {
    public init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() {
            self = .null
        } else if let b = try? c.decode(Bool.self) {
            self = .bool(b)
        } else if let d = try? c.decode(Double.self) {
            self = .number(d)
        } else if let s = try? c.decode(String.self) {
            self = .string(s)
        } else if let a = try? c.decode([SyncJSON].self) {
            self = .array(a)
        } else {
            self = .object(try c.decode([String: SyncJSON].self))
        }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        switch self {
        case .null: try c.encodeNil()
        case .bool(let b): try c.encode(b)
        case .number(let d):
            if d == d.rounded(), abs(d) < 9e15 { try c.encode(Int64(d)) } else { try c.encode(d) }
        case .string(let s): try c.encode(s)
        case .array(let a): try c.encode(a)
        case .object(let o): try c.encode(o)
        }
    }
}

/// Times in sync files: ISO 8601, compared in whole milliseconds, written in whole seconds UTC (data.json's format).
public enum SyncTime {
    private static let plain: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()
    private static let fractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
    private static let lock = NSLock()

    public static func ms(_ date: Date) -> Int64 { Int64((date.timeIntervalSince1970 * 1000).rounded()) }

    public static func ms(_ value: SyncJSON?) -> Int64? {
        guard let s = value?.string else { return nil }
        lock.lock()
        defer { lock.unlock() }
        guard let d = plain.date(from: s) ?? fractional.date(from: s) else { return nil }
        return ms(d)
    }

    public static func string(_ ms: Int64) -> String {
        let seconds = ms >= 0 ? ms / 1000 : -((-ms + 999) / 1000)
        lock.lock()
        defer { lock.unlock() }
        return plain.string(from: Date(timeIntervalSince1970: TimeInterval(seconds)))
    }

    public static func date(_ value: SyncJSON?) -> Date? {
        ms(value).map { Date(timeIntervalSince1970: TimeInterval($0) / 1000) }
    }
}
