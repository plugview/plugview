import Foundation
import PlugviewCore
import Testing

@Suite struct ParseCommandTests {
    @Test func defaultsToRunWithAudioAtFullVolume() throws {
        #expect(try parseCommand([]) == .run(RunOptions()))
    }

    @Test func readsAllRunOptions() throws {
        var expected = RunOptions()
        expected.videoQuery = "USB Video"
        expected.volumePercent = 40
        expected.startsInFullScreen = true
        #expect(try parseCommand(["--device", "USB Video", "-v", "40", "-f"]) == .run(expected))
    }

    @Test func measuresWithTheSameDeviceOptions() throws {
        var expected = RunOptions()
        expected.videoQuery = "UGREEN"
        expected.isAudioEnabled = false
        #expect(try parseCommand(["--measure", "-d", "UGREEN", "--no-audio"]) == .measure(expected))
    }

    @Test func listAndHelpWinOverOtherOptions() throws {
        #expect(try parseCommand(["-d", "x", "--list"]) == .listDevices)
        #expect(try parseCommand(["--volume", "10", "-h"]) == .showHelp)
    }

    @Test(arguments: [["--volume", "101"], ["--volume", "-1"], ["--volume", "loud"], ["--device"], ["--device", "--no-audio"], ["--bogus"]])
    func rejectsInvalidArguments(arguments: [String]) {
        #expect(throws: OptionsError.self) { try parseCommand(arguments) }
    }

    @Test func rejectsAudioQueryWithAudioDisabled() {
        #expect(throws: OptionsError(description: "--audio and --no-audio cannot be combined")) {
            try parseCommand(["--audio", "x", "--no-audio"])
        }
    }
}

@Suite struct DeviceSelectionTests {
    let virtualCamera = DeviceInfo(uniqueID: "virt-1", name: "Streaming Virtual Camera", modelID: "", transport: .virtual)
    let captureCard = DeviceInfo(uniqueID: "0x1100345f2130", name: "USB3.0 Video", modelID: "UVC 345F:2130", transport: .usb)
    let builtInMic = DeviceInfo(uniqueID: "BuiltInMic", name: "MacBook Pro Microphone", modelID: "", transport: .builtIn)
    let cardAudio = DeviceInfo(uniqueID: "AppleUSBAudioEngine:x", name: "USB Digital Audio", modelID: "USB Audio 345F", transport: .usb)

    @Test func prefersUSBHardwareOverVirtualCameras() {
        #expect(selectVideoDevice(from: [virtualCamera, captureCard], matching: nil) == captureCard)
    }

    @Test func neverAutoSelectsVirtualOrContinuityCameras() {
        let iPhone = DeviceInfo(uniqueID: "iphone", name: "iPhone Camera", modelID: "", transport: .wireless)
        #expect(selectVideoDevice(from: [virtualCamera, iPhone], matching: nil) == nil)
        #expect(selectVideoDevice(from: [iPhone], matching: "iphone") == iPhone)
    }

    @Test func acceptsWiredNonUSBCaptureHardware() {
        let thunderboltCard = DeviceInfo(uniqueID: "tb", name: "Thunderbolt Capture", modelID: "", transport: .other)
        #expect(selectVideoDevice(from: [virtualCamera, thunderboltCard], matching: nil) == thunderboltCard)
    }

    @Test func matchesQueryByNameSubstringOrUniqueID() {
        #expect(selectVideoDevice(from: [captureCard, virtualCamera], matching: "virtual") == virtualCamera)
        #expect(selectVideoDevice(from: [virtualCamera, captureCard], matching: "0x1100345f2130") == captureCard)
        #expect(selectVideoDevice(from: [captureCard], matching: "elgato") == nil)
    }

    @Test func pairsAudioWithTheSameName() {
        let sameName = DeviceInfo(uniqueID: "a", name: captureCard.name, modelID: "", transport: .other)
        #expect(selectAudioDevice(from: [builtInMic, cardAudio, sameName], for: captureCard, matching: nil) == sameName)
    }

    @Test func pairsAudioWithTheSameModelID() {
        let sameModel = DeviceInfo(uniqueID: "a", name: "Other", modelID: captureCard.modelID, transport: .other)
        #expect(selectAudioDevice(from: [builtInMic, sameModel], for: captureCard, matching: nil) == sameModel)
    }

    @Test func fallsBackToTheOnlyUSBAudioInput() {
        #expect(selectAudioDevice(from: [builtInMic, cardAudio], for: captureCard, matching: nil) == cardAudio)
    }

