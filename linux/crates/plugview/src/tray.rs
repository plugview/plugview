//! The tray icon (`StatusNotifierItem`): KDE, the Steam Deck's desktop, and GNOME with the `AppIndicator` extension show
//! it. Its menu mirrors the Mac's menu bar panel; without a tray, closing the window quits (spec: "Closing the
//! window").

use crate::ui::art::TrayTriangle;

/// What the tray asks the app to do; delivered on the GTK main thread.
#[cfg_attr(not(target_os = "linux"), allow(dead_code, reason = "only the Linux tray sends commands"))]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TrayCommand {
    Open,
    SetOpensOnPlugIn(bool),
    SetKeepsRunningInTray(bool),
    SetLaunchesAtLogin(bool),
    Quit,
}

/// What the tray shows.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrayState {
    pub status: String,
    pub triangle: TrayTriangle,
    pub opens_on_plug_in: bool,
    pub keeps_running_in_tray: bool,
    pub launches_at_login: bool,
}

/// Runs `deliver` for each command, on the GTK main thread.
pub type Deliver = fn(TrayCommand);

#[cfg(target_os = "linux")]
pub use linux::Tray;

#[cfg(not(target_os = "linux"))]
pub use other::Tray;

#[cfg(target_os = "linux")]
mod linux {
    use ksni::blocking::{Handle, TrayMethods};
    use ksni::menu::{CheckmarkItem, StandardItem};
    use ksni::{Icon, MenuItem};

    use super::{Deliver, TrayCommand, TrayState};
    use crate::ui::art::tray_icon_argb;

    struct Item {
        state: TrayState,
        deliver: Deliver,
    }

    impl Item {
        fn send(&self, command: TrayCommand) {
            let deliver = self.deliver;
            glib::MainContext::default().invoke(move || deliver(command));
        }
    }

    impl ksni::Tray for Item {
        fn id(&self) -> String {
            "app.plugview.plugview".into()
        }

        fn title(&self) -> String {
            "plugview".into()
        }

        fn icon_pixmap(&self) -> Vec<Icon> {
            [22, 32, 48].into_iter().map(|size| Icon { width: size, height: size, data: tray_icon_argb(size, self.state.triangle) }).collect()
        }

        fn tool_tip(&self) -> ksni::ToolTip {
            ksni::ToolTip { title: "plugview".into(), description: self.state.status.clone(), ..Default::default() }
        }

        fn activate(&mut self, _x: i32, _y: i32) {
            self.send(TrayCommand::Open);
        }

        fn menu(&self) -> Vec<MenuItem<Self>> {
            let state = &self.state;
            vec![
                StandardItem { label: state.status.clone(), enabled: false, ..Default::default() }.into(),
                StandardItem { label: "Open plugview".into(), activate: Box::new(|item: &mut Self| item.send(TrayCommand::Open)), ..Default::default() }.into(),
                MenuItem::Separator,
                checkmark("Open when a card is plugged in", state.opens_on_plug_in, TrayCommand::SetOpensOnPlugIn),
                checkmark("Stay in the tray when closed", state.keeps_running_in_tray, TrayCommand::SetKeepsRunningInTray),
                checkmark("Launch at login", state.launches_at_login, TrayCommand::SetLaunchesAtLogin),
                MenuItem::Separator,
                StandardItem { label: "Quit plugview".into(), activate: Box::new(|item: &mut Self| item.send(TrayCommand::Quit)), ..Default::default() }.into(),
            ]
        }
    }

    fn checkmark(label: &str, checked: bool, command: fn(bool) -> TrayCommand) -> MenuItem<Item> {
        CheckmarkItem { label: label.into(), checked, activate: Box::new(move |item: &mut Item| item.send(command(!checked))), ..Default::default() }.into()
    }

    /// A shown tray icon.
    pub struct Tray {
        handle: Handle<Item>,
    }

    impl Tray {
        /// Shows the icon; `None` when the desktop has no tray (GNOME without the `AppIndicator` extension).
        #[must_use]
        pub fn start(state: TrayState, deliver: Deliver) -> Option<Self> {
            // Flatpak cannot own the item's well-known name; hosts accept the unique name instead.
            let is_sandboxed = std::env::var_os("FLATPAK_ID").is_some();
            match (Item { state, deliver }).disable_dbus_name(is_sandboxed).spawn() {
                Ok(handle) => Some(Self { handle }),
                Err(error) => {
                    eprintln!("plugview: no tray icon ({error}); closing the window quits");
                    None
                }
            }
        }

        pub fn update(&self, state: TrayState) {
            let _ = self.handle.update(move |item: &mut Item| item.state = state);
        }
    }

    impl Drop for Tray {
        fn drop(&mut self) {
            self.handle.shutdown().wait();
        }
    }
}

/// Other systems (the macOS dev build) have no `StatusNotifierItem` host.
#[cfg(not(target_os = "linux"))]
mod other {
    use super::{Deliver, TrayState};

    pub struct Tray;

    impl Tray {
        #[must_use]
        pub fn start(_state: TrayState, _deliver: Deliver) -> Option<Self> {
            None
        }

        #[allow(clippy::unused_self, reason = "same signature as the Linux tray")]
        pub fn update(&self, _state: TrayState) {}
    }
}
