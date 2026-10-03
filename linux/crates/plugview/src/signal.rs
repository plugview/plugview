//! What the signal watch reads from a playing card: when the last frame arrived, how bright a sampled frame is, and
//! how loud the card's sound is.
//!
//! The streaming thread only stores a timestamp per frame, and hands over one frame when the watch asks (twice a
//! second). The brightness is read on the main thread, so nothing is computed on the display path; the frame goes
//! back to the card's buffer pool a few milliseconds later.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicI64, Ordering};

use gst::prelude::*;
use gst_video::prelude::*;
use plugview_core::{LumaLayout, max_luma};

/// Every 8th pixel of every 8th row, as on the Mac.
const LUMA_STEP: usize = 8;

#[derive(Debug, Default)]
struct Shared {
    /// `glib::monotonic_time()` of the newest frame in microseconds; 0 before the first one.
    last_frame_us: AtomicI64,
    wants_sample: AtomicBool,
    /// The brightest luma of the newest sampled frame; -1 when none is waiting.
    sampled_luma: AtomicI32,
}

/// Watches the frames reaching a sink pad.
#[derive(Debug)]
pub struct FrameProbe {
    shared: Arc<Shared>,
    pad: gst::Pad,
    probe: Option<gst::PadProbeId>,
}

impl FrameProbe {
    /// Watches the frames that reach `sink`.
    #[must_use]
    pub fn new(sink: &gst::Element) -> Option<Self> {
        let pad = sink.static_pad("sink")?;
        let shared = Arc::new(Shared { sampled_luma: AtomicI32::new(-1), ..Shared::default() });
        let probe_shared = Arc::clone(&shared);
        let probe = pad.add_probe(gst::PadProbeType::BUFFER, move |pad, info| {
            probe_shared.last_frame_us.store(glib::monotonic_time(), Ordering::Relaxed);
            if probe_shared.wants_sample.swap(false, Ordering::Relaxed)
                && let (Some(buffer), Some(caps)) = (info.buffer().cloned(), pad.current_caps())
            {
                let shared = Arc::clone(&probe_shared);
                glib::MainContext::default().invoke(move || {
                    if let Some(luma) = sampled_max_luma(&buffer, &caps) {
                        shared.sampled_luma.store(i32::from(luma), Ordering::Relaxed);
                    }
                });
            }
            gst::PadProbeReturn::Ok
        });
        Some(Self { shared, pad, probe })
    }

    /// When the newest frame arrived (`glib::monotonic_time()`), or `None` before the first one.
    #[must_use]
    pub fn last_frame_us(&self) -> Option<i64> {
        Some(self.shared.last_frame_us.load(Ordering::Relaxed)).filter(|time| *time > 0)
    }

    /// Asks for the next frame's brightness.
    pub fn request_sample(&self) {
        self.shared.wants_sample.store(true, Ordering::Relaxed);
    }

    /// The brightness sampled since the last call, if any.
    #[must_use]
    pub fn take_sampled_luma(&self) -> Option<u8> {
        u8::try_from(self.shared.sampled_luma.swap(-1, Ordering::Relaxed)).ok()
    }
}

impl Drop for FrameProbe {
    fn drop(&mut self) {
        if let Some(probe) = self.probe.take() {
            self.pad.remove_probe(probe);
        }
    }
}

/// The brightest luma on a coarse grid of `buffer`, or `None` for a format without a known brightness layout.
fn sampled_max_luma(buffer: &gst::Buffer, caps: &gst::Caps) -> Option<u8> {
    let info = gst_video::VideoInfo::from_caps(caps).ok()?;
    let layout = luma_layout(info.format())?;
    let frame = gst_video::VideoFrameRef::from_buffer_ref_readable(buffer.as_ref(), &info).ok()?;
    let bytes = frame.plane_data(0).ok()?;
    let bytes_per_row = usize::try_from(frame.plane_stride()[0]).ok()?;
    Some(max_luma(bytes, info.width() as usize, info.height() as usize, bytes_per_row, layout, LUMA_STEP))
}

fn luma_layout(format: gst_video::VideoFormat) -> Option<LumaLayout> {
    use gst_video::VideoFormat as Format;
    match format {
        Format::Yuy2 | Format::Yvyu => Some(LumaLayout::Y_FIRST_422),
        Format::Uyvy | Format::Vyuy => Some(LumaLayout::CHROMA_FIRST_422),
        Format::Nv12
        | Format::Nv21
        | Format::Nv16
        | Format::Nv61
        | Format::Nv24
        | Format::I420
        | Format::Yv12
        | Format::Y42b
        | Format::Y444
        | Format::Gray8 => Some(LumaLayout::LUMA_PLANE),
        Format::Bgrx | Format::Bgra | Format::Rgbx | Format::Rgba => Some(LumaLayout::RGB_FIRST_32),
        Format::Xrgb | Format::Argb | Format::Xbgr | Format::Abgr => Some(LumaLayout::PADDING_FIRST_32),
        _ => None,
    }
}

/// Louder than this (dB RMS) counts as sound; digital silence reads far lower.
/// ponytail: one threshold for every card, as on the Mac; make it per card if a noisy card's hiss reads louder.
pub const SOUND_LEVEL_DB: f64 = -70.0;

/// The loudest channel's RMS level in a `level` element message, in dB.
#[must_use]
pub fn level_rms_db(message: &gst::Message) -> Option<f64> {
    let gst::MessageView::Element(element) = message.view() else { return None };
    let structure = element.structure().filter(|structure| structure.name() == "level")?;
    let rms = structure.get::<glib::ValueArray>("rms").ok()?;
    rms.iter().filter_map(|value| value.get::<f64>().ok()).reduce(f64::max)
}
