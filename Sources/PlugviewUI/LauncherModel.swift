import Observation
import PlugviewCore

/// What the launcher window shows, owned by the app delegate and observed by `LauncherView`.
@MainActor
@Observable
public final class LauncherModel {
    public var state: LauncherState = .permissionNeeded(.camera)
    /// Names the device being waited for, in quotes when the user asked for one by name.
    public var waitingTarget = "a capture device"
    /// Set while the onboarding screen waits for the user before macOS asks for permissions.
    public var continueToPermissions: (() -> Void)?

    public init() {}
}
