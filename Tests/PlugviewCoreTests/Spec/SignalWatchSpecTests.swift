import PlugviewCore
import Testing

@Suite struct SignalWatchSpecTests {
    struct Spec: Decodable {
        let watch: [WatchCase]
        let blackFrame: [BlackFrameCase]
        let hints: [HintCase]
    }

    struct WatchCase: Decodable {
        let description: String
        let secondsSinceLastFrame: Double
        let reopenAttempts: Int
        let expected: Expected
    }

    struct Expected: Decodable {
        let showsNoSignal: Bool
        let shouldReopen: Bool
        let showsSignalHelp: Bool
    }

    struct BlackFrameCase: Decodable {
        let maxLuma: Int
        let expected: Bool
    }

    struct HintCase: Decodable {
        let description: String
        let blackPictureSeconds: Double
        let silentAudioSeconds: Double?
        let expected: [String]
    }

    let spec: Spec

    init() throws {
        spec = try Fixture.decode(Spec.self, from: "signal-watch.json")
    }

    @Test func watchMatchesTheSpec() {
        for testCase in spec.watch {
            let expected = SignalWatch(
                showsNoSignal: testCase.expected.showsNoSignal, shouldReopen: testCase.expected.shouldReopen,
                showsSignalHelp: testCase.expected.showsSignalHelp)
            #expect(
                signalWatch(secondsSinceLastFrame: testCase.secondsSinceLastFrame, reopenAttempts: testCase.reopenAttempts) == expected,
                "\(testCase.description)")
        }
    }

    @Test func blackFramesMatchTheSpec() {
        for testCase in spec.blackFrame {
            #expect(isBlackFrame(maxLuma: testCase.maxLuma) == testCase.expected, "max luma \(testCase.maxLuma)")
        }
    }

    @Test func hintsMatchTheSpec() throws {
        for testCase in spec.hints {
            let expected = try testCase.expected.map { name -> SignalHint in
                switch name {
                case "blackPicture": .blackPicture
                case "noGameSound": .noGameSound
                default: throw FixtureError.unknownValue("hint \(name)")
                }
            }
            #expect(
                signalHints(blackPictureSeconds: testCase.blackPictureSeconds, silentAudioSeconds: testCase.silentAudioSeconds) == expected,
                "\(testCase.description)")
        }
    }
}
