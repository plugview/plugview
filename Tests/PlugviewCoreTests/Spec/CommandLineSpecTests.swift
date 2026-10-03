import PlugviewCore
import Testing

@Suite struct CommandLineSpecTests {
    struct Spec: Decodable {
        let defaults: Options
        let cases: [Case]
    }

    struct Case: Decodable {
        let description: String
        let arguments: [String]
        let expected: Expected
    }

    struct Expected: Decodable {
        let command: String?
        let options: Options?
        let error: String?
    }

    /// Run options; a missing field keeps its default.
    struct Options: Decodable {
        let videoQuery: String?
        let audioQuery: String?
        let isAudioEnabled: Bool?
        let volumePercent: Int?
        let startsInFullScreen: Bool?
        let mode: String?

        func runOptions() throws -> RunOptions {
            var options = RunOptions()
            options.videoQuery = videoQuery
            options.audioQuery = audioQuery
            options.isAudioEnabled = isAudioEnabled ?? options.isAudioEnabled
            options.volumePercent = volumePercent ?? options.volumePercent
            options.startsInFullScreen = startsInFullScreen ?? options.startsInFullScreen
            options.mode = try mode.map { key in
                guard let choice = ModeChoice(key: key) else { throw FixtureError.unknownValue("mode \(key)") }
                return choice
            }
            return options
        }
    }

    let spec: Spec

    init() throws {
        spec = try Fixture.decode(Spec.self, from: "command-line.json")
    }

    @Test func defaultsMatchTheSpec() throws {
        let defaults = spec.defaults
        let options = RunOptions()
        #expect(options.videoQuery == defaults.videoQuery && options.audioQuery == defaults.audioQuery)
        #expect(options.isAudioEnabled == defaults.isAudioEnabled && options.volumePercent == defaults.volumePercent)
        #expect(options.startsInFullScreen == defaults.startsInFullScreen && options.mode?.key == defaults.mode)
    }

    @Test func parsingMatchesTheSpec() throws {
        for testCase in spec.cases {
            switch Result(catching: { () throws(OptionsError) in try parseCommand(testCase.arguments) }) {
            case .success(let command):
                #expect(testCase.expected.error == nil, "\(testCase.description): parsed, expected an error")
                #expect(command == (try expectedCommand(testCase.expected)), "\(testCase.description)")
            case .failure(let error):
                #expect(error.description == testCase.expected.error, "\(testCase.description)")
            }
        }
    }

    private func expectedCommand(_ expected: Expected) throws -> Command? {
        switch expected.command {
        case nil: nil
        case "run": .run(try (expected.options ?? spec.defaults).runOptions())
        case "measure": .measure(try (expected.options ?? spec.defaults).runOptions())
        case "listDevices": .listDevices
        case "showHelp": .showHelp
        case "showVersion": .showVersion
        case let name?: throw FixtureError.unknownValue("command \(name)")
        }
    }
}
