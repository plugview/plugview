//! App launch only: the tray icon, closing to the tray, opening when a card is plugged in, launch at login.
//! A terminal launch is the command and quits when its window closes, as on the Mac.

use std::cell::RefCell;
use std::rc::{Rc, Weak};

use adw::prelude::*;
use plugview_core::{DeviceInfo, LauncherEvent, LauncherState};

use super::{App, LaunchMode};
use crate::autostart;
use crate::tray::{Tray, TrayCommand, TrayState};
use crate::ui::art::TrayTriangle;

thread_local! {
    /// The running app, for tray commands (they arrive on the main thread through `MainContext::invoke`) and for a
    /// second desktop launch, which activates this instance.
    static CURRENT: RefCell<Weak<App>> = const { RefCell::new(Weak::new()) };
}

/// The app this process already runs, if any.
pub(super) fn current_app() -> Option<Rc<App>> {
    CURRENT.with(|current| current.borrow().upgrade())
}

fn deliver(command: TrayCommand) {
    if let Some(app) = current_app() {
        app.handle_tray(command);
    }
}

impl App {
    pub(super) fn is_app_launch(&self) -> bool {
        matches!(self.state.borrow().launch_mode, LaunchMode::App { .. })
    }

    /// Registers the app and, for an app launch, shows the tray icon.
    pub(super) fn start_background(self: &Rc<Self>) {
        CURRENT.with(|current| *current.borrow_mut() = Rc::downgrade(self));
        if self.is_app_launch() {
            let tray = Tray::start(self.tray_state(), deliver);
            self.state.borrow_mut().tray = tray;
        }
    }

    /// Whether closing the window keeps plugview in the tray instead of quitting.
    pub(super) fn closes_to_tray(&self) -> bool {
        let state = self.state.borrow();
        matches!(state.launch_mode, LaunchMode::App { .. }) && state.tray.is_some() && state.settings.keeps_running_in_tray
    }

    pub(super) fn is_window_open(&self) -> bool {
        self.widgets.window.is_visible()
    }

    /// Shows the window (from the tray, a second launch, or a plugged-in card) and plays a ready card.
    pub(super) fn open_window(self: &Rc<Self>) {
        if !self.is_window_open() {
            self.widgets.window.present();
            self.inhibit_idle();
            self.refresh_devices();
        }
        self.widgets.window.present();
    }

    /// Closing to the tray: the card is already released; the window hides and the launcher waits for a card.
    pub(super) fn hide_window(self: &Rc<Self>) {
        self.widgets.window.set_visible(false);
        self.uninhibit_idle();
        println!("Window closed; plugview stays in the tray.");
        self.send(LauncherEvent::PlaybackStopped);
        self.refresh_devices();
    }

    /// Opens the window when a card that was not there before shows up while it is closed.
    pub(super) fn open_on_plug_in(self: &Rc<Self>, previous: &[DeviceInfo], playable: &[DeviceInfo]) {
        let is_new_card = playable.iter().any(|device| !previous.contains(device));
        let opens = {
            let state = self.state.borrow();
            state.settings.opens_on_plug_in && matches!(state.launch_mode, LaunchMode::App { .. })
        };
        if is_new_card && opens && !self.is_window_open() {
            println!("Capture card plugged in; opening plugview.");
            self.open_window();
        }
    }

    /// Keeps the screen awake while the window is open (K32); a controller is not keyboard or mouse input.
    pub(super) fn inhibit_idle(&self) {
        let Some(application) = self.widgets.window.application() else { return };
        let mut state = self.state.borrow_mut();
        if state.idle_inhibit.is_none() {
            // 0: the session cannot inhibit (no session manager); nothing to undo later.
            let cookie = application.inhibit(Some(&self.widgets.window), gtk::ApplicationInhibitFlags::IDLE, Some("Playing a capture card"));
            state.idle_inhibit = (cookie > 0).then_some(cookie);
        }
    }

    fn uninhibit_idle(&self) {
        let cookie = self.state.borrow_mut().idle_inhibit.take();
        if let (Some(cookie), Some(application)) = (cookie, self.widgets.window.application()) {
            application.uninhibit(cookie);
        }
    }

    /// Mirrors the launcher state and the settings in the tray.
    pub(super) fn update_tray(&self) {
        let tray_state = self.tray_state();
        let state = self.state.borrow();
        if let Some(tray) = &state.tray {
            tray.update(tray_state);
        }
    }

    fn tray_state(&self) -> TrayState {
        let state = self.state.borrow();
        let (status, triangle) = match &state.launcher {
            LauncherState::Playing(device) => (format!("Playing {}", device.name), TrayTriangle::Filled),
            LauncherState::Ready(devices) if devices.len() == 1 => (format!("{} is ready", devices[0].name), TrayTriangle::Outlined),
            LauncherState::Ready(devices) => (format!("{} cards are ready", devices.len()), TrayTriangle::Outlined),
            LauncherState::Failed(failure) => (format!("Could not start {}", failure.device_name), TrayTriangle::None),
            LauncherState::PermissionDenied(_) => ("No access to the card".to_owned(), TrayTriangle::None),
            LauncherState::PermissionNeeded(_) | LauncherState::WaitingForSignal => ("Waiting for a capture card".to_owned(), TrayTriangle::None),
        };
        TrayState {
            status,
            triangle,
            opens_on_plug_in: state.settings.opens_on_plug_in,
            keeps_running_in_tray: state.settings.keeps_running_in_tray,
            launches_at_login: state.settings.launches_at_login,
        }
    }

    fn handle_tray(self: &Rc<Self>, command: TrayCommand) {
        match command {
            TrayCommand::Open => self.open_window(),
            TrayCommand::SetOpensOnPlugIn(is_on) => self.change_settings(|settings| settings.opens_on_plug_in = is_on),
            TrayCommand::SetKeepsRunningInTray(is_on) => self.change_settings(|settings| settings.keeps_running_in_tray = is_on),
            TrayCommand::SetLaunchesAtLogin(is_on) => match autostart::set_launches_at_login(is_on) {
                Ok(()) => self.change_settings(|settings| settings.launches_at_login = is_on),
                Err(reason) => eprintln!("plugview: launch at login: {reason}"),
            },
            TrayCommand::Quit => {
                if let Some(application) = self.widgets.window.application() {
                    application.quit();
                }
            }
        }
    }

    fn change_settings(&self, change: impl FnOnce(&mut crate::settings::Settings)) {
        {
            let mut state = self.state.borrow_mut();
            change(&mut state.settings);
            state.settings.save();
        }
        self.update_tray();
    }
}
