import Foundation
import NotchCore

/// Imports what IslandDesk 2.x stored in its preferences domain so upgrading
/// to Ezkiel Notch keeps tasks, notes, clips, shelf and water history.
enum IslandDeskMigration {
    private struct LegacyTask: Decodable { var id: UUID; var title: String; var done: Bool }
    private struct LegacyNote: Decodable { var id: UUID; var title: String; var body: String; var updatedAt: Date }
    private struct LegacyClip: Decodable { var id: UUID; var text: String; var createdAt: Date }
    private struct LegacyShelf: Decodable { var id: UUID; var path: String; var bookmark: Data?; var addedAt: Date }
    private struct LegacyWater: Decodable { var id: UUID; var amount: Int; var date: Date }

    static func importLegacyData(into data: inout AppData) {
        guard let defaults = UserDefaults(suiteName: "app.islanddesk.mac") else { return }
        let decoder = JSONDecoder() // IslandDesk used the default (reference-date) strategy.

        func load<T: Decodable>(_ key: String, as type: T.Type) -> T? {
            guard let raw = defaults.data(forKey: "island.\(key)") else { return nil }
            return try? decoder.decode(type, from: raw)
        }

        if data.tasks.isEmpty, let tasks = load("tasks", as: [LegacyTask].self) {
            data.tasks = tasks.map { TaskItem(id: $0.id, title: $0.title, done: $0.done) }
        }
        if data.notes.isEmpty, let notes = load("notes", as: [LegacyNote].self) {
            data.notes = notes
                .filter { $0.title != "Welcome to IslandDesk" }
                .map { NoteItem(id: $0.id, title: $0.title, body: $0.body, updated: $0.updatedAt) }
        }
        if data.clips.isEmpty, let clips = load("clips", as: [LegacyClip].self) {
            data.clips = clips.map { ClipItem(id: $0.id, text: $0.text, captured: $0.createdAt) }
        }
        if data.shelf.isEmpty, let shelf = load("shelf", as: [LegacyShelf].self) {
            data.shelf = shelf.map { ShelfRecord(id: $0.id, path: $0.path, bookmark: $0.bookmark, added: $0.addedAt) }
        }
        if data.water.isEmpty, let water = load("drinks", as: [LegacyWater].self) {
            data.water = water.map { WaterEntry(id: $0.id, date: $0.date, ml: $0.amount) }
        }
        if let goal = defaults.object(forKey: "island.waterGoal") as? Int, goal > 0 {
            data.settings.waterGoal = goal
        }
        if let minutes = defaults.object(forKey: "focusMinutes") as? Int, minutes > 0 {
            data.settings.focusMinutes = minutes
        }
        if let hover = defaults.object(forKey: "island.hover") as? Bool {
            data.settings.expandOnHover = hover
        }
        if let minimal = defaults.object(forKey: "island.minimalIdle") as? Bool {
            data.settings.minimalClosed = minimal
        }
        if let reminder = defaults.object(forKey: "island.waterReminder") as? Bool {
            data.settings.waterReminders = reminder
        }
    }
}
