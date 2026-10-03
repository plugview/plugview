import Foundation
import Testing

@testable import PlugviewUI

/// Checks the brand easing against `spec/fixtures/easing.json`, which every platform's animations must match.
@Suite struct KeyframesTests {
    struct Spec: Decodable {
        let tolerance: Double
        let cubicBezier: [Curve]
        let keyframes: [KeyframesCase]
    }

    struct Curve: Decodable {
        let name: String
        let points: [Double]
        let samples: [[Double]]
    }

    struct KeyframesCase: Decodable {
        let description: String
        let frames: [[Double]]
        let easing: [Double]
        let samples: [[Double]]
    }

    let spec: Spec

    init() throws {
        let url = URL(filePath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "spec/fixtures/easing.json")
        spec = try JSONDecoder().decode(Spec.self, from: Data(contentsOf: url))
    }

    @Test func cubicBeziersMatchTheSpec() throws {
        for curve in spec.cubicBezier {
            let easing = try bezier(curve.points)
            for sample in curve.samples {
                #expect(abs(easing(sample[0]) - sample[1]) < spec.tolerance, "\(curve.name) at \(sample[0])")
            }
        }
    }

    @Test func keyframesMatchTheSpec() throws {
        for testCase in spec.keyframes {
            let keyframes = Keyframes(testCase.frames.map { (at: $0[0], value: $0[1]) }, easing: try bezier(testCase.easing))
            for sample in testCase.samples {
                #expect(abs(keyframes.value(at: sample[0]) - sample[1]) < spec.tolerance, "\(testCase.description) at \(sample[0])")
            }
        }
    }

    @Test func namedCurvesAreTheBrandOnes() {
        #expect(CubicBezier.ease.x1 == 0.25 && CubicBezier.ease.y1 == 0.1 && CubicBezier.ease.x2 == 0.25 && CubicBezier.ease.y2 == 1)
        #expect(CubicBezier.easeInOut.x1 == 0.42 && CubicBezier.easeInOut.y1 == 0 && CubicBezier.easeInOut.x2 == 0.58 && CubicBezier.easeInOut.y2 == 1)
    }

    private func bezier(_ points: [Double]) throws -> CubicBezier {
        guard points.count == 4 else { throw SpecError.malformedCurve(points) }
        return CubicBezier(x1: points[0], y1: points[1], x2: points[2], y2: points[3])
    }

    enum SpecError: Error {
        case malformedCurve([Double])
    }
}
