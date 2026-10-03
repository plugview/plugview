//! The floating controls over the video, as on the Mac (`ControlsView`): mute, volume, screenshot, resolution with
//! full-range colors, device, full screen. Every control is at least 32 px tall, so it is easy to hit.

use std::cell::Cell;
use std::rc::Rc;

use gtk::prelude::*;
use plugview_core::{DeviceInfo, ModeChoice};

/// What the controls ask the app to do.
pub struct ControlsActions {
    pub set_volume: Rc<dyn Fn(i64)>,
    pub toggle_mute: Rc<dyn Fn()>,
    pub take_screenshot: Rc<dyn Fn()>,
    /// `None` is Automatic: plugview picks the smoothest mode.
    pub select_mode: Rc<dyn Fn(Option<ModeChoice>)>,
    pub set_expands_limited_range: Rc<dyn Fn(bool)>,
    pub select_device: Rc<dyn Fn(DeviceInfo)>,
    pub toggle_full_screen: Rc<dyn Fn()>,
}

pub struct Controls {
    /// The bar, in a revealer that fades it in and out.
    pub revealer: gtk::Revealer,
    bar: gtk::Box,
    mute: gtk::Button,
    volume: gtk::Scale,
    percent: gtk::Label,
    mode: gtk::MenuButton,
    device: gtk::MenuButton,
    full_screen: gtk::Button,
    actions: Rc<ControlsActions>,
    /// Set while the app moves the slider, so that move is not sent back as a user change.
    is_updating: Rc<Cell<bool>>,
}

impl Controls {
    #[must_use]
    pub fn new(actions: ControlsActions) -> Self {
        let actions = Rc::new(actions);
        let is_updating = Rc::new(Cell::new(false));

        let bar = gtk::Box::new(gtk::Orientation::Horizontal, 4);
        bar.add_css_class("glass");
        bar.add_css_class("controls-bar");

        let mute = icon_button("audio-volume-high-symbolic", "Mute (M)");
        let toggle_mute = Rc::clone(&actions.toggle_mute);
        mute.connect_clicked(move |_| toggle_mute());
        bar.append(&mute);

        let volume = gtk::Scale::with_range(gtk::Orientation::Horizontal, 0.0, 100.0, 1.0);
        volume.set_draw_value(false);
        volume.set_valign(gtk::Align::Center);
        volume.update_property(&[gtk::accessible::Property::Label("Volume")]);
        let set_volume = Rc::clone(&actions.set_volume);
        let updating = Rc::clone(&is_updating);
        volume.connect_value_changed(move |scale| {
            if !updating.get() {
                set_volume(scale.value().round() as i64);
            }
        });
        bar.append(&volume);

        let percent = gtk::Label::new(Some("100%"));
        percent.set_width_chars(4);
        percent.set_xalign(1.0);
        percent.set_margin_end(8);
        bar.append(&percent);

        let screenshot = icon_button("camera-photo-symbolic", "Screenshot (S)");
        let take_screenshot = Rc::clone(&actions.take_screenshot);
        screenshot.connect_clicked(move |_| take_screenshot());
        bar.append(&screenshot);

        let mode = gtk::MenuButton::new();
        mode.set_label("Auto");
        mode.set_tooltip_text(Some("Resolution and frame rate"));
        mode.set_direction(gtk::ArrowType::Up);
        bar.append(&mode);

        let device = gtk::MenuButton::new();
        device.set_icon_name("camera-video-symbolic");
        device.set_tooltip_text(Some("Capture device"));
        device.set_direction(gtk::ArrowType::Up);
        device.set_visible(false);
        bar.append(&device);

        let full_screen = icon_button("view-fullscreen-symbolic", "Full Screen (F)");
        let toggle_full_screen = Rc::clone(&actions.toggle_full_screen);
        full_screen.connect_clicked(move |_| toggle_full_screen());
        bar.append(&full_screen);

        // Scrolling over the bar changes the volume, up is always louder.
        let scroll = gtk::EventControllerScroll::new(gtk::EventControllerScrollFlags::VERTICAL);
        let scroll_volume = volume.clone();
        scroll.connect_scroll(move |_, _, delta_y| {
            let step = if delta_y < 0.0 {
                2.0
            } else if delta_y > 0.0 {
                -2.0
            } else {
                0.0
            };
            scroll_volume.set_value((scroll_volume.value() + step).clamp(0.0, 100.0));
            glib::Propagation::Stop
        });
        bar.add_controller(scroll);

        let revealer = gtk::Revealer::builder()
            .transition_type(gtk::RevealerTransitionType::Crossfade)
            .transition_duration(220)
            .halign(gtk::Align::Center)
            .valign(gtk::Align::End)
            .margin_bottom(22)
            .child(&bar)
            .build();
        Self { revealer, bar, mute, volume, percent, mode, device, full_screen, actions, is_updating }
    }

