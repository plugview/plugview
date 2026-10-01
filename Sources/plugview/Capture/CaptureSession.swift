import AVFoundation
import PlugviewCore

/// What is on screen after a successful start.
struct ActiveCapture: Sendable {
    let videoName: String
    let audioName: String?
    /// The running mode, with the frame rate read back from the device rather than the one requested.
    let mode: VideoMode
}

enum CaptureError: Error, CustomStringConvertible {
    case deviceUnavailable(id: String)
    case cannotOpen(name: String, cause: any Error)
    case cannotAdd(name: String)
    case noVideoFormats(name: String)

    var description: String {
        switch self {
        case .deviceUnavailable(let id): "device \(id) is no longer available"
        case .cannotOpen(let name, let cause): "cannot open \(name): \(cause.localizedDescription)"
        case .cannotAdd(let name): "\(name) cannot be added to the capture session (is another app using it?)"
        case .noVideoFormats(let name): "\(name) reports no video formats"
        }
    }
}

/// Owns the AVCaptureSession.
///
/// Video is drawn by an AVCaptureVideoPreviewLayer
/// (frames go straight to the GPU compositor) and audio is played by an
/// AVCaptureAudioPreviewOutput, so no frame or sample ever passes through our code.
///
/// `@unchecked Sendable`: every touch of the mutable AVFoundation state below
/// happens on the serial `queue`, which is the confinement Apple documents for
/// AVCaptureSession (its start/stop calls block and must stay off the main thread).
final class CaptureSession: @unchecked Sendable {
    let session = AVCaptureSession()
    private let queue = DispatchQueue(label: "plugview.capture-session", qos: .userInteractive)
    private let audioOutput = AVCaptureAudioPreviewOutput()

    func start(videoID: String, audioID: String?, volume: Float) async throws -> ActiveCapture {
        try await withCheckedThrowingContinuation { continuation in
            queue.async {
                continuation.resume(with: Result { try self.startOnQueue(videoID: videoID, audioID: audioID, volume: volume) })
            }
        }
    }

    func stop() {
        queue.async {
            self.session.stopRunning()
            self.removeInputsAndOutputs()
        }
    }

    func setVolume(_ volume: Float) {
        queue.async { self.audioOutput.volume = volume }
    }

    private func startOnQueue(videoID: String, audioID: String?, volume: Float) throws -> ActiveCapture {
        guard let videoDevice = AVCaptureDevice(uniqueID: videoID) else { throw CaptureError.deviceUnavailable(id: videoID) }
        let (requested, audioName) = try configure(video: videoDevice, audioID: audioID, volume: volume)
        // The device is still locked from lockBestFormat(), which keeps the chosen frame rate through startRunning().
        session.startRunning()
        defer { videoDevice.unlockForConfiguration() }

        let running = VideoMode(width: requested.width, height: requested.height, maxFrameRate: videoDevice.activeFrameRate, encoding: requested.encoding)
        return ActiveCapture(videoName: videoDevice.localizedName, audioName: audioName, mode: running)
    }

    /// Adds the inputs and outputs; on success the video device is left locked (see `lockBestFormat`).
    private func configure(video videoDevice: AVCaptureDevice, audioID: String?, volume: Float) throws -> (VideoMode, audioName: String?) {
        session.beginConfiguration()
        defer { session.commitConfiguration() }
        removeInputsAndOutputs()

        try addInput(for: videoDevice)
        let mode = try videoDevice.lockBestFormat()
        do {
            return (mode, try audioID.map { id in try attachAudio(deviceID: id, volume: volume) })
        } catch {
            videoDevice.unlockForConfiguration()
            throw error
        }
    }

    private func attachAudio(deviceID: String, volume: Float) throws -> String {
        guard let audioDevice = AVCaptureDevice(uniqueID: deviceID) else { throw CaptureError.deviceUnavailable(id: deviceID) }
        try addInput(for: audioDevice)

        guard session.canAddOutput(audioOutput) else { throw CaptureError.cannotAdd(name: "audio output") }
        session.addOutput(audioOutput)
        audioOutput.volume = volume
        requestLowLatencyAudioBuffers(inputUID: deviceID)
        return audioDevice.localizedName
    }

    private func addInput(for device: AVCaptureDevice) throws {
        let input: AVCaptureDeviceInput
        do {
            input = try AVCaptureDeviceInput(device: device)
        } catch {
            throw CaptureError.cannotOpen(name: device.localizedName, cause: error)
        }
        guard session.canAddInput(input) else { throw CaptureError.cannotAdd(name: device.localizedName) }
        session.addInput(input)
    }

    private func removeInputsAndOutputs() {
        for input in session.inputs { session.removeInput(input) }
        for output in session.outputs { session.removeOutput(output) }
    }
}
