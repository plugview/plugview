import PlugviewCore
import Testing

@Suite struct DeviceSelectionSpecTests {
    struct Spec: Decodable {
        let devices: FixtureDevices
        let playableVideoDevices: [ListCase]
        let selectVideoDevice: [PickCase]
        let selectAudioDevice: [AudioCase]
        let preferredFirst: [OrderCase]
        let shouldAutoPlay: [AutoPlayCase]
    }

    struct ListCase: Decodable {
        let description: String
        let candidates: [String]
        let query: String?
        let expected: [String]
    }

    struct PickCase: Decodable {
        let description: String
        let candidates: [String]
        let query: String?
        let expected: String?
    }

    struct AudioCase: Decodable {
        let description: String
        let candidates: [String]
        let video: String
        let query: String?
        let expected: String?
    }

    struct OrderCase: Decodable {
        let description: String
        let devices: [String]
        let preferredID: String?
        let expected: [String]
    }

    struct AutoPlayCase: Decodable {
        let description: String
        let devices: [String]
        let preferredID: String?
        let expected: Bool
    }

    let spec: Spec

    init() throws {
        spec = try Fixture.decode(Spec.self, from: "device-selection.json")
    }

    @Test func playableVideoDevicesMatchTheSpec() throws {
        for testCase in spec.playableVideoDevices {
            let devices = playableVideoDevices(from: try spec.devices.devices(testCase.candidates), matching: testCase.query)
            #expect(devices == (try spec.devices.devices(testCase.expected)), "\(testCase.description)")
        }
    }

    @Test func videoSelectionMatchesTheSpec() throws {
        for testCase in spec.selectVideoDevice {
            let device = selectVideoDevice(from: try spec.devices.devices(testCase.candidates), matching: testCase.query)
            #expect(device == (try testCase.expected.map(spec.devices.device)), "\(testCase.description)")
        }
    }

    @Test func audioPairingMatchesTheSpec() throws {
        for testCase in spec.selectAudioDevice {
            let device = selectAudioDevice(
                from: try spec.devices.devices(testCase.candidates), for: try spec.devices.device(testCase.video), matching: testCase.query)
            #expect(device == (try testCase.expected.map(spec.devices.device)), "\(testCase.description)")
        }
    }

    @Test func preferredOrderMatchesTheSpec() throws {
        for testCase in spec.preferredFirst {
            let devices = preferredFirst(try spec.devices.devices(testCase.devices), preferredID: testCase.preferredID)
            #expect(devices == (try spec.devices.devices(testCase.expected)), "\(testCase.description)")
        }
    }

    @Test func autoPlayMatchesTheSpec() throws {
        for testCase in spec.shouldAutoPlay {
            let isAutoPlaying = shouldAutoPlay(try spec.devices.devices(testCase.devices), preferredID: testCase.preferredID)
            #expect(isAutoPlaying == testCase.expected, "\(testCase.description)")
        }
    }
}