    /// Whether the pointer rests on the bar or one of its menus is open: the bar then stays up.
    #[must_use]
    pub fn is_in_use(&self, is_hovered: bool) -> bool {
        is_hovered || self.mode.is_active() || self.device.is_active()
    }

    /// The bar itself, to watch for the pointer.
    #[must_use]
    pub fn bar(&self) -> &gtk::Box {
        &self.bar
    }

    pub fn show_volume(&self, percent: i64, is_muted: bool) {
        let effective = if is_muted { 0 } else { percent };
        self.is_updating.set(true);
        self.volume.set_value(effective as f64);
        self.is_updating.set(false);
        self.percent.set_text(&format!("{effective}%"));
        self.mute.set_icon_name(match effective {
            0 => "audio-volume-muted-symbolic",
            1..34 => "audio-volume-low-symbolic",
            34..67 => "audio-volume-medium-symbolic",
            _ => "audio-volume-high-symbolic",
        });
        self.mute.set_tooltip_text(Some(if effective == 0 { "Unmute (M)" } else { "Mute (M)" }));
    }

    pub fn show_full_screen(&self, is_full_screen: bool) {
        self.full_screen.set_icon_name(if is_full_screen { "view-restore-symbolic" } else { "view-fullscreen-symbolic" });
    }

    /// The resolution button and its panel: Automatic, every pickable mode, then Full-range colors.
    pub fn show_modes(&self, modes: &[ModeChoice], active: Option<ModeChoice>, choice: Option<ModeChoice>, expands_limited_range: bool) {
        self.mode.set_label(&active.map_or_else(|| "Auto".to_owned(), |mode| format!("{}p{}", mode.height, mode.frame_rate)));
        let list = gtk::Box::new(gtk::Orientation::Vertical, 2);
        list.set_margin_top(6);
        list.set_margin_bottom(6);
        list.set_margin_start(6);
        list.set_margin_end(6);
        let popover = gtk::Popover::new();
        popover.add_css_class("mode-picker");

        let mut rows: Vec<(String, Option<ModeChoice>)> = vec![("Automatic".to_owned(), None)];
        rows.extend(modes.iter().map(|mode| (mode.title(), Some(*mode))));
        for (title, mode) in rows {
            let row = gtk::Button::with_label(&title);
            row.add_css_class("mode-row");
            if mode == choice {
                row.add_css_class("selected");
            }
            let select = Rc::clone(&self.actions.select_mode);
            let popover_for_row = popover.clone();
            row.connect_clicked(move |_| {
                popover_for_row.popdown();
                select(mode);
            });
            list.append(&row);
        }

        let range = gtk::Box::new(gtk::Orientation::Horizontal, 10);
        range.set_margin_top(8);
        range.set_margin_start(10);
        range.set_margin_end(4);
        let label = gtk::Label::new(Some("Full-range colors"));
        label.set_hexpand(true);
        label.set_xalign(0.0);
        label.set_tooltip_text(Some("Turn on if blacks look gray and colors washed out."));
        let switch = gtk::Switch::new();
        switch.set_active(expands_limited_range);
        switch.set_valign(gtk::Align::Center);
        let set_range = Rc::clone(&self.actions.set_expands_limited_range);
        switch.connect_active_notify(move |switch| set_range(switch.is_active()));
        range.append(&label);
        range.append(&switch);
        list.append(&range);

        popover.set_child(Some(&list));
        self.mode.set_popover(Some(&popover));
    }

