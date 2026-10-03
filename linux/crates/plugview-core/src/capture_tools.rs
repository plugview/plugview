/// Stretches video range (16–235) to full range (0–255): the fix for a card that labels its range wrong.
#[derive(Debug)]
pub struct LimitedRangeExpansion;

impl LimitedRangeExpansion {
    pub const SCALE: f64 = 255.0 / 219.0;
    pub const BIAS: f64 = -16.0 / 219.0;

    /// Where a channel value (0...1) lands after expansion, clamped to 0...1.
    #[must_use]
    pub fn expand(value: f64) -> f64 {
        value.mul_add(Self::SCALE, Self::BIAS).clamp(0.0, 1.0)
    }
}

/// The screenshot file name, e.g. `plugview 2026-10-01 at 19.05.12.png`.
#[must_use]
pub fn screenshot_file_name(year: i32, month: u32, day: u32, hour: u32, minute: u32, second: u32) -> String {
    format!("plugview {year}-{month:02}-{day:02} at {hour:02}.{minute:02}.{second:02}.png")
}
