import Observation
import PlugviewCore

/// What the launcher window shows, owned by the app delegate and observed by `LauncherView`.
@MainActor
@Observable
public final class LauncherModel {
    public var state: LauncherState = .permissionNeeded(.camera)
    /// Names the device being waited for, in quotes when the user asked for one by name.
    public var waitingTarget = "a capture device"
    /// Each ready device's mode and sound by unique ID, e.g. "1920×1080 · 60 fps · audio on".
    public var readyDetails: [String: String] = [:]
    /// Set while the onboarding screen waits for the user before macOS asks for permissions.
    public var continueToPermissions: (() -> Void)?
    /// Set while ready devices can be started from the launcher (an app launch); plays the given device now.
    public var play: ((DeviceInfo) -> Void)?
    /// The first ready device is starting on its own.
    public var isAutoPlayPending = false

    public init() {}
}
