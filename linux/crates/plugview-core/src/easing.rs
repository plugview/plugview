/// A CSS `cubic-bezier()` timing function, so the native animations follow the brand SVGs exactly.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CubicBezier {
    pub x1: f64,
    pub y1: f64,
    pub x2: f64,
    pub y2: f64,
}

impl CubicBezier {
    pub const EASE: Self = Self { x1: 0.25, y1: 0.1, x2: 0.25, y2: 1.0 };
    pub const EASE_IN_OUT: Self = Self { x1: 0.42, y1: 0.0, x2: 0.58, y2: 1.0 };
    pub const LINEAR: Self = Self { x1: 0.0, y1: 0.0, x2: 1.0, y2: 1.0 };

    /// Eased progress for linear progress `x` in 0...1.
    #[must_use]
    pub fn value(&self, x: f64) -> f64 {
        if x <= 0.0 {
            return 0.0;
        }
        if x >= 1.0 {
            return 1.0;
        }
        // Bisection on the curve parameter: x(t) is monotonic for valid CSS curves, and 30 steps is far below a pixel.
        let (mut low, mut high, mut t) = (0.0, 1.0, x);
        for _ in 0..30 {
            t = f64::midpoint(low, high);
            if coordinate(t, self.x1, self.x2) < x {
                low = t;
            } else {
                high = t;
            }
        }
        coordinate(t, self.y1, self.y2)
    }
}

fn coordinate(t: f64, p1: f64, p2: f64) -> f64 {
    let u = 1.0 - t;
    3.0 * u * u * t * p1 + 3.0 * u * t * t * p2 + t * t * t
}

/// One animated property: values at points of the animation's progress, like a CSS `@keyframes` rule.
#[derive(Debug, Clone, PartialEq)]
pub struct Keyframes {
    /// `(progress, value)` pairs in increasing progress.
    pub frames: Vec<(f64, f64)>,
    pub easing: CubicBezier,
}

impl Keyframes {
    /// The value at `progress`; before the first and after the last keyframe it holds (CSS `fill-mode: both`).
    #[must_use]
    pub fn value(&self, progress: f64) -> f64 {
        let (Some(first), Some(last)) = (self.frames.first(), self.frames.last()) else { return 0.0 };
        if progress <= first.0 {
            return first.1;
        }
        for pair in self.frames.windows(2) {
            let ((from_at, from_value), (to_at, to_value)) = (pair[0], pair[1]);
            if progress <= to_at {
                let local = (progress - from_at) / (to_at - from_at);
                return from_value + (to_value - from_value) * self.easing.value(local);
            }
        }
        last.1
    }
}
