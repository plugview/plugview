import AVFoundation
import AppKit
import PlugviewCore
import PlugviewUI
import SwiftUI

enum PlayerCommand {
    case toggleMute
    case changeVolume(byPercent: Int)
}

/// The single resizable window: live preview, the launcher while no video is on screen, and key shortcuts.
@MainActor
final class PlayerWindowController: NSWindowController, NSWindowDelegate {
    private let playerView: PlayerView

    init(session: AVCaptureSession, launcher: LauncherModel, onCommand: @escaping (PlayerCommand) -> Void) {
        playerView = PlayerView(session: session, launcher: launcher, onCommand: onCommand)
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1280, height: 720),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "plugview"
        // Dark in light mode too: a light title bar over the CRT surface and black video looks cheap.
        window.appearance = NSAppearance(named: .darkAqua)
        window.titlebarAppearsTransparent = true
        window.backgroundColor = NSColor(srgbRed: 0x0C / 255, green: 0x0C / 255, blue: 0x0E / 255, alpha: 1)
        window.contentView = playerView
        window.collectionBehavior.insert(.fullScreenPrimary)
        window.center()
        window.setFrameAutosaveName("PlayerWindow")
        super.init(window: window)
        window.delegate = self
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("PlayerWindowController is created in code only")
    }

    /// Titles the window with the device, e.g. "UGREEN 25773" over "1920×1080 @ 60 fps · audio from UGREEN 25773".
    func showLiveVideo(deviceName: String, details: String, width: Int, height: Int) {
        window?.title = deviceName
        window?.subtitle = details
        window?.contentAspectRatio = NSSize(width: width, height: height)
        fitContentHeight(toAspectRatio: Double(width) / Double(height))
        playerView.isLauncherVisible = false
    }

    /// The restored frame may come from a source with another shape; keep its width and fix its height
    /// so the video fills the window instead of showing black bars.
    private func fitContentHeight(toAspectRatio aspectRatio: Double) {
        guard let window, !window.styleMask.contains(.fullScreen), aspectRatio > 0 else { return }
        let contentRect = window.contentRect(forFrameRect: window.frame)
        let fittedHeight = (contentRect.width / aspectRatio).rounded()
        guard abs(fittedHeight - contentRect.height) >= 1 else { return }

        // Grow or shrink downward so the title bar stays where the user left it.
        let fittedRect = NSRect(x: contentRect.minX, y: contentRect.maxY - fittedHeight, width: contentRect.width, height: fittedHeight)
        window.setFrame(window.frameRect(forContentRect: fittedRect), display: true, animate: false)
    }

    func showLauncher() {
        window?.title = "plugview"
        window?.subtitle = ""
        playerView.isLauncherVisible = true
    }

    func windowDidEnterFullScreen(_ notification: Notification) {
        NSCursor.setHiddenUntilMouseMoves(true)
    }
}

private final class PlayerView: NSView {
    private let previewLayer: AVCaptureVideoPreviewLayer
    private let launcherView: NSHostingView<LauncherView>
    private let onCommand: (PlayerCommand) -> Void

    init(session: AVCaptureSession, launcher: LauncherModel, onCommand: @escaping (PlayerCommand) -> Void) {
        previewLayer = AVCaptureVideoPreviewLayer(session: session)
        previewLayer.videoGravity = .resizeAspect
        previewLayer.backgroundColor = NSColor.black.cgColor
        launcherView = NSHostingView(rootView: LauncherView(model: launcher))
        self.onCommand = onCommand
        super.init(frame: .zero)
        wantsLayer = true

        launcherView.autoresizingMask = [.width, .height]
        addSubview(launcherView)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("PlayerView is created in code only")
    }

    override func makeBackingLayer() -> CALayer { previewLayer }

    override var acceptsFirstResponder: Bool { true }

    /// Hidden while video plays, so it never covers the picture or takes clicks.
    var isLauncherVisible: Bool {
        get { !launcherView.isHidden }
        set { launcherView.isHidden = !newValue }
    }

    override func layout() {
        super.layout()
        launcherView.frame = bounds
    }

    override func keyDown(with event: NSEvent) {
        switch (event.keyCode, event.charactersIgnoringModifiers?.lowercased()) {
        case (_, "f"): window?.toggleFullScreen(nil)
        case (_, "m"): onCommand(.toggleMute)
        case (_, "q"): NSApp.terminate(nil)
        case (Self.escapeKeyCode, _) where window?.styleMask.contains(.fullScreen) == true: window?.toggleFullScreen(nil)
        case (Self.upArrowKeyCode, _): onCommand(.changeVolume(byPercent: Tuning.volumeStepPercent))
        case (Self.downArrowKeyCode, _): onCommand(.changeVolume(byPercent: -Tuning.volumeStepPercent))
        default: super.keyDown(with: event)
        }
    }

    private static let escapeKeyCode: UInt16 = 53
    private static let downArrowKeyCode: UInt16 = 125
    private static let upArrowKeyCode: UInt16 = 126
}