    /// The device menu, only when there is more than one device.
    pub fn show_devices(&self, devices: &[DeviceInfo], playing_id: Option<&str>) {
        self.device.set_visible(devices.len() > 1);
        let list = gtk::Box::new(gtk::Orientation::Vertical, 2);
        list.set_margin_top(6);
        list.set_margin_bottom(6);
        list.set_margin_start(6);
        list.set_margin_end(6);
        let popover = gtk::Popover::new();
        popover.add_css_class("mode-picker");
        for device in devices {
            let row = gtk::Button::with_label(&device.name);
            row.add_css_class("mode-row");
            if Some(device.unique_id.as_str()) == playing_id {
                row.add_css_class("selected");
            }
            let select = Rc::clone(&self.actions.select_device);
            let (device, popover_for_row) = (device.clone(), popover.clone());
            row.connect_clicked(move |_| {
                popover_for_row.popdown();
                select(device.clone());
            });
            list.append(&row);
        }
        popover.set_child(Some(&list));
        self.device.set_popover(Some(&popover));
    }
}

fn icon_button(icon: &str, tooltip: &str) -> gtk::Button {
    let button = gtk::Button::from_icon_name(icon);
    button.set_tooltip_text(Some(tooltip));
    button.update_property(&[gtk::accessible::Property::Label(tooltip.split(" (").next().unwrap_or(tooltip))]);
    button
}

/// What a toast's button does.
pub type ToastAction = Rc<dyn Fn()>;

/// A message at the top of the video that slides away on its own: longer with an action (as on the Mac).
pub struct Toasts {
    pub revealer: gtk::Revealer,
    generation: Rc<Cell<u32>>,
}

impl Toasts {
    #[must_use]
    pub fn new() -> Self {
        let revealer = gtk::Revealer::builder()
            .transition_type(gtk::RevealerTransitionType::SlideDown)
            .transition_duration(300)
            .halign(gtk::Align::Center)
            .valign(gtk::Align::Start)
            .margin_top(14)
            .build();
        Self { revealer, generation: Rc::new(Cell::new(0)) }
    }

    /// Shows `message` with an icon, and an action button when given.
    pub fn show(&self, icon: &str, is_warning: bool, message: &str, action: Option<(&str, ToastAction)>) {
        let content = gtk::Box::new(gtk::Orientation::Horizontal, 10);
        content.add_css_class("glass");
        content.add_css_class("toast");
        let image = gtk::Image::from_icon_name(icon);
        image.add_css_class(if is_warning { "warning" } else { "ok" });
        content.append(&image);
        content.append(&gtk::Label::new(Some(message)));
        let seconds = if action.is_some() { 6 } else { 3 };
        if let Some((title, run)) = action {
            let button = gtk::Button::with_label(title);
            button.add_css_class("brand");
            let revealer = self.revealer.clone();
            button.connect_clicked(move |_| {
                revealer.set_reveal_child(false);
                run();
            });
            content.append(&button);
        }
        self.revealer.set_child(Some(&content));
        self.revealer.set_reveal_child(true);

        let generation = self.generation.get().wrapping_add(1);
        self.generation.set(generation);
        let (revealer, current) = (self.revealer.clone(), Rc::clone(&self.generation));
        glib::timeout_add_seconds_local_once(seconds, move || {
            if current.get() == generation {
                revealer.set_reveal_child(false);
            }
        });
    }
}

impl Default for Toasts {
    fn default() -> Self {
        Self::new()
    }
}
