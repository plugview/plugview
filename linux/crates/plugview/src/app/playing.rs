//! While a card plays: the controls and their fade, volume, screenshots, mode and device changes, window size, keys.

use std::path::PathBuf;
use std::rc::Rc;
use std::time::Duration;

use adw::prelude::*;
use plugview_core::{DeviceInfo, LauncherState, ModeChoice, Tuning, pickable_modes, screenshot_file_name};

use super::App;
use crate::devices;
use crate::pipeline::AudioPipeline;

/// Ctrl+= and Ctrl+- grow and shrink the window by this much, as ⌘+ and ⌘- on the Mac.
const ZOOM_STEP: f64 = 1.25;

impl App {
    pub(super) fn is_playing(&self) -> bool {
        matches!(self.state.borrow().launcher, LauncherState::Playing(_))
    }

    fn playing_device(&self) -> Option<DeviceInfo> {
        self.state.borrow().playback.as_ref().map(|playback| playback.device.info.clone())
    }

    // MARK: - Controls fade

    /// Watches the pointer: moving it shows the controls, leaving the window hides them.
    pub(super) fn connect_chrome(self: &Rc<Self>) {
        let motion = gtk::EventControllerMotion::new();
        let weak = Rc::downgrade(self);
        motion.connect_motion(move |_, x, y| {
            let Some(app) = weak.upgrade() else { return };
            let has_moved = app.chrome.borrow_mut().pointer.replace((x, y)) != Some((x, y));
            if has_moved {
                app.show_controls();
            }
        });
        let weak = Rc::downgrade(self);
        motion.connect_leave(move |_| {
            if let Some(app) = weak.upgrade()
                && app.is_playing()
                && !app.widgets.controls.is_in_use(false)
            {
                app.set_chrome_visible(false);
            }
        });
        self.widgets.window.add_controller(motion);

        let bar_motion = gtk::EventControllerMotion::new();
        let weak = Rc::downgrade(self);
        bar_motion.connect_enter(move |_, _, _| {
            if let Some(app) = weak.upgrade() {
                app.chrome.borrow_mut().is_bar_hovered = true;
            }
        });
        let weak = Rc::downgrade(self);
        bar_motion.connect_leave(move |_| {
            if let Some(app) = weak.upgrade() {
                app.chrome.borrow_mut().is_bar_hovered = false;
            }
        });
        self.widgets.controls.bar().add_controller(bar_motion);

        let weak = Rc::downgrade(self);
        self.widgets.window.connect_fullscreened_notify(move |window| {
            if let Some(app) = weak.upgrade() {
                app.widgets.controls.show_full_screen(window.is_fullscreen());
            }
        });
    }

    /// Shows the controls and window buttons; they fade after `CONTROLS_HIDE_SECONDS` unless the pointer rests on
    /// the bar or one of its menus is open.
    pub(super) fn show_controls(self: &Rc<Self>) {
        if !self.is_playing() {
            return;
        }
        self.set_chrome_visible(true);
        self.cancel_hide_timer();
        let weak = Rc::downgrade(self);
        let timer = glib::timeout_add_local_once(Duration::from_secs_f64(Tuning::CONTROLS_HIDE_SECONDS), move || {
            let Some(app) = weak.upgrade() else { return };
            app.chrome.borrow_mut().hide_timer = None;
            let is_hovered = app.chrome.borrow().is_bar_hovered;
            if app.widgets.controls.is_in_use(is_hovered) {
                app.show_controls();
            } else if app.is_playing() {
                app.set_chrome_visible(false);
            }
        });
        self.chrome.borrow_mut().hide_timer = Some(timer);
    }

    /// The launcher has nothing to cover: window buttons on, controls off.
    pub(super) fn show_launcher_chrome(&self) {
        self.cancel_hide_timer();
        self.widgets.controls.revealer.set_reveal_child(false);
        self.widgets.window_buttons.set_reveal_child(true);
        self.widgets.window.set_cursor_from_name(None);
    }

    fn cancel_hide_timer(&self) {
        if let Some(timer) = self.chrome.borrow_mut().hide_timer.take() {
            timer.remove();
        }
    }

    /// The pointer hides with the controls, so nothing sits over the game.
    fn set_chrome_visible(&self, is_visible: bool) {
        self.widgets.controls.revealer.set_reveal_child(is_visible);
        self.widgets.window_buttons.set_reveal_child(is_visible);
        self.widgets.window.set_cursor_from_name(if is_visible { None } else { Some("none") });
    }

