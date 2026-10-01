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

/// Plain-language fixes for the latency problems the facts reveal, most important first.
public func latencyAdvice(for facts: LatencyFacts) -> [String] {
    var advice: [String] = []
    if facts.deliveredFrameRate < facts.requestedFrameRate * 0.9 {
        advice.append(
            "The card delivers \(Int(facts.deliveredFrameRate.rounded())) fps instead of \(Int(facts.requestedFrameRate.rounded())). "
                + "Plug it straight into a USB 3 port (no hub) and check the console's video output setting.")
    }
    if facts.isOutputBluetooth {
        advice.append("Sound plays over Bluetooth, which adds 100–300 ms. Use the Mac speakers or wired headphones for games.")
    } else if let latency = facts.outputLatencyMilliseconds, latency > Tuning.slowOutputLatencyMilliseconds {
        advice.append("The playback device adds \(Int(latency.rounded())) ms. Wired headphones or the Mac speakers are faster.")
    }
    if let refreshRate = facts.displayRefreshRate, facts.deliveredFrameRate > 0, !isWholeMultiple(refreshRate, of: facts.deliveredFrameRate) {
        advice.append(
            "The display runs at \(Int(refreshRate.rounded())) Hz, not a multiple of \(Int(facts.deliveredFrameRate.rounded())) fps, "
                + "so frames are shown for uneven times. A refresh rate of 60 or 120 Hz gives even motion.")
    }
    return advice
}

private func isWholeMultiple(_ value: Double, of base: Double) -> Bool {
    let ratio = value / base
    return ratio >= 0.98 && abs(ratio - ratio.rounded()) < 0.02
}
