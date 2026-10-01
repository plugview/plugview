import AppKit
import PlugviewCapture
import PlugviewCore

/// sysexits.h EX_USAGE: the command was used incorrectly.
let usageExitCode: Int32 = 64

// Status lines must appear immediately even when output is piped or logged.
setvbuf(stdout, nil, _IOLBF, 0)

let arguments = Array(CommandLine.arguments.dropFirst())
let launchMode = detectLaunchMode(arguments: arguments, parentProcessID: getppid())

let command: Command
do {
    // Opened as an app: no arguments to parse (older macOS adds a `-psn_` one), show the launcher window.
    command = launchMode == .app ? .run(RunOptions()) : try parseCommand(arguments)
} catch {
    printError("\(error)\nRun 'plugview --help' for usage.")
    exit(usageExitCode)
}

switch command {
case .showHelp:
    print(usageText)
case .showVersion:
    print(plugviewVersion)
case .listDevices:
    DeviceCatalog.printDevices()
case .measure(let options):
    Task { exit(await LatencyProbe.run(options)) }
    dispatchMain()
case .run(let options):
    let delegate = AppDelegate(options: options, launchMode: launchMode)
    let app = NSApplication.shared
    app.delegate = delegate
    app.setActivationPolicy(.regular)
    app.run()
}
