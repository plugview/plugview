//! Launch at login: the Background portal inside Flatpak, an XDG autostart entry otherwise. Either way plugview
//! starts hidden in the tray (`--app --background`).

use std::path::PathBuf;

use glib::prelude::*;

use crate::app::{APP_MARKER, BACKGROUND_MARKER};

const APP_ID: &str = "app.plugview.plugview";

/// Turns launch at login on or off; the reason when the system refused.
pub fn set_launches_at_login(is_on: bool) -> Result<(), String> {
    if std::env::var_os("FLATPAK_ID").is_some() { request_background(is_on) } else { write_autostart_entry(is_on) }
}

fn autostart_entry() -> PathBuf {
    glib::user_config_dir().join("autostart").join(format!("{APP_ID}.desktop"))
}

fn write_autostart_entry(is_on: bool) -> Result<(), String> {
    let path = autostart_entry();
    if !is_on {
        return match std::fs::remove_file(&path) {
            Err(error) if error.kind() != std::io::ErrorKind::NotFound => Err(error.to_string()),
            _ => Ok(()),
        };
    }
    let executable = std::env::current_exe().map_err(|error| error.to_string())?;
    // Exec quoting (Desktop Entry spec): a path with spaces or quotes goes in double quotes, with `"`, `` ` ``,
    // `$` and `\` escaped.
    let escaped: String = executable
        .to_string_lossy()
        .chars()
        .flat_map(|character| if matches!(character, '"' | '`' | '$' | '\\') { vec!['\\', character] } else { vec![character] })
        .collect();
    let entry = format!(
        "[Desktop Entry]\nType=Application\nName=plugview\nIcon={APP_ID}\nExec=\"{escaped}\" {APP_MARKER} {BACKGROUND_MARKER}\nX-GNOME-Autostart-enabled=true\nNoDisplay=true\n"
    );
    let directory = path.parent().ok_or("no autostart folder")?;
    std::fs::create_dir_all(directory).map_err(|error| error.to_string())?;
    std::fs::write(&path, entry).map_err(|error| error.to_string())
}

/// `org.freedesktop.portal.Background.RequestBackground`: the desktop may ask the user; the answer arrives later and
/// is not awaited (the tray shows the setting as asked).
fn request_background(is_on: bool) -> Result<(), String> {
    let connection = gio::bus_get_sync(gio::BusType::Session, gio::Cancellable::NONE).map_err(|error| error.to_string())?;
    let options = glib::VariantDict::new(None);
    options.insert("reason", "Open plugview when a capture card is plugged in");
    options.insert("autostart", is_on);
    options.insert("commandline", vec!["plugview".to_owned(), APP_MARKER.to_owned(), BACKGROUND_MARKER.to_owned()]);
    let parameters = ("", options.end()).to_variant();
    connection
        .call_sync(
            Some("org.freedesktop.portal.Desktop"),
            "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.Background",
            "RequestBackground",
            Some(&parameters),
            None,
            gio::DBusCallFlags::NONE,
            5_000,
            gio::Cancellable::NONE,
        )
        .map(|_| ())
        .map_err(|error| error.to_string())
}
