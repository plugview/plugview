import AVFoundation
import AppKit
import PlugviewCore
import PlugviewUI
import SwiftUI

enum PlayerCommand {
    case toggleMute
    case changeVolume(byPercent: Int)
    case setVolume(percent: Int)
}

/// The single resizable window: live preview, the launcher while no video is on screen, and key shortcuts.
///
/// There is no visible title bar: the video fills the window, and the window buttons and the controls fade in
/// on mouse movement and out again when the mouse rests, so nothing covers the picture while you play.
@MainActor
final class PlayerWindowController: NSWindowController, NSWindowDelegate {
    private let playerView: PlayerView
    private var isPlaying = false
    private var hideChromeTask: Task<Void, Never>?

    init(session: AVCaptureSession, launcher: LauncherModel, controls: ControlsModel, onCommand: @escaping (PlayerCommand) -> Void) {
        playerView = PlayerView(session: session, launcher: launcher, controls: controls, onCommand: onCommand)
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1280, height: 720),
            styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        // The title still names the window in Mission Control and the Window menu; it is just not drawn.
        window.title = "plugview"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.isMovableByWindowBackground = true
        // Dark in light mode too, so the window buttons and controls match the CRT surface and the video.
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = .black
        window.contentView = playerView
        window.collectionBehavior.insert(.fullScreenPrimary)
        window.center()
        window.setFrameAutosaveName("PlayerWindow")
        super.init(window: window)
        window.delegate = self
        playerView.onMouseActivity = { [weak self] in self?.revealChrome() }
        playerView.onMouseExit = { [weak self] in self?.hideChrome() }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("PlayerWindowController is created in code only")
    }

    /// Names the window after the device, e.g. "UGREEN 25773" with "1920×1080 @ 60 fps · audio from UGREEN 25773".
    func showLiveVideo(deviceName: String, details: String, width: Int, height: Int) {
        window?.title = deviceName
        window?.subtitle = details
        window?.contentAspectRatio = NSSize(width: width, height: height)
        fitContentHeight(toAspectRatio: Double(width) / Double(height))
        playerView.isLauncherVisible = false
        isPlaying = true
        hideChrome()
    }

    /// The restored frame may come from a source with another shape; keep its width and fix its height
    /// so the video fills the window instead of showing black bars.
    private func fitContentHeight(toAspectRatio aspectRatio: Double) {
        guard let window, !window.styleMask.contains(.fullScreen), aspectRatio > 0 else { return }
        let contentRect = window.contentRect(forFrameRect: window.frame)
        let fittedHeight = (contentRect.width / aspectRatio).rounded()
        guard abs(fittedHeight - contentRect.height) >= 1 else { return }

        // Grow or shrink downward so the top edge stays where the user left it.
        let fittedRect = NSRect(x: contentRect.minX, y: contentRect.maxY - fittedHeight, width: contentRect.width, height: fittedHeight)
        window.setFrame(window.frameRect(forContentRect: fittedRect), display: true, animate: false)
    }

    func showLauncher() {
        window?.title = "plugview"
        window?.subtitle = ""
        playerView.isLauncherVisible = true
        isPlaying = false
        // Nothing to cover: the window buttons stay visible and the controls go away.
        hideChromeTask?.cancel()
        setChrome(isWindowButtonsVisible: true, isControlsVisible: false)
    }

    func windowDidEnterFullScreen(_ notification: Notification) {
        NSCursor.setHiddenUntilMouseMoves(true)
    }

    // MARK: - Chrome over the video

    private func revealChrome() {
        guard isPlaying else { return }
        setChrome(isWindowButtonsVisible: true, isControlsVisible: true)
        hideChromeTask?.cancel()
        hideChromeTask = Task { [weak self] in
            do {
                try await Task.sleep(for: .seconds(Tuning.controlsHideSeconds))
            } catch {
                return
            }
            self?.hideChromeUnlessHovered()
        }
    }

    private func hideChromeUnlessHovered() {
        // Keep the controls up while the pointer rests on them, e.g. before dragging the volume.
        if playerView.isMouseOverControls {
            revealChrome()
        } else {
            hideChrome()
        }
    }

    private func hideChrome() {
        guard isPlaying else { return }
        hideChromeTask?.cancel()
        setChrome(isWindowButtonsVisible: false, isControlsVisible: false)
        if window?.styleMask.contains(.fullScreen) == true { NSCursor.setHiddenUntilMouseMoves(true) }
    }

    private func setChrome(isWindowButtonsVisible: Bool, isControlsVisible: Bool) {
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.2
            // In full screen macOS shows its own title bar on hover; leave it alone.
            if window?.styleMask.contains(.fullScreen) == false {
                window?.standardWindowButton(.closeButton)?.superview?.animator().alphaValue = isWindowButtonsVisible ? 1 : 0
            }
            playerView.setControlsVisible(isControlsVisible)
        }
    }
}

