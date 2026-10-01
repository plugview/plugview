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

@Suite struct LauncherStateTests {
    let card = DeviceInfo(uniqueID: "card", name: "USB3.0 Video", modelID: "UVC 345F:2130", transport: .usb)
    let secondCard = DeviceInfo(uniqueID: "card-2", name: "Cam Link 4K", modelID: "", transport: .usb)
    let failure = CaptureFailure(deviceName: "USB3.0 Video", reason: "is another app using it?")

    private func states(from start: LauncherState, through events: [LauncherEvent]) -> [LauncherState] {
        var state = start
        return events.map { event in
            state = nextLauncherState(after: state, on: event)
            return state
        }
    }

    @Test func plugUnplugAndReplugMovesThroughWaitingReadyAndPlaying() {
        let events: [LauncherEvent] = [
            .permissionsGranted,
            .devicesChanged([]),
            .devicesChanged([card]),
            .playbackStarted(card),
            .playbackStopped,
            .devicesChanged([]),
            .devicesChanged([card]),
            .playbackStarted(card),
        ]
        let expected: [LauncherState] = [
            .waitingForSignal, .waitingForSignal, .ready([card]), .playing(card),
            .waitingForSignal, .waitingForSignal, .ready([card]), .playing(card),
        ]
        #expect(states(from: .permissionNeeded(.camera), through: events) == expected)
    }

    @Test func permissionStatesIgnoreDeviceChanges() {
        #expect(nextLauncherState(after: .permissionNeeded(.camera), on: .devicesChanged([card])) == .permissionNeeded(.camera))
        #expect(nextLauncherState(after: .permissionDenied(.camera), on: .devicesChanged([card])) == .permissionDenied(.camera))
    }

    @Test func permissionEventsShowTheirKind() {
        #expect(nextLauncherState(after: .permissionNeeded(.camera), on: .permissionRequested(.microphone)) == .permissionNeeded(.microphone))
        #expect(nextLauncherState(after: .permissionNeeded(.camera), on: .permissionDenied(.camera)) == .permissionDenied(.camera))
    }

    @Test func keepsPlayingWhileThePlayingDeviceIsStillConnected() {
        #expect(nextLauncherState(after: .playing(card), on: .devicesChanged([secondCard, card])) == .playing(card))
    }

    @Test func leavesPlayingWhenThePlayingDeviceDisappears() {
        #expect(nextLauncherState(after: .playing(card), on: .devicesChanged([])) == .waitingForSignal)
        #expect(nextLauncherState(after: .playing(card), on: .devicesChanged([secondCard])) == .ready([secondCard]))
    }

    @Test func listsEveryReadyDeviceInOrder() {
        #expect(nextLauncherState(after: .waitingForSignal, on: .devicesChanged([card, secondCard])) == .ready([card, secondCard]))
        #expect(nextLauncherState(after: .ready([card, secondCard]), on: .devicesChanged([secondCard])) == .ready([secondCard]))
    }

    @Test func aFailureIsRetriedOnTheNextDeviceChange() {
        #expect(nextLauncherState(after: .ready([card]), on: .playbackFailed(failure)) == .failed(failure))
        #expect(nextLauncherState(after: .failed(failure), on: .devicesChanged([card])) == .ready([card]))
        #expect(nextLauncherState(after: .failed(failure), on: .devicesChanged([])) == .waitingForSignal)
    }
}

@Suite struct PlayableDevicesTests {
    let usbCard = DeviceInfo(uniqueID: "usb", name: "USB3.0 Video", modelID: "", transport: .usb)
    let thunderboltCard = DeviceInfo(uniqueID: "tb", name: "Thunderbolt Capture", modelID: "", transport: .other)
    let iPhone = DeviceInfo(uniqueID: "iphone", name: "iPhone Camera", modelID: "", transport: .wireless)

    @Test func listsWiredHardwareUSBFirst() {
        #expect(playableVideoDevices(from: [thunderboltCard, iPhone, usbCard], matching: nil) == [usbCard, thunderboltCard])
    }

    @Test func aQueryListsOnlyMatchingDevices() {
        #expect(playableVideoDevices(from: [usbCard, iPhone], matching: "iphone") == [iPhone])
    }
}

@Suite struct DevicePreferenceTests {
    let usbCard = DeviceInfo(uniqueID: "card", name: "USB3.0 Video", modelID: "", transport: .usb)
    let camLink = DeviceInfo(uniqueID: "camlink", name: "Cam Link 4K", modelID: "", transport: .usb)

    @Test func movesThePreferredDeviceToTheFront() {
        #expect(preferredFirst([usbCard, camLink], preferredID: "camlink") == [camLink, usbCard])
    }

    @Test func keepsTheOrderWithoutAConnectedPreference() {
        #expect(preferredFirst([usbCard, camLink], preferredID: nil) == [usbCard, camLink])
        #expect(preferredFirst([usbCard, camLink], preferredID: "unplugged") == [usbCard, camLink])
    }

    @Test func autoPlaysASingleDeviceOrTheRememberedOne() {
        #expect(shouldAutoPlay([usbCard], preferredID: nil))
        #expect(shouldAutoPlay([usbCard, camLink], preferredID: "camlink"))
    }

    @Test func waitsForAPickAmongSeveralNewDevices() {
        #expect(!shouldAutoPlay([usbCard, camLink], preferredID: nil))
        #expect(!shouldAutoPlay([usbCard, camLink], preferredID: "unplugged"))
    }
}
