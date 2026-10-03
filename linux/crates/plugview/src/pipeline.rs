//! The GStreamer pipelines: card video to a GTK paintable with the least delay, and card audio to the speakers.

use gst::prelude::*;

use crate::devices::{Device, Mode};

/// Why a pipeline could not be built.
#[derive(Debug)]
pub struct PipelineError(pub String);

impl std::fmt::Display for PipelineError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter.write_str(&self.0)
    }
}

impl std::error::Error for PipelineError {}

impl From<glib::BoolError> for PipelineError {
    fn from(error: glib::BoolError) -> Self {
        Self(error.to_string())
    }
}

/// How the frames reach the sink.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FramePath {
    /// Linux: V4L2 exports dmabufs and GTK imports them (graphics offload, direct scanout on Wayland).
    ZeroCopy,
    /// Frames in system memory through a converter: the fallback when the zero-copy caps do not negotiate (L18),
    /// and the path on other systems.
    Converted,
    /// The converted path plus `videobalance` stretching video range to full range (Full-range colors). Touches every
    /// frame, so only when the user turns it on.
    Expanded,
}

/// What the video ends in: the GTK sink (playing) or a fake sink (measuring).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum VideoSink {
    Window,
    Measure,
}

/// A built video pipeline and its last element (`gtk4paintablesink` or `fakesink`).
#[derive(Debug)]
pub struct VideoPipeline {
    pub pipeline: gst::Pipeline,
    pub sink: gst::Element,
    pub path: FramePath,
    /// The decoder in use for compressed cards, for diagnostics (K08).
    pub decoder: Option<String>,
}

/// `source ! caps ! [jpegparse ! decoder] ! [videoconvert] ! queue(1, leaky) ! sink sync=false`.
///
/// # Errors
/// An element is missing or cannot be linked.
pub fn video(device: &Device, mode: &Mode, path: FramePath, sink_kind: VideoSink) -> Result<VideoPipeline, PipelineError> {
    let pipeline = gst::Pipeline::with_name("plugview-video");
    let source = device.gst.create_element(Some("source"))?;
    let is_v4l2 = source.factory().is_some_and(|factory| factory.name() == "v4l2src");
    let is_compressed = mode.mode.encoding == plugview_core::FrameEncoding::Compressed;
    if is_v4l2 && path == FramePath::ZeroCopy && !is_compressed {
        source.set_property_from_str("io-mode", "dmabuf");
    }

    let caps = make("capsfilter")?;
    caps.set_property("caps", mode_caps(mode, is_v4l2 && path == FramePath::ZeroCopy && !is_compressed));
    let mut chain = vec![source, caps];

    let mut decoder_name = None;
    if is_compressed {
        if mode.caps.structure(0).is_none_or(|structure| structure.name() != "image/jpeg") {
            return Err(PipelineError("only MJPEG is decoded so far; pick a raw or MJPEG mode with --mode".into()));
        }
        let decoder = first_available(&["vajpegdec", "jpegdec"]).ok_or_else(|| PipelineError("no JPEG decoder (install gst-plugins-good)".into()))?;
        decoder_name = Some(decoder.clone());
        chain.push(make("jpegparse")?);
        chain.push(make(&decoder)?);
    }
    if path != FramePath::ZeroCopy {
        chain.push(make("videoconvert")?);
    }
    if path == FramePath::Expanded {
        chain.push(range_expansion()?);
    }

    // Newest frame wins: one buffer at most, older ones dropped (K09).
    let queue = make("queue")?;
    queue.set_property("max-size-buffers", 1u32);
    queue.set_property("max-size-bytes", 0u32);
    queue.set_property("max-size-time", 0u64);
    queue.set_property_from_str("leaky", "downstream");
    chain.push(queue);

    let sink = match sink_kind {
        VideoSink::Window => make("gtk4paintablesink")?,
        VideoSink::Measure => make("fakesink")?,
    };
    // Draw each frame as it arrives instead of holding it to the pipeline clock.
    sink.set_property("sync", false);
    chain.push(sink.clone());

    pipeline.add_many(&chain)?;
    gst::Element::link_many(&chain)?;
    Ok(VideoPipeline { pipeline, sink, path, decoder: decoder_name })
}

