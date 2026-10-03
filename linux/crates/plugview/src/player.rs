//! The player window: the card's picture in a `GtkGraphicsOffload`, its sound, and the spec's keys (Linux column).

use std::cell::Cell;
use std::rc::Rc;

use adw::prelude::*;
use gst::prelude::*;
use plugview_core::{RunOptions, Tuning};

use crate::devices::{Device, Mode};
use crate::pipeline::{self, FramePath, VideoSink};

/// Plays `video` (and `audio`) in a window until it closes; the exit code the command returns.
pub fn run(video: &Device, mode: &Mode, audio: Option<&Device>, options: &RunOptions) -> std::process::ExitCode {
    // The GTK sink needs GTK running before its pipeline leaves NULL.
    if let Err(error) = gtk::init() {
        eprintln!("plugview: no display: {error}");
        return std::process::ExitCode::FAILURE;
    }
    let video_pipeline = match start_video(video, mode) {
        Ok(pipeline) => pipeline,
        Err(error) => {
            eprintln!("plugview: {}: {error}", video.info.name);
            return std::process::ExitCode::FAILURE;
        }
    };
    let audio_pipeline = audio.and_then(|audio| match pipeline::audio(audio, false) {
        Ok(pipeline) => Some(pipeline),
        Err(error) => {
            eprintln!("plugview: no sound: {error}");
            None
        }
    });

    let audio_summary = audio.map_or_else(|| "no audio".to_owned(), |audio| format!("audio from {}", audio.info.name));
    println!("Showing {} · {}, {audio_summary}", video.info.name, mode.mode.summary());

    let app = adw::Application::builder().application_id("app.plugview.plugview").flags(gio::ApplicationFlags::NON_UNIQUE).build();
    let title = format!("{} · {}", video.info.name, mode.mode.summary());
    let starts_in_full_screen = options.starts_in_full_screen;
    let volume_percent = Rc::new(Cell::new(options.volume_percent));
    let is_muted = Rc::new(Cell::new(false));
    let video_for_ui = video_pipeline.pipeline.clone();
    let audio_for_ui = audio_pipeline.as_ref().map(|audio| audio.pipeline.clone());
    let device_name = video.info.name.clone();
    let failed = Rc::new(Cell::new(false));
    let failed_in_ui = Rc::clone(&failed);
    let sink = video_pipeline.sink.clone();
    let volume = audio_pipeline.as_ref().map(|audio| audio.volume.clone());
    apply_volume(volume.as_ref(), volume_percent.get(), false);

    app.connect_activate(move |app| {
        let paintable = sink.property::<gtk::gdk::Paintable>("paintable");
        let picture = gtk::Picture::for_paintable(&paintable);
        picture.set_content_fit(gtk::ContentFit::Contain);
        // The video alone in an offload widget with a black background: on Wayland the compositor scans the frames
        // out directly, without GTK compositing them (K36).
        let offload = gtk::GraphicsOffload::new(Some(&picture));
        offload.set_black_background(true);

        let window = gtk::ApplicationWindow::builder().application(app).title(title.as_str()).default_width(1280).default_height(720).child(&offload).build();
        window.add_css_class("plugview-player");
        load_css();
        add_keys(&window, volume.clone(), Rc::clone(&volume_percent), Rc::clone(&is_muted));
        watch_bus(&video_for_ui, &window);
        // Keep the screen awake while playing (K32).
        app.inhibit(Some(&window), gtk::ApplicationInhibitFlags::IDLE, Some("Playing a capture card"));
        window.present();
        if starts_in_full_screen {
            window.fullscreen();
        }
        if video_for_ui.set_state(gst::State::Playing).is_err() {
            eprintln!("plugview: {device_name} could not start (is another app using it?)");
            failed_in_ui.set(true);
            window.close();
            return;
        }
        if let Some(audio) = &audio_for_ui {
            let _ = audio.set_state(gst::State::Playing);
        }
    });

    // GTK must not read plugview's own options.
    let status = app.run_with_args(&["plugview"]);
    let _ = video_pipeline.pipeline.set_state(gst::State::Null);
    if let Some(audio) = &audio_pipeline {
        let _ = audio.pipeline.set_state(gst::State::Null);
    }
    if status == glib::ExitCode::SUCCESS && !failed.get() { std::process::ExitCode::SUCCESS } else { std::process::ExitCode::FAILURE }
}

