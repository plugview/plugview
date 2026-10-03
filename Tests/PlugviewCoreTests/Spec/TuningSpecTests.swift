import PlugviewCore
import Testing

@Suite struct TuningSpecTests {
    struct Spec: Decodable {
        let smoothFrameRate: Double
        let cardAudioWaitSeconds: Double
        let volumeStepPercent: Int
        let controlsHideSeconds: Double
        let updateCheckIntervalSeconds: Double
        let measurementSeconds: Double
        let slowOutputLatencyMilliseconds: Double
    }

    struct CaptureTools: Decodable {
        let screenshotFileName: [ScreenshotCase]
        let limitedRangeExpansion: RangeExpansion
    }

    struct ScreenshotCase: Decodable {
        let date: Moment
        let expected: String
    }

    struct Moment: Decodable {
        let year: Int
        let month: Int
        let day: Int
        let hour: Int
        let minute: Int
        let second: Int
    }

    struct RangeExpansion: Decodable {
        let tolerance: Double
        let samples: [Sample]
    }

    struct Sample: Decodable {
        let input: Double
        let expected: Double
    }

    @Test func tuningValuesMatchTheSpec() throws {
        let spec = try Fixture.decode(Spec.self, from: "tuning.json")
        #expect(Tuning.smoothFrameRate == spec.smoothFrameRate)
        #expect(Tuning.cardAudioWaitSeconds == spec.cardAudioWaitSeconds)
        #expect(Tuning.volumeStepPercent == spec.volumeStepPercent)
        #expect(Tuning.controlsHideSeconds == spec.controlsHideSeconds)
        #expect(Tuning.updateCheckIntervalSeconds == spec.updateCheckIntervalSeconds)
        #expect(Tuning.measurementSeconds == spec.measurementSeconds)
        #expect(Tuning.slowOutputLatencyMilliseconds == spec.slowOutputLatencyMilliseconds)
    }

    @Test func captureToolsMatchTheSpec() throws {
        let spec = try Fixture.decode(CaptureTools.self, from: "capture-tools.json")
        for testCase in spec.screenshotFileName {
            let date = testCase.date
            let name = screenshotFileName(year: date.year, month: date.month, day: date.day, hour: date.hour, minute: date.minute, second: date.second)
            #expect(name == testCase.expected)
        }
        for sample in spec.limitedRangeExpansion.samples {
            #expect(abs(LimitedRangeExpansion.expand(sample.input) - sample.expected) < spec.limitedRangeExpansion.tolerance, "\(sample.input)")
        }
    }
}
