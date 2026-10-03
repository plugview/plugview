use std::fmt;

use crate::format::ModeChoice;
use crate::text::whole_number;

/// What to show and how, as given on the command line.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RunOptions {
    pub video_query: Option<String>,
    pub audio_query: Option<String>,
    pub is_audio_enabled: bool,
    pub volume_percent: i64,
    pub starts_in_full_screen: bool,
    /// The capture mode to start in instead of the best one.
    pub mode: Option<ModeChoice>,
}

impl Default for RunOptions {
    fn default() -> Self {
        Self { video_query: None, audio_query: None, is_audio_enabled: true, volume_percent: 100, starts_in_full_screen: false, mode: None }
    }
}

/// The action requested on the command line.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Command {
    Run(RunOptions),
    /// Measure latency of the devices `RunOptions` selects, without opening a window.
    Measure(RunOptions),
    ListDevices,
    ShowHelp,
    ShowVersion,
}

/// A command line that cannot be run, with the message the spec prescribes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OptionsError(pub String);

impl fmt::Display for OptionsError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.0)
    }
}

impl std::error::Error for OptionsError {}

/// Parses the arguments that follow the executable name.
///
/// # Errors
/// An unknown option, a missing or malformed value, or `--audio` with `--no-audio`.
pub fn parse_command<S: AsRef<str>>(arguments: &[S]) -> Result<Command, OptionsError> {
    let mut options = RunOptions::default();
    let mut is_measuring = false;
    let mut remaining = arguments.iter().map(AsRef::as_ref);

    while let Some(argument) = remaining.next() {
        match argument {
            "-h" | "--help" => return Ok(Command::ShowHelp),
            "--version" => return Ok(Command::ShowVersion),
            "-l" | "--list" => return Ok(Command::ListDevices),
            "-d" | "--device" => options.video_query = Some(require_value(argument, remaining.next())?.to_owned()),
            "-a" | "--audio" => options.audio_query = Some(require_value(argument, remaining.next())?.to_owned()),
            "--no-audio" => options.is_audio_enabled = false,
            "--measure" => is_measuring = true,
            "-f" | "--fullscreen" => options.starts_in_full_screen = true,
            "-v" | "--volume" => options.volume_percent = require_volume_percent(argument, remaining.next())?,
            "--mode" => options.mode = Some(require_mode(argument, remaining.next())?),
            _ => return Err(OptionsError(format!("unknown option '{argument}'"))),
        }
    }

    if !options.is_audio_enabled && options.audio_query.is_some() {
        return Err(OptionsError("--audio and --no-audio cannot be combined".to_owned()));
    }
    Ok(if is_measuring { Command::Measure(options) } else { Command::Run(options) })
}

fn require_value<'a>(option: &str, value: Option<&'a str>) -> Result<&'a str, OptionsError> {
    match value {
        Some(value) if !value.is_empty() && !value.starts_with('-') => Ok(value),
        _ => Err(OptionsError(format!("{option} needs a value"))),
    }
}

fn require_volume_percent(option: &str, value: Option<&str>) -> Result<i64, OptionsError> {
    let value = require_value(option, value)?;
    whole_number(value).filter(|percent| *percent <= 100).ok_or_else(|| OptionsError(format!("{option} must be a whole number from 0 to 100, got '{value}'")))
}

fn require_mode(option: &str, value: Option<&str>) -> Result<ModeChoice, OptionsError> {
    let value = require_value(option, value)?;
    ModeChoice::from_key(&value.to_lowercase().replace('×', "x")).ok_or_else(|| OptionsError(format!("{option} must look like 1920x1080@60, got '{value}'")))
}
