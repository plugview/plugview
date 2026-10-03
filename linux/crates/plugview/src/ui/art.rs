//! The mark and its animations, drawn with cairo from the brand kit's 64-unit geometry in `brand.json`.
//!
//! Mirrors the Mac's `BrandGeometry` and `BrandAnimations`: the waiting loop (the connector hovers beside a dashed
//! port, prongs blink), and the plug-in intro that ends in the live loop (bars scrolling in the triangle). With
//! animations off in the desktop settings, every animation rests on its final frame.

use std::cell::Cell;
use std::rc::Rc;

use gtk::cairo::Context;
use gtk::prelude::*;

use crate::brand::{Brand, brand, rgb};

/// Which artwork a launcher screen shows.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Art {
    Mark,
    Waiting,
    Signal,
}

impl Art {
    /// The width the Mac gives each artwork (`Screen.artwork`).
    fn width(self) -> i32 {
        match self {
            Self::Mark => 96,
            Self::Waiting => 300,
            Self::Signal => 150,
        }
    }

    /// Width over height, from each artwork's view box.
    fn aspect(self) -> f64 {
        match self {
            Self::Waiting => brand().animations.waiting.view_box_width / 64.0,
            Self::Mark | Self::Signal => 1.0,
        }
    }
}

/// A drawing area that plays `art`, frame-synced to the display.
pub fn widget(art: Art) -> gtk::DrawingArea {
    let area = gtk::DrawingArea::new();
    let width = art.width();
    area.set_content_width(width);
    area.set_content_height((f64::from(width) / art.aspect()).round() as i32);
    area.set_halign(gtk::Align::Center);
    area.set_accessible_role(gtk::AccessibleRole::Presentation);

    let started = Rc::new(Cell::new(None::<i64>));
    let elapsed = Rc::new(Cell::new(0.0_f64));
    if art != Art::Mark {
        let started = Rc::clone(&started);
        let elapsed = Rc::clone(&elapsed);
        area.add_tick_callback(move |area, clock| {
            let now = clock.frame_time();
            let start = *started.get().get_or_insert(now);
            started.set(Some(start));
            elapsed.set((now - start) as f64 / 1e6);
            area.queue_draw();
            glib::ControlFlow::Continue
        });
    }
    area.set_draw_func(move |area, context, width, height| {
        let animates = area.settings().is_gtk_enable_animations();
        let _ = draw(context, art, f64::from(width), f64::from(height), elapsed.get(), animates);
    });
    area
}

fn draw(context: &Context, art: Art, width: f64, height: f64, seconds: f64, animates: bool) -> Result<(), gtk::cairo::Error> {
    let brand = brand();
    match art {
        Art::Mark => {
            fit(context, 64.0, width, height);
            stroke_port(context, brand, None)?;
            draw_triangle(context, brand, None)
        }
        Art::Waiting => draw_waiting(context, brand, width, height, if animates { seconds } else { 0.0 }),
        Art::Signal => {
            let intro = &brand.animations.intro;
            let length = intro.delay_seconds + intro.duration_seconds;
            draw_signal(context, brand, width, height, if animates { seconds } else { length })
        }
    }
}

fn draw_waiting(context: &Context, brand: &Brand, width: f64, height: f64, seconds: f64) -> Result<(), gtk::cairo::Error> {
    let waiting = &brand.animations.waiting;
    fit(context, waiting.view_box_width, width, height);

    context.save()?;
    context.translate(waiting.connector_translate[0], waiting.connector_translate[1]);
    context.scale(waiting.connector_scale, waiting.connector_scale);
    let hover = waiting.hover.keyframes().value((seconds % waiting.hover_seconds) / waiting.hover_seconds);
    context.translate(hover, 0.0);
    // CSS steps(1): full brightness for the first part of each blink, dim for the rest.
    let is_dim = seconds % waiting.blink_seconds >= waiting.dim_from_seconds;
    draw_connector(context, brand, if is_dim { waiting.dim_opacity } else { 1.0 })?;
    context.restore()?;

    context.save()?;
    context.translate(waiting.port_offset_x, 0.0);
    context.push_group();
    context.set_dash(&waiting.port_dash, 0.0);
    stroke_port(context, brand, None)?;
    context.pop_group_to_source()?;
    context.paint_with_alpha(waiting.port_opacity)?;
    context.restore()
}

