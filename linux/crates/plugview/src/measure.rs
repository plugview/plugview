//! `plugview --measure`: frame rate and latency for a few seconds, reported like the Mac, advice worded for Linux.

use std::sync::{Arc, Mutex};

use gst::prelude::*;
use gtk::gdk::prelude::{DisplayExt, MonitorExt};
use gtk::gio::prelude::ListModelExt;
use plugview_core::{FrameEncoding, LatencyFacts, Tuning, latency_advice, percentile};

use crate::devices::{Device, Mode};
use crate::pipeline::{self, FramePath, VideoSink};

#[derive(Default)]
struct Samples {
    video: Vec<f64>,
    intervals: Vec<f64>,
    audio: Vec<f64>,
    last_pts: Option<gst::ClockTime>,
}

/// Measures `video` (and `audio`); the exit code the command returns.
pub fn run(video: &Device, mode: &Mode, audio: Option<&Device>) -> std::process::ExitCode {
    println!("Measuring {} for {} seconds…", video.info.name, Tuning::MEASUREMENT_SECONDS as i64);
    let path = if cfg!(target_os = "linux") { FramePath::ZeroCopy } else { FramePath::Converted };
    let video_pipeline = match pipeline::video(video, mode, path, VideoSink::Measure) {
        Ok(pipeline) => pipeline,
        Err(error) => {
            eprintln!("plugview: {}: {error}", video.info.name);
            return std::process::ExitCode::FAILURE;
        }
    };
    let audio_pipeline = audio.and_then(|audio| pipeline::audio(audio, true).ok());
    let samples = Arc::new(Mutex::new(Samples::default()));

    probe(&video_pipeline.pipeline, &video_pipeline.sink, &samples, true);
    if let Some(audio) = &audio_pipeline {
        probe(&audio.pipeline, &audio.sink, &samples, false);
    }
    if video_pipeline.pipeline.set_state(gst::State::Playing).is_err() {
        eprintln!("plugview: {} could not start (is another app using it?)", video.info.name);
        return std::process::ExitCode::FAILURE;
    }
    if let Some(audio) = &audio_pipeline {
        let _ = audio.pipeline.set_state(gst::State::Playing);
    }
    // Start-up frames include negotiation work; sample after half a second, as the Mac does.
    std::thread::sleep(std::time::Duration::from_millis(500));
    if let Ok(mut samples) = samples.lock() {
        *samples = Samples::default();
    }
    std::thread::sleep(std::time::Duration::from_secs_f64(Tuning::MEASUREMENT_SECONDS));
    let _ = video_pipeline.pipeline.set_state(gst::State::Null);
    if let Some(audio) = &audio_pipeline {
        let _ = audio.pipeline.set_state(gst::State::Null);
    }

    let Ok(samples) = samples.lock() else { return std::process::ExitCode::FAILURE };
    report(video, mode, audio, &samples, &video_pipeline);
    std::process::ExitCode::SUCCESS
}

/// Records, for each buffer reaching the sink, how long after its capture time it arrived.
fn probe(pipeline: &gst::Pipeline, sink: &gst::Element, samples: &Arc<Mutex<Samples>>, is_video: bool) {
    let Some(pad) = sink.static_pad("sink") else { return };
    let pipeline = pipeline.clone();
    let samples = Arc::clone(samples);
    pad.add_probe(gst::PadProbeType::BUFFER, move |_, info| {
        let (Some(buffer), Some(clock)) = (info.buffer(), pipeline.clock()) else { return gst::PadProbeReturn::Ok };
        let now = clock.time();
        let (Some(pts), Some(base)) = (buffer.pts(), pipeline.base_time()) else { return gst::PadProbeReturn::Ok };
        let running = now.saturating_sub(base);
        let latency = running.saturating_sub(pts).nseconds() as f64 / 1e6;
        if let Ok(mut samples) = samples.lock() {
            if is_video {
                samples.video.push(latency);
                if let Some(last) = samples.last_pts {
                    samples.intervals.push(pts.saturating_sub(last).nseconds() as f64 / 1e6);
                }
                samples.last_pts = Some(pts);
            } else {
                samples.audio.push(latency);
            }
        }
        gst::PadProbeReturn::Ok
    });
}