    @Test func refusesToGuessBetweenSeveralUSBAudioInputs() {
        let usbHeadset = DeviceInfo(uniqueID: "h", name: "USB Headset", modelID: "", transport: .usb)
        #expect(selectAudioDevice(from: [cardAudio, usbHeadset], for: captureCard, matching: nil) == nil)
        #expect(selectAudioDevice(from: [cardAudio, usbHeadset], for: captureCard, matching: "headset") == usbHeadset)
    }

    @Test func neverPicksTheBuiltInMicrophone() {
        #expect(selectAudioDevice(from: [builtInMic], for: captureCard, matching: nil) == nil)
    }
}

@Suite struct FormatSelectionTests {
    @Test func prefersSmooth1080pOver4KAt30() {
        let modes = [
            VideoMode(width: 3840, height: 2160, maxFrameRate: 30, encoding: .compressed),
            VideoMode(width: 1920, height: 1080, maxFrameRate: 60, encoding: .compressed),
            VideoMode(width: 1280, height: 720, maxFrameRate: 60, encoding: .native),
        ]
        #expect(bestModeIndex(in: modes) == 1)
    }

    @Test func prefersTheEncodingWithLeastWorkWhenEverythingElseTies() {
        let modes = [
            VideoMode(width: 1920, height: 1080, maxFrameRate: 60, encoding: .compressed),
            VideoMode(width: 1920, height: 1080, maxFrameRate: 60, encoding: .converted),
            VideoMode(width: 1920, height: 1080, maxFrameRate: 60, encoding: .native),
            VideoMode(width: 1920, height: 1080, maxFrameRate: 60, encoding: .converted),
        ]
        #expect(bestModeIndex(in: modes) == 2)
    }

    @Test func fallsBackToHighestResolutionWhenNothingIsSmooth() {
        let modes = [
            VideoMode(width: 1280, height: 720, maxFrameRate: 30, encoding: .native),
            VideoMode(width: 1920, height: 1080, maxFrameRate: 30, encoding: .native),
        ]
        #expect(bestModeIndex(in: modes) == 1)
    }

    @Test func returnsNilForNoModes() {
        #expect(bestModeIndex(in: []) == nil)
    }

    @Test func summarizesWithRoundedFrameRate() {
        #expect(VideoMode(width: 1920, height: 1080, maxFrameRate: 60.00024, encoding: .native).summary == "1920×1080 @ 60 fps")
    }
}

@Suite struct MeasurementTests {
    let healthy = LatencyFacts(
        requestedFrameRate: 60, deliveredFrameRate: 60, displayRefreshRate: 120, outputLatencyMilliseconds: 16, isOutputBluetooth: false)

    @Test func percentileUsesNearestRank() {
        #expect(percentile([], 0.5) == nil)
        #expect(percentile([3, 1, 2], 0.5) == 2)
        #expect(percentile([1, 2, 3, 4, 100], 1) == 100)
        #expect(percentile([5, 1], 0) == 1)
    }

    @Test func givesNoAdviceForAHealthySetup() {
        #expect(latencyAdvice(for: healthy).isEmpty)
        var sixtyHertz = healthy
        sixtyHertz.displayRefreshRate = 59.94
        #expect(latencyAdvice(for: sixtyHertz).isEmpty)
    }

    @Test func flagsAFrameRateBelowTheRequestedOne() {
        var facts = healthy
        facts.deliveredFrameRate = 20
        #expect(latencyAdvice(for: facts).first?.contains("delivers 20 fps instead of 60") == true)
    }

    @Test func flagsBluetoothBeforeGenericSlowOutput() {
        var facts = healthy
        facts.isOutputBluetooth = true
        facts.outputLatencyMilliseconds = 200
        let advice = latencyAdvice(for: facts)
        #expect(advice.count == 1)
        #expect(advice.first?.contains("Bluetooth") == true)
    }

    @Test func flagsASlowWiredOutput() {
        var facts = healthy
        facts.outputLatencyMilliseconds = 55
        #expect(latencyAdvice(for: facts).first?.contains("adds 55 ms") == true)
    }

    @Test func flagsARefreshRateThatIsNotAMultipleOfTheFrameRate() {
        var facts = healthy
        facts.displayRefreshRate = 100
        #expect(latencyAdvice(for: facts).first?.contains("100 Hz") == true)
    }
}

@Suite struct VersionTests {
    @Test func versionIsSemantic() {
        let parts = plugviewVersion.split(separator: ".")
        #expect(parts.count == 3 && parts.allSatisfy { Int($0) != nil })
    }

    /// The embedded Info.plist carries the version too; a release must not ship two different numbers.
    @Test func infoPlistMatchesTheVersionConstant() throws {
        let plistURL = URL(filePath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "Sources/plugview/Resources/Info.plist")
        let plist = try #require(NSDictionary(contentsOf: plistURL))
        #expect(plist["CFBundleShortVersionString"] as? String == plugviewVersion)
    }
}
