import AppKit
@preconcurrency import AVFoundation
import SwiftUI

/// Mirror: a quick look at yourself before a call. The camera runs only while this is on screen;
/// nothing is recorded or saved.
@MainActor
final class CameraMirror: ObservableObject {
    enum State: Equatable { case idle, asking, running, denied, noCamera }

    @Published private(set) var state: State = .idle
    let session = AVCaptureSession()
    private var configured = false
    private let queue = DispatchQueue(label: "store.ezkielyna.notch.mirror")

    func start() {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            run()
        case .notDetermined:
            state = .asking
            AVCaptureDevice.requestAccess(for: .video) { granted in
                DispatchQueue.main.async {
                    MainActor.assumeIsolated { granted ? self.run() : (self.state = .denied) }
                }
            }
        default:
            state = .denied
        }
    }

    private func run() {
        if !configured {
            guard let device = AVCaptureDevice.default(for: .video),
                  let input = try? AVCaptureDeviceInput(device: device),
                  session.canAddInput(input) else {
                state = .noCamera
                return
            }
            session.beginConfiguration()
            session.sessionPreset = .medium
            session.addInput(input)
            session.commitConfiguration()
            configured = true
        }
        state = .running
        let session = session
        queue.async { if !session.isRunning { session.startRunning() } }
    }

    func stop() {
        let session = session
        queue.async { if session.isRunning { session.stopRunning() } }
        if state == .running { state = .idle }
    }
}

/// The live preview, mirrored like a real mirror.
struct CameraPreview: NSViewRepresentable {
    let session: AVCaptureSession

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        view.wantsLayer = true
        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        if let connection = layer.connection, connection.isVideoMirroringSupported {
            connection.automaticallyAdjustsVideoMirroring = false
            connection.isVideoMirrored = true
        }
        view.layer = layer
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

/// Fills the page of the open notch; Esc or the close button puts it away.
struct MirrorOverlay: View {
    @ObservedObject var mirror: CameraMirror
    var close: () -> Void

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: Theme.Radius.card, style: .continuous).fill(Color(white: 0.06))
            switch mirror.state {
            case .running:
                CameraPreview(session: mirror.session)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.card, style: .continuous))
            case .asking, .idle:
                ProgressView().controlSize(.small)
            case .denied:
                message("Camera access is off", "System Settings › Privacy & Security › Camera") {
                    NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Camera")!)
                }
            case .noCamera:
                message("No camera found", "Connect one, or open the lid", action: nil)
            }
        }
        .overlay(alignment: .topTrailing) {
            IconButton(icon: "xmark", label: L("Close mirror"), size: 26, tint: Theme.ink, action: close)
                .padding(8)
        }
        .overlay(alignment: .bottomLeading) {
            if mirror.state == .running {
                Label(L("Only you can see this"), systemImage: "lock.fill")
                    .font(Theme.font(Theme.Size.caption, .medium))
                    .foregroundStyle(.white.opacity(0.85))
                    .padding(.horizontal, 8).padding(.vertical, 4)
                    .background(Capsule().fill(.black.opacity(0.45)))
                    .padding(8)
            }
        }
        .onAppear { mirror.start() }
        .onDisappear { mirror.stop() }
    }

    private func message(_ title: String, _ detail: String, action: (() -> Void)?) -> some View {
        VStack(spacing: 6) {
            Image(systemName: "video.slash").font(.system(size: 18, weight: .medium)).foregroundStyle(Theme.mist)
            Text(L(title)).font(Theme.font(Theme.Size.body, .semibold)).foregroundStyle(Theme.ink)
            Text(L(detail)).font(Theme.font(Theme.Size.secondary)).foregroundStyle(Theme.mist)
            if let action { TextButton(title: L("Open Settings"), action: action) }
        }
    }
}
