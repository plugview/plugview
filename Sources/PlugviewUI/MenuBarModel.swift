import Foundation
import Observation

/// The result of the last check for a newer plugview release.
public enum UpdateStatus: Equatable, Sendable {
    case notChecked
    case checking
    case upToDate
    /// A newer release, with the page to download it from.
    case available(version: String, url: URL)
    /// The check could not finish, e.g. offline.
    case failed
}

/// What the menu bar panel shows besides the launcher and the controls: settings and the update check.
///
/// Owned by the app delegate, which saves every change and acts on it.
@MainActor
@Observable
public final class MenuBarModel {
    public var isWindowOpen = true
    /// e.g. "1920×1080 @ 60 fps · audio from UGREEN 25773" while playing.
    public var playingDetails = ""
    public var opensOnPlugIn = true
    public var keepsRunningInMenuBar = true
    public var launchesAtLogin = false
    /// Only an app in a bundle can be a login item.
    public var canLaunchAtLogin = false
    /// Set when macOS needs the user to approve the login item in System Settings.
    public var launchAtLoginNote: String?
    public var checksForUpdates = true
    public var updateStatus = UpdateStatus.notChecked
    public let version: String

    public var openWindow: () -> Void = {}
    public var setOpensOnPlugIn: (Bool) -> Void = { _ in }
    public var setKeepsRunningInMenuBar: (Bool) -> Void = { _ in }
    public var setLaunchesAtLogin: (Bool) -> Void = { _ in }
    public var setChecksForUpdates: (Bool) -> Void = { _ in }
    public var checkForUpdates: () -> Void = {}
    public var quit: () -> Void = {}

    public init(version: String) {
        self.version = version
    }
}
