import PlugviewCore
import Testing

@Suite struct VideoModeSpecTests {
    struct Spec: Decodable {
        let bestModeIndex: [BestCase]
        let pickableModes: [PickableCase]
        let modeIndex: [IndexCase]
        let modeChoiceFromMode: [ChoiceFromModeCase]
        let modeChoiceKeys: Keys
        let summary: [SummaryCase]
    }

    struct BestCase: Decodable {
        let description: String
        let modes: [FixtureMode]
        let expected: Int?
    }

    struct PickableCase: Decodable {
        let description: String
        let modes: [FixtureMode]
        let expected: [String]
    }

    struct IndexCase: Decodable {
        let description: String
        let modes: [FixtureMode]
        let choice: String?
        let expected: Int?
    }

    struct ChoiceFromModeCase: Decodable {
        let description: String
        let mode: FixtureMode
        let key: String
        let title: String
    }

    struct Keys: Decodable {
        let valid: [ValidKey]
        let invalid: [String]
    }

    struct ValidKey: Decodable {
        let key: String
        let width: Int
        let height: Int
        let frameRate: Int
    }

    struct SummaryCase: Decodable {
        let description: String
        let mode: FixtureMode
        let expected: String
    }

    let spec: Spec

    init() throws {
        spec = try Fixture.decode(Spec.self, from: "video-modes.json")
    }

    @Test func bestModeMatchesTheSpec() throws {
        for testCase in spec.bestModeIndex {
            #expect(bestModeIndex(in: try testCase.modes.map { try $0.mode() }) == testCase.expected, "\(testCase.description)")
        }
    }

    @Test func pickableModesMatchTheSpec() throws {
        for testCase in spec.pickableModes {
            #expect(pickableModes(in: try testCase.modes.map { try $0.mode() }).map(\.key) == testCase.expected, "\(testCase.description)")
        }
    }

    @Test func startingModeMatchesTheSpec() throws {
        for testCase in spec.modeIndex {
            let choice = try testCase.choice.map { key in
                guard let choice = ModeChoice(key: key) else { throw FixtureError.unknownValue("mode \(key)") }
                return choice
            }
            #expect(modeIndex(in: try testCase.modes.map { try $0.mode() }, choice: choice) == testCase.expected, "\(testCase.description)")
        }
    }

    @Test func modeChoicesMatchTheSpec() throws {
        for testCase in spec.modeChoiceFromMode {
            let choice = ModeChoice(try testCase.mode.mode())
            #expect(choice.key == testCase.key, "\(testCase.description)")
            #expect(choice.title == testCase.title, "\(testCase.description)")
        }
        for valid in spec.modeChoiceKeys.valid {
            #expect(ModeChoice(key: valid.key) == ModeChoice(width: valid.width, height: valid.height, frameRate: valid.frameRate), "\(valid.key)")
        }
        for invalid in spec.modeChoiceKeys.invalid {
            #expect(ModeChoice(key: invalid) == nil, "\(invalid)")
        }
    }

    @Test func summariesMatchTheSpec() throws {
        for testCase in spec.summary {
            #expect(try testCase.mode.mode().summary == testCase.expected, "\(testCase.description)")
        }
    }
}