fn draw_signal(context: &Context, brand: &Brand, width: f64, height: f64, seconds: f64) -> Result<(), gtk::cairo::Error> {
    let intro = &brand.animations.intro;
    let live = &brand.animations.live;
    fit(context, 64.0, width, height);
    let length = intro.delay_seconds + intro.duration_seconds;
    if seconds >= length {
        let scroll = ((seconds - length) % live.scroll_seconds) / live.scroll_seconds * live.scroll_distance;
        stroke_port(context, brand, None)?;
        return draw_triangle(context, brand, Some(scroll));
    }

    let progress = (seconds - intro.delay_seconds) / intro.duration_seconds;
    let tracks = &intro.tracks;
    let value = |track: &crate::brand::Track| track.keyframes().value(progress);

    stroke_port(context, brand, Some(value(&tracks.port_opacity)))?;

    let (scale_x, scale_y) = (value(&tracks.flash_scale_x), value(&tracks.flash_scale_y));
    layer(context, value(&tracks.flash_opacity), |context| {
        // A zero scale would make cairo's matrix singular; there is nothing to draw then anyway.
        if scale_x.abs() < 1e-6 || scale_y.abs() < 1e-6 {
            return Ok(());
        }
        scale_about(context, scale_x, scale_y, intro.flash_center);
        let [x, y, w, h] = intro.flash_rect;
        rounded_rectangle(context, x, y, w, h, intro.flash_corner_radius);
        set_color(context, &brand.palette.signal);
        context.fill()
    })?;

    let connector_scale = value(&tracks.connector_scale);
    layer(context, value(&tracks.connector_opacity), |context| {
        context.translate(value(&tracks.connector_x), 0.0);
        scale_about(context, connector_scale, connector_scale, intro.connector_center);
        draw_connector(context, brand, 1.0)
    })?;

    let triangle_scale = value(&tracks.triangle_scale);
    layer(context, value(&tracks.triangle_opacity), |context| {
        if triangle_scale.abs() < 1e-6 {
            return Ok(());
        }
        scale_about(context, triangle_scale, triangle_scale, intro.triangle_center);
        draw_triangle(context, brand, None)
    })
}

/// Draws `body` into its own group and paints it at `opacity` (cairo's equivalent of a layer's opacity on the Mac).
fn layer(context: &Context, opacity: f64, body: impl FnOnce(&Context) -> Result<(), gtk::cairo::Error>) -> Result<(), gtk::cairo::Error> {
    if opacity <= 0.0 {
        return Ok(());
    }
    context.save()?;
    context.push_group();
    let drawn = body(context);
    context.pop_group_to_source()?;
    context.paint_with_alpha(opacity.min(1.0))?;
    context.restore()?;
    drawn
}

/// Maps the kit's `view_box_width`×64 box onto the widget, centered, keeping its proportions.
fn fit(context: &Context, view_box_width: f64, width: f64, height: f64) {
    let scale = (width / view_box_width).min(height / 64.0);
    context.translate((width - view_box_width * scale) / 2.0, (height - 64.0 * scale) / 2.0);
    context.scale(scale, scale);
}

fn scale_about(context: &Context, x: f64, y: f64, center: [f64; 2]) {
    context.translate(center[0], center[1]);
    context.scale(x, y);
    context.translate(-center[0], -center[1]);
}

fn polygon(context: &Context, points: &[[f64; 2]]) {
    for (index, [x, y]) in points.iter().enumerate() {
        if index == 0 {
            context.move_to(*x, *y);
        } else {
            context.line_to(*x, *y);
        }
    }
    context.close_path();
}

