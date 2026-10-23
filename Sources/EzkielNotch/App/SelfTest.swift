import AppKit
import NotchCore

/// `EzkielNotch --self-test` drives the real panel through every state and checks
/// that clicks land on the notch (and pass through everywhere else). Exit code 0 = pass.
@MainActor
enum SelfTest {
    private static var failures: [String] = []

    static func run(model: AppModel, controller: NotchWindowController) {
        var steps: [(String, () -> Void)] = []

        steps.append(("panel on screen", {
            check(controller.panel.isVisible, "panel is visible")
            if let screen = controller.targetScreen() {
                check(abs(controller.panel.frame.maxY - screen.frame.maxY) < 0.5, "panel is flush with the top edge")
            }
            checkClicks(model: model, controller: controller, label: "compact")
        }))

        steps.append(("expand", { model.expand(.focus) }))
        steps.append(("expanded clicks", {
            check(model.presentation == .expanded, "expanded after expand()")
            checkClicks(model: model, controller: controller, label: "expanded")
        }))

        for tab in NotchTab.allCases {
            steps.append(("tab \(tab.title)", {
                model.select(tab)
                check(model.tab == tab, "selected \(tab.title)")
                let r = model.shapeRect
                check(controller.hitTest(topLeft: CGPoint(x: r.midX, y: r.midY)) != nil, "\(tab.title): content accepts clicks")
            }))
        }

        steps.append(("focus timer", {
            model.select(.focus)
            model.focusStartPause()
            check(model.focus.isRunning, "focus timer starts")
            model.focusStartPause()
            check(model.focus.isPaused, "focus timer pauses")
            model.focusReset()
        }))

        steps.append(("collapse", { model.collapse() }))
        steps.append(("compact again", {
            check(model.presentation == .compact, "collapsed after collapse()")
            let expandedArea = CGPoint(x: model.panelSize.width / 2, y: model.panelSize.height - 40)
            check(controller.hitTest(topLeft: expandedArea) == nil, "area below the closed notch passes clicks through")
        }))

        steps.append(("peek", {
            model.showPeek(Peek(icon: "sparkle", title: "Self test", subtitle: "Peek card"))
            check(model.presentation == .peek, "peek shows")
        }))
        steps.append(("peek tap opens", {
            model.openPeek()
            check(model.presentation == .expanded, "tapping a peek opens the notch")
            model.collapse()
        }))

        steps.append(("lion icon", {
            check(IconRenderer.statusImage().size.width > 0, "lion renders")
        }))

        for (i, step) in steps.enumerated() {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8 + Double(i) * 0.7) {
                print("▶︎ \(step.0)")
                step.1()
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5 + Double(steps.count) * 0.7) {
            print(failures.isEmpty ? "SELF-TEST PASSED" : "SELF-TEST FAILED: \(failures.count) check(s)")
            fflush(stdout)
            exit(failures.isEmpty ? 0 : 1)
        }
    }

    private static func checkClicks(model: AppModel, controller: NotchWindowController, label: String) {
        let r = model.shapeRect
        check(controller.hitTest(topLeft: CGPoint(x: r.midX, y: r.midY)) != nil, "\(label): notch accepts clicks")
        check(controller.hitTest(topLeft: CGPoint(x: 2, y: model.panelSize.height - 2)) == nil, "\(label): corner passes clicks through")
    }

    private static func check(_ ok: Bool, _ message: String) {
        print("  \(ok ? "✓" : "✗") \(message)")
        if !ok { failures.append(message) }
    }
}
