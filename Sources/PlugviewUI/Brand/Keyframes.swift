import Foundation

/// A CSS `cubic-bezier()` timing function, so the native animations follow the brand SVGs exactly.
struct CubicBezier: Sendable {
    let x1: Double
    let y1: Double
    let x2: Double
    let y2: Double

    static let ease = CubicBezier(x1: 0.25, y1: 0.1, x2: 0.25, y2: 1)
    static let easeInOut = CubicBezier(x1: 0.42, y1: 0, x2: 0.58, y2: 1)
    static let linear = CubicBezier(x1: 0, y1: 0, x2: 1, y2: 1)

    /// Eased progress for linear progress `x` in 0...1.
    func callAsFunction(_ x: Double) -> Double {
        guard x > 0 else { return 0 }
        guard x < 1 else { return 1 }
        // Bisection on the curve parameter: x(t) is monotonic for valid CSS curves, and 30 steps is far below a pixel.
        var low = 0.0
        var high = 1.0
        var t = x
        for _ in 0..<30 {
            t = (low + high) / 2
            if Self.coordinate(t, x1, x2) < x { low = t } else { high = t }
        }
        return Self.coordinate(t, y1, y2)
    }

    private static func coordinate(_ t: Double, _ p1: Double, _ p2: Double) -> Double {
        let u = 1 - t
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t
    }
}

/// One animated property: values at points of the animation's progress, like a CSS `@keyframes` rule.
struct Keyframes: Sendable {
    let frames: [(at: Double, value: Double)]
    let easing: CubicBezier

    init(_ frames: [(at: Double, value: Double)], easing: CubicBezier) {
        self.frames = frames
        self.easing = easing
    }

    /// The value at `progress` (0...1); before the first and after the last keyframe it holds, like `fill-mode: both`.
    func value(at progress: Double) -> Double {
        guard let first = frames.first, let last = frames.last else { return 0 }
        guard progress > first.at else { return first.value }
        for (from, to) in zip(frames, frames.dropFirst()) where progress <= to.at {
            let local = (progress - from.at) / (to.at - from.at)
            return from.value + (to.value - from.value) * easing(local)
        }
        return last.value
    }
}
