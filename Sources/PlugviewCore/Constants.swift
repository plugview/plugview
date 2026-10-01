/// Single source of truth for the released version.
///
/// The release workflow refuses a tag that differs.
public let plugviewVersion = "0.1.0"

/// Tuning values, each backed by a measurement on real hardware (see docs/PERFORMANCE.md).
public enum Tuning {
    /// Frame rate at which motion counts as smooth for gaming.
    public static let smoothFrameRate = 50.0

    /// Core Audio I/O buffer, in frames, requested for the card input and the playback device.
    ///
    /// 128 frames is 2.7 ms at 48 kHz. On an MS2130 card it cut capture latency from 12.4 ms (512) to 4.3 ms;
    /// 64 frames caused irregular spikes.
    public static let audioBufferFrames: UInt32 = 128

    /// Volume change per arrow-key press, in percent.
    public static let volumeStepPercent = 10

    /// Seconds a card found from the app launcher waits before it plays.
    ///
    /// The length of the plug-in intro animation (plugview-intro.svg: 0.2 s delay + 2.6 s). Play starts it sooner.
    public static let launcherAutoPlaySeconds = 2.8

    /// How long `plugview --measure` samples the capture pipeline.
    public static let measurementSeconds = 5.0

    /// Playback-device latency above which `--measure` warns (Bluetooth headphones are typically 150 ms+).
    public static let slowOutputLatencyMilliseconds = 40.0
}
