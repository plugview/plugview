use crate::tuning::Tuning;

/// What the window does about the signal while a card is open.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SignalWatch {
    /// Show "No signal" instead of a frozen last frame.
    pub shows_no_signal: bool,
    /// Close and reopen the card now (it often stops after a console mode change, a wake, or an HDMI replug).
    pub should_reopen: bool,
    /// Also show the console checklist: reopening alone has not brought the picture back.
    pub shows_signal_help: bool,
}

/// Decides, on each tick, what to do when frames stop while the card is still connected.
///
/// The caller counts its reopen attempts and resets them when a frame arrives.
#[must_use]
pub fn signal_watch(seconds_since_last_frame: f64, reopen_attempts: u32) -> SignalWatch {
    if seconds_since_last_frame < Tuning::NO_SIGNAL_AFTER_SECONDS {
        return SignalWatch { shows_no_signal: false, should_reopen: false, shows_signal_help: false };
    }
    let backoff = Tuning::REOPEN_BACKOFF_SECONDS;
    let waited: f64 = (0..reopen_attempts as usize).map(|attempt| backoff[attempt.min(backoff.len() - 1)]).sum();
    SignalWatch {
        shows_no_signal: true,
        should_reopen: seconds_since_last_frame >= Tuning::NO_SIGNAL_AFTER_SECONDS + waited,
        shows_signal_help: reopen_attempts >= Tuning::SIGNAL_HELP_AFTER_ATTEMPTS,
    }
}

/// A likely cause the window can point out while video plays.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SignalHint {
    /// Frames arrive but are black: usually HDCP (PlayStation), or a mode the card cannot capture.
    BlackPicture,
    /// The picture is fine but the sound is digital silence: Dolby/DTS or surround output, or a headset on the controller.
    NoGameSound,
}

/// Whether a sampled frame counts as black, from its brightest luma (0–255).
#[must_use]
pub fn is_black_frame(max_luma: u8) -> bool {
    i64::from(max_luma) <= Tuning::BLACK_LUMA_MAX
}

/// The hints to show, from how long the picture has been black and the audio silent (`None` without card audio).
#[must_use]
pub fn signal_hints(black_picture_seconds: f64, silent_audio_seconds: Option<f64>) -> Vec<SignalHint> {
    if black_picture_seconds >= Tuning::BLACK_PICTURE_SECONDS {
        return vec![SignalHint::BlackPicture];
    }
    if silent_audio_seconds.is_some_and(|seconds| seconds >= Tuning::SILENT_AUDIO_SECONDS) {
        return vec![SignalHint::NoGameSound];
    }
    Vec::new()
}
