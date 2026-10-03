/// Facts gathered by `plugview --measure`, reduced to what the advice depends on.
public struct LatencyFacts: Equatable, Sendable {
    public var requestedFrameRate: Double
    /// Frame rate derived from the median interval between delivered frames.
    public var deliveredFrameRate: Double
    public var displayRefreshRate: Double?
    public var outputLatencyMilliseconds: Double?
    public var isOutputBluetooth: Bool

    public init(
        requestedFrameRate: Double, deliveredFrameRate: Double, displayRefreshRate: Double?, outputLatencyMilliseconds: Double?,
        isOutputBluetooth: Bool
    ) {
        self.requestedFrameRate = requestedFrameRate
        self.deliveredFrameRate = deliveredFrameRate
        self.displayRefreshRate = displayRefreshRate
        self.outputLatencyMilliseconds = outputLatencyMilliseconds
        self.isOutputBluetooth = isOutputBluetooth
    }
}

/// A latency problem `--measure` found, with the rounded numbers its advice shows.
///
/// Every platform finds the same issues (`spec/fixtures/latency-advice.json`) and words them for its own hardware.
public enum LatencyIssue: Equatable, Sendable {
    /// The card delivers fewer frames than the mode promises: a USB 2 link, a hub, or the console's output setting.
    case lowFrameRate(delivered: Int, requested: Int)
    /// Sound plays over Bluetooth, which adds 100–300 ms whatever the device reports.
    case bluetoothOutput
    /// A wired playback device that reports more than `Tuning.slowOutputLatencyMilliseconds`.
    case slowOutput(milliseconds: Int)
    /// The display refresh rate is not a whole multiple of the frame rate, so frames show for uneven times.
    case unevenRefresh(refreshRate: Int, frameRate: Int)
}

/// The latency problems the facts reveal, most important first.
public func latencyIssues(for facts: LatencyFacts) -> [LatencyIssue] {
    var issues: [LatencyIssue] = []
    if facts.deliveredFrameRate < facts.requestedFrameRate * 0.9 {
        issues.append(.lowFrameRate(delivered: Int(facts.deliveredFrameRate.rounded()), requested: Int(facts.requestedFrameRate.rounded())))
    }
    if facts.isOutputBluetooth {
        issues.append(.bluetoothOutput)
    } else if let latency = facts.outputLatencyMilliseconds, latency > Tuning.slowOutputLatencyMilliseconds {
        issues.append(.slowOutput(milliseconds: Int(latency.rounded())))
    }
    if let refreshRate = facts.displayRefreshRate, facts.deliveredFrameRate > 0, !isWholeMultiple(refreshRate, of: facts.deliveredFrameRate) {
        issues.append(.unevenRefresh(refreshRate: Int(refreshRate.rounded()), frameRate: Int(facts.deliveredFrameRate.rounded())))
    }
    return issues
}

/// Plain-language fixes for the latency problems the facts reveal, most important first.
public func latencyAdvice(for facts: LatencyFacts) -> [String] {
    latencyIssues(for: facts).map(adviceText)
}

private func adviceText(for issue: LatencyIssue) -> String {
    switch issue {
    case .lowFrameRate(let delivered, let requested):
        "The card delivers \(delivered) fps instead of \(requested). "
            + "Plug it straight into a USB 3 port (no hub) and check the console's video output setting."
    case .bluetoothOutput:
        "Sound plays over Bluetooth, which adds 100–300 ms. Use the Mac speakers or wired headphones for games."
    case .slowOutput(let milliseconds):
        "The playback device adds \(milliseconds) ms. Wired headphones or the Mac speakers are faster."
    case .unevenRefresh(let refreshRate, let frameRate):
        "The display runs at \(refreshRate) Hz, not a multiple of \(frameRate) fps, "
            + "so frames are shown for uneven times. A refresh rate of 60 or 120 Hz gives even motion."
    }
}

private func isWholeMultiple(_ value: Double, of base: Double) -> Bool {
    let ratio = value / base
    return ratio >= 0.98 && abs(ratio - ratio.rounded()) < 0.02
}
