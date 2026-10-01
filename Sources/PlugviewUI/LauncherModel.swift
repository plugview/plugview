import Observation
import PlugviewCore

/// What the launcher window shows, owned by the app delegate and observed by `LauncherView`.
@MainActor
@Observable
public final class LauncherModel {
    public var state: LauncherState = .permissionNeeded(.camera)
    /// Names the device being waited for, in quotes when the user asked for one by name.
    public var waitingTarget = "a capture device"
    /// The ready device's mode and sound, e.g. "1920×1080 · 60 fps · audio on".
    public var readyDetails = ""
    /// Set while the onboarding screen waits for the user before macOS asks for permissions.
    public var continueToPermissions: (() -> Void)?
    /// Set while a found device waits to auto-play; the Play button calls it to start right away.
    public var playNow: (() -> Void)?

    public init() {}
}
