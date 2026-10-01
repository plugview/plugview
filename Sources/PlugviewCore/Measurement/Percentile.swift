/// Nearest-rank percentile of `samples`, with `fraction` in 0...1.
///
/// Returns nil for no samples.
public func percentile(_ samples: [Double], _ fraction: Double) -> Double? {
    guard !samples.isEmpty else { return nil }
    let sorted = samples.sorted()
    let rank = Int((Double(sorted.count - 1) * min(1, max(0, fraction))).rounded())
    return sorted[rank]
}
