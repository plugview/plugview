import PlugviewCapture
import PlugviewCore

/// The resolution and frame rate choice, saved per device; only an app launch offers it.
extension AppDelegate {
    /// Saves the mode for the current device and restarts a playing device in it.
    func selectMode(_ choice: ModeChoice?) {
        guard launchMode == .app, let deviceID = currentDeviceID else { return }
        settings.setModeChoice(choice, forDevice: deviceID)
        refreshModes()
        switch launcher.state {
        case .playing(let video) where !isAttaching:
            Task { await attach(video) }
        case .ready(let devices):
            launcher.readyDetails = readyDetails(for: devices)
        case .permissionNeeded, .permissionDenied, .waitingForSignal, .playing, .failed:
            break
        }
    }

    /// The device the mode menu is about: the one playing, or the one that would play next.
    var currentDeviceID: String? {
        if case .playing(let video) = launcher.state { return video.uniqueID }
        return controls.devices.first?.uniqueID
    }

    /// The saved mode for a device; a terminal launch always starts in the best mode, as in v0.1.
    func modeChoice(for video: DeviceInfo) -> ModeChoice? {
        launchMode == .app ? settings.modeChoice(forDevice: video.uniqueID) : nil
    }

    /// Fills the mode menu for the current device; only an app launch offers it.
    func refreshModes() {
        guard launchMode == .app, let deviceID = currentDeviceID else {
            controls.modes = []
            return
        }
        let modes = DeviceCatalog.videoModes(ofVideoDevice: deviceID)
        controls.modes = pickableModes(in: modes)
        controls.modeChoice = settings.modeChoice(forDevice: deviceID)
        controls.activeMode = modeIndex(in: modes, choice: controls.modeChoice).map { ModeChoice(modes[$0]) }
    }

    /// "1920×1080 · 60 fps · audio on" per device for the ready screen, read without starting the devices.
    func readyDetails(for devices: [DeviceInfo]) -> [String: String] {
        Dictionary(devices.map { ($0.uniqueID, readyDetails(for: $0)) }) { first, _ in first }
    }

    func readyDetails(for video: DeviceInfo) -> String {
        let hasAudio =
            options.isAudioEnabled && isAudioPermitted
            && selectAudioDevice(from: DeviceCatalog.audioDevices(), for: video, matching: options.audioQuery) != nil
        let mode = DeviceCatalog.mode(ofVideoDevice: video.uniqueID, choice: modeChoice(for: video)).map { "\(ModeChoice($0).title) · " }
        return (mode ?? "") + (hasAudio ? "audio on" : "no audio")
    }
}
