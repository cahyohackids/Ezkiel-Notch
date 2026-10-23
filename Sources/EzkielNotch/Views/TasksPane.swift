import AppKit
import NotchCore
import SwiftUI

struct TasksPane: View {
    @ObservedObject var model: AppModel
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

        VStack(alignment: .leading, spacing: 9) {
            HStack(spacing: 8) {
                NotchField(placeholder: "Add something to do…", text: $draft, onSubmit: add)
                IconButton(icon: "plus", size: 30, filled: true, help: "Add task (Return)", action: add)
            }
            HStack(spacing: 5) {
                Image(systemName: "bell").font(.system(size: 9.5, weight: .bold)).foregroundStyle(Theme.mist)
                ForEach(DueChoice.allCases) { choice in
                    Chip(title: choice.rawValue, selected: dueChoice == choice, tint: Theme.orange) {
                        dueChoice = choice
                    }
                    .scaleEffect(0.9)
                }
                Spacer(minLength: 0)
                Text("\(doneCount)/\(tasks.count) done")
                    .font(Theme.rounded(10, .semibold))
                    .foregroundStyle(Theme.mist)
                    .contentTransition(.numericText())
                if doneCount > 0 {
                    Button("Clear done") { model.clearDoneTasks() }
                        .buttonStyle(PressableStyle())
                        .font(Theme.rounded(10, .semibold))
                        .foregroundStyle(Theme.gold)
                }
            }

            if tasks.isEmpty {
                EmptyHint(title: "A clear list is a good start.", subtitle: "Type a task and press Return. Add a time and Ezkiel will remind you.")
                    .transition(.opacity)
            } else {
                ScrollView(.vertical, showsIndicators: false) {
                    LazyVStack(spacing: 5) {
                        ForEach(tasks) { task in
                            TaskRow(task: task,
                                    toggle: { model.toggleTask(task.id) },
                                    delete: { model.deleteTask(task.id) })
                                .transition(.asymmetric(insertion: .move(edge: .top).combined(with: .opacity),
                                                        removal: .scale(scale: 0.9).combined(with: .opacity)))
                        }
                    }
                    .padding(.bottom, 4)
                }
            }
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

private struct TaskRow: View {
    var task: TaskItem
    var toggle: () -> Void
    var delete: () -> Void
    @State private var hovering = false

    var body: some View {
        HStack(spacing: 10) {
            Button(action: toggle) {
                ZStack {
                    Circle().strokeBorder(task.done ? Color.clear : Theme.mist.opacity(0.7), lineWidth: 1.5)
                    if task.done {
                        Circle().fill(Theme.tealGradient)
                        Image(systemName: "checkmark").font(.system(size: 9, weight: .heavy)).foregroundStyle(Theme.navy)
                            .transition(.scale.combined(with: .opacity))
                    }
                }
                .frame(width: 18, height: 18)
            }
            .buttonStyle(PressableStyle(scale: 0.8))

            Text(task.title)
                .font(Theme.rounded(12.5, .medium))
                .foregroundStyle(task.done ? Theme.mist : Theme.ink)
                .strikethrough(task.done, color: Theme.mist)
                .lineLimit(1)
            Spacer(minLength: 6)
            if let due = task.due, !task.done {
                let overdue = due <= Date()
                Label(due.formatted(Calendar.current.isDateInToday(due) ? .dateTime.hour().minute() : .dateTime.weekday(.abbreviated).hour().minute()),
                      systemImage: overdue ? "bell.badge.fill" : "bell")
                    .font(Theme.rounded(10, .semibold))
                    .foregroundStyle(overdue ? Theme.berry : Theme.orange)
                    .padding(.horizontal, 7)
                    .frame(height: 20)
                    .background(Capsule().fill((overdue ? Theme.berry : Theme.orange).opacity(0.14)))
            }
            Button(action: delete) {
                Image(systemName: "xmark")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundStyle(Theme.mist)
                    .frame(width: 20, height: 20)
                    .background(Circle().fill(Color.white.opacity(hovering ? 0.1 : 0)))
            }
            .buttonStyle(PressableStyle())
            .opacity(hovering ? 1 : 0.35)
            .help("Delete task")
        }
        .padding(.horizontal, 11)
        .frame(height: 34)
        .background(RoundedRectangle(cornerRadius: 11, style: .continuous)
            .fill(task.done ? Theme.teal.opacity(0.07) : Color.white.opacity(hovering ? 0.09 : 0.055)))
        .onHover { h in withAnimation(Theme.soft) { hovering = h } }
        .animation(Theme.spring, value: task.done)
    }
}
