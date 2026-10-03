//! The brand, read from `spec/fixtures/brand.json` (compiled in), so the Linux app draws with exactly the numbers the
//! Mac's tests check. Nothing brand-related is typed by hand here.

use std::fmt::Write as _;
use std::sync::OnceLock;

use serde::Deserialize;

const BRAND_JSON: &str = include_str!("../../../../spec/fixtures/brand.json");

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Brand {
    pub palette: Palette,
    pub bars: Vec<String>,
    pub geometry: Geometry,
    pub animations: Animations,
}

#[derive(Debug, Deserialize)]
pub struct Palette {
    pub cathode: String,
    pub surface: String,
    pub raised: String,
    pub line: String,
    pub bone: String,
    pub muted: String,
    pub signal: String,
    pub tally: String,
    pub ok: String,
    pub glow: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Geometry {
    pub port: Vec<[f64; 2]>,
    pub triangle: Vec<[f64; 2]>,
    pub connector: Vec<[f64; 2]>,
    pub connector_window: Vec<[f64; 2]>,
    pub prongs: Vec<[f64; 4]>,
    pub prong_corner_radius: f64,
    pub port_stroke_width: f64,
    pub triangle_stroke_width: f64,
    pub bar_starts: Vec<f64>,
    pub bar_width: f64,
    pub connector_bars: Bars,
    pub triangle_bars: Bars,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Bars {
    pub x: f64,
    pub scale_x: f64,
    #[serde(default)]
    pub live_second_copy_x: f64,
}

#[derive(Debug, Deserialize)]
pub struct Animations {
    pub waiting: Waiting,
    pub intro: Intro,
    pub live: Live,
}

#[derive(Debug, Deserialize)]
pub struct Track {
    pub frames: Vec<[f64; 2]>,
    pub easing: [f64; 4],
}

impl Track {
    /// The track as the shared core evaluates it (`easing.json` pins the curve maths).
    #[must_use]
    pub fn keyframes(&self) -> plugview_core::Keyframes {
        let [x1, y1, x2, y2] = self.easing;
        plugview_core::Keyframes {
            frames: self.frames.iter().map(|[at, value]| (*at, *value)).collect(),
            easing: plugview_core::CubicBezier { x1, y1, x2, y2 },
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Waiting {
    pub view_box_width: f64,
    pub connector_translate: [f64; 2],
    pub connector_scale: f64,
    pub hover_seconds: f64,
    pub hover: Track,
    pub blink_seconds: f64,
    pub dim_from_seconds: f64,
    pub dim_opacity: f64,
    pub port_offset_x: f64,
    pub port_opacity: f64,
    pub port_dash: Vec<f64>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Intro {
    pub delay_seconds: f64,
    pub duration_seconds: f64,
    pub flash_rect: [f64; 4],
    pub flash_corner_radius: f64,
    pub flash_center: [f64; 2],
    pub connector_center: [f64; 2],
    pub triangle_center: [f64; 2],
    pub tracks: IntroTracks,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IntroTracks {
    pub port_opacity: Track,
    pub flash_scale_x: Track,
    pub flash_scale_y: Track,
    pub flash_opacity: Track,
    pub connector_x: Track,
    pub connector_scale: Track,
    pub connector_opacity: Track,
    pub triangle_scale: Track,
    pub triangle_opacity: Track,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Live {
    pub scroll_seconds: f64,
    pub scroll_distance: f64,
}

/// The brand, parsed once.
///
/// # Panics
/// Never at run time: `brand.json` is compiled in and a test parses it.
#[must_use]
#[allow(clippy::expect_used)]
pub fn brand() -> &'static Brand {
    static BRAND: OnceLock<Brand> = OnceLock::new();
    BRAND.get_or_init(|| serde_json::from_str(BRAND_JSON).expect("spec/fixtures/brand.json is checked by the tests"))
}

/// An `#RRGGBB` color as cairo components (0...1).
#[must_use]
pub fn rgb(hex: &str) -> (f64, f64, f64) {
    let value = u32::from_str_radix(hex.trim_start_matches('#'), 16).unwrap_or(0);
    let channel = |shift: u32| f64::from((value >> shift) & 0xFF) / 255.0;
    (channel(16), channel(8), channel(0))
}

/// The app's stylesheet, with every color from the palette (GTK CSS variables via `@define-color`).
#[must_use]
pub fn stylesheet() -> String {
    let palette = &brand().palette;
    let colors = [
        ("cathode", &palette.cathode),
        ("surface", &palette.surface),
        ("raised", &palette.raised),
        ("line", &palette.line),
        ("bone", &palette.bone),
        ("muted", &palette.muted),
        ("signal", &palette.signal),
        ("tally", &palette.tally),
        ("ok", &palette.ok),
        ("glow", &palette.glow),
    ];
    let mut css = String::new();
    for (name, hex) in colors {
        let _ = writeln!(css, "@define-color pv_{name} {hex};");
    }
    css.push_str(include_str!("ui/style.css"));
    css
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_brand_kit_parses_completely() {
        let brand = brand();
        assert_eq!(brand.bars.len(), 7);
        assert_eq!(brand.geometry.port.len(), 6);
        assert_eq!(rgb(&brand.palette.signal), (1.0, 176.0 / 255.0, 0.0));
        assert!((brand.animations.intro.tracks.connector_x.keyframes().value(1.0)).abs() < 1e-9);
    }

    #[test]
    fn the_stylesheet_defines_every_palette_color() {
        let css = stylesheet();
        for name in ["cathode", "surface", "raised", "line", "bone", "muted", "signal", "tally", "ok", "glow"] {
            assert!(css.contains(&format!("@define-color pv_{name} #")), "{name}");
        }
    }
}
