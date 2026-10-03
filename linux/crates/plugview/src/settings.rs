//! What plugview remembers between runs (the Mac keeps the same in its user defaults): the card the user picked, the
//! mode and color range picked per card, and the window size. JSON in `$XDG_CONFIG_HOME/plugview/settings.json`.

use std::collections::BTreeMap;
use std::path::PathBuf;

use serde::{Deserialize, Serialize};

#[derive(Debug, Default, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    pub preferred_device_id: Option<String>,
    /// `ModeChoice` keys such as `1920x1080@60`, by device unique ID.
    pub mode_choices: BTreeMap<String, String>,
    /// Full-range colors, by device unique ID.
    pub expands_limited_range: BTreeMap<String, bool>,
    pub window_size: Option<(i32, i32)>,
}

impl Settings {
    fn path() -> PathBuf {
        glib::user_config_dir().join("plugview").join("settings.json")
    }

    /// The saved settings, or the defaults when there are none or they cannot be read.
    #[must_use]
    pub fn load() -> Self {
        std::fs::read_to_string(Self::path()).ok().and_then(|text| serde_json::from_str(&text).ok()).unwrap_or_default()
    }

    /// Saves the settings; a failure only costs remembering them.
    pub fn save(&self) {
        let path = Self::path();
        if let (Some(directory), Ok(text)) = (path.parent(), serde_json::to_string_pretty(self)) {
            let _ = std::fs::create_dir_all(directory);
            let _ = std::fs::write(path, text);
        }
    }
}
