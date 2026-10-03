/// Single source of truth for the released version.
///
/// The release workflow refuses a tag that differs.
public let plugviewVersion = "0.3.1"

/// Tuning values, each backed by a measurement on real hardware (see docs/PERFORMANCE.md).
public enum Tuning {
    /// Frame rate at which motion counts as smooth for gaming.
    public static let smoothFrameRate = 50.0

    /// Core Audio I/O buffer, in frames, requested for the playback device.
    ///
    /// 128 frames is 2.7 ms at 48 kHz; it cut speaker latency from 27 to 19 ms. 64 frames caused irregular spikes.
    /// The card input keeps the default (see `requestLowLatencyAudioBuffers`).
    public static let audioBufferFrames: UInt32 = 128

    /// Longest wait for a card's audio input after its video appears, so playback starts once, with sound.
    ///
    /// A replugged UGREEN 25773 adds its audio input 488 ms after its video; the wait ends as soon as it appears.
    public static let cardAudioWaitSeconds = 1.0

    /// Volume change per arrow-key press, in percent.
    public static let volumeStepPercent = 10

    /// Seconds without mouse movement before the controls and window buttons fade out over the video.
    public static let controlsHideSeconds = 2.5

    /// Seconds between automatic update checks from the app (one a day).
    public static let updateCheckIntervalSeconds = 24.0 * 60 * 60

    /// How long `plugview --measure` samples the capture pipeline.
    public static let measurementSeconds = 5.0

    /// Playback-device latency above which `--measure` warns (Bluetooth headphones are typically 150 ms+).
    public static let slowOutputLatencyMilliseconds = 40.0

    // Signal watch (spec/fixtures/signal-watch.json). From user reports of other viewers, not yet measured on our
    // cards: calibrate them with the hardware test plan (spec/hardware-test-plan.md) before relying on them.

    /// Seconds without a frame, while the card is still connected, before the window says "No signal" and reopens it.
    ///
    /// Cards stop sending while the console changes resolution or wakes; 1.5 s covers a normal mode switch.
    public static let noSignalAfterSeconds = 1.5

    /// Waits between reopen attempts after the first, in seconds; the last one repeats.
    public static let reopenBackoffSeconds = [1.0, 2.0, 4.0]

    /// Reopen attempts after which the window also shows the console checklist (HDCP, 1080p, 120 Hz).
    public static let signalHelpAfterAttempts = 3

    /// Brightest luma (0–255) a sampled frame may have and still count as black: video black is 16, plus card noise.
    public static let blackLumaMax = 24

    /// Seconds of black frames before the window suggests turning HDCP off (PlayStation sends black when it is on).
    public static let blackPictureSeconds = 3.0

    /// Seconds of digital silence, with a picture, before the window suggests stereo PCM output on the console.
    public static let silentAudioSeconds = 5.0
}
