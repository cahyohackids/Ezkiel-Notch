import AppKit

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let args = ProcessInfo.processInfo.arguments

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
