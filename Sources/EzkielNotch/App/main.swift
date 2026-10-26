import AppKit

MainActor.assumeIsolated {
    let args = ProcessInfo.processInfo.arguments

    // Scout senders (`--claude-hook`, `--codex-notify`, `--notify`): hand the event over and exit,
    // before anything connects to the window server.
    if ScoutService.runSender(args) { exit(0) }

    let app = NSApplication.shared

    // `EzkielNotch --render-icon <dir.iconset>` draws the app icon (used by scripts/build_app.sh).
    if let i = args.firstIndex(of: "--render-icon"), i + 1 < args.count {
        exit(IconRenderer.renderIconset(to: args[i + 1]) ? 0 : 1)
    }

    if let i = args.firstIndex(of: "--render-mascot"), i + 1 < args.count {
        exit(IconRenderer.renderMascotSheet(to: args[i + 1]) ? 0 : 1)
    }

    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.accessory)
    withExtendedLifetime(delegate) {
        app.run()
    }
}
