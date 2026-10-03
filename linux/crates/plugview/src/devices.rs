//! The capture devices and audio inputs GStreamer finds, described the way the spec classifies them.

use gst::prelude::*;
use plugview_core::{DeviceInfo, DeviceTransport, FrameEncoding, VideoMode};

/// A device plugview can play: the spec's description plus what GStreamer needs to open it.
#[derive(Debug, Clone)]
pub struct Device {
    pub info: DeviceInfo,
    pub gst: gst::Device,
    /// USB vendor and product IDs, for card quirks and diagnostics.
    pub usb_ids: Option<(u16, u16)>,
}

/// One capture format, with the caps that select it.
#[derive(Debug, Clone)]
pub struct Mode {
    pub mode: VideoMode,
    pub caps: gst::Caps,
}

/// Every video capture device. On Linux only V4L2 ones: PipeWire's camera nodes list the same cameras again (L11).
#[must_use]
pub fn video_devices() -> Vec<Device> {
    devices("Video/Source").into_iter().filter(|device| api(device).is_none_or(|api| api != "pipewire")).map(describe_video).collect()
}

/// Every audio input, paired to its card by the same USB parent (`model_id`).
#[must_use]
pub fn audio_inputs() -> Vec<Device> {
    devices("Audio/Source").into_iter().map(describe_audio).collect()
}

fn devices(class: &str) -> Vec<gst::Device> {
    let monitor = gst::DeviceMonitor::new();
    monitor.add_filter(Some(class), None);
    if monitor.start().is_err() {
        return Vec::new();
    }
    let found = monitor.devices().into_iter().collect();
    monitor.stop();
    found
}

fn property(device: &gst::Device, name: &str) -> Option<String> {
    device.properties()?.get::<String>(name).ok()
}

fn api(device: &gst::Device) -> Option<String> {
    property(device, "device.api")
}

fn describe_video(device: gst::Device) -> Device {
    let name = device.display_name().to_string();
    let path = property(&device, "device.path").or_else(|| property(&device, "api.v4l2.path"));
    let unique_id = property(&device, "device.bus_path").or_else(|| path.clone()).unwrap_or_else(|| name.clone());
    let (transport, model_id, usb_ids) = match path.as_deref().and_then(|path| path.strip_prefix("/dev/")) {
        Some(node) if cfg!(target_os = "linux") => place("video4linux", node),
        // Development on macOS: AVFoundation devices, all treated as cards.
        _ => (DeviceTransport::Usb, String::new(), None),
    };
    Device { info: DeviceInfo { unique_id, name, model_id, transport }, gst: device, usb_ids }
}

fn describe_audio(device: gst::Device) -> Device {
    let name = device.display_name().to_string();
    let unique_id = property(&device, "node.name").or_else(|| property(&device, "device.name")).unwrap_or_else(|| name.clone());
    // PulseAudio and PipeWire name the ALSA card behind a source; its sysfs entry leads to the USB parent.
    let card = property(&device, "alsa.card").or_else(|| property(&device, "api.alsa.card"));
    let (transport, model_id, usb_ids) = match card {
        Some(card) if cfg!(target_os = "linux") => place("sound", &format!("card{card}")),
        _ => (DeviceTransport::Other, String::new(), None),
    };
    Device { info: DeviceInfo { unique_id, name, model_id, transport }, gst: device, usb_ids }
}

fn place(class: &str, node: &str) -> (DeviceTransport, String, Option<(u16, u16)>) {
    let sys = std::path::Path::new("/sys");
    let placement = crate::sysfs::placement(sys, class, node);
    let usb_ids = std::fs::canonicalize(sys.join("class").join(class).join(node))
        .ok()
        .and_then(|path| crate::sysfs::usb_parent(&path))
        .and_then(|usb| crate::sysfs::usb_ids(&usb));
    (placement.transport, placement.model_id, usb_ids)
}

/// The formats a device offers, one per caps structure in system memory, at each structure's fastest frame rate and
/// best pixel format.
#[must_use]
pub fn modes(device: &gst::Device) -> Vec<Mode> {
    let Some(caps) = device.caps() else { return Vec::new() };
    caps.iter_with_features()
        // GPU-memory variants (macOS lists GLMemory twins) are not what the pipeline asks for.
        .filter(|(_, features)| features.is_any() || features.contains("memory:SystemMemory") || features.is_empty())
        .filter_map(|(structure, _)| mode(structure))
        .collect()
}

/// Card formats the GPU draws as they are, best first.
const NATIVE_FORMATS: [&str; 3] = ["YUY2", "UYVY", "NV12"];

fn mode(structure: &gst::StructureRef) -> Option<Mode> {
    let width = structure.get::<i32>("width").ok()?;
    let height = structure.get::<i32>("height").ok()?;
    let frame_rate = fastest_rate(structure.value("framerate").ok()?)?;
    let mut fixed = structure.to_owned();
    fixed.set("framerate", frame_rate);
    // A structure may list several pixel formats; pin the one with the least work.
    if let Ok(formats) = structure.get::<gst::List>("format") {
        let names: Vec<String> = formats.iter().filter_map(|format| format.get::<String>().ok()).collect();
        if let Some(best) =
            NATIVE_FORMATS.iter().find(|native| names.iter().any(|name| name == *native)).map(|native| (*native).to_owned()).or_else(|| names.first().cloned())
        {
            fixed.set("format", best);
        }
    }
    let encoding = encoding(&fixed);
    Some(Mode {
        mode: VideoMode { width: i64::from(width), height: i64::from(height), max_frame_rate: fraction_value(frame_rate), encoding },
        caps: gst::Caps::builder_full().structure(fixed).build(),
    })
}

/// Packed 4:2:2 and NV12 are what cards send and what the GPU draws as is; JPEG and H.264 need decoding.
fn encoding(structure: &gst::StructureRef) -> FrameEncoding {
    match structure.name().as_str() {
        "image/jpeg" | "video/x-h264" | "video/x-h265" => FrameEncoding::Compressed,
        "video/x-raw" => match structure.get::<&str>("format") {
            Ok(format) if NATIVE_FORMATS.contains(&format) => FrameEncoding::Native,
            _ => FrameEncoding::Converted,
        },
        _ => FrameEncoding::Converted,
    }
}

fn fastest_rate(value: &glib::SendValue) -> Option<gst::Fraction> {
    if let Ok(rate) = value.get::<gst::Fraction>() {
        return Some(rate);
    }
    if let Ok(list) = value.get::<gst::List>() {
        return list.iter().filter_map(|rate| rate.get::<gst::Fraction>().ok()).max_by(|a, b| fraction_value(*a).total_cmp(&fraction_value(*b)));
    }
    value.get::<gst::FractionRange>().ok().map(|range| range.max())
}

fn fraction_value(fraction: gst::Fraction) -> f64 {
    if fraction.denom() == 0 { 0.0 } else { f64::from(fraction.numer()) / f64::from(fraction.denom()) }
}
