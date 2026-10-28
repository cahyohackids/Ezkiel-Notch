import AppKit
import NotchCore
import SwiftUI

/// Hunt: tasks with reminders, and Rally (the next meeting) on top when Calendar is on.
struct HuntPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var calendar: CalendarService
    @State private var draft = ""
    @State private var dueChoice: DueChoice = .none
    /// A time from "Pick time…" (it wins over the chips).
    @State private var picked: Date?
    @State private var picking = false
    @State private var pickHour = 9
    @State private var pickMinute = 0
    @State private var pickTomorrow = false
    /// The × on a typed time: keep those words as part of the task.
    @State private var ignoreTyped = false

    /// A time typed into the task ("kul 3", "besok jam 9"), unless you said to ignore it.
    private var typed: DuePhrase.Result? {
        ignoreTyped ? nil : DuePhrase.parse(draft)
    }

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
                if let typed {
                    // The time you typed, before Return: what the reminder will be.
                    Chip(title: "⏰ " + Self.when(typed.due), selected: true, accent: .hunt) {}
                    IconButton(icon: "xmark", label: L("Keep these words in the task"), size: 22, tint: Theme.mist) {
                        ignoreTyped = true
                    }
                } else if picking {
                    timePicker
                } else {
                    ForEach(DueChoice.allCases) { choice in
                        Chip(title: choice.rawValue, selected: picked == nil && dueChoice == choice, accent: .hunt) {
                            dueChoice = choice
                            picked = nil
                        }
                    }
                    Chip(title: picked.map { "⏰ " + Self.when($0) } ?? L("Pick time…"), selected: picked != nil, accent: .hunt) {
                        startPicking()
                    }
                }
                Spacer(minLength: 0)
                if doneCount > 0 {
                    TextButton(title: L("Clear %d done", doneCount), tint: Theme.mist) { model.clearDoneTasks() }
                }
                IconButton(icon: "person.crop.square", label: L("Mirror: check your camera"), size: 24, tint: Theme.mist) {
                    model.setMirror(true)
                }
            }

            .onChange(of: draft) { text in
                if text.isEmpty { ignoreTyped = false }
            }
            .animation(Theme.snappy, value: typed?.due)
            .animation(Theme.snappy, value: picking)

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
                Text(L("Rally needs Calendar access")).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
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

    /// Today/Tomorrow, the hour and the minute (5-minute steps), then Set.
    private var timePicker: some View {
        HStack(spacing: 4) {
            Chip(title: "Today", selected: !pickTomorrow, accent: .hunt) { pickTomorrow = false }
            Chip(title: "Tomorrow", selected: pickTomorrow, accent: .hunt) { pickTomorrow = true }
            TimeStepper(value: String(format: "%02d", pickHour), label: L("Hour")) { step in
                pickHour = (pickHour + step + 24) % 24
            }
            Text(":").font(Theme.font(Theme.Size.secondary, .semibold)).foregroundStyle(Theme.mist)
            TimeStepper(value: String(format: "%02d", pickMinute), label: L("Minute")) { step in
                pickMinute = (pickMinute + step * 5 + 60) % 60
            }
            Chip(title: "Set", selected: true, accent: .hunt) {
                picked = pickedDate()
                picking = false
            }
            IconButton(icon: "xmark", label: L("Cancel"), size: 22, tint: Theme.mist) {
                picking = false
                picked = nil
            }
        }
    }

    private func startPicking() {
        // Starts at the next whole hour (or where you left it).
        let cal = Calendar.current
        let start = picked ?? cal.date(byAdding: .hour, value: 1, to: Date())!
        pickHour = cal.component(.hour, from: start)
        pickMinute = picked == nil ? 0 : cal.component(.minute, from: start)
        pickTomorrow = !cal.isDateInToday(start)
        picking = true
    }

    private func pickedDate(now: Date = Date()) -> Date {
        let cal = Calendar.current
        let day = cal.date(byAdding: .day, value: pickTomorrow ? 1 : 0, to: cal.startOfDay(for: now))!
        return cal.date(bySettingHour: pickHour, minute: pickMinute, second: 0, of: day)!
    }

    /// "15:00", "Tomorrow 09:00", "Fri 10:00"
    static func when(_ date: Date, now: Date = Date()) -> String {
        let cal = Calendar.current
        let time = date.formatted(.dateTime.locale(Lang.locale).hour(.twoDigits(amPM: .omitted)).minute())
        if cal.isDate(date, inSameDayAs: now) { return time }
        if let tomorrow = cal.date(byAdding: .day, value: 1, to: now), cal.isDate(date, inSameDayAs: tomorrow) {
            return L("Tomorrow") + " " + time
        }
        return date.formatted(.dateTime.locale(Lang.locale).weekday(.abbreviated)) + " " + time
    }

    private func add() {
        var title = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { return }
        var due = picked ?? dueChoice.date()
        if let typed, !typed.title.isEmpty {
            title = typed.title
            due = typed.due
        }
        model.addTask(title, due: due)
        draft = ""
        dueChoice = .none
        picked = nil
        picking = false
        ignoreTyped = false
    }
}

