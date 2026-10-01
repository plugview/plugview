/// How plugview was started, which decides between the v0.1 command and the launcher window.
public enum LaunchMode: Equatable, Sendable {
    /// Typed in a terminal (or run by a script): parse the arguments and behave exactly as v0.1.
    case terminal
    /// Opened from Finder, the Dock, Launchpad, or `open -a`: show the launcher window.
    case app
}

/// Tells an app launch from a command launch.
///
/// macOS starts apps opened through Launch Services (Finder, Dock, `open -a`) as children of launchd
/// (pid 1) and passes no arguments, apart from a `-psn_…` process serial number on older systems.
/// A shell is always the parent of a command typed in a terminal.
public func detectLaunchMode(arguments: [String], parentProcessID: Int32) -> LaunchMode {
    let launchdProcessID: Int32 = 1
    let isLaunchedByLaunchServices = parentProcessID == launchdProcessID && arguments.allSatisfy { $0.hasPrefix("-psn_") }
    return isLaunchedByLaunchServices ? .app : .terminal
}
