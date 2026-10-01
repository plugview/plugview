/// How much work a frame needs before it can be shown.
///
/// Later cases reach the screen sooner.
public enum FrameEncoding: Int, Comparable, Sendable {
    /// MJPEG, H.264, or HEVC: every frame needs a decode step.
    case compressed
    /// Uncompressed but not the card's wire layout (e.g. `420v`), so macOS converts every frame.
    case converted
    /// Packed 4:2:2 (`yuvs`, `2vuy`): what USB capture cards send natively. Measured 4.6 ms faster than `420v`.
    case native

    public static func < (lhs: Self, rhs: Self) -> Bool { lhs.rawValue < rhs.rawValue }
}

/// Framework-free description of one capture format.
public struct VideoMode: Equatable, Sendable {
    public let width: Int
    public let height: Int
    public let maxFrameRate: Double
    public let encoding: FrameEncoding

    public init(width: Int, height: Int, maxFrameRate: Double, encoding: FrameEncoding) {
        self.width = width
        self.height = height
        self.maxFrameRate = maxFrameRate
        self.encoding = encoding
    }

    /// Human-readable summary, e.g. `1920×1080 @ 60 fps`.
    public var summary: String { "\(width)×\(height) @ \(Int(maxFrameRate.rounded())) fps" }
}

/// Returns the index of the best mode for live play.
///
/// Ranks smooth frame rate first, then resolution, then frame rate, then the encoding with the least work per frame.
/// A 1080p60 mode therefore beats a 4K30 one.
public func bestModeIndex(in modes: [VideoMode]) -> Int? {
    func rank(_ mode: VideoMode) -> (Int, Int, Double, Int) {
        (mode.maxFrameRate >= Tuning.smoothFrameRate ? 1 : 0, mode.width * mode.height, mode.maxFrameRate, mode.encoding.rawValue)
    }
    return modes.indices.max { rank(modes[$0]) < rank(modes[$1]) }
}