/// The next meeting: calendar colour, title, time, and Join when there is a link.
private struct RallyRow: View {
    var meeting: CalendarService.Meeting
    var more: Int
    var join: (URL) -> Void

    /// "10:00–10:30 · in 4 min · +1 more"
    static func when(_ m: CalendarService.Meeting, started: Bool, minutes: Int, more: Int) -> String {
        var text = "\(m.start.formatted(date: .omitted, time: .shortened))–\(m.end.formatted(date: .omitted, time: .shortened))"
        if started {
            text += " · " + L("now")
        } else if minutes < 60 {
            text += " · " + L("in %d min", minutes)
        }
        if more > 0 { text += " · " + L("+%d more", more) }
        return text
    }

    var body: some View {
        TimelineView(.everyMinute) { tl in
            let started = meeting.start <= tl.date
            let minutes = Int((meeting.start.timeIntervalSince(tl.date) / 60).rounded(.up))
            HStack(spacing: 8) {
                Capsule().fill(meeting.color).frame(width: 3, height: 20)
                VStack(alignment: .leading, spacing: 0) {
                    Text(meeting.title).font(Theme.font(Theme.Size.body, .medium)).foregroundStyle(Theme.ink).lineLimit(1)
                    Text(Self.when(meeting, started: started, minutes: minutes, more: more))
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
                Label(due.formatted(Calendar.current.isDateInToday(due) ? .dateTime.hour().minute() : .dateTime.locale(Lang.locale).weekday(.abbreviated).hour().minute()),
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

/// − value + in a capsule, for the time picker (the wheel works too).
private struct TimeStepper: View {
    var value: String
    var label: String
    var step: (Int) -> Void

    var body: some View {
        HStack(spacing: 0) {
            button("chevron.left", -1)
            Text(value)
                .font(Theme.font(Theme.Size.secondary, .semibold).monospacedDigit())
                .foregroundStyle(Theme.ink)
                .frame(minWidth: 20)
            button("chevron.right", 1)
        }
        .frame(height: 24)
        .background(Capsule().fill(Color.white.opacity(0.06)))
        .accessibilityElement(children: .combine)
        .accessibilityLabel(label)
        .accessibilityValue(value)
        .accessibilityAdjustableAction { d in step(d == .increment ? 1 : -1) }
    }

    private func button(_ icon: String, _ by: Int) -> some View {
        Button { step(by) } label: {
            Image(systemName: icon).font(.system(size: 8.5, weight: .bold)).foregroundStyle(Theme.mist)
                .frame(width: 18, height: 24).contentShape(Rectangle())
        }
        .buttonStyle(PressableStyle())
    }
}
