import Observation
import PlugviewCore

/// What the floating controls over the video show, owned by the app delegate and observed by `ControlsView`.
@MainActor
@Observable
public final class ControlsModel {
    public var volumePercent = 100
    public var isMuted = false
    /// Playable devices; the device menu appears only when there is more than one.
    public var devices: [DeviceInfo] = []
    public var playingDeviceID: String?

    public var setVolume: (Int) -> Void = { _ in }
    public var toggleMute: () -> Void = {}
    public var toggleFullScreen: () -> Void = {}
    public var selectDevice: (DeviceInfo) -> Void = { _ in }

    public init() {}
}
