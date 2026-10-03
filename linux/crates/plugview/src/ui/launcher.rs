//! Everything the window shows while no video is on screen: one layout per `LauncherState`, as on the Mac
//! (artwork, a mono amber title with a soft glow, a message, then actions).

use std::rc::Rc;

use gtk::prelude::*;
use plugview_core::{DeviceInfo, LauncherState};

use crate::ui::art::{self, Art};

pub const HELP_URL: &str = "https://plugview.app/guide/#troubleshooting";

/// Plays a device the user picked.
pub type PlayAction = Rc<dyn Fn(&DeviceInfo)>;

/// What a launcher screen needs besides the state.
pub struct ScreenContext {
    /// e.g. "1920×1080 · 60 fps · audio on" for each ready device, by unique ID.
    pub details: Box<dyn Fn(&DeviceInfo) -> String>,
    /// Plays a device; `None` while one is starting.
    pub play: Option<PlayAction>,
    /// Several devices, one of them remembered: it plays unless the user picks another.
    pub is_auto_play_pending: bool,
}

/// A stable name per screen, so the window crossfades between screens but not within one.
#[must_use]
pub fn screen_id(state: &LauncherState) -> &'static str {
    match state {
        LauncherState::PermissionNeeded(_) | LauncherState::WaitingForSignal => "waiting",
        LauncherState::PermissionDenied(_) => "denied",
        LauncherState::Ready(_) => "ready",
        LauncherState::Playing(_) => "playing",
        LauncherState::Failed(_) => "failed",
    }
}

/// The screen for `state`.
#[must_use]
pub fn screen(state: &LauncherState, context: &ScreenContext) -> gtk::Widget {
    match state {
        // Linux has no permission prompt for capture devices; access is the video group (see `denied`).
        LauncherState::PermissionNeeded(_) | LauncherState::WaitingForSignal => {
            layout(Art::Waiting, "WAITING FOR SIGNAL", false, Some("Plug a capture card into a USB port.\nYour console will show up here."), &[help_button()])
        }
        LauncherState::PermissionDenied(_) => layout(
            Art::Mark,
            "NO ACCESS TO THE CARD",
            true,
            Some("Linux lets members of the video group open capture cards.\nRun sudo usermod -aG video $USER, then log out and in."),
            &[help_button()],
        ),
        LauncherState::Ready(devices) => ready(devices, context),
        LauncherState::Playing(_) => gtk::Box::new(gtk::Orientation::Vertical, 0).upcast(),
        LauncherState::Failed(failure) => {
            let message = format!("{}: {}", failure.device_name, failure.reason);
            layout(Art::Mark, "COULD NOT START", true, Some(&message), &[help_button()])
        }
    }
}

fn ready(devices: &[DeviceInfo], context: &ScreenContext) -> gtk::Widget {
    let is_picking = devices.len() > 1 && context.play.is_some();
    let message = match (is_picking, context.is_auto_play_pending) {
        (true, true) => Some("Playing the one you used last, or pick another."),
        (true, false) => Some("Pick one. plugview remembers your choice."),
        (false, _) => None,
    };
    let mut actions: Vec<gtk::Widget> =
        devices.iter().map(|device| device_row(device, &(context.details)(device), is_picking.then(|| context.play.clone()).flatten())).collect();
    if let (Some(play), Some(first)) = (&context.play, devices.first())
        && (devices.len() == 1 || context.is_auto_play_pending)
    {
        let label = if devices.len() == 1 { "Play".to_owned() } else { format!("Play {}", first.name) };
        let button = brand_button(&label, true);
        let (play, first) = (Rc::clone(play), first.clone());
        button.connect_clicked(move |_| play(&first));
        actions.push(button.upcast());
    }
    layout(Art::Signal, "SIGNAL FOUND", false, message, &actions)
}

/// The launcher's one layout: artwork, title, message, actions, centered on the glowing surface.
fn layout(art: Art, title: &str, is_error: bool, message: Option<&str>, actions: &[gtk::Widget]) -> gtk::Widget {
    let column = gtk::Box::new(gtk::Orientation::Vertical, 0);
    column.set_halign(gtk::Align::Center);
    column.set_valign(gtk::Align::Center);
    column.set_width_request(440);
    column.append(&art::widget(art));

    let title_label = gtk::Label::new(Some(title));
    title_label.add_css_class("launcher-title");
    if is_error {
        title_label.add_css_class("error");
    }
    title_label.set_margin_top(20);
    column.append(&title_label);

    if let Some(message) = message {
        let message_label = gtk::Label::new(Some(message));
        message_label.add_css_class("launcher-message");
        message_label.set_justify(gtk::Justification::Center);
        message_label.set_wrap(true);
        message_label.set_margin_top(8);
        column.append(&message_label);
    }

    let action_column = gtk::Box::new(gtk::Orientation::Vertical, 14);
    action_column.set_halign(gtk::Align::Center);
    action_column.set_margin_top(18);
    for action in actions {
        action_column.append(action);
    }
    column.append(&action_column);

    let surface = gtk::Box::new(gtk::Orientation::Vertical, 0);
    surface.add_css_class("launcher");
    surface.set_hexpand(true);
    surface.set_vexpand(true);
    surface.append(&column);
    column.set_vexpand(true);
    surface.upcast()
}

/// A found card: a green dot, its name and what it plays; a button when the user picks among several.
fn device_row(device: &DeviceInfo, details: &str, play: Option<PlayAction>) -> gtk::Widget {
    let row = gtk::Box::new(gtk::Orientation::Horizontal, 12);
    let dot = gtk::Box::new(gtk::Orientation::Horizontal, 0);
    dot.add_css_class("device-dot");
    dot.set_valign(gtk::Align::Center);
    row.append(&dot);

    let text = gtk::Box::new(gtk::Orientation::Vertical, 3);
    let name = gtk::Label::new(Some(&device.name));
    name.add_css_class("device-name");
    name.set_xalign(0.0);
    text.append(&name);
    if !details.is_empty() {
        let details_label = gtk::Label::new(Some(details));
        details_label.add_css_class("device-details");
        details_label.set_xalign(0.0);
        text.append(&details_label);
    }
    text.set_hexpand(true);
    row.append(&text);

    let Some(play) = play else {
        row.add_css_class("device-row");
        return row.upcast();
    };
    let arrow = gtk::Image::from_icon_name("media-playback-start-symbolic");
    arrow.add_css_class("device-play");
    row.append(&arrow);
    let button = gtk::Button::builder().child(&row).build();
    button.add_css_class("device-row");
    button.update_property(&[gtk::accessible::Property::Label(&format!("Play {}", device.name))]);
    let device = device.clone();
    button.connect_clicked(move |_| play(&device));
    button.upcast()
}

#[must_use]
pub fn brand_button(label: &str, is_primary: bool) -> gtk::Button {
    let button = gtk::Button::with_label(label);
    button.add_css_class("brand");
    if is_primary {
        button.add_css_class("primary");
    }
    button.set_halign(gtk::Align::Center);
    button
}

fn help_button() -> gtk::Widget {
    let button = brand_button("Help me connect", false);
    button.connect_clicked(|button| {
        let window = button.root().and_downcast::<gtk::Window>();
        gtk::UriLauncher::new(HELP_URL).launch(window.as_ref(), gio::Cancellable::NONE, |_| {});
    });
    button.upcast()
}
