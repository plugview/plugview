import AVFoundation
import CoreImage
import PlugviewCore

/// Takes single frames for screenshots and the signal watch without touching the display path.
///
/// Its video data output sits in the session with its connection disabled, so no frame reaches this process while
/// you play; a grab enables it for one frame and disables it again. Adding or removing an output would reconfigure
/// the running session instead, which drops frames and can reset the card's frame rate.
///
/// `@unchecked Sendable`: `waiters` and `grabCount` are only touched on `queue`; the output's connection is toggled
/// on `queue` too.
final class FrameGrabber: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate, @unchecked Sendable {
    /// What one grab got.
    enum Grab {
        case frame(CVPixelBuffer)
        /// A frame arrived without an image buffer (a compressed format AVFoundation did not decode).
        case undecodedFrame
        /// No frame within a second.
        case timedOut
        /// No video connection: nothing is playing.
        case notPlaying
    }

    let output = AVCaptureVideoDataOutput()
    private let queue = DispatchQueue(label: "plugview.frame-grabber", qos: .userInitiated)
    /// Everyone waiting for the next frame: a screenshot and the signal watch can ask at the same time.
    private var waiters: [@Sendable (Grab) -> Void] = []
    /// Counts grabs, so a timeout left over from an earlier grab never ends a later one.
    private var grabCount = 0

    override init() {
        super.init()
        output.alwaysDiscardsLateVideoFrames = true
        output.setSampleBufferDelegate(self, queue: queue)
        setDecodesFrames(false)
    }

    /// Call after adding `output` to the session, inside the same configuration.
    func disable() {
        output.connection(with: .video)?.isEnabled = false
    }

    /// Keeps the device's own pixel format (no conversion set up for frames never asked for), unless the device
    /// sends a compressed format: then AVFoundation decodes the grabbed frames, and only those.
    ///
    /// Call inside the session's configuration.
    func setDecodesFrames(_ isDecoding: Bool) {
        output.videoSettings = isDecoding ? [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange] : [:]
    }

    /// Runs `body` on the next frame from the card (on the grabber's queue) and returns its result.
    func withNextFrame<Result: Sendable>(_ body: @escaping @Sendable (CVPixelBuffer) throws -> Result) async throws -> Result {
        try await withCheckedThrowingContinuation { continuation in
            grab { grab in
                continuation.resume(
                    with: Swift.Result { () throws -> Result in
                        switch grab {
                        case .frame(let frame): return try body(frame)
                        case .undecodedFrame: throw GrabError.undecoded
                        case .timedOut: throw GrabError.timedOut
                        case .notPlaying: throw GrabError.notPlaying
                        }
                    })
            }
        }
    }

    /// Whether a frame arrives within a second, and how bright it is; `nil` when none arrives.
    func nextSignalSample() async -> SignalSample? {
        await withCheckedContinuation { continuation in
            grab { grab in
                switch grab {
                case .frame(let frame): continuation.resume(returning: SignalSample(maxLuma: Self.maxLuma(of: frame)))
                case .undecodedFrame: continuation.resume(returning: SignalSample(maxLuma: nil))
                case .timedOut, .notPlaying: continuation.resume(returning: nil)
                }
            }
        }
    }

    /// Calls `handler` once, on the grabber's queue, with the next frame or why there is none.
    private func grab(_ handler: @escaping @Sendable (Grab) -> Void) {
        queue.async {
            guard let connection = self.output.connection(with: .video) else {
                handler(.notPlaying)
                return
            }
            self.waiters.append(handler)
            guard self.waiters.count == 1 else { return }
            self.grabCount += 1
            let grab = self.grabCount
            connection.isEnabled = true
            self.queue.asyncAfter(deadline: .now() + 1) { if self.grabCount == grab { self.finish(with: .timedOut) } }
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        finish(with: CMSampleBufferGetImageBuffer(sampleBuffer).map(Grab.frame) ?? .undecodedFrame)
    }

    private func finish(with grab: Grab) {
        guard !waiters.isEmpty else { return }
        let finished = waiters
        waiters = []
        grabCount += 1
        output.connection(with: .video)?.isEnabled = false
        for waiter in finished { waiter(grab) }
    }

    /// The brightest luma on a coarse grid; `nil` for pixel formats it does not know.
    private static func maxLuma(of frame: CVPixelBuffer) -> Int? {
        let layout: LumaLayout
        switch CVPixelBufferGetPixelFormatType(frame) {
        case kCVPixelFormatType_422YpCbCr8_yuvs: layout = .yFirst422
        case kCVPixelFormatType_422YpCbCr8: layout = .chromaFirst422
        case kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange, kCVPixelFormatType_420YpCbCr8BiPlanarFullRange: layout = .lumaPlane
        case kCVPixelFormatType_32BGRA: layout = .bgra
        default: return nil
        }
        CVPixelBufferLockBaseAddress(frame, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(frame, .readOnly) }
        let isPlanar = CVPixelBufferIsPlanar(frame)
        guard let base = isPlanar ? CVPixelBufferGetBaseAddressOfPlane(frame, 0) : CVPixelBufferGetBaseAddress(frame) else { return nil }
        let bytesPerRow = isPlanar ? CVPixelBufferGetBytesPerRowOfPlane(frame, 0) : CVPixelBufferGetBytesPerRow(frame)
        let height = isPlanar ? CVPixelBufferGetHeightOfPlane(frame, 0) : CVPixelBufferGetHeight(frame)
        let width = isPlanar ? CVPixelBufferGetWidthOfPlane(frame, 0) : CVPixelBufferGetWidth(frame)
        let bytes = UnsafeRawBufferPointer(start: base, count: bytesPerRow * height)
        return PlugviewCore.maxLuma(in: bytes, width: width, height: height, bytesPerRow: bytesPerRow, layout: layout)
    }

    enum GrabError: Error, CustomStringConvertible {
        case notPlaying
        case timedOut
        case undecoded

        var description: String {
            switch self {
            case .notPlaying: "nothing is playing"
            case .timedOut: "the card sent no frame within a second"
            case .undecoded: "the card's frame could not be decoded"
            }
        }
    }
}

/// One look at the signal: a frame arrived, with its brightest luma when the format allows reading it.
public struct SignalSample: Sendable, Equatable {
    public let maxLuma: Int?
}