fn report(video: &Device, mode: &Mode, audio: Option<&Device>, samples: &Samples, pipeline: &pipeline::VideoPipeline) {
    let decoder = pipeline.decoder.as_deref();
    let median_interval = percentile(&samples.intervals, 0.5).unwrap_or(0.0);
    let delivered = if median_interval > 0.0 { 1000.0 / median_interval } else { 0.0 };
    println!();
    println!("Video    {} · {}×{} · {}", video.info.name, mode.mode.width, mode.mode.height, encoding_name(mode.mode.encoding, decoder));
    println!("         device set to {:.1} fps, delivered {delivered:.1} fps ({} frames)", mode.mode.max_frame_rate, samples.video.len());
    println!("         frame interval  {}", spread(&samples.intervals));
    println!("         capture → app   {}", spread(&samples.video));
    match audio {
        Some(audio) => {
            println!("Audio    {}", audio.info.name);
            println!("         capture → app   {}", spread(&samples.audio));
        }
        None => println!("Audio    not measured (no audio input selected)"),
    }
    let (output_name, is_bluetooth) = default_output();
    println!("Output   {output_name}");
    let refresh_rate = display_refresh_rate();
    println!("Display  {}", refresh_rate.map_or_else(|| "unknown".to_owned(), |hertz| format!("{hertz:.1} Hz")));
    let path = match pipeline.path {
        FramePath::ZeroCopy => "zero-copy (dmabuf to GTK)",
        FramePath::Converted => "converted in system memory",
    };
    println!("Path     {path}");

    let facts = LatencyFacts {
        requested_frame_rate: mode.mode.max_frame_rate,
        delivered_frame_rate: delivered,
        display_refresh_rate: refresh_rate,
        output_latency_milliseconds: None,
        is_output_bluetooth: is_bluetooth,
    };
    let advice = latency_advice(&facts);
    println!();
    println!("{}", if advice.is_empty() { "Everything plugview controls is at its fastest setting." } else { "To lower latency:" });
    for line in advice {
        println!("  • {line}");
    }
}

fn spread(milliseconds: &[f64]) -> String {
    let (Some(median), Some(p95), Some(worst)) = (percentile(milliseconds, 0.5), percentile(milliseconds, 0.95), percentile(milliseconds, 1.0)) else {
        return "no samples".to_owned();
    };
    format!("median {median:.1} ms · p95 {p95:.1} ms · worst {worst:.1} ms")
}

fn encoding_name(encoding: FrameEncoding, decoder: Option<&str>) -> String {
    match encoding {
        FrameEncoding::Native => "uncompressed (native)".to_owned(),
        FrameEncoding::Converted => "uncompressed, converted".to_owned(),
        FrameEncoding::Compressed => format!("compressed (decoded by {})", decoder.unwrap_or("software")),
    }
}

/// The default playback device's name, and whether it is Bluetooth (PulseAudio and PipeWire mark `bluez` devices).
fn default_output() -> (String, bool) {
    let monitor = gst::DeviceMonitor::new();
    monitor.add_filter(Some("Audio/Sink"), None);
    if monitor.start().is_err() {
        return ("unknown".to_owned(), false);
    }
    let devices: Vec<gst::Device> = monitor.devices().into_iter().collect();
    monitor.stop();
    let is_default = |device: &gst::Device| device.properties().and_then(|properties| properties.get::<bool>("is-default").ok()).unwrap_or(false);
    let Some(device) = devices.iter().find(|device| is_default(device)).or_else(|| devices.first()) else { return ("unknown".to_owned(), false) };
    let properties = device.properties();
    let text = |name: &str| properties.as_ref().and_then(|properties| properties.get::<String>(name).ok()).unwrap_or_default();
    let is_bluetooth = text("device.bus") == "bluetooth" || text("device.api") == "bluez5" || text("node.name").contains("bluez");
    (device.display_name().to_string(), is_bluetooth)
}

fn display_refresh_rate() -> Option<f64> {
    gtk::init().ok()?;
    let display = gtk::gdk::Display::default()?;
    let monitor = display.monitors().item(0)?.downcast::<gtk::gdk::Monitor>().ok()?;
    let rate = monitor.refresh_rate();
    // GTK documents millihertz; its macOS backend returns hertz. No display refreshes at under 1 Hz.
    // ponytail: drop the hertz branch once GTK fixes the macOS backend; it only matters for development on a Mac.
    match rate {
        ..=0 => None,
        1..1000 => Some(f64::from(rate)),
        _ => Some(f64::from(rate) / 1000.0),
    }
}
