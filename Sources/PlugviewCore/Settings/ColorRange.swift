/// Stretches video range (16–235) to full range (0–255): the fix for a card that sends full-range video but labels it
/// limited, or the reverse, which shows as gray blacks and dull whites.
///
/// Applied per channel as `output = input × scale + bias`, on values from 0 to 1.
public enum LimitedRangeExpansion {
    public static let scale = 255.0 / 219.0
    public static let bias = -16.0 / 219.0

    /// Where a channel value lands after expansion, clamped to 0…1.
    public static func expand(_ value: Double) -> Double { min(1, max(0, value * scale + bias)) }
}

/// The screenshot file name, e.g. `plugview 2026-10-01 at 19.05.12.png`, in the style of macOS screenshots.
public func screenshotFileName(year: Int, month: Int, day: Int, hour: Int, minute: Int, second: Int) -> String {
    func twoDigits(_ value: Int) -> String { value < 10 ? "0\(value)" : "\(value)" }
    return "plugview \(year)-\(twoDigits(month))-\(twoDigits(day)) at \(twoDigits(hour)).\(twoDigits(minute)).\(twoDigits(second)).png"
}
