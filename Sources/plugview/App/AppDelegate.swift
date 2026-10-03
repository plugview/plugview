import AVFoundation
import AppKit
import PlugviewCapture
import PlugviewCore
import PlugviewUI

/// Wires permissions, device hot-plug, the capture session, and the window together.
///
/// Every change goes through `send(_:)`, which moves the launcher state and updates the window. Terminal launches
/// also print status lines and exit when camera access is denied, exactly as v0.1.
///
/// An app launch also gets a menu bar icon: closing the window then releases the card and keeps plugview there,
/// watching for a card to open itself again.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    let options: RunOptions
    let launchMode: LaunchMode
    let launcher = LauncherModel()
    let controls = ControlsModel()
    let capture = CaptureSession()
    let settings = AppSettings()
    let menuBar = MenuBarModel(version: plugviewVersion)
    lazy var settingsController = SettingsController(model: menuBar, settings: settings)
    var menuBarController: MenuBarController?
    lazy var windowController = PlayerWindowController(
        session: capture.session, launcher: launcher, controls: controls,
        onCommand: { [weak self] command in self?.perform(command) },
        onClose: { [weak self] in self?.windowDidClose() }
    )

    private var attachedDeviceIDs: Set<String> = []
    var isAttaching = false
    var isAudioPermitted = false
    private var volumePercent: Int
    private var isMuted = false
    private var hasReportedWaiting = false
    private var hasWarnedAboutSound = false
    private var notificationObservers: [any NSObjectProtocol] = []
    var displaySleepActivity: (any NSObjectProtocol)?
    var autoPlayTask: Task<Void, Never>?
    /// Playable devices seen last, to tell a newly plugged card from one that was already there.
    private var knownPlayableIDs: Set<String> = []

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
        controls.selectMode = { [weak self] choice in self?.selectMode(choice) }
        controls.setExpandsLimitedRange = { [weak self] isOn in self?.setExpandsLimitedRange(isOn) }
        controls.takeScreenshot = { [weak self] in self?.perform(.takeScreenshot) }
        menuBar.openWindow = { [weak self] in self?.openWindow(isPlaying: true) }
        menuBar.quit = { NSApp.terminate(nil) }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Before the window exists, so it opens with the frame saved under the old bundle id.
        let defaults = UserDefaults.standard
        LegacyDefaults.migrate(from: defaults.persistentDomain(forName: LegacyDefaults.domainName), into: defaults)
        NSApp.mainMenu = makeMainMenu(hasMenuBarItem: launchMode == .app)
        BrandFonts.register()
        if launchMode == .app { setUpMenuBar() }

        // Started at login, plugview waits in the menu bar; it still needs the window to ask for permissions.
        if launchMode == .app && Self.isLaunchedAsLoginItem && !Permissions.isUndetermined(for: .video) {
            menuBar.isWindowOpen = false
            NSApp.setActivationPolicy(.accessory)
        } else {
            windowController.showWindow(nil)
            windowController.showLauncher()
            NSApp.activate()
            beginDisplayActivity()
        }
        // Toggling during launch is ignored; wait until the window is on screen and the app is active.
        if options.startsInFullScreen {
            DispatchQueue.main.async { [weak self] in self?.windowController.window?.toggleFullScreen(nil) }
        }
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

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        launchMode == .terminal || !settings.keepsRunningInMenuBar
    }

    /// Opening plugview again (Finder, Spotlight, `open -a`) while it waits in the menu bar brings the window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows: Bool) -> Bool {
        if launchMode == .app && !menuBar.isWindowOpen { openWindow(isPlaying: false) }
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        capture.stop()
        endDisplayActivity()
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
    ///
    /// While the window is closed it only updates the menu bar, and opens the window when `isPlugIn` brought a new
    /// card and the user wants that.
    func attachIfPossible(isPlugIn: Bool = false) async {
        let playable = playableVideoDevices(from: DeviceCatalog.videoDevices(), matching: options.videoQuery)
        // A terminal launch picks exactly as v0.1; only the app remembers the user's pick.
        let preferredID = launchMode == .app ? settings.preferredDeviceID : nil
        let devices = preferredFirst(playable, preferredID: preferredID)
        let playableIDs = Set(devices.map(\.uniqueID))
        let hasNewDevice = !playableIDs.subtracting(knownPlayableIDs).isEmpty
        knownPlayableIDs = playableIDs
        controls.devices = devices
        send(.devicesChanged(devices))
        refreshModes()
        if launchMode == .app && !menuBar.isWindowOpen {
            if case .ready(let devices) = launcher.state { launcher.readyDetails = readyDetails(for: devices) }
            if isPlugIn && hasNewDevice && settings.opensOnPlugIn { openWindow(isPlaying: false) }
            return
        }
        if case .playing(let video) = launcher.state {
            await attachLateAudio(to: video)
            return
        }
        guard attachedDeviceIDs.isEmpty, !isAttaching else { return }

        switch launcher.state {
        case .ready(let devices):
            launcher.readyDetails = readyDetails(for: devices)
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

    /// Lets the user pick a ready device; when auto-playing, the first one starts right away.
    ///
    /// No waiting for the plug-in intro: the card is ready in about 0.2 s, and the intro would add 2.8 s.
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
            guard let self, case .ready(let devices) = self.launcher.state, let first = devices.first else { return }
            await self.play(first, isUserChoice: false)
        }
    }

    func play(_ video: DeviceInfo, isUserChoice: Bool) async {
        autoPlayTask?.cancel()
        autoPlayTask = nil
        // Cleared only once the video covers the launcher, so the buttons never jump the layout.
        defer {
            launcher.play = nil
            launcher.isAutoPlayPending = false
        }
        // The card may have been unplugged since it was found.
        guard case .ready(let devices) = launcher.state, devices.contains(video), attachedDeviceIDs.isEmpty, !isAttaching else { return }
        if isUserChoice { settings.preferredDeviceID = video.uniqueID }
        await attach(video)
    }

    /// The device menu over the video: switches to another device and remembers it.
    private func switchPlayback(to video: DeviceInfo) {
        guard case .playing(let current) = launcher.state, current != video, !isAttaching else { return }
        settings.preferredDeviceID = video.uniqueID
        Task { await attach(video) }
    }

    func attach(_ video: DeviceInfo) async {
        let audio = selectAudio(for: video)

        isAttaching = true
        defer { isAttaching = false }
        var isFreshStart = true
        if case .playing = launcher.state { isFreshStart = false }
        do {
            let activeCapture = try await capture.start(
                videoID: video.uniqueID, mode: modeChoice(for: video), audioID: audio?.uniqueID, volume: effectiveVolume)
            attachedDeviceIDs = Set([video.uniqueID, audio?.uniqueID].compactMap { $0 })
            hasReportedWaiting = false
            // The window was closed while the device started: give the card back right away.
            if launchMode == .app && !menuBar.isWindowOpen {
                detach(reason: "Window closed; plugview stays in the menu bar.")
                return
            }
            send(.playbackStarted(video))
            controls.playingDeviceID = video.uniqueID
            refreshModes()
            showAttached(activeCapture, isFreshStart: isFreshStart)
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

    func detach(reason: String) {
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

    /// `isFreshStart`: the picture replaces the launcher (not a device, mode, or sound change while playing), so
    /// the app greets it with the plug-in intro in a toast, over the video instead of before it.
    private func showAttached(_ activeCapture: ActiveCapture, isFreshStart: Bool) {
        let mode = activeCapture.mode
        let audioSummary = activeCapture.audioName.map { "audio from \($0)" } ?? "no audio"
        print("Showing \(activeCapture.videoName) · \(mode.summary), \(audioSummary)")
        menuBar.playingDetails = "\(mode.summary) · \(audioSummary)"
        if launchMode == .app && isFreshStart { controls.toast = Toast(icon: .signal, message: "\(activeCapture.videoName) · \(mode.summary)") }
        warnAboutMissingSoundOnce()
        windowController.showLiveVideo(
            deviceName: activeCapture.videoName, details: "\(mode.summary) · \(audioSummary)", width: mode.width, height: mode.height)
    }

    /// The app plays silently when microphone access is off; say so once, with the way to turn it on.
    private func warnAboutMissingSoundOnce() {
        guard launchMode == .app, options.isAudioEnabled, !isAudioPermitted, !hasWarnedAboutSound else { return }
        hasWarnedAboutSound = true
        controls.toast = Toast(symbol: "speaker.slash", message: "No sound: microphone access is off", actionTitle: "Open Settings", isWarning: true) {
            if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone") { NSWorkspace.shared.open(url) }
        }
    }

    // MARK: - State

    private func send(_ event: LauncherEvent) {
        launcher.state = nextLauncherState(after: launcher.state, on: event)
        menuBarController?.update(for: launcher.state)
        if case .playing = launcher.state { return }
        windowController.showLauncher()
    }

    private func observeDeviceChanges() {
        let center = NotificationCenter.default
        notificationObservers = [
            center.addObserver(forName: AVCaptureDevice.wasConnectedNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    guard let self else { return }
                    Task { await self.attachIfPossible(isPlugIn: true) }
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
                    // An unplugged card's session reports errors after it was already detached; nothing to restart.
                    guard let self, !self.attachedDeviceIDs.isEmpty else { return }
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
        case .takeScreenshot:
            takeScreenshot()
            return
        case .toggleMute:
            isMuted.toggle()
        case .changeVolume(let deltaPercent), .scrollVolume(let deltaPercent):
            volumePercent = min(100, max(0, volumePercent + deltaPercent))
            isMuted = false
        case .setVolume(let percent):
            volumePercent = min(100, max(0, percent))
            isMuted = false
        }
        capture.setVolume(effectiveVolume)
        controls.volumePercent = volumePercent
        controls.isMuted = isMuted
        // The slider and scrolling send a value per step; only keys print, as in v0.1.
        switch command {
        case .setVolume, .scrollVolume, .takeScreenshot: return
        case .toggleMute, .changeVolume: break
        }
        print(isMuted ? "Muted" : "Volume \(volumePercent)%")
    }
}
