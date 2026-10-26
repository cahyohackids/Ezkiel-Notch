// Reads a data.json with the Mac app's own NotchCore code and writes it back (see check_format.sh).
import Foundation

let args = CommandLine.arguments
guard args.count == 3 else { print("usage: format-check <in.json> <out.json>"); exit(2) }
let store = DataStore(url: URL(fileURLWithPath: args[1]))
let data = store.load()
if let recovery = store.recovery { print("RECOVERY: \(recovery)"); exit(1) }
let s = data.settings
print("tasks=\(data.tasks.count) notes=\(data.notes.count) clips=\(data.clips.count) water=\(data.water.count) focusLog=\(data.focusLog.count)",
      "focusState=\(data.focusState.map { "\($0.mode.rawValue)/\(Int($0.duration))" } ?? "nil")",
      "focus=\(s.focusMinutes) break=\(s.breakMinutes) goal=\(s.waterGoal) lang=\(s.language.rawValue) hotkey=\(s.hotkey.rawValue) musicAllowed=\(s.musicAllowed)",
      "shelf=\(data.shelf.count) scout=\(data.scoutLog.count)")
for t in data.tasks { print("task", t.title, t.done, t.due.map { "\($0)" } ?? "-") }
DataStore(url: URL(fileURLWithPath: args[2])).save(data, synchronously: true)
