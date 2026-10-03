use std::cmp::Ordering;
use std::collections::BTreeSet;

use crate::text::{rounded, whole_number};
use crate::tuning::Tuning;

/// How much work a frame needs before it can be shown; later variants reach the screen sooner.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum FrameEncoding {
    /// MJPEG, H.264 or HEVC: every frame needs a decode step.
    Compressed,
    /// Uncompressed, but not the card's wire layout, so every frame is converted.
    Converted,
    /// What the card sends natively (packed 4:2:2 on USB capture cards).
    Native,
}

/// Framework-free description of one capture format.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct VideoMode {
    pub width: i64,
    pub height: i64,
    pub max_frame_rate: f64,
    pub encoding: FrameEncoding,
}

impl VideoMode {
    /// Human-readable summary, e.g. `1920×1080 @ 60 fps`.
    #[must_use]
    pub fn summary(&self) -> String {
        format!("{}×{} @ {} fps", self.width, self.height, rounded(self.max_frame_rate))
    }
}

/// Ranks smooth frame rate first, then resolution, then frame rate, then the encoding with the least work.
fn compare_for_play(a: &VideoMode, b: &VideoMode) -> Ordering {
    let is_smooth = |mode: &VideoMode| mode.max_frame_rate >= Tuning::SMOOTH_FRAME_RATE;
    is_smooth(a)
        .cmp(&is_smooth(b))
        .then((a.width * a.height).cmp(&(b.width * b.height)))
        .then(a.max_frame_rate.total_cmp(&b.max_frame_rate))
        .then(a.encoding.cmp(&b.encoding))
}

/// The index of the greatest item; the first of equals wins, as in Swift (`Iterator::max_by` keeps the last).
fn first_max_index<T>(items: &[T], indices: impl Iterator<Item = usize>, compare: impl Fn(&T, &T) -> Ordering) -> Option<usize> {
    indices.fold(None, |best, index| match best {
        Some(best) if compare(&items[index], &items[best]) != Ordering::Greater => Some(best),
        _ => Some(index),
    })
}

/// The index of the best mode for live play: 1080p60 beats 4K30.
#[must_use]
pub fn best_mode_index(modes: &[VideoMode]) -> Option<usize> {
    first_max_index(modes, 0..modes.len(), compare_for_play)
}

/// A size and frame rate the user picked for a device, saved as text such as `1920x1080@60`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct ModeChoice {
    pub width: i64,
    pub height: i64,
    pub frame_rate: i64,
}

impl ModeChoice {
    #[must_use]
    pub fn from_mode(mode: &VideoMode) -> Self {
        Self { width: mode.width, height: mode.height, frame_rate: rounded(mode.max_frame_rate) }
    }

    /// Reads a saved choice: `<width>x<height>@<fps>` with positive whole numbers, else `None`.
    #[must_use]
    pub fn from_key(key: &str) -> Option<Self> {
        let (size, frame_rate) = split_exactly_once(key, '@')?;
        let (width, height) = split_exactly_once(size, 'x')?;
        let choice = Self { width: whole_number(width)?, height: whole_number(height)?, frame_rate: whole_number(frame_rate)? };
        (choice.width > 0 && choice.height > 0 && choice.frame_rate > 0).then_some(choice)
    }

    #[must_use]
    pub fn key(&self) -> String {
        format!("{}x{}@{}", self.width, self.height, self.frame_rate)
    }

    /// Menu text, e.g. `1920×1080 · 60 fps`.
    #[must_use]
    pub fn title(&self) -> String {
        format!("{}×{} · {} fps", self.width, self.height, self.frame_rate)
    }

    fn menu_rank(&self) -> (i64, i64, i64) {
        (self.width * self.height, self.frame_rate, self.width)
    }
}

fn split_exactly_once(text: &str, separator: char) -> Option<(&str, &str)> {
    let (first, second) = text.split_once(separator)?;
    (!second.contains(separator)).then_some((first, second))
}

/// The modes worth offering in a menu: one per size and frame rate, largest and fastest first, then widest.
#[must_use]
pub fn pickable_modes(modes: &[VideoMode]) -> Vec<ModeChoice> {
    let choices: BTreeSet<ModeChoice> = modes.iter().filter(|mode| mode.max_frame_rate >= 1.0).map(ModeChoice::from_mode).collect();
    let mut sorted: Vec<ModeChoice> = choices.into_iter().collect();
    sorted.sort_by_key(|choice| std::cmp::Reverse(choice.menu_rank()));
    sorted
}

/// The mode to start: the user's choice when the device still offers it (cheapest encoding), otherwise the best.
#[must_use]
pub fn mode_index(modes: &[VideoMode], choice: Option<ModeChoice>) -> Option<usize> {
    let Some(choice) = choice else { return best_mode_index(modes) };
    let matching = (0..modes.len()).filter(|&index| ModeChoice::from_mode(&modes[index]) == choice);
    first_max_index(modes, matching, |a, b| a.encoding.cmp(&b.encoding)).or_else(|| best_mode_index(modes))
}
