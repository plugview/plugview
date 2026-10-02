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
    /// The sizes and frame rates the current device offers, largest first.
    public var modes: [ModeChoice] = []
    /// The user's pick for the current device; `nil` means plugview picks the best mode.
    public var modeChoice: ModeChoice?
    /// The mode the device is running in, or would start in.
    public var activeMode: ModeChoice?
    /// The resolution panel is open; the window keeps the controls up meanwhile.
    public var isPickerOpen = false

    public var setVolume: (Int) -> Void = { _ in }
    public var toggleMute: () -> Void = {}
    public var toggleFullScreen: () -> Void = {}
    public var selectDevice: (DeviceInfo) -> Void = { _ in }
    /// Picks a mode for the current device, or `nil` for automatic; a playing device restarts in it.
    public var selectMode: (ModeChoice?) -> Void = { _ in }

    public init() {}

    var effectiveVolumePercent: Int { isMuted ? 0 : volumePercent }
}

extension ModeChoice {
    /// A compact name for the controls bar, e.g. `1080p60`.
    var shortTitle: String { "\(height)p\(frameRate)" }
}
