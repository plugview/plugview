//! The app: one window that shows the launcher until a card plays, then the video, driven by the shared launcher
//! state machine (`next_launcher_state`) exactly as on the Mac.

mod playing;

use std::cell::RefCell;
use std::process::ExitCode;
use std::rc::{Rc, Weak};

use adw::prelude::*;
use gst::prelude::*;
use plugview_core::{
    CaptureFailure, LauncherEvent, LauncherState, ModeChoice, RunOptions, mode_index, next_launcher_state, playable_video_devices, preferred_first,
    should_auto_play,
};

use crate::devices::{self, Device, Mode};
use crate::pipeline::{self, AudioPipeline, FramePath, VideoPipeline, VideoSink};
use crate::settings::Settings;
use crate::ui::controls::{Controls, ControlsActions, Toasts};
use crate::ui::fonts;
use crate::ui::launcher::{self, ScreenContext};

/// Opens the window and plays; the exit code the command returns.
pub fn run(options: RunOptions) -> ExitCode {
    if let Err(error) = gtk::init() {
        eprintln!("plugview: no display: {error}");
        return ExitCode::FAILURE;
    }
    fonts::register();
    let app = adw::Application::builder().application_id("app.plugview.plugview").flags(gio::ApplicationFlags::NON_UNIQUE).build();
    app.connect_activate(move |app| App::start(app, options.clone()));
    // GTK must not read plugview's own options.
    if app.run_with_args(&["plugview"]) == glib::ExitCode::SUCCESS { ExitCode::SUCCESS } else { ExitCode::FAILURE }
}

/// A playing card: its pipelines and how it plays.
struct Playback {
    device: Device,
    mode: Mode,
    video: VideoPipeline,
    audio: Option<AudioPipeline>,
    _bus_watch: gst::bus::BusWatchGuard,
}

impl Drop for Playback {
    fn drop(&mut self) {
        let _ = self.video.pipeline.set_state(gst::State::Null);
        if let Some(audio) = &self.audio {
            let _ = audio.pipeline.set_state(gst::State::Null);
        }
    }
}

struct Widgets {
    window: gtk::ApplicationWindow,
    /// "launcher" or "video".
    pages: gtk::Stack,
    launcher: gtk::Stack,
    picture: gtk::Picture,
    controls: Controls,
    toasts: Toasts,
    /// The window buttons, which fade with the controls while playing.
    window_buttons: gtk::Revealer,
}

/// Whether the controls show over the video, and when they go away.
#[derive(Default)]
struct Chrome {
    hide_timer: Option<glib::SourceId>,
    is_bar_hovered: bool,
    /// The last pointer position, so a motion event without movement (widgets changing under it) wakes nothing.
    pointer: Option<(f64, f64)>,
}

struct State {
    options: RunOptions,
    launcher: LauncherState,
    screen_id: &'static str,
    devices: Vec<Device>,
    playback: Option<Playback>,
    is_starting: bool,
    settings: Settings,
    volume_percent: i64,
    is_muted: bool,
    is_refresh_pending: bool,
    device_monitor: Option<(gst::DeviceMonitor, gst::bus::BusWatchGuard)>,
    /// The cards that can play now, for the device menu.
    playable: Vec<plugview_core::DeviceInfo>,
}

struct App {
    widgets: Widgets,
    state: RefCell<State>,
    chrome: RefCell<Chrome>,
}

impl App {
    fn start(application: &adw::Application, options: RunOptions) {
        // After libadwaita has started, which the application does on activation.
        load_css();
        let settings = Settings::load();
        let volume_percent = options.volume_percent;
        let starts_in_full_screen = options.starts_in_full_screen;
        let app = Rc::new_cyclic(|weak: &Weak<Self>| Self {
            widgets: build_window(application, &settings, controls_actions(weak)),
            state: RefCell::new(State {
                options,
                launcher: LauncherState::WaitingForSignal,
                screen_id: "",
                devices: Vec::new(),
                playback: None,
                is_starting: false,
                settings,
                volume_percent,
                is_muted: false,
                is_refresh_pending: false,
                device_monitor: None,
                playable: Vec::new(),
            }),
            chrome: RefCell::new(Chrome::default()),
        });
        // Callbacks hold weak references; the application owns the app until it shuts down.
        let owner = Rc::clone(&app);
        application.connect_shutdown(move |_| drop(owner.state.borrow_mut().playback.take()));
        app.connect_window();
        app.watch_devices();
        app.render();
        app.widgets.window.present();
        if starts_in_full_screen {
            app.widgets.window.fullscreen();
        }
        // Keep the screen awake while the window is open (K32); a controller is not keyboard or mouse input.
        application.inhibit(Some(&app.widgets.window), gtk::ApplicationInhibitFlags::IDLE, Some("Playing a capture card"));
        app.refresh_devices();
    }

