import AppKit
import PlugviewCapture
import PlugviewCore
import PlugviewUI

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

    /// The saved mode for a device; a terminal launch uses `--mode`, or the best mode as in v0.1.
    func modeChoice(for video: DeviceInfo) -> ModeChoice? {
        launchMode == .app ? settings.modeChoice(forDevice: video.uniqueID) : options.mode
    }

    /// Turns the color-range fix on or off for the current device, on screen and in screenshots.
    func setExpandsLimitedRange(_ isOn: Bool) {
        guard launchMode == .app, let deviceID = currentDeviceID else { return }
        settings.setExpandsLimitedRange(isOn, forDevice: deviceID)
        refreshModes()
    }

    /// Saves the next frame to ~/Pictures/plugview and says where.
    func takeScreenshot() {
        guard case .playing = launcher.state else { return }
        let expandsLimitedRange = controls.expandsLimitedRange
        Task {
            do {
                let url = try await capture.saveScreenshot(expandsLimitedRange: expandsLimitedRange)
                NSSound(named: "Grab")?.play()
                print("Screenshot saved to \(url.path)")
                controls.toast = Toast(symbol: "camera", message: "Screenshot saved", actionTitle: "Show") {
                    NSWorkspace.shared.activateFileViewerSelecting([url])
                }
            } catch {
                printError("screenshot failed: \(error)")
                controls.toast = Toast(symbol: "exclamationmark.triangle", message: "Could not save the screenshot", isWarning: true)
            }
        }
    }

    /// Fills the mode menu for the current device; only an app launch offers it.
    func refreshModes() {
        guard launchMode == .app, let deviceID = currentDeviceID else {
            controls.modes = []
            return
        }
        controls.expandsLimitedRange = settings.expandsLimitedRange(forDevice: deviceID)
        windowController.setExpandsLimitedRange(controls.expandsLimitedRange)
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
