//! plugview's decisions that need no capture hardware, ported from the macOS `PlugviewCore`.
//!
//! Every function here must match the shared behavior spec (`spec/README.md` and `spec/fixtures/` at the
//! repository root); `tests/spec.rs` runs the fixtures.

mod capture_tools;
mod command_line;
mod easing;
mod format;
mod launcher;
mod measurement;
mod release;
mod selection;
mod signal_watch;
mod text;
mod tuning;

pub use capture_tools::{LimitedRangeExpansion, screenshot_file_name};
pub use command_line::{Command, OptionsError, RunOptions, parse_command};
pub use easing::{CubicBezier, Keyframes};
pub use format::{FrameEncoding, ModeChoice, VideoMode, best_mode_index, mode_index, pickable_modes};
pub use launcher::{CaptureFailure, LauncherEvent, LauncherState, PermissionKind, next_launcher_state};
pub use measurement::{LatencyFacts, LatencyIssue, latency_advice, latency_issues, percentile};
pub use release::{FeedError, Update, is_version_newer, update_from_feed};
pub use selection::{DeviceInfo, DeviceTransport, playable_video_devices, preferred_first, select_audio_device, select_video_device, should_auto_play};
pub use signal_watch::{SignalHint, SignalWatch, is_black_frame, signal_hints, signal_watch};
pub use tuning::{PLUGVIEW_VERSION, Tuning};