    fn send(self: &Rc<Self>, event: LauncherEvent) {
        {
            let mut state = self.state.borrow_mut();
            state.launcher = next_launcher_state(&state.launcher, event);
        }
        self.render();
    }

    /// Shows the video while playing, otherwise the launcher screen for the state (crossfading between screens).
    fn render(self: &Rc<Self>) {
        let (launcher_state, previous_id) = {
            let state = self.state.borrow();
            (state.launcher.clone(), state.screen_id)
        };
        if let LauncherState::Playing(_) = launcher_state {
            self.widgets.pages.set_visible_child_name("video");
            self.refresh_controls();
            self.state.borrow_mut().screen_id = "playing";
            return;
        }
        self.show_launcher_chrome();
        self.widgets.pages.set_visible_child_name("launcher");
        let id = launcher::screen_id(&launcher_state);
        let screen = launcher::screen(&launcher_state, &self.screen_context());
        let name = format!("{id}-{}", glib::monotonic_time());
        self.widgets.launcher.set_transition_type(if id == previous_id { gtk::StackTransitionType::None } else { gtk::StackTransitionType::Crossfade });
        self.widgets.launcher.add_named(&screen, Some(&name));
        self.widgets.launcher.set_visible_child_name(&name);
        // Drop the screens that are no longer shown once the crossfade is over.
        let stack = self.widgets.launcher.clone();
        glib::timeout_add_local_once(std::time::Duration::from_millis(400), move || {
            let visible = stack.visible_child();
            let mut child = stack.first_child();
            while let Some(current) = child {
                child = current.next_sibling();
                if Some(&current) != visible.as_ref() {
                    stack.remove(&current);
                }
            }
        });
        self.state.borrow_mut().screen_id = id;
    }

    fn screen_context(self: &Rc<Self>) -> ScreenContext {
        let state = self.state.borrow();
        let preferred = state.settings.preferred_device_id.clone();
        let devices = state.devices.clone();
        let saved_modes = state.settings.mode_choices.clone();
        let weak = Rc::downgrade(self);
        let is_auto_play_pending = match &state.launcher {
            LauncherState::Ready(ready) => ready.len() > 1 && should_auto_play(ready, preferred.as_deref()),
            _ => false,
        };
        ScreenContext {
            details: Box::new(move |info| {
                let Some(device) = devices.iter().find(|device| &device.info == info) else { return String::new() };
                let choice = saved_modes.get(&info.unique_id).and_then(|key| ModeChoice::from_key(key));
                let modes = devices::modes(&device.gst);
                let index = mode_index(&modes.iter().map(|mode| mode.mode).collect::<Vec<_>>(), choice);
                index.and_then(|index| modes.get(index)).map(|mode| mode.mode.summary()).unwrap_or_default()
            }),
            play: (!state.is_starting).then(|| {
                let play: Rc<dyn Fn(&plugview_core::DeviceInfo)> = Rc::new(move |info: &plugview_core::DeviceInfo| {
                    if let Some(app) = weak.upgrade() {
                        app.state.borrow_mut().settings.preferred_device_id = Some(info.unique_id.clone());
                        app.state.borrow().settings.save();
                        app.play(info);
                    }
                });
                play
            }),
            is_auto_play_pending,
        }
    }

