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

/// A size and frame rate the user picked for a device, saved as text such as `1920x1080@60`.
public struct ModeChoice: Equatable, Hashable, Sendable {
    public let width: Int
    public let height: Int
    public let frameRate: Int

    public init(width: Int, height: Int, frameRate: Int) {
        self.width = width
        self.height = height
        self.frameRate = frameRate
    }

    public init(_ mode: VideoMode) {
        self.init(width: mode.width, height: mode.height, frameRate: Int(mode.maxFrameRate.rounded()))
    }

    /// Reads a saved choice; `nil` for anything that is not `<width>x<height>@<fps>` with positive whole numbers.
    public init?(key: String) {
        let sizeAndRate = key.split(separator: "@", omittingEmptySubsequences: false)
        guard sizeAndRate.count == 2 else { return nil }
        let size = sizeAndRate[0].split(separator: "x", omittingEmptySubsequences: false)
        guard size.count == 2, let width = wholeNumber(size[0]), let height = wholeNumber(size[1]), let frameRate = wholeNumber(sizeAndRate[1]),
            width > 0, height > 0, frameRate > 0
        else { return nil }
        self.init(width: width, height: height, frameRate: frameRate)
    }

    public var key: String { "\(width)x\(height)@\(frameRate)" }

    /// Menu text, e.g. `1920×1080 · 60 fps`.
    public var title: String { "\(width)×\(height) · \(frameRate) fps" }

    func matches(_ mode: VideoMode) -> Bool { self == ModeChoice(mode) }
}

/// The modes worth offering in a menu: one per size and frame rate, largest and fastest first.
public func pickableModes(in modes: [VideoMode]) -> [ModeChoice] {
    let choices = Set(modes.filter { $0.maxFrameRate >= 1 }.map(ModeChoice.init))
    return choices.sorted { ($0.width * $0.height, $0.frameRate) > ($1.width * $1.height, $1.frameRate) }
}

/// Returns the index of the mode to start: the user's choice when the device still offers it, otherwise the best.
///
/// Among formats with the chosen size and frame rate, the one with the least work per frame wins.
public func modeIndex(in modes: [VideoMode], choice: ModeChoice?) -> Int? {
    guard let choice else { return bestModeIndex(in: modes) }
    let matching = modes.indices.filter { choice.matches(modes[$0]) }
    return matching.max { modes[$0].encoding < modes[$1].encoding } ?? bestModeIndex(in: modes)
}
