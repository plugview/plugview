use crate::text::rounded;
use crate::tuning::Tuning;

/// Facts gathered by `plugview --measure`, reduced to what the advice depends on.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct LatencyFacts {
    pub requested_frame_rate: f64,
    /// Frame rate derived from the median interval between delivered frames.
    pub delivered_frame_rate: f64,
    pub display_refresh_rate: Option<f64>,
    pub output_latency_milliseconds: Option<f64>,
    pub is_output_bluetooth: bool,
}

/// A latency problem `--measure` found, with the rounded numbers its advice shows.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LatencyIssue {
    LowFrameRate { delivered: i64, requested: i64 },
    BluetoothOutput,
    SlowOutput { milliseconds: i64 },
    UnevenRefresh { refresh_rate: i64, frame_rate: i64 },
}

/// The latency problems the facts reveal, most important first.
#[must_use]
pub fn latency_issues(facts: &LatencyFacts) -> Vec<LatencyIssue> {
    let mut issues = Vec::new();
    if facts.delivered_frame_rate < facts.requested_frame_rate * 0.9 {
        issues.push(LatencyIssue::LowFrameRate { delivered: rounded(facts.delivered_frame_rate), requested: rounded(facts.requested_frame_rate) });
    }
    if facts.is_output_bluetooth {
        issues.push(LatencyIssue::BluetoothOutput);
    } else if let Some(latency) = facts.output_latency_milliseconds.filter(|latency| *latency > Tuning::SLOW_OUTPUT_LATENCY_MILLISECONDS) {
        issues.push(LatencyIssue::SlowOutput { milliseconds: rounded(latency) });
    }
    if let Some(refresh_rate) = facts.display_refresh_rate
        && facts.delivered_frame_rate > 0.0
        && !is_whole_multiple(refresh_rate, facts.delivered_frame_rate)
    {
        issues.push(LatencyIssue::UnevenRefresh { refresh_rate: rounded(refresh_rate), frame_rate: rounded(facts.delivered_frame_rate) });
    }
    issues
}

/// Plain-language fixes for the issues, worded for a Linux computer.
#[must_use]
pub fn latency_advice(facts: &LatencyFacts) -> Vec<String> {
    latency_issues(facts).into_iter().map(advice_text).collect()
}

fn advice_text(issue: LatencyIssue) -> String {
    match issue {
        LatencyIssue::LowFrameRate { delivered, requested } => format!(
            "The card delivers {delivered} fps instead of {requested}. \
             Plug it straight into a USB 3 port (no hub) and check the console's video output setting."
        ),
        LatencyIssue::BluetoothOutput => {
            "Sound plays over Bluetooth, which adds 100–300 ms. Use the computer's speakers or wired headphones for games.".to_owned()
        }
        LatencyIssue::SlowOutput { milliseconds } => {
            format!("The playback device adds {milliseconds} ms. Wired headphones or the computer's speakers are faster.")
        }
        LatencyIssue::UnevenRefresh { refresh_rate, frame_rate } => format!(
            "The display runs at {refresh_rate} Hz, not a multiple of {frame_rate} fps, \
             so frames are shown for uneven times. A refresh rate of 60 or 120 Hz gives even motion."
        ),
    }
}

fn is_whole_multiple(value: f64, base: f64) -> bool {
    let ratio = value / base;
    ratio >= 0.98 && (ratio - ratio.round()).abs() < 0.02
}

/// Nearest-rank percentile of `samples`, with `fraction` clamped to 0...1; `None` for no samples.
#[must_use]
pub fn percentile(samples: &[f64], fraction: f64) -> Option<f64> {
    let mut sorted = samples.to_vec();
    sorted.sort_by(f64::total_cmp);
    let rank = ((sorted.len().checked_sub(1)?) as f64 * fraction.clamp(0.0, 1.0)).round() as usize;
    sorted.get(rank).copied()
}