/// Live video with the launcher and the controls on top, and the key shortcuts.
private final class PlayerView: NSView {
    private let previewLayer: AVCaptureVideoPreviewLayer
    private let launcherView: NSHostingView<LauncherView>
    private let controlsView: NSHostingView<ControlsView>
    private let onCommand: (PlayerCommand) -> Void
    var onMouseActivity: () -> Void = {}
    var onMouseExit: () -> Void = {}

    init(session: AVCaptureSession, launcher: LauncherModel, controls: ControlsModel, onCommand: @escaping (PlayerCommand) -> Void) {
        previewLayer = AVCaptureVideoPreviewLayer(session: session)
        previewLayer.videoGravity = .resizeAspect
        previewLayer.backgroundColor = NSColor.black.cgColor
        launcherView = NSHostingView(rootView: LauncherView(model: launcher))
        controlsView = NSHostingView(rootView: ControlsView(model: controls))
        self.onCommand = onCommand
        super.init(frame: .zero)
        wantsLayer = true

        launcherView.autoresizingMask = [.width, .height]
        addSubview(launcherView)

        controlsView.translatesAutoresizingMaskIntoConstraints = false
        controlsView.alphaValue = 0
        controlsView.isHidden = true
        addSubview(controlsView)
        NSLayoutConstraint.activate([
            controlsView.centerXAnchor.constraint(equalTo: centerXAnchor),
            controlsView.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -20),
        ])
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("PlayerView is created in code only")
    }

    override func makeBackingLayer() -> CALayer { previewLayer }

    override var acceptsFirstResponder: Bool { true }

    override var mouseDownCanMoveWindow: Bool { true }

    /// Hidden while video plays, so it never covers the picture or takes clicks.
    var isLauncherVisible: Bool {
        get { !launcherView.isHidden }
        set { launcherView.isHidden = !newValue }
    }

    var isMouseOverControls: Bool {
        guard let window, !controlsView.isHidden else { return false }
        return controlsView.frame.contains(convert(window.mouseLocationOutsideOfEventStream, from: nil))
    }

    /// Fades the controls in or out (hidden ones take no clicks); call inside an animation group.
    func setControlsVisible(_ isVisible: Bool) {
        if isVisible { controlsView.isHidden = false }
        controlsView.animator().alphaValue = isVisible ? 1 : 0
        if !isVisible {
            // Hide after the fade, unless the controls were shown again meanwhile.
            NSAnimationContext.current.completionHandler = { [weak self] in
                MainActor.assumeIsolated {
                    guard let self, self.controlsView.alphaValue == 0 else { return }
                    self.controlsView.isHidden = true
                }
            }
        }
    }

    override func layout() {
        super.layout()
        launcherView.frame = bounds
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        for area in trackingAreas { removeTrackingArea(area) }
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self))
    }

    override func mouseMoved(with event: NSEvent) { onMouseActivity() }

    override func mouseEntered(with event: NSEvent) { onMouseActivity() }

    override func mouseExited(with event: NSEvent) { onMouseExit() }

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