    /// Fills the controls for the playing card: volume, its modes, the other cards, full screen.
    pub(super) fn refresh_controls(&self) {
        let state = self.state.borrow();
        let Some(playback) = &state.playback else { return };
        let id = &playback.device.info.unique_id;
        let video_modes: Vec<_> = devices::modes(&playback.device.gst).iter().map(|mode| mode.mode).collect();
        let choice = state.options.mode.or_else(|| state.settings.mode_choices.get(id).and_then(|key| ModeChoice::from_key(key)));
        let expands_limited_range = state.settings.expands_limited_range.get(id).copied().unwrap_or(false);
        let controls = &self.widgets.controls;
        controls.show_modes(&pickable_modes(&video_modes), Some(ModeChoice::from_mode(&playback.mode.mode)), choice, expands_limited_range);
        controls.show_devices(&state.playable, Some(id));
        controls.show_volume(state.volume_percent, state.is_muted);
        controls.show_full_screen(self.widgets.window.is_fullscreen());
    }

    // MARK: - Volume

    pub(super) fn set_volume(&self, percent: i64) {
        {
            let mut state = self.state.borrow_mut();
            state.volume_percent = percent.clamp(0, 100);
            state.is_muted = false;
        }
        self.apply_volume();
    }

    pub(super) fn toggle_mute(self: &Rc<Self>) {
        let is_muted = {
            let mut state = self.state.borrow_mut();
            state.is_muted = !state.is_muted;
            state.is_muted
        };
        self.apply_volume();
        let percent = self.state.borrow().volume_percent;
        println!("{}", if is_muted { "Muted".to_owned() } else { format!("Volume {percent}%") });
    }

    pub(super) fn apply_volume_to(&self, audio: Option<&AudioPipeline>) {
        let state = self.state.borrow();
        if let Some(audio) = audio {
            audio.volume.set_property("volume", if state.is_muted { 0.0 } else { state.volume_percent as f64 / 100.0 });
        }
    }

    fn apply_volume(&self) {
        {
            let state = self.state.borrow();
            self.apply_volume_to(state.playback.as_ref().and_then(|playback| playback.audio.as_ref()));
        }
        let state = self.state.borrow();
        self.widgets.controls.show_volume(state.volume_percent, state.is_muted);
    }

    // MARK: - Screenshot

    /// Saves the picture on screen to ~/Pictures/plugview and says where.
    pub(super) fn take_screenshot(self: &Rc<Self>) {
        if !self.is_playing() {
            return;
        }
        match self.save_screenshot() {
            Ok(path) => {
                println!("Screenshot saved to {}", path.display());
                let window = self.widgets.window.clone();
                let show: Rc<dyn Fn()> = Rc::new(move || {
                    gtk::FileLauncher::new(Some(&gio::File::for_path(&path))).open_containing_folder(Some(&window), gio::Cancellable::NONE, |_| {});
                });
                self.widgets.toasts.show("camera-photo-symbolic", false, "Screenshot saved", Some(("Show", show)));
            }
            Err(reason) => {
                eprintln!("plugview: screenshot failed: {reason}");
                self.widgets.toasts.show("dialog-warning-symbolic", true, "Could not save the screenshot", None);
            }
        }
    }

    /// Renders the sink's current frame at its own size (with Full-range colors when on, since the pipeline applies
    /// them) and writes a PNG.
    fn save_screenshot(&self) -> Result<PathBuf, String> {
        let paintable = self.widgets.picture.paintable().ok_or("no picture")?;
        let (width, height) = (paintable.intrinsic_width(), paintable.intrinsic_height());
        if width <= 0 || height <= 0 {
            return Err("no frame yet".into());
        }
        let snapshot = gtk::Snapshot::new();
        paintable.snapshot(&snapshot, f64::from(width), f64::from(height));
        let node = snapshot.to_node().ok_or("no frame yet")?;
        let renderer = self.widgets.window.renderer().ok_or("the window is not on screen")?;
        let texture = renderer.render_texture(&node, None);

        let folder = glib::user_special_dir(glib::UserDirectory::Pictures).unwrap_or_else(glib::home_dir).join("plugview");
        std::fs::create_dir_all(&folder).map_err(|error| format!("{}: {error}", folder.display()))?;
        let now = glib::DateTime::now_local().map_err(|error| error.to_string())?;
        let name = screenshot_file_name(
            now.year(),
            now.month().unsigned_abs(),
            now.day_of_month().unsigned_abs(),
            now.hour().unsigned_abs(),
            now.minute().unsigned_abs(),
            now.second().unsigned_abs(),
        );
        let path = folder.join(name);
        texture.save_to_png(&path).map_err(|error| error.to_string())?;
        Ok(path)
    }

    // MARK: - Mode, color range, device

    /// `None` is Automatic. Remembered per card; a pick in the window replaces `--mode`.
    pub(super) fn select_mode(self: &Rc<Self>, choice: Option<ModeChoice>) {
        let Some(device) = self.playing_device() else { return };
        {
            let mut state = self.state.borrow_mut();
            match choice {
                Some(choice) => state.settings.mode_choices.insert(device.unique_id.clone(), choice.key()),
                None => state.settings.mode_choices.remove(&device.unique_id),
            };
            state.options.mode = None;
            state.settings.save();
        }
        self.restart(device);
    }

