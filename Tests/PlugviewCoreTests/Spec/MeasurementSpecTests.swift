import Foundation
import PlugviewCore
import Testing

@Suite struct MeasurementSpecTests {
    struct Facts: Decodable {
        let requestedFrameRate: Double
        let deliveredFrameRate: Double
        let displayRefreshRate: Double?
        let outputLatencyMilliseconds: Double?
        let isOutputBluetooth: Bool
    }

    struct AdviceCase: Decodable {
        let description: String
        let expected: [Issue]
    }

    struct Issue: Decodable {
        let issue: String
        let delivered: Int?
        let requested: Int?
        let milliseconds: Int?
        let refreshRate: Int?
        let frameRate: Int?

        func latencyIssue() throws -> LatencyIssue {
            switch issue {
            case "lowFrameRate": .lowFrameRate(delivered: try field(delivered), requested: try field(requested))
            case "bluetoothOutput": .bluetoothOutput
            case "slowOutput": .slowOutput(milliseconds: try field(milliseconds))
            case "unevenRefresh": .unevenRefresh(refreshRate: try field(refreshRate), frameRate: try field(frameRate))
            default: throw FixtureError.unknownValue("issue \(issue)")
            }
        }

        private func field(_ value: Int?) throws -> Int {
            guard let value else { throw FixtureError.missingField("number in \(issue)") }
            return value
        }
    }

    struct PercentileCase: Decodable {
        let description: String
        let samples: [Double]
        let fraction: Double
        let expected: Double?
    }

    /// Each case's `facts` override the healthy setup; an explicit `null` means unknown.
    @Test func latencyIssuesMatchTheSpec() throws {
        struct Spec: Decodable { let cases: [AdviceCase] }
        let cases = try Fixture.decode(Spec.self, from: "latency-advice.json").cases
        let raw = try Fixture.object("latency-advice.json")
        guard let healthy = raw["healthy"] as? [String: Any], let rawCases = raw["cases"] as? [[String: Any]] else {
            throw FixtureError.missingField("healthy or cases")
        }

        for (testCase, rawCase) in zip(cases, rawCases) {
            let overrides = rawCase["facts"] as? [String: Any] ?? [:]
            let merged = healthy.merging(overrides) { _, override in override }
            let facts = try JSONDecoder().decode(Facts.self, from: JSONSerialization.data(withJSONObject: merged))
            let latencyFacts = LatencyFacts(
                requestedFrameRate: facts.requestedFrameRate, deliveredFrameRate: facts.deliveredFrameRate, displayRefreshRate: facts.displayRefreshRate,
                outputLatencyMilliseconds: facts.outputLatencyMilliseconds, isOutputBluetooth: facts.isOutputBluetooth)
            #expect(latencyIssues(for: latencyFacts) == (try testCase.expected.map { try $0.latencyIssue() }), "\(testCase.description)")
        }
    }

    @Test func percentilesMatchTheSpec() throws {
        for testCase in try Fixture.decode([PercentileCase].self, from: "percentile.json") {
            #expect(percentile(testCase.samples, testCase.fraction) == testCase.expected, "\(testCase.description)")
        }
    }
}