/// `LimitedRangeExpansion` in YUV: videobalance maps luma to `16 + (y - 16) * contrast + brightness * 255` and chroma
/// to `128 + (c - 128) * saturation`, so luma 16–235 and chroma 16–240 land on 0–255.
fn range_expansion() -> Result<gst::Element, PipelineError> {
    let balance = make("videobalance")?;
    balance.set_property("contrast", plugview_core::LimitedRangeExpansion::SCALE);
    balance.set_property("brightness", -16.0 / 255.0);
    balance.set_property("saturation", 255.0 / 224.0);
    Ok(balance)
}

/// The mode's caps; for the zero-copy path, the same format as dmabuf memory.
fn mode_caps(mode: &Mode, is_dmabuf: bool) -> gst::Caps {
    if !is_dmabuf {
        return mode.caps.clone();
    }
    let mut caps = mode.caps.clone();
    caps.make_mut().set_features_simple(Some(gst::CapsFeatures::new(["memory:DMABuf"])));
    // Plain caps as a second choice, so v4l2src can still negotiate if the sink takes no dmabuf.
    caps.make_mut().append(mode.caps.clone());
    caps
}

/// How often the `level` element reports, as often as the signal watch ticks.
const LEVEL_INTERVAL_NS: u64 = 500_000_000;

/// A built audio pipeline with its volume element.
#[derive(Debug)]
pub struct AudioPipeline {
    pub pipeline: gst::Pipeline,
    pub volume: gst::Element,
    /// The sink's pad, for `--measure` probes.
    pub sink: gst::Element,
}

/// `card source ! audioconvert ! [level] ! audioresample ! volume ! sink` with 20 ms buffers.
///
/// The pipeline runs on the card's clock and the sink slaves to it, so the speakers follow the card's rate instead of
/// drifting away (K23); PipeWire resamples inside the graph.
///
/// # Errors
/// An element is missing or cannot be linked.
pub fn audio(device: &Device, is_measuring: bool) -> Result<AudioPipeline, PipelineError> {
    let pipeline = gst::Pipeline::with_name("plugview-audio");
    let source = device.gst.create_element(Some("source"))?;
    set_if_present(&source, "buffer-time", 20_000i64);
    set_if_present(&source, "latency-time", 5_000i64);
    let convert = make("audioconvert")?;
    let resample = make("audioresample")?;
    let volume = make("volume")?;
    let sink = if is_measuring { make("fakesink")? } else { make("autoaudiosink")? };
    if is_measuring {
        sink.set_property("sync", false);
    }
    let mut chain = vec![source, convert];
    // The signal watch's sound meter, before the volume so muting is not silence. Optional: without it the
    // no-game-sound hint stays off.
    if !is_measuring && let Ok(level) = make("level") {
        level.set_property("interval", LEVEL_INTERVAL_NS);
        level.set_property("post-messages", true);
        chain.push(level);
    }
    chain.extend([resample, volume.clone(), sink.clone()]);
    pipeline.add_many(&chain)?;
    gst::Element::link_many(&chain)?;
    Ok(AudioPipeline { pipeline, volume, sink })
}

fn make(factory: &str) -> Result<gst::Element, PipelineError> {
    gst::ElementFactory::make(factory).build().map_err(|_| PipelineError(format!("GStreamer element '{factory}' is missing")))
}

fn first_available(factories: &[&str]) -> Option<String> {
    factories.iter().find(|name| gst::ElementFactory::find(name).is_some()).map(|name| (*name).to_owned())
}

fn set_if_present(element: &gst::Element, name: &str, value: i64) {
    if element.find_property(name).is_some() {
        element.set_property(name, value);
    }
}
