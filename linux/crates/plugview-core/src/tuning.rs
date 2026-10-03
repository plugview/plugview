/// The released version; the same number on every platform.
pub const PLUGVIEW_VERSION: &str = env!("CARGO_PKG_VERSION");

/// Tuning values shared with every platform (`spec/fixtures/tuning.json`).
#[derive(Debug)]
pub struct Tuning;

impl Tuning {
    /// Frame rate at which motion counts as smooth for gaming.
    pub const SMOOTH_FRAME_RATE: f64 = 50.0;
    /// Longest wait for a card's audio input after its video appears, so playback starts once, with sound.
    pub const CARD_AUDIO_WAIT_SECONDS: f64 = 1.0;
    /// Volume change per arrow-key press, in percent.
    pub const VOLUME_STEP_PERCENT: i64 = 10;
    /// Seconds without mouse movement before the controls fade out over the video.
    pub const CONTROLS_HIDE_SECONDS: f64 = 2.5;
    /// Seconds between automatic update checks (one a day).
    pub const UPDATE_CHECK_INTERVAL_SECONDS: f64 = 24.0 * 60.0 * 60.0;
    /// How long `plugview --measure` samples the capture pipeline.
    pub const MEASUREMENT_SECONDS: f64 = 5.0;
    /// Playback-device latency above which `--measure` warns.
    pub const SLOW_OUTPUT_LATENCY_MILLISECONDS: f64 = 40.0;
    /// Seconds without a frame, while the card is still connected, before "No signal" and a reopen.
    pub const NO_SIGNAL_AFTER_SECONDS: f64 = 1.5;
    /// Waits between reopen attempts after the first, in seconds; the last one repeats.
    pub const REOPEN_BACKOFF_SECONDS: [f64; 3] = [1.0, 2.0, 4.0];
    /// Reopen attempts after which the window also shows the console checklist.
    pub const SIGNAL_HELP_AFTER_ATTEMPTS: u32 = 3;
    /// Brightest luma (0–255) a sampled frame may have and still count as black.
    pub const BLACK_LUMA_MAX: i64 = 24;
    /// Seconds of black frames before the HDCP hint.
    pub const BLACK_PICTURE_SECONDS: f64 = 3.0;
    /// Seconds of digital silence, with a picture, before the console audio hint.
    pub const SILENT_AUDIO_SECONDS: f64 = 20.0;
}
