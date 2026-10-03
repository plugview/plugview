//! The signal watch (`spec/README.md`, "Signal watch and picture help"), as the Mac's `SignalMonitor`.
//!
//! Twice a second it reads when the last frame arrived and how bright a sampled frame was (`FrameProbe`), and the
//! card audio's level meter. Frames that stop while the card is still connected show "No signal" and reopen the card
//! in place, with the shared backoff; a black picture or silence since the start shows the console checklist once.

use std::rc::Rc;
use std::time::Duration;

use adw::prelude::*;
use plugview_core::{LauncherState, SignalHint, is_black_frame, signal_hints, signal_watch};

use super::App;
use crate::ui::notice::{self, SignalNotice};

const TICK: Duration = Duration::from_millis(500);

#[derive(Default)]
#[allow(clippy::struct_excessive_bools, reason = "the Mac's SignalMonitor flags, one per spec rule")]
pub(super) struct SignalState {
    timer: Option<glib::SourceId>,
    device_name: String,
    has_audio: bool,
    /// The next start is the watch's own reopen: it keeps the tries.
    is_reopening: bool,
    reopen_attempts: u32,
    /// `glib::monotonic_time()` of the newest frame seen, or of the start before the first one.
    last_frame_us: i64,
    started_at_us: i64,
    /// A frame arrived since the start: a pipeline error is then a lost signal, not a card that cannot start.
    has_had_frame: bool,
    has_read_luma: bool,
    has_seen_picture: bool,
    pub(super) has_heard_sound: bool,
    shown_hints: Vec<SignalHint>,
    notice: Option<SignalNotice>,
}

impl App {
    /// Call after every successful start. A start the watch asked for keeps its tries; any other starts afresh.
    pub(super) fn did_start_signal_watch(self: &Rc<Self>, device_name: &str, has_audio: bool) {
        let is_reopening = std::mem::take(&mut self.signal.borrow_mut().is_reopening);
        if is_reopening {
            self.signal.borrow_mut().has_audio = has_audio;
        } else {
            let now = glib::monotonic_time();
            let mut signal = self.signal.borrow_mut();
            *signal = SignalState {
                timer: signal.timer.take(),
                // Kept so that `set_notice` below sees it and takes it off the screen.
                notice: signal.notice.take(),
                device_name: device_name.to_owned(),
                has_audio,
                last_frame_us: now,
                started_at_us: now,
                ..SignalState::default()
            };
            drop(signal);
            self.set_notice(None);
        }
        if self.signal.borrow().timer.is_some() {
            return;
        }
        let weak = Rc::downgrade(self);
        let timer = glib::timeout_add_local(TICK, move || {
            let Some(app) = weak.upgrade() else { return glib::ControlFlow::Break };
            if app.tick_signal_watch() {
                glib::ControlFlow::Continue
            } else {
                app.signal.borrow_mut().timer = None;
                app.set_notice(None);
                glib::ControlFlow::Break
            }
        });
        self.signal.borrow_mut().timer = Some(timer);
    }

    /// Call when nothing plays any more (unplugged, window closed, failed).
    pub(super) fn stop_signal_watch(self: &Rc<Self>) {
        let timer = {
            let mut signal = self.signal.borrow_mut();
            signal.is_reopening = false;
            signal.timer.take()
        };
        if let Some(timer) = timer {
            timer.remove();
        }
        self.set_notice(None);
    }

    /// Whether a pipeline error after the first frame should be handled as a lost signal (reopen in place).
    pub(super) fn has_had_frame(&self) -> bool {
        self.signal.borrow().has_had_frame
    }

