import AVFoundation
import AppKit
import PlugviewCore

/// Wires permissions, device hot-plug, the capture session, and the window together.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let options: RunOptions
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

    init(options: RunOptions) {
        self.options = options
        volumePercent = options.volumePercent
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.mainMenu = makeMainMenu()
        windowController.showWindow(nil)
        windowController.showStatus("Waiting for camera permission…\nAllow it in the macOS prompt if one appears.")
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
            await requestPermissions()
            await attachIfPossible()
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        capture.stop()
        if let displaySleepActivity { ProcessInfo.processInfo.endActivity(displaySleepActivity) }
    }

    // MARK: - Permissions

    private func requestPermissions() async {
        guard await Permissions.isGranted(for: .video) else {
            printError(Permissions.cameraDeniedMessage)
            exit(EXIT_FAILURE)
        }
        guard options.isAudioEnabled else { return }

        isAudioPermitted = await Permissions.isGranted(for: .audio)
        if !isAudioPermitted { printError(Permissions.microphoneDeniedMessage) }
    }

    // MARK: - Attaching devices

    private func attachIfPossible() async {
        guard attachedDeviceIDs.isEmpty, !isAttaching else { return }

        guard let video = selectVideoDevice(from: DeviceCatalog.videoDevices(), matching: options.videoQuery) else {
            reportWaiting()
            return
        }
        let audio = selectAudio(for: video)

        isAttaching = true
        defer { isAttaching = false }
        do {
            let activeCapture = try await capture.start(videoID: video.uniqueID, audioID: audio?.uniqueID, volume: effectiveVolume)
            attachedDeviceIDs = Set([video.uniqueID, audio?.uniqueID].compactMap { $0 })
            hasReportedWaiting = false
            showAttached(activeCapture)
        } catch {
            printError("\(error)")
            windowController.showStatus("Could not start \(video.name).\nSee the terminal for details.")
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
        windowController.showStatus(reason)
    }

    private func reportWaiting() {
        let target = options.videoQuery.map { "'\($0)'" } ?? "a capture device"
        windowController.showStatus("Waiting for \(target)…\nPlug it in and it will appear here.")
        guard !hasReportedWaiting else { return }
        hasReportedWaiting = true
        print("Waiting for \(target)… (run 'plugview --list' to see detected devices)")
    }

    private func showAttached(_ activeCapture: ActiveCapture) {
        let mode = activeCapture.mode
        let audioSummary = activeCapture.audioName.map { "audio from \($0)" } ?? "no audio"
        print("Showing \(activeCapture.videoName) · \(mode.summary), \(audioSummary)")
        windowController.showLiveVideo(
            deviceName: activeCapture.videoName, details: "\(mode.summary) · \(audioSummary)", width: mode.width, height: mode.height)
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
                    guard let self, let deviceID, self.attachedDeviceIDs.contains(deviceID) else { return }
                    self.detach(reason: "Capture device disconnected. Waiting for it to come back…")
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
