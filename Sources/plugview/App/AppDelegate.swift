import AVFoundation
import AppKit
import PlugviewCapture
import PlugviewCore

/// Wires permissions, device hot-plug, the capture session, and the window together.
///
/// Every change goes through `send(_:)`, which moves `state` and updates the window. Terminal launches
/// also print status lines and exit when camera access is denied, exactly as v0.1.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let options: RunOptions
    private let launchMode: LaunchMode
    private var state: LauncherState = .permissionNeeded(.camera)
    private let capture = CaptureSession()
    private lazy var windowController = PlayerWindowController(session: capture.session) { [weak self] command in
        self?.perform(command)
    }

    private var attachedDeviceIDs: Set<String> = []
    private var isAttaching = false
    private var isAudioPermitted = false
    private var volumePercent: Int
    private var isMuted = false
    private var hasReportedWaiting = false
    private var notificationObservers: [any NSObjectProtocol] = []
    private var displaySleepActivity: (any NSObjectProtocol)?

    init(options: RunOptions, launchMode: LaunchMode) {
        self.options = options
        self.launchMode = launchMode
        volumePercent = options.volumePercent
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.mainMenu = makeMainMenu()
        windowController.showWindow(nil)
        send(.permissionRequested(.camera))
        NSApp.activate()
        // Toggling during launch is ignored; wait until the window is on screen and the app is active.
        if options.startsInFullScreen {
            DispatchQueue.main.async { [weak self] in self?.windowController.window?.toggleFullScreen(nil) }
        }

        // A game controller is not keyboard/mouse input, so without this the display would sleep mid-game.
        // latencyCritical also keeps App Nap and timer coalescing away from the capture pipeline.
        displaySleepActivity = ProcessInfo.processInfo.beginActivity(
            options: [.idleDisplaySleepDisabled, .userInitiated, .latencyCritical],
            reason: "Showing live capture video"
        )
        observeDeviceChanges()

        Task {
            guard await requestPermissions() else { return }
            await attachIfPossible()
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        capture.stop()
        if let displaySleepActivity { ProcessInfo.processInfo.endActivity(displaySleepActivity) }
    }

    // MARK: - Permissions

    /// Returns whether plugview can play; without camera access a terminal launch exits, as in v0.1.
    private func requestPermissions() async -> Bool {
        guard await Permissions.isGranted(for: .video) else {
            switch launchMode {
            case .terminal:
                printError(Permissions.cameraDeniedMessage)
                exit(EXIT_FAILURE)
            case .app:
                send(.permissionDenied(.camera))
                return false
            }
        }
        if options.isAudioEnabled {
            isAudioPermitted = await Permissions.isGranted(for: .audio)
            if !isAudioPermitted { printError(Permissions.microphoneDeniedMessage) }
        }
        send(.permissionsGranted)
        return true
    }

    // MARK: - Attaching devices

    /// Reports the connected devices and, when nothing is playing, starts the best one.
    private func attachIfPossible() async {
        send(.devicesChanged(playableVideoDevices(from: DeviceCatalog.videoDevices(), matching: options.videoQuery)))
        guard attachedDeviceIDs.isEmpty, !isAttaching else { return }

        switch state {
        case .ready(let devices):
            guard let video = devices.first else { return }
            await attach(video)
        case .waitingForSignal:
            reportWaiting()
        case .permissionNeeded, .permissionDenied, .playing, .failed:
            return
        }
    }

    private func attach(_ video: DeviceInfo) async {
        let audio = selectAudio(for: video)

        isAttaching = true
        defer { isAttaching = false }
        do {
            let activeCapture = try await capture.start(videoID: video.uniqueID, audioID: audio?.uniqueID, volume: effectiveVolume)
            attachedDeviceIDs = Set([video.uniqueID, audio?.uniqueID].compactMap { $0 })
            hasReportedWaiting = false
            send(.playbackStarted(video))
            showAttached(activeCapture)
        } catch {
            printError("\(error)")
            send(.playbackFailed(CaptureFailure(deviceName: video.name, reason: "\(error)")))
        }
    }

    private func selectAudio(for video: DeviceInfo) -> DeviceInfo? {
        guard options.isAudioEnabled, isAudioPermitted else { return nil }

        let audio = selectAudioDevice(from: DeviceCatalog.audioDevices(), for: video, matching: options.audioQuery)
        if audio == nil {
            printError("no audio input matched \(video.name); playing without sound. Run 'plugview --list' and pass '--audio <name>'.")
        }
        return audio
    }

    private func detach(reason: String) {
        guard !attachedDeviceIDs.isEmpty else { return }
        attachedDeviceIDs = []
        capture.stop()
        print(reason)
        send(.playbackStopped)
    }

    private var waitingTarget: String { options.videoQuery.map { "'\($0)'" } ?? "a capture device" }

    private func reportWaiting() {
        guard !hasReportedWaiting else { return }
        hasReportedWaiting = true
        print("Waiting for \(waitingTarget)… (run 'plugview --list' to see detected devices)")
    }

    private func showAttached(_ activeCapture: ActiveCapture) {
        let mode = activeCapture.mode
        let audioSummary = activeCapture.audioName.map { "audio from \($0)" } ?? "no audio"
        print("Showing \(activeCapture.videoName) · \(mode.summary), \(audioSummary)")
        windowController.showLiveVideo(
            deviceName: activeCapture.videoName, details: "\(mode.summary) · \(audioSummary)", width: mode.width, height: mode.height)
    }

    // MARK: - State

    private func send(_ event: LauncherEvent) {
        state = nextLauncherState(after: state, on: event)
        if let message = statusMessage(for: state) { windowController.showStatus(message) }
    }

    /// The plain-text window for each state (styled in Phase 1.4); `nil` while video is on screen.
    private func statusMessage(for state: LauncherState) -> String? {
        switch state {
        case .permissionNeeded(.camera):
            "Waiting for camera permission…\nAllow it in the macOS prompt if one appears."
        case .permissionNeeded(.microphone):
            "Waiting for microphone permission…\nAllow it in the macOS prompt if one appears."
        case .permissionDenied(.camera):
            "Camera access is denied, so the capture card cannot be shown.\nAllow plugview in System Settings › Privacy & Security › Camera."
        case .permissionDenied(.microphone):
            "Microphone access is denied, so the console cannot be heard.\nAllow plugview in System Settings › Privacy & Security › Microphone."
        case .waitingForSignal:
            "Waiting for \(waitingTarget)…\nPlug it in and it will appear here."
        case .ready(let devices):
            "Starting \(devices.first?.name ?? "the capture device")…"
        case .playing:
            nil
        case .failed(let failure):
            "Could not start \(failure.deviceName).\n\(failure.reason)"
        }
    }

    private func observeDeviceChanges() {
        let center = NotificationCenter.default
        notificationObservers = [
            center.addObserver(forName: AVCaptureDevice.wasConnectedNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    guard let self else { return }
                    Task { await self.attachIfPossible() }
                }
            },
            center.addObserver(forName: AVCaptureDevice.wasDisconnectedNotification, object: nil, queue: .main) { [weak self] notification in
                let deviceID = (notification.object as? AVCaptureDevice)?.uniqueID
                MainActor.assumeIsolated {
                    guard let self else { return }
                    if let deviceID, self.attachedDeviceIDs.contains(deviceID) {
                        self.detach(reason: "Capture device disconnected. Waiting for it to come back…")
                    }
                    // Also refreshes the launcher when a device that was not playing goes away.
                    Task { await self.attachIfPossible() }
                }
            },
            // e.g. after the Mac wakes from sleep; a fresh start usually recovers.
            center.addObserver(forName: AVCaptureSession.runtimeErrorNotification, object: capture.session, queue: .main) { [weak self] notification in
                let message = (notification.userInfo?[AVCaptureSessionErrorKey] as? NSError)?.localizedDescription ?? "unknown error"
                MainActor.assumeIsolated {
                    guard let self else { return }
                    printError("capture stopped: \(message). Restarting…")
                    self.detach(reason: "Capture stopped. Restarting…")
                    Task { await self.attachIfPossible() }
                }
            },
        ]
    }

    // MARK: - Commands

    private var effectiveVolume: Float { isMuted ? 0 : Float(volumePercent) / 100 }

    private func perform(_ command: PlayerCommand) {
        switch command {
        case .toggleMute:
            isMuted.toggle()
        case .changeVolume(let deltaPercent):
            volumePercent = min(100, max(0, volumePercent + deltaPercent))
            isMuted = false
        }
        capture.setVolume(effectiveVolume)
        print(isMuted ? "Muted" : "Volume \(volumePercent)%")
    }
}