    /// Reports the playable devices and, when nothing plays, starts the remembered or only one.
    fn refresh_devices(self: &Rc<Self>) {
        let videos = devices::video_devices();
        let infos: Vec<_> = videos.iter().map(|device| device.info.clone()).collect();
        let (query, preferred) = {
            let state = self.state.borrow();
            (state.options.video_query.clone(), state.settings.preferred_device_id.clone())
        };
        let playable = preferred_first(&playable_video_devices(&infos, query.as_deref()), preferred.as_deref());
        {
            let mut state = self.state.borrow_mut();
            state.devices = videos;
            state.playable.clone_from(&playable);
        }

        // The playing card went away: release it before the launcher shows what is left.
        let lost = self.state.borrow().playback.as_ref().is_some_and(|playback| !playable.contains(&playback.device.info));
        if lost {
            self.state.borrow_mut().playback = None;
            println!("Capture device disconnected. Waiting for it to come back…");
            self.send(LauncherEvent::PlaybackStopped);
        }
        self.send(LauncherEvent::DevicesChanged(playable.clone()));

        let state = self.state.borrow();
        let can_start = state.playback.is_none() && !state.is_starting && matches!(state.launcher, LauncherState::Ready(_));
        let first = playable.first().cloned();
        let auto_plays = should_auto_play(&playable, preferred.as_deref());
        drop(state);
        if let (true, true, Some(first)) = (can_start, auto_plays, first) {
            self.play(&first);
        }
    }

    fn play(self: &Rc<Self>, info: &plugview_core::DeviceInfo) {
        let Some(device) = self.state.borrow().devices.iter().find(|device| &device.info == info).cloned() else { return };
        self.state.borrow_mut().is_starting = true;
        let result = self.start_playback(&device);
        self.state.borrow_mut().is_starting = false;
        match result {
            Ok(playback) => {
                let audio_summary = playback.audio.as_ref().map_or_else(|| "no audio".to_owned(), |_| "audio on".to_owned());
                println!("Showing {} · {}, {audio_summary}", device.info.name, playback.mode.mode.summary());
                self.widgets.window.set_title(Some(&format!("{} · {}", device.info.name, playback.mode.mode.summary())));
                let aspect_ratio = playback.mode.mode.width as f64 / playback.mode.mode.height.max(1) as f64;
                self.state.borrow_mut().playback = Some(playback);
                self.fit_window_height(aspect_ratio);
                self.send(LauncherEvent::PlaybackStarted(device.info));
                self.show_controls();
            }
            Err(reason) => {
                eprintln!("plugview: {}: {reason}", device.info.name);
                self.send(LauncherEvent::PlaybackFailed(CaptureFailure { device_name: device.info.name.clone(), reason }));
            }
        }
    }

    fn start_playback(self: &Rc<Self>, device: &Device) -> Result<Playback, String> {
        let (choice, options, expands_limited_range) = {
            let state = self.state.borrow();
            let id = &device.info.unique_id;
            let saved = state.settings.mode_choices.get(id).and_then(|key| ModeChoice::from_key(key));
            (state.options.mode.or(saved), state.options.clone(), state.settings.expands_limited_range.get(id).copied().unwrap_or(false))
        };
        let modes = devices::modes(&device.gst);
        let index = mode_index(&modes.iter().map(|mode| mode.mode).collect::<Vec<_>>(), choice).ok_or("reports no video formats")?;
        let mode = modes[index].clone();

        // Find the card's audio before building the video pipeline: device discovery restarts the system's capture
        // devices on some platforms (AVFoundation) and would leave an already-built camera source without frames.
        let audio = if options.is_audio_enabled { devices::card_audio(device, options.audio_query.as_deref()) } else { None };
        let video = start_video(device, &mode, expands_limited_range).map_err(|error| error.to_string())?;
        let paintable = video.sink.property::<gtk::gdk::Paintable>("paintable");
        self.widgets.picture.set_paintable(Some(&paintable));
        let audio = audio.and_then(|audio| pipeline::audio(&audio, false).map_err(|error| eprintln!("plugview: no sound: {error}")).ok());
        self.apply_volume_to(audio.as_ref());

        video.pipeline.set_state(gst::State::Playing).map_err(|_| "could not start (is another app using it?)".to_owned())?;
        if let Some(audio) = &audio {
            let _ = audio.pipeline.set_state(gst::State::Playing);
        }
        let bus_watch = self.watch_pipeline(&video.pipeline, &device.info.name)?;
        Ok(Playback { device: device.clone(), mode, video, audio, _bus_watch: bus_watch })
    }