    /// One look at the card; false once nothing plays, which ends the watch.
    fn tick_signal_watch(self: &Rc<Self>) -> bool {
        if !self.is_playing() {
            return false;
        }
        let now = glib::monotonic_time();
        let (last_frame, luma) = {
            let state = self.state.borrow();
            state.playback.as_ref().map_or((None, None), |playback| {
                playback.frames.request_sample();
                (playback.frames.last_frame_us(), playback.frames.take_sampled_luma())
            })
        };
        if let Some(last_frame) = last_frame.filter(|time| *time > self.signal.borrow().last_frame_us) {
            self.note_frame(last_frame, luma);
        }

        let (since_frame, attempts, device_name) = {
            let signal = self.signal.borrow();
            (seconds_between(signal.last_frame_us, now), signal.reopen_attempts, signal.device_name.clone())
        };
        let watch = signal_watch(since_frame, attempts);
        if watch.shows_no_signal {
            self.set_notice(Some(SignalNotice::NoSignal { device_name, shows_help: watch.shows_signal_help }));
            if watch.should_reopen {
                {
                    let mut signal = self.signal.borrow_mut();
                    signal.reopen_attempts += 1;
                    signal.is_reopening = true;
                }
                self.reopen();
            }
            return true;
        }

        let hints = {
            let signal = self.signal.borrow();
            let since_start = seconds_between(signal.started_at_us, now);
            let black_seconds = if signal.has_read_luma && !signal.has_seen_picture { since_start } else { 0.0 };
            let silent_seconds = (signal.has_audio && !signal.has_heard_sound).then_some(since_start);
            signal_hints(black_seconds, silent_seconds).into_iter().filter(|hint| !signal.shown_hints.contains(hint)).collect::<Vec<_>>()
        };
        for hint in hints {
            self.signal.borrow_mut().shown_hints.push(hint);
            self.show_hint(hint);
        }
        true
    }

    fn note_frame(self: &Rc<Self>, time_us: i64, luma: Option<u8>) {
        let clears_notice = {
            let mut signal = self.signal.borrow_mut();
            signal.last_frame_us = time_us;
            signal.has_had_frame = true;
            signal.reopen_attempts = 0;
            let mut clears = matches!(signal.notice, Some(SignalNotice::NoSignal { .. }));
            if let Some(luma) = luma {
                signal.has_read_luma = true;
                if !is_black_frame(luma) {
                    signal.has_seen_picture = true;
                    clears |= signal.notice == Some(SignalNotice::BlackPicture);
                }
            }
            clears
        };
        if clears_notice {
            self.set_notice(None);
        }
    }

    /// Closes and reopens the playing card in place. A failed start keeps "No signal" up; the next backoff tries
    /// again, and a card that went away is handled by the device monitor.
    fn reopen(self: &Rc<Self>) {
        let device = {
            let state = self.state.borrow();
            let LauncherState::Playing(info) = &state.launcher else { return };
            state.devices.iter().find(|device| &device.info == info).cloned()
        };
        let Some(device) = device else { return };
        println!("No frames from {}. Reconnecting…", device.info.name);
        self.state.borrow_mut().playback = None;
        match self.start_playback(&device) {
            Ok(playback) => {
                let has_audio = playback.audio.is_some();
                self.state.borrow_mut().playback = Some(playback);
                self.did_start_signal_watch(&device.info.name, has_audio);
            }
            Err(reason) => eprintln!("plugview: {}: {reason}", device.info.name),
        }
    }

    fn show_hint(self: &Rc<Self>, hint: SignalHint) {
        match hint {
            SignalHint::BlackPicture => self.set_notice(Some(SignalNotice::BlackPicture)),
            SignalHint::NoGameSound => {
                // The picture is fine, so a toast rather than a panel over it; the panel is one click away.
                let weak = Rc::downgrade(self);
                let help: Rc<dyn Fn()> = Rc::new(move || {
                    if let Some(app) = weak.upgrade() {
                        app.set_notice(Some(SignalNotice::NoGameSound));
                    }
                });
                self.widgets.toasts.show("audio-volume-muted-symbolic", true, "No game sound", Some(("Help", help)));
            }
        }
    }

    pub(super) fn set_notice(self: &Rc<Self>, notice: Option<SignalNotice>) {
        if self.signal.borrow().notice == notice {
            return;
        }
        let revealer = &self.widgets.notice;
        if let Some(notice) = &notice {
            let weak = Rc::downgrade(self);
            let close: Rc<dyn Fn()> = Rc::new(move || {
                if let Some(app) = weak.upgrade() {
                    app.set_notice(None);
                }
            });
            revealer.set_child(Some(&notice::widget(notice, close)));
        }
        revealer.set_reveal_child(notice.is_some());
        revealer.set_can_target(notice.is_some());
        self.signal.borrow_mut().notice = notice;
    }
}

fn seconds_between(start_us: i64, end_us: i64) -> f64 {
    (end_us - start_us) as f64 / 1e6
}
