import AppKit
import EventKit
import SwiftUI

/// Rally: today's next meetings from Calendar, with the video link when one is in the event.
@MainActor
final class CalendarService: ObservableObject {
    struct Meeting: Identifiable, Equatable {
        var id: String
        var title: String
        var start: Date
        var end: Date
        var link: URL?
        var color: Color
    }

    enum Access: Equatable { case unknown, granted, denied }

    @Published private(set) var upcoming: [Meeting] = []
    @Published private(set) var access: Access = .unknown

    private let store = EKEventStore()
    private var observer: NSObjectProtocol?
    private var enabled = false
    private var simulated = false

    func setEnabled(_ on: Bool) {
        guard on != enabled else { return }
        enabled = on
        if !on {
            if let observer { NotificationCenter.default.removeObserver(observer) }
            observer = nil
            upcoming = []
            return
        }
        observer = NotificationCenter.default.addObserver(forName: .EKEventStoreChanged, object: store, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        requestAccess()
    }

    private func requestAccess() {
        let finish: @Sendable (Bool) -> Void = { granted in
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.access = granted ? .granted : .denied
                self.refresh()
            }
        }
        if Self.isAuthorized {
            finish(true)
            return
        }
        if #available(macOS 14.0, *) {
            store.requestFullAccessToEvents { granted, _ in finish(granted) }
        } else {
            store.requestAccess(to: .event) { granted, _ in finish(granted) }
        }
    }

    private static var isAuthorized: Bool {
        let status = EKEventStore.authorizationStatus(for: .event)
        if #available(macOS 14.0, *) { return status == .fullAccess }
        return status.rawValue == 3 // .authorized before macOS 14
    }

    /// Timed events from 5 minutes ago to the end of today, earliest first; skips all-day and declined ones.
    func refresh(now: Date = Date()) {
        guard enabled, access == .granted, !simulated else { return }
        let cal = Calendar.current
        guard let end = cal.date(byAdding: .day, value: 1, to: cal.startOfDay(for: now)) else { return }
        let predicate = store.predicateForEvents(withStart: now.addingTimeInterval(-300), end: end, calendars: nil)
        let events = store.events(matching: predicate)
            .filter { !$0.isAllDay && $0.endDate > now && !Self.declined($0) }
            .sorted { $0.startDate < $1.startDate }
            .prefix(3)
        let meetings = events.map { e in
            Meeting(id: e.eventIdentifier ?? UUID().uuidString, title: e.title ?? "Event", start: e.startDate, end: e.endDate,
                    link: Self.joinLink(e), color: Color(cgColor: e.calendar?.cgColor ?? NSColor.systemPurple.cgColor))
        }
        if meetings != upcoming { upcoming = Array(meetings) }
    }

    private static func declined(_ e: EKEvent) -> Bool {
        e.attendees?.first(where: \.isCurrentUser)?.participantStatus == .declined
    }

    private static let linkPattern = try? NSRegularExpression(
        pattern: #"https?://[^\s<>"]*(zoom\.us/(j|my|w)/|meet\.google\.com/|teams\.microsoft\.com/l/meetup-join|teams\.live\.com/meet|webex\.com/)[^\s<>"]*"#,
        options: .caseInsensitive)

    /// The first Zoom / Meet / Teams / Webex link in the event's URL, location or notes.
    static func joinLink(_ e: EKEvent) -> URL? {
        let haystack = [e.url?.absoluteString, e.location, e.notes].compactMap { $0 }.joined(separator: "\n")
        return joinLink(in: haystack)
    }

    static func joinLink(in text: String) -> URL? {
        guard let linkPattern,
              let match = linkPattern.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
              let range = Range(match.range, in: text) else { return nil }
        return URL(string: String(text[range]).trimmingCharacters(in: CharacterSet(charactersIn: ").,>")))
    }

    /// Self-test: show made-up meetings without Calendar access.
    func simulate(_ meetings: [Meeting]) {
        enabled = true
        simulated = true
        access = .granted
        upcoming = meetings
    }
}