    pub(super) fn set_expands_limited_range(self: &Rc<Self>, is_on: bool) {
        let Some(device) = self.playing_device() else { return };
        {
            let mut state = self.state.borrow_mut();
            state.settings.expands_limited_range.insert(device.unique_id.clone(), is_on);
            state.settings.save();
        }
        self.restart(device);
    }

    pub(super) fn select_device(self: &Rc<Self>, device: &DeviceInfo) {
        {
            let mut state = self.state.borrow_mut();
            state.settings.preferred_device_id = Some(device.unique_id.clone());
            state.settings.save();
        }
        if self.playing_device().as_ref() != Some(device) {
            self.restart(device.clone());
        }
    }

    /// Plays `device` again from scratch. Deferred, so the menu that asked for it is not replaced mid-signal.
    fn restart(self: &Rc<Self>, device: DeviceInfo) {
        let weak = Rc::downgrade(self);
        glib::idle_add_local_once(move || {
            let Some(app) = weak.upgrade() else { return };
            app.state.borrow_mut().playback = None;
            app.play(&device);
        });
    }

    // MARK: - Window

    pub(super) fn toggle_full_screen(self: &Rc<Self>) {
        let window = &self.widgets.window;
        if window.is_fullscreen() {
            window.unfullscreen();
        } else {
            window.fullscreen();
        }
    }

    /// Keeps the window's width and fits its height to the video, so there are no black bars.
    pub(super) fn fit_window_height(&self, aspect_ratio: f64) {
        let window = &self.widgets.window;
        if window.is_fullscreen() || window.is_maximized() || aspect_ratio <= 0.0 {
            return;
        }
        let width = if window.width() > 0 { window.width() } else { window.default_size().0 };
        window.set_default_size(width, (f64::from(width) / aspect_ratio).round() as i32);
    }

    /// Ctrl+0: one video pixel per screen pixel.
    fn show_actual_size(&self) {
        let Some((width, height)) = self.state.borrow().playback.as_ref().map(|playback| (playback.mode.mode.width, playback.mode.mode.height)) else { return };
        let scale = i64::from(self.widgets.window.scale_factor().max(1));
        self.resize_window(f64::from(i32::try_from(width / scale).unwrap_or(i32::MAX)), f64::from(i32::try_from(height / scale).unwrap_or(i32::MAX)));
    }

    fn zoom(&self, factor: f64) {
        let window = &self.widgets.window;
        self.resize_window(f64::from(window.width()) * factor, f64::from(window.height()) * factor);
    }

    /// ponytail: GTK 4 cannot keep a window on screen or on top (Wayland leaves both to the compositor), so the
    /// size is clamped to the monitor and Ctrl+Shift+T is not offered.
    fn resize_window(&self, width: f64, height: f64) {
        let window = &self.widgets.window;
        if window.is_fullscreen() {
            return;
        }
        let monitor = window.surface().and_then(|surface| surface.display().monitor_at_surface(&surface)).map(|monitor| monitor.geometry());
        let (max_width, max_height) = monitor.map_or((f64::MAX, f64::MAX), |area| (f64::from(area.width()), f64::from(area.height())));
        let scale = (max_width / width).min(max_height / height).min(1.0);
        window.unmaximize();
        window.set_default_size((width * scale).max(320.0).round() as i32, (height * scale).max(180.0).round() as i32);
    }

    // MARK: - Keys

    /// The spec's keys, Linux column.
    pub(super) fn handle_key(self: &Rc<Self>, key: gtk::gdk::Key, modifiers: gtk::gdk::ModifierType) -> bool {
        use gtk::gdk::Key;
        let is_control = modifiers.contains(gtk::gdk::ModifierType::CONTROL_MASK);
        let window = &self.widgets.window;
        match (key, is_control) {
            (Key::f | Key::F | Key::F11, false) => self.toggle_full_screen(),
            (Key::Escape, false) if window.is_fullscreen() => window.unfullscreen(),
            (Key::m | Key::M, false) => self.toggle_mute(),
            (Key::s | Key::S, false) => self.take_screenshot(),
            (Key::Up | Key::Down, false) => {
                let step = if key == Key::Up { Tuning::VOLUME_STEP_PERCENT } else { -Tuning::VOLUME_STEP_PERCENT };
                let percent = self.state.borrow().volume_percent + step;
                self.set_volume(percent);
                println!("Volume {}%", self.state.borrow().volume_percent);
            }
            (Key::_0 | Key::KP_0, true) => self.show_actual_size(),
            (Key::equal | Key::plus | Key::KP_Add, true) => self.zoom(ZOOM_STEP),
            (Key::minus | Key::KP_Subtract, true) => self.zoom(1.0 / ZOOM_STEP),
            (Key::w | Key::W, true) | (Key::q | Key::Q, _) => window.close(),
            _ => return false,
        }
        true
    }
}
