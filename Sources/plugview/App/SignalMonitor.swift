import PlugviewCapture
import PlugviewCore
import PlugviewUI

/// The signal watch for app launches (`spec/README.md`, "Signal watch and picture help").
///
/// Twice a second it asks the capture session for one frame, off the display path, and reads the card audio's level
/// meter. Frames that stop while the card is still connected show "No signal" and reopen the card in place, with the
/// shared backoff; a black picture or silence since the start shows the console checklist once.
///
/// Terminal launches never run it, so the command behaves exactly as v0.1.
@MainActor
final class SignalMonitor {
    /// Reopens the playing card in place; the monitor keeps watching and counting its tries.
    var reopen: () async -> Void = {}
    /// True while the app is starting a card itself (a mode or device change): no sample is expected then.
    var isStarting: () -> Bool = { false }

    private let capture: CaptureSession
    private let controls: ControlsModel
    private var task: Task<Void, Never>?
    private var deviceName = ""
    private var hasAudio = false
    private var isReopening = false
    private var reopenAttempts = 0
    private var lastFrame = ContinuousClock.now
    private var startedAt = ContinuousClock.now
    private var hasReadLuma = false
    private var hasSeenPicture = false
    private var hasHeardSound = false
    private var shownHints: Set<SignalHint> = []

    /// Louder than this (dB) counts as sound; digital silence reads about -120 dB.
    /// ponytail: one threshold for every card; make it per card if a noisy card's hiss reads louder.
    private static let soundLevel: Float = -70
    private static let interval = Duration.milliseconds(500)

    init(capture: CaptureSession, controls: ControlsModel) {
        self.capture = capture
        self.controls = controls
    }

    /// Call after every successful start.
    ///
    /// A start the monitor asked for keeps its tries; any other starts afresh.
    func didStart(deviceName: String, hasAudio: Bool) {
        self.deviceName = deviceName
        self.hasAudio = hasAudio
        if isReopening {
            isReopening = false
        } else {
            startFresh()
        }
        guard task == nil else { return }
        task = Task { [weak self] in await self?.run() }
    }

    /// Call when nothing plays any more (unplugged, window closed, failed).
    func stop() {
        task?.cancel()
        task = nil
        isReopening = false
        controls.signalNotice = nil
    }

    private func startFresh() {
        let now = ContinuousClock.now
        lastFrame = now
        startedAt = now
        reopenAttempts = 0
        hasReadLuma = false
        hasSeenPicture = false
        hasHeardSound = false
        shownHints = []
        controls.signalNotice = nil
    }

    private func run() async {
        while !Task.isCancelled {
            let sample = await capture.sampleSignal()
            let audioLevel = hasAudio ? await capture.audioLevel() : nil
            guard !Task.isCancelled else { return }
            if !isStarting() { await update(sample: sample, audioLevel: audioLevel) }
            do {
                try await Task.sleep(for: Self.interval)
            } catch {
                return
            }
        }
    }

    private func update(sample: SignalSample?, audioLevel: Float?) async {
        let now = ContinuousClock.now
        if let sample { noteFrame(sample, at: now) }
        if let audioLevel, audioLevel > Self.soundLevel { hasHeardSound = true }

        let watch = signalWatch(secondsSinceLastFrame: Self.seconds(from: lastFrame, to: now), reopenAttempts: reopenAttempts)
        if watch.showsNoSignal {
            controls.signalNotice = .noSignal(deviceName: deviceName, showsHelp: watch.showsSignalHelp)
            guard watch.shouldReopen else { return }
            reopenAttempts += 1
            isReopening = true
            await reopen()
            return
        }

        let sinceStart = Self.seconds(from: startedAt, to: now)
        let hints = signalHints(
            blackPictureSeconds: hasReadLuma && !hasSeenPicture ? sinceStart : 0,
            silentAudioSeconds: hasAudio && !hasHeardSound ? sinceStart : nil)
        for hint in hints where !shownHints.contains(hint) {
            shownHints.insert(hint)
            show(hint)
        }
    }

    private func noteFrame(_ sample: SignalSample, at now: ContinuousClock.Instant) {
        lastFrame = now
        reopenAttempts = 0
        if case .noSignal = controls.signalNotice { controls.signalNotice = nil }
        guard let luma = sample.maxLuma else { return }
        hasReadLuma = true
        guard !isBlackFrame(maxLuma: luma) else { return }
        hasSeenPicture = true
        if controls.signalNotice == .blackPicture { controls.signalNotice = nil }
    }

    private func show(_ hint: SignalHint) {
        switch hint {
        case .blackPicture:
            controls.signalNotice = .blackPicture
        case .noGameSound:
            // The picture is fine, so a toast rather than a panel over it; the panel is one click away.
            controls.toast = Toast(symbol: "speaker.slash", message: "No game sound", actionTitle: "Help", isWarning: true) { [weak controls] in
                controls?.signalNotice = .noGameSound
            }
        }
    }

    private static func seconds(from start: ContinuousClock.Instant, to end: ContinuousClock.Instant) -> Double {
        let duration = end - start
        return Double(duration.components.seconds) + Double(duration.components.attoseconds) / 1e18
    }
}
