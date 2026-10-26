import Foundation
import NotchCore

/// Weather glance from Open-Meteo (free, no account, no key). Uses the city you type in Settings,
/// so no Location permission; only that city's coordinates are sent. Off until a city is set.
@MainActor
final class WeatherService: ObservableObject {
    struct Now: Equatable {
        let temperature: Double
        let code: Int
        let high: Double
        let low: Double
        let rainChance: Int?
        let isDay: Bool
        let fetched: Date
    }

    struct Place: Identifiable, Equatable {
        let name: String
        let detail: String
        let latitude: Double
        let longitude: Double
        var id: String { "\(latitude),\(longitude)" }
    }

    @Published private(set) var now: Now?
    @Published private(set) var results: [Place] = []
    @Published private(set) var searching = false

    private var place: (lat: Double, lon: Double)?
    private var loading = false

    func configure(latitude: Double?, longitude: Double?) {
        let next = latitude.flatMap { lat in longitude.map { (lat, $0) } }
        guard next?.0 != place?.lat || next?.1 != place?.lon else { return }
        place = next
        now = nil
        refresh(force: true)
    }

    /// Every 30 minutes is plenty for a glance.
    func refresh(force: Bool = false) {
        guard let place, !loading else { return }
        if !force, let now, Date().timeIntervalSince(now.fetched) < 1800 { return }
        var c = URLComponents(string: "https://api.open-meteo.com/v1/forecast")!
        c.queryItems = [
            .init(name: "latitude", value: String(format: "%.3f", place.lat)),
            .init(name: "longitude", value: String(format: "%.3f", place.lon)),
            .init(name: "current", value: "temperature_2m,weather_code,is_day"),
            .init(name: "daily", value: "temperature_2m_max,temperature_2m_min,precipitation_probability_max"),
            .init(name: "forecast_days", value: "1"),
            .init(name: "timezone", value: "auto"),
        ]
        loading = true
        URLSession.shared.dataTask(with: c.url!) { data, _, _ in
            let parsed = data.flatMap(Self.parseForecast)
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.loading = false
                    if let parsed { self.now = parsed }
                }
            }
        }.resume()
    }

    func search(_ text: String) {
        let q = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard q.count >= 2 else { results = []; return }
        var c = URLComponents(string: "https://geocoding-api.open-meteo.com/v1/search")!
        c.queryItems = [.init(name: "name", value: q), .init(name: "count", value: "5"),
                        .init(name: "language", value: Lang.isIndonesian ? "id" : "en")]
        searching = true
        URLSession.shared.dataTask(with: c.url!) { data, _, _ in
            let places = data.flatMap(Self.parsePlaces) ?? []
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.searching = false
                    self.results = places
                }
            }
        }.resume()
    }

    func clearResults() { results = [] }

    /// Self-test snapshots.
    func simulate(_ value: Now?) { now = value }

    nonisolated static func parseForecast(_ data: Data) -> Now? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let current = json["current"] as? [String: Any],
              let temp = (current["temperature_2m"] as? NSNumber)?.doubleValue,
              let code = (current["weather_code"] as? NSNumber)?.intValue,
              let daily = json["daily"] as? [String: Any],
              let high = ((daily["temperature_2m_max"] as? [Any])?.first as? NSNumber)?.doubleValue,
              let low = ((daily["temperature_2m_min"] as? [Any])?.first as? NSNumber)?.doubleValue else { return nil }
        let rain = ((daily["precipitation_probability_max"] as? [Any])?.first as? NSNumber)?.intValue
        let day = (current["is_day"] as? NSNumber)?.intValue ?? 1
        return Now(temperature: temp, code: code, high: high, low: low, rainChance: rain, isDay: day == 1, fetched: Date())
    }

    nonisolated static func parsePlaces(_ data: Data) -> [Place]? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let items = json["results"] as? [[String: Any]] else { return [] }
        return items.compactMap { r in
            guard let name = r["name"] as? String,
                  let lat = (r["latitude"] as? NSNumber)?.doubleValue,
                  let lon = (r["longitude"] as? NSNumber)?.doubleValue else { return nil }
            let detail = [r["admin1"] as? String, r["country"] as? String].compactMap { $0 }.joined(separator: ", ")
            return Place(name: name, detail: detail, latitude: lat, longitude: lon)
        }
    }

    /// WMO weather code → SF Symbol (multicolour) and a short word.
    static func symbol(_ code: Int, day: Bool) -> String {
        switch code {
        case 0: return day ? "sun.max.fill" : "moon.stars.fill"
        case 1, 2: return day ? "cloud.sun.fill" : "cloud.moon.fill"
        case 3: return "cloud.fill"
        case 45, 48: return "cloud.fog.fill"
        case 51...57: return "cloud.drizzle.fill"
        case 61...67, 80...82: return "cloud.rain.fill"
        case 71...77, 85, 86: return "cloud.snow.fill"
        case 95...99: return "cloud.bolt.rain.fill"
        default: return "cloud.fill"
        }
    }

    static func word(_ code: Int) -> String {
        switch code {
        case 0: return L("Clear sky")
        case 1, 2: return L("Partly cloudy")
        case 3: return L("Cloudy")
        case 45, 48: return L("Fog")
        case 51...57: return L("Drizzle")
        case 61...67, 80...82: return L("Rain")
        case 71...77, 85, 86: return L("Snow")
        case 95...99: return L("Thunderstorm")
        default: return L("Cloudy")
        }
    }
}
