import Testing

@testable import PlugviewUI

@Suite struct KeyframesTests {
    @Test func cubicBezierMatchesCSSEndpointsAndLinear() {
        #expect(CubicBezier.ease(0) == 0)
        #expect(CubicBezier.ease(1) == 1)
        #expect(abs(CubicBezier.linear(0.3) - 0.3) < 0.0001)
        // CSS `ease` at half time is about 80 % of the way.
        #expect(abs(CubicBezier.ease(0.5) - 0.8024) < 0.001)
    }

    @Test func overshootingCurveGoesPastTheTarget() {
        let pop = CubicBezier(x1: 0.2, y1: 1.4, x2: 0.4, y2: 1)
        #expect(stride(from: 0.3, through: 0.8, by: 0.05).contains { pop($0) > 1 })
    }

    @Test func keyframesHoldOutsideTheirRangeAndInterpolateInside() {
        let opacity = Keyframes([(0.5, 0), (0.6, 1)], easing: .linear)
        #expect(opacity.value(at: -1) == 0)
        #expect(opacity.value(at: 0.2) == 0)
        #expect(abs(opacity.value(at: 0.55) - 0.5) < 0.0001)
        #expect(opacity.value(at: 0.9) == 1)
    }
}
