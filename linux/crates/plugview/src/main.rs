//! plugview for Linux: the command (`--list`, `--measure`) and the player window (phase 6.7 latency spike).

mod app;
mod brand;
mod devices;
mod measure;
mod pipeline;
mod settings;
mod signal;
mod sysfs;
mod ui;

use std::process::ExitCode;

use plugview_core::{Command, PLUGVIEW_VERSION, RunOptions, pickable_modes, select_video_device};

/// sysexits.h `EX_USAGE`, as on the Mac (spec: "Command").
const USAGE_EXIT_CODE: u8 = 64;

const USAGE: &str = "plugview — watch and play a capture device (e.g. Nintendo Switch) in a window.

USAGE
  plugview [options]

OPTIONS
  -d, --device <name>   Video device to use (name substring or unique ID).
                        Default: first USB capture device found.
  -a, --audio <name>    Audio input to play (name substring or unique ID).
                        Default: the audio input that belongs to the video device.
      --no-audio        Do not play audio.
  -v, --volume <0-100>  Start volume in percent. Default: 100.
  -f, --fullscreen      Start in full screen.
      --mode <WxH@fps>  Capture mode, e.g. 1280x720@60 (see --list).
                        Default: the smoothest mode the device offers.
  -l, --list            List video and audio devices, then exit.
      --measure         Measure the capture pipeline's latency for a few seconds, then exit.
  -h, --help            Show this help.
      --version         Show the version.

KEYS (in the window)
  F, F11       Toggle full screen        Esc   Leave full screen
  M            Mute / unmute             ↑ ↓   Volume up / down
  Q, Ctrl+Q    Quit

Plug and play: start plugview before or after connecting the device;
it attaches automatically and re-attaches if the cable is unplugged.";

fn main() -> ExitCode {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let command = match plugview_core::parse_command(&arguments) {
        Ok(command) => command,
        Err(error) => {
            eprintln!("plugview: {error}");
            eprintln!("Run 'plugview --help' for usage.");
            return ExitCode::from(USAGE_EXIT_CODE);
        }
    };
    match command {
        Command::ShowHelp => {
            println!("{USAGE}");
            ExitCode::SUCCESS
        }
        Command::ShowVersion => {
            println!("{PLUGVIEW_VERSION}");
            ExitCode::SUCCESS
        }
        Command::ListDevices => with_gstreamer(list),
        Command::Measure(options) => with_gstreamer(|| measure_card(&options)),
        Command::Run(options) => with_gstreamer(|| app::run(options)),
    }
}

fn with_gstreamer(body: impl FnOnce() -> ExitCode) -> ExitCode {
    if let Err(error) = gst::init() {
        eprintln!("plugview: GStreamer could not start: {error}");
        return ExitCode::FAILURE;
    }
    // The GTK sink is built in, so the app needs no system plugin for it (and neither does the Flatpak).
    if let Err(error) = gstgtk4::plugin_register_static() {
        eprintln!("plugview: {error}");
        return ExitCode::FAILURE;
    }
    body()
}

fn list() -> ExitCode {
    let videos = devices::video_devices();
    let audios = devices::audio_inputs();
    println!("Video devices:");
    if videos.is_empty() {
        println!("  (none)");
    }
    for video in &videos {
        println!("  {}  [{}]  id: {}", video.info.name, transport_name(video.info.transport), video.info.unique_id);
        let modes: Vec<_> = devices::modes(&video.gst).into_iter().map(|mode| mode.mode).collect();
        let keys: Vec<String> = pickable_modes(&modes).iter().map(plugview_core::ModeChoice::key).collect();
        if !keys.is_empty() {
            println!("    modes: {}", keys.join(", "));
        }
    }
    println!();
    println!("Audio inputs:");
    if audios.is_empty() {
        println!("  (none)");
    }
    for audio in &audios {
        println!("  {}  [{}]  id: {}", audio.info.name, transport_name(audio.info.transport), audio.info.unique_id);
    }
    ExitCode::SUCCESS
}

fn measure_card(options: &RunOptions) -> ExitCode {
    let videos = devices::video_devices();
    let infos: Vec<_> = videos.iter().map(|device| device.info.clone()).collect();
    let Some(selected) = select_video_device(&infos, options.video_query.as_deref()) else {
        eprintln!("plugview: no capture device found. Run 'plugview --list' to see detected devices.");
        return ExitCode::FAILURE;
    };
    let Some(video) = videos.iter().find(|device| device.info == selected) else { return ExitCode::FAILURE };
    let modes = devices::modes(&video.gst);
    let index = plugview_core::mode_index(&modes.iter().map(|mode| mode.mode).collect::<Vec<_>>(), options.mode);
    let Some(mode) = index.and_then(|index| modes.get(index)) else {
        eprintln!("plugview: {} reports no video formats", video.info.name);
        return ExitCode::FAILURE;
    };
    warn_about_old_kernel_quirks(video);
    let audio = if options.is_audio_enabled { devices::card_audio(video, options.audio_query.as_deref()) } else { None };
    measure::run(video, mode, audio.as_ref())
}

/// The transport's case name, as the Mac prints it.
fn transport_name(transport: plugview_core::DeviceTransport) -> &'static str {
    match transport {
        plugview_core::DeviceTransport::BuiltIn => "builtIn",
        plugview_core::DeviceTransport::Usb => "usb",
        plugview_core::DeviceTransport::Virtual => "virtual",
        plugview_core::DeviceTransport::Wireless => "wireless",
        plugview_core::DeviceTransport::Other => "other",
    }
}

/// The MS2109 sends 48 kHz stereo while declaring 96 kHz mono; Linux reads it right from 5.9 on (L03, K25).
fn warn_about_old_kernel_quirks(video: &devices::Device) {
    const MS2109: (u16, u16) = (0x534d, 0x2109);
    if video.usb_ids != Some(MS2109) {
        return;
    }
    let Ok(release) = std::fs::read_to_string("/proc/sys/kernel/osrelease") else { return };
    let mut parts = release.trim().split(['.', '-']).map(|part| part.parse::<u32>().unwrap_or(0));
    let version = (parts.next().unwrap_or(0), parts.next().unwrap_or(0));
    if version < (5, 9) {
        eprintln!("plugview: this MS2109 card's sound may play distorted on Linux {release}; Linux 5.9 or later reads it correctly.", release = release.trim());
    }
}
