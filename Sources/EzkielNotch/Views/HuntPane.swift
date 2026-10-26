import AppKit
import NotchCore
import SwiftUI

/// Hunt: tasks with reminders, and Rally (the next meeting) on top when Calendar is on.
struct HuntPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var calendar: CalendarService
    @State private var draft = ""
    @State private var dueChoice: DueChoice = .none

    enum DueChoice: String, CaseIterable, Identifiable {
        case none = "No time", m15 = "15m", h1 = "1h", h3 = "3h", tomorrow = "Tomorrow"
        var id: String { rawValue }

        func date(from now: Date = Date()) -> Date? {
            switch self {
            case .none: return nil
            case .m15: return now.addingTimeInterval(15 * 60)
            case .h1: return now.addingTimeInterval(3600)
            case .h3: return now.addingTimeInterval(3 * 3600)
            case .tomorrow:
                let cal = Calendar.current
                let start = cal.startOfDay(for: now.addingTimeInterval(86_400))
                return cal.date(byAdding: .hour, value: 9, to: start)
            }
        }
    }

    var body: some View {
        let tasks = model.data.tasks
        let doneCount = tasks.filter(\.done).count

        VStack(alignment: .leading, spacing: Theme.Space.s) {
            if model.settings.rally { rally }

            HStack(spacing: 6) {
                NotchField(placeholder: "New task", text: $draft, onSubmit: add)
                IconButton(icon: "plus", label: "Add task (Return)", size: 28, filled: true, accent: .hunt, action: add)
            }
            HStack(spacing: 4) {
                Image(systemName: "bell").font(.system(size: 10.5, weight: .medium)).foregroundStyle(Theme.mist)
                ForEach(DueChoice.allCases) { choice in
                    Chip(title: choice.rawValue, selected: dueChoice == choice, accent: .hunt) { dueChoice = choice }
                }
                Spacer(minLength: 0)
                if doneCount > 0 {
                    TextButton(title: "Clear \(doneCount) done", tint: Theme.mist) { model.clearDoneTasks() }
                }
            }

            if tasks.isEmpty {
                EmptyHint(title: "Nothing on the hunt", subtitle: "Type a task and press Return. Pick a time for a reminder.")
                    .transition(.opacity)
            } else {
                // Due labels and overdue colours refresh every minute on their own.
                TimelineView(.everyMinute) { tl in
                    ScrollView(.vertical, showsIndicators: false) {
                        LazyVStack(spacing: 4) {
                            ForEach(tasks) { task in
                                TaskRow(task: task, now: tl.date,
                                        toggle: { model.toggleTask(task.id) },
                                        delete: { model.deleteTask(task.id) })
                                    .transition(.asymmetric(insertion: .move(edge: .top).combined(with: .opacity),
                                                            removal: .opacity))
                            }
                        }
                        .padding(.vertical, 2)
                    }
                    .scrollEdgeFade(6)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
    }

    @ViewBuilder
    private var rally: some View {
        if calendar.access == .denied {
            HStack(spacing: 8) {
                Image(systemName: "calendar.badge.exclamationmark").foregroundStyle(Accent.rally.deep)
                Text("Rally needs Calendar access").font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
                Spacer()
                TextButton(title: "Open Settings", tint: Accent.rally.light) {
                    NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars")!)
                }
            }
            .padding(.horizontal, 10)
            .frame(height: 30)
            .cardSurface()
        } else if let next = calendar.upcoming.first {
            RallyRow(meeting: next, more: calendar.upcoming.count - 1) { url in model.join(url) }
        }
    }

    private func add() {
        let title = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { return }
        model.addTask(title, due: dueChoice.date())
        draft = ""
        dueChoice = .none
    }
}

/// The next meeting: calendar colour, title, time, and Join when there is a link.
private struct RallyRow: View {
    var meeting: CalendarService.Meeting
    var more: Int
    var join: (URL) -> Void

    var body: some View {
        TimelineView(.everyMinute) { tl in
            let started = meeting.start <= tl.date
            let minutes = Int((meeting.start.timeIntervalSince(tl.date) / 60).rounded(.up))
            HStack(spacing: 8) {
                Capsule().fill(meeting.color).frame(width: 3, height: 20)
                VStack(alignment: .leading, spacing: 0) {
                    Text(meeting.title).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink).lineLimit(1)
                    Text("\(meeting.start.formatted(date: .omitted, time: .shortened))–\(meeting.end.formatted(date: .omitted, time: .shortened))"
                         + (started ? " · now" : minutes < 60 ? " · in \(minutes) min" : "")
                         + (more > 0 ? " · +\(more) more" : ""))
                        .font(Theme.font(Theme.Size.caption))
                        .foregroundStyle(started || minutes <= 5 ? Accent.rally.light : Theme.mist)
                        .lineLimit(1)
                }
                Spacer(minLength: 4)
                if let link = meeting.link {
                    PillButton(title: "Join", icon: "video.fill", prominent: started || minutes <= 10, accent: .rally) { join(link) }
                }
            }
            .padding(.horizontal, 10)
            .frame(height: 34)
            .cardSurface()
        }
    }
}

private struct TaskRow: View {
    var task: TaskItem
    var now: Date
    var toggle: () -> Void
    var delete: () -> Void
    @State private var hovering = false

    var body: some View {
        HStack(spacing: 8) {
            Button(action: toggle) {
                ZStack {
                    Circle().strokeBorder(task.done ? Color.clear : Theme.mist, lineWidth: 1.5)
                        .frame(width: 16, height: 16)
                    if task.done {
                        Circle().fill(Accent.hunt.linear).frame(width: 16, height: 16)
                        Image(systemName: "checkmark").font(.system(size: 8.5, weight: .bold)).foregroundStyle(.white)
                            .transition(.scale.combined(with: .opacity))
                    }
                }
                .frame(width: 24, height: 24)
                .contentShape(Rectangle())
            }
            .buttonStyle(PressableStyle(scale: 0.8))
            .accessibilityLabel(task.done ? "Mark as not done" : "Mark as done")

            Text(task.title)
                .font(Theme.font(Theme.Size.body))
                .foregroundStyle(task.done ? Theme.mist : Theme.ink)
                .strikethrough(task.done, color: Theme.mist)
                .lineLimit(1)
            Spacer(minLength: 6)
            if let due = task.due, !task.done {
                let overdue = due <= now
                Label(due.formatted(Calendar.current.isDateInToday(due) ? .dateTime.hour().minute() : .dateTime.weekday(.abbreviated).hour().minute()),
                      systemImage: overdue ? "bell.badge.fill" : "bell")
                    .font(Theme.font(Theme.Size.caption, .medium))
                    .foregroundStyle(overdue ? Accent.hunt.light : Theme.mist)
            }
            Button(action: delete) {
                Image(systemName: "xmark")
                    .font(.system(size: 9, weight: .semibold))
                    .foregroundStyle(Theme.mist)
                    .frame(width: 24, height: 24)
                    .background(Circle().fill(Color.white.opacity(hovering ? 0.08 : 0)))
            }
            .buttonStyle(PressableStyle())
            .opacity(hovering ? 1 : 0)
            .help("Delete task")
            .accessibilityLabel("Delete task")
        }
        .padding(.leading, 4)
        .padding(.trailing, 4)
        .frame(height: 30)
        .cardSurface(hover: hovering)
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
        .animation(Theme.smooth, value: task.done)
    }
}