    /// A pipeline error (the card unplugged mid-frame, a negotiation failure) stops playback and says why.
    fn watch_pipeline(self: &Rc<Self>, pipeline: &gst::Pipeline, name: &str) -> Result<gst::bus::BusWatchGuard, String> {
        let bus = pipeline.bus().ok_or("has no message bus")?;
        let weak = Rc::downgrade(self);
        let name = name.to_owned();
        bus.add_watch_local(move |_, message| {
            if let gst::MessageView::Error(error) = message.view()
                && let Some(app) = weak.upgrade()
            {
                eprintln!("plugview: {}", error.error());
                app.state.borrow_mut().playback = None;
                app.send(LauncherEvent::PlaybackFailed(CaptureFailure { device_name: name.clone(), reason: error.error().to_string() }));
            }
            glib::ControlFlow::Continue
        })
        .map_err(|error| error.to_string())
    }

    /// Hot-plug: GStreamer's device monitor reports cards coming and going; a burst of changes refreshes once.
    fn watch_devices(self: &Rc<Self>) {
        let monitor = gst::DeviceMonitor::new();
        monitor.add_filter(Some("Video/Source"), None);
        monitor.add_filter(Some("Audio/Source"), None);
        let Some(bus) = Some(monitor.bus()) else { return };
        let weak = Rc::downgrade(self);
        let watch = bus.add_watch_local(move |_, message| {
            if matches!(message.view(), gst::MessageView::DeviceAdded(_) | gst::MessageView::DeviceRemoved(_) | gst::MessageView::DeviceChanged(_))
                && let Some(app) = weak.upgrade()
            {
                app.schedule_refresh();
            }
            glib::ControlFlow::Continue
        });
        if monitor.start().is_ok()
            && let Ok(watch) = watch
        {
            self.state.borrow_mut().device_monitor = Some((monitor, watch));
        }
    }

    fn schedule_refresh(self: &Rc<Self>) {
        if std::mem::replace(&mut self.state.borrow_mut().is_refresh_pending, true) {
            return;
        }
        // A replugged card shows its video node a moment before its audio (Tuning::CARD_AUDIO_WAIT_SECONDS).
        let weak: Weak<Self> = Rc::downgrade(self);
        glib::timeout_add_local_once(std::time::Duration::from_millis(500), move || {
            if let Some(app) = weak.upgrade() {
                app.state.borrow_mut().is_refresh_pending = false;
                app.refresh_devices();
            }
        });
    }

    fn connect_window(self: &Rc<Self>) {
        let keys = gtk::EventControllerKey::new();
        let weak = Rc::downgrade(self);
        keys.connect_key_pressed(move |_, key, _, modifiers| {
            let Some(app) = weak.upgrade() else { return glib::Propagation::Proceed };
            if app.handle_key(key, modifiers) { glib::Propagation::Stop } else { glib::Propagation::Proceed }
        });
        self.widgets.window.add_controller(keys);
        self.connect_chrome();

        let weak = Rc::downgrade(self);
        self.widgets.window.connect_close_request(move |window| {
            if let Some(app) = weak.upgrade() {
                let mut state = app.state.borrow_mut();
                if !window.is_fullscreen() && !window.is_maximized() {
                    state.settings.window_size = Some((window.width(), window.height()));
                }
                state.settings.save();
                // Release the card now, so its light goes off with the window.
                state.playback = None;
            }
            glib::Propagation::Proceed
        });
    }
}

/// The zero-copy path first; if its caps do not negotiate, the converted path (L18). Full-range colors need the
/// frames in memory, so they skip the zero-copy path.
fn start_video(device: &Device, mode: &Mode, expands_limited_range: bool) -> Result<VideoPipeline, pipeline::PipelineError> {
    let path = match (expands_limited_range, cfg!(target_os = "linux")) {
        (true, _) => FramePath::Expanded,
        (false, true) => FramePath::ZeroCopy,
        (false, false) => FramePath::Converted,
    };
    let first = pipeline::video(device, mode, path, VideoSink::Window)?;
    if path != FramePath::ZeroCopy || prerolls(&first.pipeline) {
        return Ok(first);
    }
    let _ = first.pipeline.set_state(gst::State::Null);
    eprintln!("plugview: zero-copy video did not negotiate; using the converted path");
    pipeline::video(device, mode, FramePath::Converted, VideoSink::Window)
}

