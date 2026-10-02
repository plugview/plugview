import AVFoundation
import CoreImage
import PlugviewCore

/// Takes single frames for screenshots without touching the display path.
///
/// Its video data output sits in the session with its connection disabled, so no frame reaches this process while
/// you play; a grab enables it for one frame and disables it again. Adding or removing an output would reconfigure
/// the running session instead, which drops frames and can reset the card's frame rate.
///
/// `@unchecked Sendable`: `pending` is only touched on `queue`; the output's connection is toggled on `queue` too.
final class FrameGrabber: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate, @unchecked Sendable {
    let output = AVCaptureVideoDataOutput()
    private let queue = DispatchQueue(label: "plugview.frame-grabber", qos: .userInitiated)
    /// Set while a grab waits; called with the frame, or `nil` on timeout.
    private var pending: ((CVPixelBuffer?) -> Void)?
    /// Counts grabs, so a timeout left over from an earlier grab never ends a later one.
    private var grabCount = 0

    override init() {
        super.init()
        // An empty dictionary keeps the device's own pixel format: no conversion is set up for frames never asked for.
        output.videoSettings = [:]
        output.alwaysDiscardsLateVideoFrames = true
        output.setSampleBufferDelegate(self, queue: queue)
    }

    /// Call after adding `output` to the session, inside the same configuration.
    func disable() {
        output.connection(with: .video)?.isEnabled = false
    }

    /// Runs `body` on the next frame from the card (on the grabber's queue) and returns its result; fails with
    /// `GrabError.timedOut` after a second without a frame.
    func withNextFrame<Result: Sendable>(_ body: @escaping @Sendable (CVPixelBuffer) throws -> Result) async throws -> Result {
        try await withCheckedThrowingContinuation { continuation in
            queue.async {
                guard self.pending == nil, let connection = self.output.connection(with: .video) else {
                    continuation.resume(throwing: GrabError.notPlaying)
                    return
                }
                self.pending = { frame in
                    continuation.resume(
                        with: Swift.Result { () throws -> Result in
                            guard let frame else { throw GrabError.timedOut }
                            return try body(frame)
                        })
                }
                self.grabCount += 1
                let grab = self.grabCount
                connection.isEnabled = true
                self.queue.asyncAfter(deadline: .now() + 1) { if self.grabCount == grab { self.finish(with: nil) } }
            }
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        finish(with: pixelBuffer)
    }

    private func finish(with frame: CVPixelBuffer?) {
        guard let pending else { return }
        self.pending = nil
        output.connection(with: .video)?.isEnabled = false
        pending(frame)
    }

    enum GrabError: Error, CustomStringConvertible {
        case notPlaying
        case timedOut

        var description: String {
            switch self {
            case .notPlaying: "nothing is playing"
            case .timedOut: "the card sent no frame within a second"
            }
        }
    }
}
