import AVFoundation
import AppKit
import PlugviewCore

/// `plugview --measure`: captures for a few seconds without a window and reports where the latency goes.
///
/// Latency is the time from the driver's capture timestamp to the frame reaching this process, i.e. the part
/// plugview controls. Glass-to-glass latency also includes the card itself and the display, which software
/// cannot observe (see docs/PERFORMANCE.md for how to measure it with a phone camera).
///
/// `@unchecked Sendable`: the sample arrays are only touched on `queue`.
public final class LatencyProbe: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate, AVCaptureAudioDataOutputSampleBufferDelegate,
    @unchecked Sendable
{
    private let session = AVCaptureSession()
    private let queue = DispatchQueue(label: "plugview.latency-probe", qos: .userInteractive)
    private var videoLatencies: [Double] = []
    private var frameIntervals: [Double] = []
    private var lastFrameTime: Double?
    private var audioLatencies: [Double] = []
    /// Samples before this host time include start-up work and are ignored.
    private var warmUpEndTime = Double.infinity

    public static func run(_ options: RunOptions) async -> Int32 {
        guard await Permissions.isGranted(for: .video) else {
            printError(Permissions.cameraDeniedMessage)
            return EXIT_FAILURE
        }
        guard let video = selectVideoDevice(from: DeviceCatalog.videoDevices(), matching: options.videoQuery) else {
            printError("no capture device found. Run 'plugview --list' to see detected devices.")
            return EXIT_FAILURE
        }
        let isAudioMeasured = options.isAudioEnabled ? await Permissions.isGranted(for: .audio) : false
        let audio = isAudioMeasured ? selectAudioDevice(from: DeviceCatalog.audioDevices(), for: video, matching: options.audioQuery) : nil

        print("Measuring \(video.name) for \(Int(Tuning.measurementSeconds)) seconds…")
        do {
            try await LatencyProbe().measure(videoID: video.uniqueID, audioID: audio?.uniqueID)
            return EXIT_SUCCESS
        } catch {
            printError("\(error)")
            return EXIT_FAILURE
        }
    }

    private func measure(videoID: String, audioID: String?) async throws {
        guard let videoDevice = AVCaptureDevice(uniqueID: videoID) else { throw CaptureError.deviceUnavailable(id: videoID) }
        let audioDevice = audioID.flatMap(AVCaptureDevice.init(uniqueID:))

        let requested = try configure(video: videoDevice, audio: audioDevice)
        session.startRunning()
        let deliveredFrameRateSetting = videoDevice.activeFrameRate
        videoDevice.unlockForConfiguration()
        queue.sync { warmUpEndTime = hostTimeSeconds() + 0.5 }

        try await Task.sleep(for: .seconds(Tuning.measurementSeconds))
        session.stopRunning()

        let samples = queue.sync { (videoLatencies, frameIntervals, audioLatencies) }
        report(video: videoDevice, mode: requested, frameRateSetting: deliveredFrameRateSetting, audio: audioDevice, samples: samples)
    }

    /// Mirrors `CaptureSession`: same format choice and audio buffers, plus data outputs to timestamp samples.
    private func configure(video videoDevice: AVCaptureDevice, audio audioDevice: AVCaptureDevice?) throws -> VideoMode {
        session.beginConfiguration()
        defer { session.commitConfiguration() }

        try add(AVCaptureDeviceInput(device: videoDevice))
        let videoOutput = AVCaptureVideoDataOutput()
        videoOutput.alwaysDiscardsLateVideoFrames = true
        videoOutput.setSampleBufferDelegate(self, queue: queue)
        try add(videoOutput)

        if let audioDevice {
            try add(AVCaptureDeviceInput(device: audioDevice))
            let audioOutput = AVCaptureAudioDataOutput()
            audioOutput.setSampleBufferDelegate(self, queue: queue)
            try add(audioOutput)
            requestLowLatencyAudioBuffers(inputUID: audioDevice.uniqueID)
        }
        // Last, so no later session change can reset the format before startRunning().
        return try videoDevice.lockBestFormat()
    }

    private func add(_ input: AVCaptureInput) throws {
        guard session.canAddInput(input) else { throw CaptureError.cannotAdd(name: "measurement input") }
        session.addInput(input)
    }

    private func add(_ output: AVCaptureOutput) throws {
        guard session.canAddOutput(output) else { throw CaptureError.cannotAdd(name: "measurement output") }
        session.addOutput(output)
    }

    public func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        let now = hostTimeSeconds()
        guard now >= warmUpEndTime else { return }
        let captured = sampleBuffer.presentationTimeStamp.seconds
        let latencyMilliseconds = (now - captured) * 1000

        guard output is AVCaptureVideoDataOutput else {
            audioLatencies.append(latencyMilliseconds)
            return
        }
        videoLatencies.append(latencyMilliseconds)
        if let lastFrameTime { frameIntervals.append((captured - lastFrameTime) * 1000) }
        lastFrameTime = captured
    }

    // MARK: - Report

    private func report(
        video: AVCaptureDevice, mode: VideoMode, frameRateSetting: Double, audio: AVCaptureDevice?,
        samples: (video: [Double], intervals: [Double], audio: [Double])
    ) {
        let medianInterval = percentile(samples.intervals, 0.5) ?? 0
        let deliveredFrameRate = medianInterval > 0 ? 1000 / medianInterval : 0

        print("")
        print("Video    \(video.localizedName) · \(mode.width)×\(mode.height) · \(encodingName(mode.encoding))")
        print("         device set to \(format(frameRateSetting)) fps, delivered \(format(deliveredFrameRate)) fps (\(samples.video.count) frames)")
        print("         frame interval  \(spread(samples.intervals))")
        print("         capture → app   \(spread(samples.video))")

        if let audio {
            print("Audio    \(audio.localizedName) · buffer \(Tuning.audioBufferFrames) frames")
            print("         capture → app   \(spread(samples.audio))")
        } else {
            print("Audio    not measured (no audio input selected)")
        }

        let output = describeOutput()
        print("Output   \(output.summary)")
        let refreshRate = NSScreen.main.map { Double($0.maximumFramesPerSecond) }
        print("Display  \(refreshRate.map { "\(format($0)) Hz" } ?? "unknown")")

        let facts = LatencyFacts(
            requestedFrameRate: mode.maxFrameRate, deliveredFrameRate: deliveredFrameRate, displayRefreshRate: refreshRate,
            outputLatencyMilliseconds: output.latency, isOutputBluetooth: output.isBluetooth)
        let advice = latencyAdvice(for: facts)
        print("")
        print(advice.isEmpty ? "Everything plugview controls is at its fastest setting." : "To lower latency:")
        for line in advice { print("  • \(line)") }
    }

    private func describeOutput() -> (summary: String, latency: Double?, isBluetooth: Bool) {
        do {
            let device = try CoreAudioDevice.defaultOutputDeviceID()
            let latency = try CoreAudioDevice.outputLatencyMilliseconds(of: device)
            let isBluetooth = try CoreAudioDevice.isBluetooth(device)
            return ("\(try CoreAudioDevice.name(of: device)) · reports \(format(latency)) ms", latency, isBluetooth)
        } catch {
            return ("unknown (\(error))", nil, false)
        }
    }

    private func spread(_ milliseconds: [Double]) -> String {
        guard let median = percentile(milliseconds, 0.5), let p95 = percentile(milliseconds, 0.95), let worst = percentile(milliseconds, 1)
        else { return "no samples" }
        return "median \(format(median)) ms · p95 \(format(p95)) ms · worst \(format(worst)) ms"
    }

    private func hostTimeSeconds() -> Double { CMClockGetTime(CMClockGetHostTimeClock()).seconds }

    private func format(_ value: Double) -> String { String(format: "%.1f", value) }

    private func encodingName(_ encoding: FrameEncoding) -> String {
        switch encoding {
        case .native: "uncompressed 4:2:2 (native)"
        case .converted: "uncompressed, converted by macOS"
        case .compressed: "compressed (decoded by macOS)"
        }
    }
}