/// The zero-copy path first; if its caps do not negotiate, the converted path (L18).
fn start_video(video: &Device, mode: &Mode) -> Result<pipeline::VideoPipeline, pipeline::PipelineError> {
    let path = if cfg!(target_os = "linux") { FramePath::ZeroCopy } else { FramePath::Converted };
    let first = pipeline::video(video, mode, path, VideoSink::Window)?;
    if path == FramePath::Converted || prerolls(&first.pipeline) {
        return Ok(first);
    }
    let _ = first.pipeline.set_state(gst::State::Null);
    eprintln!("plugview: zero-copy video did not negotiate; using the converted path");
    pipeline::video(video, mode, FramePath::Converted, VideoSink::Window)
}

/// Whether the pipeline reaches `PAUSED` (a live source reports `NO_PREROLL`) without a negotiation error.
fn prerolls(pipeline: &gst::Pipeline) -> bool {
    match pipeline.set_state(gst::State::Paused) {
        Ok(gst::StateChangeSuccess::NoPreroll | gst::StateChangeSuccess::Success) => true,
        Ok(gst::StateChangeSuccess::Async) => pipeline.state(gst::ClockTime::from_seconds(2)).0.is_ok(),
        Err(_) => false,
    }
}

fn apply_volume(volume: Option<&gst::Element>, percent: i64, is_muted: bool) {
    if let Some(volume) = volume {
        volume.set_property("volume", if is_muted { 0.0 } else { percent as f64 / 100.0 });
    }
}

fn add_keys(window: &gtk::ApplicationWindow, volume: Option<gst::Element>, percent: Rc<Cell<i64>>, is_muted: Rc<Cell<bool>>) {
    let keys = gtk::EventControllerKey::new();
    let weak_window = window.downgrade();
    keys.connect_key_pressed(move |_, key, _, _| {
        let Some(window) = weak_window.upgrade() else { return glib::Propagation::Proceed };
        match key {
            gtk::gdk::Key::f | gtk::gdk::Key::F | gtk::gdk::Key::F11 => {
                if window.is_fullscreen() {
                    window.unfullscreen();
                } else {
                    window.fullscreen();
                }
            }
            gtk::gdk::Key::Escape if window.is_fullscreen() => window.unfullscreen(),
            gtk::gdk::Key::m | gtk::gdk::Key::M => {
                is_muted.set(!is_muted.get());
                apply_volume(volume.as_ref(), percent.get(), is_muted.get());
                println!("{}", if is_muted.get() { "Muted".to_owned() } else { format!("Volume {}%", percent.get()) });
            }
            gtk::gdk::Key::Up | gtk::gdk::Key::Down => {
                let step = if key == gtk::gdk::Key::Up { Tuning::VOLUME_STEP_PERCENT } else { -Tuning::VOLUME_STEP_PERCENT };
                percent.set((percent.get() + step).clamp(0, 100));
                is_muted.set(false);
                apply_volume(volume.as_ref(), percent.get(), false);
                println!("Volume {}%", percent.get());
            }
            // Q and Ctrl+Q both quit, so the modifiers do not matter here.
            gtk::gdk::Key::q | gtk::gdk::Key::Q => window.close(),
            _ => return glib::Propagation::Proceed,
        }
        glib::Propagation::Stop
    });
    window.add_controller(keys);
}

/// Closes the window on a pipeline error (the card unplugged, a negotiation failure) and says why.
fn watch_bus(pipeline: &gst::Pipeline, window: &gtk::ApplicationWindow) {
    let Some(bus) = pipeline.bus() else { return };
    let weak_window = window.downgrade();
    let watch = bus.add_watch_local(move |_, message| {
        if let gst::MessageView::Error(error) = message.view() {
            eprintln!("plugview: {}", error.error());
            if let Some(window) = weak_window.upgrade() {
                window.close();
            }
        }
        glib::ControlFlow::Continue
    });
    // The watch lives as long as the window.
    if let Ok(watch) = watch {
        let cell = std::cell::RefCell::new(Some(watch));
        window.connect_destroy(move |_| drop(cell.borrow_mut().take()));
    }
}

fn load_css() {
    let provider = gtk::CssProvider::new();
    provider.load_from_string("window.plugview-player { background: black; }");
    if let Some(display) = gtk::gdk::Display::default() {
        gtk::style_context_add_provider_for_display(&display, &provider, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION);
    }
}
