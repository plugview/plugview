import PlugviewCore
import Testing

@Suite struct LaunchModeTests {
    @Test func opensTheLauncherWhenLaunchServicesStartsTheApp() {
        #expect(detectLaunchMode(arguments: [], parentProcessID: 1) == .app)
        #expect(detectLaunchMode(arguments: ["-psn_0_123456"], parentProcessID: 1) == .app)
    }

    @Test func behavesAsTheCommandWhenAShellStartsIt() {
        #expect(detectLaunchMode(arguments: [], parentProcessID: 4242) == .terminal)
        #expect(detectLaunchMode(arguments: ["--list"], parentProcessID: 4242) == .terminal)
    }

    @Test func treatsArgumentsPassedThroughOpenAsACommand() {
        #expect(detectLaunchMode(arguments: ["--device", "UGREEN"], parentProcessID: 1) == .terminal)
    }
}
