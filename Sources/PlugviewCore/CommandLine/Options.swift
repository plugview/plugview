/// What to show and how, as given on the command line.
public struct RunOptions: Equatable, Sendable {
    public var videoQuery: String?
    public var audioQuery: String?
    public var isAudioEnabled = true
    public var volumePercent = 100
    public var startsInFullScreen = false

    public init() {}
}

/// The action requested on the command line.
public enum Command: Equatable, Sendable {
    case run(RunOptions)
    /// Measure latency of the devices `RunOptions` selects, without opening a window.
    case measure(RunOptions)
    case listDevices
    case showHelp
    case showVersion
}

/// A command line that cannot be run, with a message for the user.
public struct OptionsError: Error, Equatable, CustomStringConvertible {
    public let description: String

    public init(description: String) {
        self.description = description
    }
}

/// Parses the arguments that follow the executable name.
public func parseCommand(_ arguments: [String]) throws(OptionsError) -> Command {
    var options = RunOptions()
    var isMeasuring = false
    var remaining = arguments[...]

    while let argument = remaining.popFirst() {
        switch argument {
        case "-h", "--help": return .showHelp
        case "--version": return .showVersion
        case "-l", "--list": return .listDevices
        case "-d", "--device": options.videoQuery = try requireValue(for: argument, from: &remaining)
        case "-a", "--audio": options.audioQuery = try requireValue(for: argument, from: &remaining)
        case "--no-audio": options.isAudioEnabled = false
        case "--measure": isMeasuring = true
        case "-f", "--fullscreen": options.startsInFullScreen = true
        case "-v", "--volume": options.volumePercent = try requireVolumePercent(for: argument, from: &remaining)
        default: throw OptionsError(description: "unknown option '\(argument)'")
        }
    }

    if !options.isAudioEnabled && options.audioQuery != nil {
        throw OptionsError(description: "--audio and --no-audio cannot be combined")
    }
    return isMeasuring ? .measure(options) : .run(options)
}

private func requireValue(for option: String, from remaining: inout ArraySlice<String>) throws(OptionsError) -> String {
    guard let value = remaining.popFirst(), !value.isEmpty, !value.hasPrefix("-") else {
        throw OptionsError(description: "\(option) needs a value")
    }
    return value
}

private func requireVolumePercent(for option: String, from remaining: inout ArraySlice<String>) throws(OptionsError) -> Int {
    let value = try requireValue(for: option, from: &remaining)
    guard let percent = Int(value), (0...100).contains(percent) else {
        throw OptionsError(description: "\(option) must be a whole number from 0 to 100, got '\(value)'")
    }
    return percent
}
