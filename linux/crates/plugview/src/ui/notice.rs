//! What the signal watch tells the user over the video (`spec/README.md`, "Signal watch and picture help"), worded
//! as on the Mac so every platform says the same.

use std::rc::Rc;

use gtk::prelude::*;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SignalNotice {
    /// Frames stopped while the card is connected; plugview is reopening it. `shows_help` after a few tries.
    NoSignal { device_name: String, shows_help: bool },
    /// Black frames since the start: usually HDCP.
    BlackPicture,
    /// Digital silence since the start, with a picture: the console's audio format, or a headset on the controller.
    NoGameSound,
}

const PICTURE_HELP: [&str; 4] = [
    "PlayStation 5: Settings › System › HDMI › turn off Enable HDCP. PlayStation 4: Settings › System › turn off Enable HDCP.",
    "Set the console to 1080p, and turn off 120 Hz and VRR output.",
    "Nintendo Switch: it must sit in its dock. Switch Lite has no video output.",
    "Unplug the HDMI cable from the card and plug it back in.",
];

const SOUND_HELP: [&str; 4] = [
    "PlayStation 5: Settings › Sound › Audio Output › Audio Format (Priority) › Linear PCM.",
    "Xbox: Settings › General › Volume & audio output › HDMI audio › Stereo uncompressed.",
    "Nintendo Switch 2: set TV sound to Stereo.",
    "Unplug headphones from the controller or the console: game sound then goes back to HDMI.",
];

/// "No signal" covers the picture (never a frozen frame); the hints are a panel with a Close button.
#[must_use]
pub fn widget(notice: &SignalNotice, close: Rc<dyn Fn()>) -> gtk::Widget {
    match notice {
        SignalNotice::NoSignal { device_name, shows_help } => {
            let message = format!("{device_name} stopped sending a picture. Reconnecting…");
            let cover = gtk::Box::new(gtk::Orientation::Vertical, 0);
            cover.add_css_class("notice-cover");
            cover.append(&panel("NO SIGNAL", &message, if *shows_help { &PICTURE_HELP } else { &[] }, None));
            cover.upcast()
        }
        SignalNotice::BlackPicture => {
            panel("BLACK PICTURE", "The card sends a black picture. Usually the console's HDCP protection:", &PICTURE_HELP, Some(close)).upcast()
        }
        SignalNotice::NoGameSound => {
            panel("NO GAME SOUND", "The card sends silence. The console may be sending surround or Dolby sound:", &SOUND_HELP, Some(close)).upcast()
        }
    }
}

fn panel(title: &str, message: &str, checklist: &[&str], close: Option<Rc<dyn Fn()>>) -> gtk::Box {
    let panel = gtk::Box::new(gtk::Orientation::Vertical, 12);
    panel.add_css_class("glass");
    panel.add_css_class("notice");
    panel.set_halign(gtk::Align::Center);
    panel.set_valign(gtk::Align::Center);
    panel.set_vexpand(true);
    panel.set_margin_start(24);
    panel.set_margin_end(24);

    let title_label = gtk::Label::new(Some(title));
    title_label.add_css_class("notice-title");
    title_label.set_xalign(0.0);
    panel.append(&title_label);
    let message_label = wrapping_label(message, "notice-message");
    panel.append(&message_label);
    for line in checklist {
        let row = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let bullet = gtk::Box::new(gtk::Orientation::Horizontal, 0);
        bullet.add_css_class("notice-bullet");
        bullet.set_valign(gtk::Align::Start);
        bullet.set_margin_top(7);
        row.append(&bullet);
        row.append(&wrapping_label(line, "notice-line"));
        panel.append(&row);
    }
    if let Some(close) = close {
        let button = crate::ui::launcher::brand_button("Close", false);
        button.set_halign(gtk::Align::End);
        button.connect_clicked(move |_| close());
        panel.append(&button);
    }
    panel
}

fn wrapping_label(text: &str, class: &str) -> gtk::Label {
    let label = gtk::Label::new(Some(text));
    label.add_css_class(class);
    label.set_wrap(true);
    label.set_xalign(0.0);
    label.set_max_width_chars(60);
    label
}
