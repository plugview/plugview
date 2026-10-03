import Foundation
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
    /// Stretches limited-range video to full range for the current device (gray blacks, dull whites).
    public var expandsLimitedRange = false
    /// A short message over the video, e.g. after a screenshot; it goes away on its own.
    public var toast: Toast?
    /// What the signal watch says over the video, if anything.
    public var signalNotice: SignalNotice?

    public var setVolume: (Int) -> Void = { _ in }
    public var toggleMute: () -> Void = {}
    public var toggleFullScreen: () -> Void = {}
    public var selectDevice: (DeviceInfo) -> Void = { _ in }
    /// Picks a mode for the current device, or `nil` for automatic; a playing device restarts in it.
    public var selectMode: (ModeChoice?) -> Void = { _ in }
    public var setExpandsLimitedRange: (Bool) -> Void = { _ in }
    public var takeScreenshot: () -> Void = {}

    public init() {}

    var effectiveVolumePercent: Int { isMuted ? 0 : volumePercent }
}

/// A message shown over the video for a few seconds, with an optional action.
public struct Toast: Identifiable {
    public enum Icon {
        case symbol(String)
        /// The plug-in intro, small: a card was found and its picture is already on screen.
        case signal
    }

    public let id = UUID()
    public let icon: Icon
    public let message: String
    public let actionTitle: String?
    public let action: (() -> Void)?
    public let isWarning: Bool

    public init(symbol: String, message: String, actionTitle: String? = nil, isWarning: Bool = false, action: (() -> Void)? = nil) {
        self.init(icon: .symbol(symbol), message: message, actionTitle: actionTitle, isWarning: isWarning, action: action)
    }

    public init(icon: Icon, message: String, actionTitle: String? = nil, isWarning: Bool = false, action: (() -> Void)? = nil) {
        self.icon = icon
        self.message = message
        self.actionTitle = actionTitle
        self.action = action
        self.isWarning = isWarning
    }
}

extension ModeChoice {
    /// A compact name for the controls bar, e.g. `1080p60`.
    var shortTitle: String { "\(height)p\(frameRate)" }
}
