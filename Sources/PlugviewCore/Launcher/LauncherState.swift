/// A permission plugview needs from macOS.
public enum PermissionKind: Equatable, Sendable {
    /// Shows the card's video.
    case camera
    /// Plays the console's sound (never the Mac's own microphone).
    case microphone
}

/// Why a device that was found could not be played.
public struct CaptureFailure: Equatable, Sendable {
    public let deviceName: String
    /// What went wrong, in words the user can act on.
    public let reason: String

    public init(deviceName: String, reason: String) {
        self.deviceName = deviceName
        self.reason = reason
    }
}

/// What the launcher window shows.
///
/// Every screen switches over it with no `default:`, so a new state fails the build until it is handled.
public enum LauncherState: Equatable, Sendable {
    /// macOS is asking for a permission.
    case permissionNeeded(PermissionKind)
    /// The user refused a permission plugview cannot work without.
    case permissionDenied(PermissionKind)
    /// No playable device is connected.
    case waitingForSignal
    /// Playable devices are connected, best first, and none is playing yet.
    case ready([DeviceInfo])
    /// The device whose video is on screen.
    case playing(DeviceInfo)
    /// Starting a device failed; the next device change tries again.
    case failed(CaptureFailure)
}

/// Something that happened, reported by the app delegate and the device watcher.
public enum LauncherEvent: Equatable, Sendable {
    case permissionRequested(PermissionKind)
    case permissionDenied(PermissionKind)
    /// Every permission plugview needs to play is granted.
    case permissionsGranted
    /// The playable video devices right now, best first (see `playableVideoDevices`).
    case devicesChanged([DeviceInfo])
    case playbackStarted(DeviceInfo)
    case playbackFailed(CaptureFailure)
    /// The playing device stopped (unplugged, or a capture error); a `devicesChanged` follows.
    case playbackStopped
}

/// The state that follows `state` once `event` happens.
///
/// Pure, so every transition is unit-tested.
public func nextLauncherState(after state: LauncherState, on event: LauncherEvent) -> LauncherState {
    switch event {
    case .permissionRequested(let kind): .permissionNeeded(kind)
    case .permissionDenied(let kind): .permissionDenied(kind)
    case .permissionsGranted, .playbackStopped: .waitingForSignal
    case .devicesChanged(let devices): stateAfterDevicesChanged(state, devices: devices)
    case .playbackStarted(let device): .playing(device)
    case .playbackFailed(let failure): .failed(failure)
    }
}

private func stateAfterDevicesChanged(_ state: LauncherState, devices: [DeviceInfo]) -> LauncherState {
    switch state {
    case .permissionNeeded, .permissionDenied:
        return state
    case .playing(let device) where devices.contains(device):
        return state
    case .playing, .waitingForSignal, .ready, .failed:
        return devices.isEmpty ? .waitingForSignal : .ready(devices)
    }
}
