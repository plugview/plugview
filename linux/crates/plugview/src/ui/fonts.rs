//! Geist and Geist Mono (SIL Open Font License), the same files the Mac app ships, embedded in the binary.

use std::path::PathBuf;

use gtk::pango::prelude::FontMapExt;

const FONTS: [(&str, &[u8]); 5] = [
    ("Geist-Regular.ttf", include_bytes!("../../../../../Sources/PlugviewUI/Fonts/Geist-Regular.ttf")),
    ("Geist-Medium.ttf", include_bytes!("../../../../../Sources/PlugviewUI/Fonts/Geist-Medium.ttf")),
    ("Geist-SemiBold.ttf", include_bytes!("../../../../../Sources/PlugviewUI/Fonts/Geist-SemiBold.ttf")),
    ("GeistMono-Medium.ttf", include_bytes!("../../../../../Sources/PlugviewUI/Fonts/GeistMono-Medium.ttf")),
    ("GeistMono-SemiBold.ttf", include_bytes!("../../../../../Sources/PlugviewUI/Fonts/GeistMono-SemiBold.ttf")),
];

/// Makes the brand fonts available to this process; text falls back to the system font if this fails.
///
/// Pango registers fonts from files, so they are written once to the user's cache folder.
pub fn register() {
    let Some(directory) = glib::user_cache_dir().to_str().map(|cache| PathBuf::from(cache).join("plugview").join("fonts")) else { return };
    if std::fs::create_dir_all(&directory).is_err() {
        return;
    }
    let font_map = pangocairo::FontMap::default();
    for (name, bytes) in FONTS {
        let path = directory.join(name);
        let is_current = std::fs::metadata(&path).is_ok_and(|metadata| metadata.len() == bytes.len() as u64);
        if !is_current && std::fs::write(&path, bytes).is_err() {
            continue;
        }
        let _ = font_map.add_font_file(&path);
    }
}
