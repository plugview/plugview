import AVFoundation
import AppKit
import PlugviewCapture
import PlugviewCore
import PlugviewUI

/// Wires permissions, device hot-plug, the capture session, and the window together.
///
/// Every change goes through `send(_:)`, which moves the launcher state and updates the window. Terminal launches
/// also print status lines and exit when camera access is denied, exactly as v0.1.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let options: RunOptions
    private let launchMode: LaunchMode
    private let launcher = LauncherModel()
    private let controls = ControlsModel()
    private let capture = CaptureSession()
    private lazy var windowController = PlayerWindowController(session: capture.session, launcher: launcher, controls: controls) {
        [weak self] command in self?.perform(command)
    }

    private var attachedDeviceIDs: Set<String> = []
    private var isAttaching = false
    private var isAudioPermitted = false
    private var volumePercent: Int
    private var isMuted = false
    private var hasReportedWaiting = false
    private var notificationObservers: [any NSObjectProtocol] = []
    private var displaySleepActivity: (any NSObjectProtocol)?
    private var autoPlayTask: Task<Void, Never>?

    init(options: RunOptions, launchMode: LaunchMode) {
        self.options = options
        self.launchMode = launchMode
        launcher.waitingTarget = options.videoQuery.map { "'\($0)'" } ?? "a capture device"
        volumePercent = options.volumePercent
        super.init()
        controls.volumePercent = volumePercent
        controls.setVolume = { [weak self] percent in self?.perform(.setVolume(percent: percent)) }
        controls.toggleMute = { [weak self] in self?.perform(.toggleMute) }
        controls.toggleFullScreen = { [weak self] in self?.windowController.window?.toggleFullScreen(nil) }
        controls.selectDevice = { [weak self] device in self?.switchPlayback(to: device) }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Before the window exists, so it opens with the frame saved under the old bundle id.
        let defaults = UserDefaults.standard
        LegacyDefaults.migrate(from: defaults.persistentDomain(forName: LegacyDefaults.domainName), into: defaults)
        NSApp.mainMenu = makeMainMenu()
        BrandFonts.register()
        windowController.showWindow(nil)
        windowController.showLauncher()
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

        if launchMode == .app && Permissions.isUndetermined(for: .video) {
            // First run from Finder: the onboarding screen explains both permissions before macOS asks.
            launcher.continueToPermissions = { [weak self] in
                self?.launcher.continueToPermissions = nil
                Task { await self?.requestPermissionsAndAttach() }
            }
            return
        }
        Task { await requestPermissionsAndAttach() }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        capture.stop()
        if let displaySleepActivity { ProcessInfo.processInfo.endActivity(displaySleepActivity) }
    }

    // MARK: - Permissions

    private func requestPermissionsAndAttach() async {
        guard await requestPermissions() else { return }
        await attachIfPossible()
    }

    /// Returns whether plugview can play; without camera access a terminal launch exits, as in v0.1.
    private func requestPermissions() async -> Bool {
        if Permissions.isUndetermined(for: .video) { send(.permissionRequested(.camera)) }
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
            if Permissions.isUndetermined(for: .audio) { send(.permissionRequested(.microphone)) }
            isAudioPermitted = await Permissions.isGranted(for: .audio)
            if !isAudioPermitted { printError(Permissions.microphoneDeniedMessage) }
        }
        send(.permissionsGranted)
        return true
    }

    // MARK: - Attaching devices

    /// Reports the connected devices and, when nothing is playing, starts the best one.
    private func attachIfPossible() async {
        let playable = playableVideoDevices(from: DeviceCatalog.videoDevices(), matching: options.videoQuery)
        // A terminal launch picks exactly as v0.1; only the app remembers the user's pick.
        let preferredID = launchMode == .app ? UserDefaults.standard.string(forKey: Self.preferredDeviceKey) : nil
        let devices = preferredFirst(playable, preferredID: preferredID)
        controls.devices = devices
        send(.devicesChanged(devices))
        if case .playing(let video) = launcher.state {
            await attachLateAudio(to: video)
            return
        }
        guard attachedDeviceIDs.isEmpty, !isAttaching else { return }

        switch launcher.state {
        case .ready(let devices):
            launcher.readyDetails = Dictionary(devices.map { ($0.uniqueID, readyDetails(for: $0)) }) { first, _ in first }
            switch launchMode {
            case .terminal:
                guard let video = devices.first else { return }
                await attach(video)
            case .app:
                offerReadyDevices(isAutoPlaying: shouldAutoPlay(devices, preferredID: preferredID))
            }
        case .waitingForSignal:
            reportWaiting()
        case .permissionNeeded, .permissionDenied, .playing, .failed:
            return
        }
    }

    /// Lets the user pick a ready device; when auto-playing, the first one starts once the plug-in intro ends.
    private func offerReadyDevices(isAutoPlaying: Bool) {
        launcher.play = { [weak self] device in Task { await self?.play(device, isUserChoice: true) } }
        launcher.isAutoPlayPending = isAutoPlaying
        guard isAutoPlaying else {
            // e.g. a second card arrived during the countdown: now the user chooses.
            autoPlayTask?.cancel()
            autoPlayTask = nil
            return
        }
        guard autoPlayTask == nil else { return }
        autoPlayTask = Task { [weak self] in
            do {
                try await Task.sleep(for: .seconds(Tuning.launcherAutoPlaySeconds))
            } catch {
                return
            }
            guard let self, case .ready(let devices) = self.launcher.state, let first = devices.first else { return }
            await self.play(first, isUserChoice: false)
        }
    }

    private func play(_ video: DeviceInfo, isUserChoice: Bool) async {
        autoPlayTask?.cancel()
        autoPlayTask = nil
        // Cleared only once the video covers the launcher, so the buttons never jump the layout.
        defer {
            launcher.play = nil
            launcher.isAutoPlayPending = false
        }
        // The card may have been unplugged during the countdown.
        guard case .ready(let devices) = launcher.state, devices.contains(video), attachedDeviceIDs.isEmpty, !isAttaching else { return }
        if isUserChoice { UserDefaults.standard.set(video.uniqueID, forKey: Self.preferredDeviceKey) }
        await attach(video)
    }

    /// The device menu over the video: switches to another device and remembers it.
    private func switchPlayback(to video: DeviceInfo) {
        guard case .playing(let current) = launcher.state, current != video, !isAttaching else { return }
        UserDefaults.standard.set(video.uniqueID, forKey: Self.preferredDeviceKey)
        Task { await attach(video) }
    }

    private static let preferredDeviceKey = "PreferredVideoDeviceID"

    /// "1920×1080 · 60 fps · audio on" for the ready screen, read without starting the device.
    private func readyDetails(for video: DeviceInfo) -> String {
        let hasAudio =
            options.isAudioEnabled && isAudioPermitted
            && selectAudioDevice(from: DeviceCatalog.audioDevices(), for: video, matching: options.audioQuery) != nil
        let mode = DeviceCatalog.bestMode(ofVideoDevice: video.uniqueID).map { "\($0.width)×\($0.height) · \(Int($0.maxFrameRate.rounded())) fps · " }
        return (mode ?? "") + (hasAudio ? "audio on" : "no audio")
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
            controls.playingDeviceID = video.uniqueID
            showAttached(activeCapture)
        } catch {
            printError("\(error)")
            send(.playbackFailed(CaptureFailure(deviceName: video.name, reason: "\(error)")))
        }
    }

    /// Restarts with sound when the card's audio input appears after its video, as it does on a replug.
    private func attachLateAudio(to video: DeviceInfo) async {
        let isPlayingWithoutSound = attachedDeviceIDs == [video.uniqueID]
        guard isPlayingWithoutSound, !isAttaching, options.isAudioEnabled, isAudioPermitted,
            selectAudioDevice(from: DeviceCatalog.audioDevices(), for: video, matching: options.audioQuery) != nil
        else { return }
        await attach(video)
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

    private func reportWaiting() {
        guard !hasReportedWaiting else { return }
        hasReportedWaiting = true
        print("Waiting for \(launcher.waitingTarget)… (run 'plugview --list' to see detected devices)")
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
        launcher.state = nextLauncherState(after: launcher.state, on: event)
        if case .playing = launcher.state { return }
        windowController.showLauncher()
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
        case .setVolume(let percent):
            volumePercent = min(100, max(0, percent))
            isMuted = false
        }
        capture.setVolume(effectiveVolume)
        controls.volumePercent = volumePercent
        controls.isMuted = isMuted
        // The slider sends a value per step of a drag; only keys print, as in v0.1.
        if case .setVolume = command { return }
        print(isMuted ? "Muted" : "Volume \(volumePercent)%")
    }
}