fn rounded_rectangle(context: &Context, x: f64, y: f64, width: f64, height: f64, radius: f64) {
    use std::f64::consts::{FRAC_PI_2, PI};
    context.new_sub_path();
    context.arc(x + width - radius, y + radius, radius, -FRAC_PI_2, 0.0);
    context.arc(x + width - radius, y + height - radius, radius, 0.0, FRAC_PI_2);
    context.arc(x + radius, y + height - radius, radius, FRAC_PI_2, PI);
    context.arc(x + radius, y + radius, radius, PI, PI + FRAC_PI_2);
    context.close_path();
}

fn set_color(context: &Context, hex: &str) {
    let (red, green, blue) = rgb(hex);
    context.set_source_rgb(red, green, blue);
}

/// The HDMI port outline in bone, at `opacity` when given.
fn stroke_port(context: &Context, brand: &Brand, opacity: Option<f64>) -> Result<(), gtk::cairo::Error> {
    let draw = |context: &Context| {
        polygon(context, &brand.geometry.port);
        set_color(context, &brand.palette.bone);
        context.set_line_width(brand.geometry.port_stroke_width);
        context.set_line_join(gtk::cairo::LineJoin::Round);
        context.stroke()
    };
    match opacity {
        Some(opacity) => layer(context, opacity, draw),
        None => draw(context),
    }
}

/// The seven SMPTE bars, 64 units wide before `scale_x`, starting at `x`.
fn draw_bars(context: &Context, brand: &Brand, x: f64, scale_x: f64) -> Result<(), gtk::cairo::Error> {
    for (start, color) in brand.geometry.bar_starts.iter().zip(&brand.bars) {
        context.rectangle(x + start * scale_x, 0.0, brand.geometry.bar_width * scale_x, 64.0);
        set_color(context, color);
        context.fill()?;
    }
    Ok(())
}

/// The connector with its bars and amber prongs.
fn draw_connector(context: &Context, brand: &Brand, prong_opacity: f64) -> Result<(), gtk::cairo::Error> {
    let geometry = &brand.geometry;
    context.save()?;
    polygon(context, &geometry.connector_window);
    context.clip();
    draw_bars(context, brand, geometry.connector_bars.x, geometry.connector_bars.scale_x)?;
    context.restore()?;

    polygon(context, &geometry.connector);
    set_color(context, &brand.palette.bone);
    context.set_line_width(geometry.port_stroke_width);
    context.set_line_join(gtk::cairo::LineJoin::Round);
    context.stroke()?;

    layer(context, prong_opacity, |context| {
        for [x, y, width, height] in &geometry.prongs {
            rounded_rectangle(context, *x, *y, *width, *height, geometry.prong_corner_radius);
            set_color(context, &brand.palette.signal);
            context.fill()?;
        }
        Ok(())
    })
}

/// The play triangle filled with bars (scrolled by `bars_offset` in the live loop) and its outline.
fn draw_triangle(context: &Context, brand: &Brand, bars_offset: Option<f64>) -> Result<(), gtk::cairo::Error> {
    let geometry = &brand.geometry;
    let bars = &geometry.triangle_bars;
    context.save()?;
    polygon(context, &geometry.triangle);
    context.clip();
    match bars_offset {
        Some(offset) => {
            draw_bars(context, brand, bars.live_second_copy_x + offset, bars.scale_x)?;
            draw_bars(context, brand, bars.x + offset, bars.scale_x)?;
        }
        None => draw_bars(context, brand, bars.x, bars.scale_x)?,
    }
    context.restore()?;

    polygon(context, &geometry.triangle);
    set_color(context, &brand.palette.bone);
    context.set_line_width(geometry.triangle_stroke_width);
    context.set_line_join(gtk::cairo::LineJoin::Round);
    context.stroke()
}