fn prerolls(pipeline: &gst::Pipeline) -> bool {
    match pipeline.set_state(gst::State::Paused) {
        Ok(gst::StateChangeSuccess::NoPreroll | gst::StateChangeSuccess::Success) => true,
        Ok(gst::StateChangeSuccess::Async) => pipeline.state(gst::ClockTime::from_seconds(2)).0.is_ok(),
        Err(_) => false,
    }
}

/// A dark window with no title bar: the launcher or the video fills it, and dragging anywhere moves it (as on the
/// Mac). The video sits alone in a graphics offload widget with a black background (K36).
fn build_window(application: &adw::Application, settings: &Settings, actions: ControlsActions) -> Widgets {
    let (width, height) = settings.window_size.unwrap_or((1280, 720));
    let window = gtk::ApplicationWindow::builder().application(application).title("plugview").default_width(width).default_height(height).build();
    window.add_css_class("plugview");
    // An empty title bar: no header, the content goes to the top edge, the desktop still resizes the window.
    window.set_titlebar(Some(&gtk::Box::new(gtk::Orientation::Horizontal, 0)));

    let launcher = gtk::Stack::new();
    launcher.set_transition_duration(320);

    let picture = gtk::Picture::new();
    picture.set_content_fit(gtk::ContentFit::Contain);
    let offload = gtk::GraphicsOffload::new(Some(&picture));
    offload.set_black_background(true);

    let pages = gtk::Stack::new();
    pages.set_transition_type(gtk::StackTransitionType::Crossfade);
    pages.set_transition_duration(250);
    pages.add_named(&launcher, Some("launcher"));
    pages.add_named(&offload, Some("video"));

    // Window buttons in the desktop's own order, over the content's top edge.
    let buttons = gtk::WindowControls::new(gtk::PackType::End);
    let window_buttons = gtk::Revealer::builder()
        .transition_type(gtk::RevealerTransitionType::Crossfade)
        .transition_duration(220)
        .halign(gtk::Align::End)
        .valign(gtk::Align::Start)
        .margin_top(6)
        .margin_end(6)
        .reveal_child(true)
        .child(&buttons)
        .build();
    let controls = Controls::new(actions);
    let toasts = Toasts::new();
    let overlay = gtk::Overlay::new();
    overlay.set_child(Some(&pages));
    overlay.add_overlay(&window_buttons);
    overlay.add_overlay(&controls.revealer);
    overlay.add_overlay(&toasts.revealer);

    let handle = gtk::WindowHandle::new();
    handle.set_child(Some(&overlay));
    window.set_child(Some(&handle));
    Widgets { window, pages, launcher, picture, controls, toasts, window_buttons }
}

fn load_css() {
    let provider = gtk::CssProvider::new();
    provider.load_from_string(&crate::brand::stylesheet());
    if let Some(display) = gtk::gdk::Display::default() {
        gtk::style_context_add_provider_for_display(&display, &provider, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION);
    }
    // Dark in light mode too: the window is a CRT surface (brand.json "window").
    adw::StyleManager::default().set_color_scheme(adw::ColorScheme::ForceDark);
}

/// The controls' buttons call back into the app; they do nothing once it is gone.
fn controls_actions(weak: &Weak<App>) -> ControlsActions {
    fn on<T: 'static>(weak: &Weak<App>, run: impl Fn(&Rc<App>, T) + 'static) -> Rc<dyn Fn(T)> {
        let weak = weak.clone();
        Rc::new(move |value| {
            if let Some(app) = weak.upgrade() {
                run(&app, value);
            }
        })
    }
    fn on_click(weak: &Weak<App>, run: impl Fn(&Rc<App>) + 'static) -> Rc<dyn Fn()> {
        let run = on(weak, move |app, ()| run(app));
        Rc::new(move || run(()))
    }
    ControlsActions {
        set_volume: on(weak, |app, percent| app.set_volume(percent)),
        toggle_mute: on_click(weak, App::toggle_mute),
        take_screenshot: on_click(weak, App::take_screenshot),
        select_mode: on(weak, App::select_mode),
        set_expands_limited_range: on(weak, App::set_expands_limited_range),
        select_device: on(weak, |app, device: plugview_core::DeviceInfo| app.select_device(&device)),
        toggle_full_screen: on_click(weak, App::toggle_full_screen),
    }
}
