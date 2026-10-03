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
}
