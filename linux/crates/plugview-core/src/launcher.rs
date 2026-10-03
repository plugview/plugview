use crate::selection::DeviceInfo;

/// A permission plugview needs from the system.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PermissionKind {
    /// Shows the card's video.
    Camera,
    /// Plays the console's sound (never the computer's own microphone).
    Microphone,
}

/// Why a device that was found could not be played.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CaptureFailure {
    pub device_name: String,
    /// What went wrong, in words the user can act on.
    pub reason: String,
}

/// What the launcher window shows. Every screen matches on it exhaustively.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum LauncherState {
    PermissionNeeded(PermissionKind),
    PermissionDenied(PermissionKind),
    WaitingForSignal,
    /// Playable devices are connected, best first, and none is playing yet.
    Ready(Vec<DeviceInfo>),
    Playing(DeviceInfo),
    /// Starting a device failed; the next device change tries again.
    Failed(CaptureFailure),
}

/// Something that happened, reported by the app and the device watcher.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum LauncherEvent {
    PermissionRequested(PermissionKind),
    PermissionDenied(PermissionKind),
    PermissionsGranted,
    /// The playable video devices right now, best first.
    DevicesChanged(Vec<DeviceInfo>),
    PlaybackStarted(DeviceInfo),
    PlaybackFailed(CaptureFailure),
    /// The playing device stopped; a `DevicesChanged` follows.
    PlaybackStopped,
}

/// The state that follows `state` once `event` happens.
#[must_use]
pub fn next_launcher_state(state: &LauncherState, event: LauncherEvent) -> LauncherState {
    match event {
        LauncherEvent::PermissionRequested(kind) => LauncherState::PermissionNeeded(kind),
        LauncherEvent::PermissionDenied(kind) => LauncherState::PermissionDenied(kind),
        LauncherEvent::PermissionsGranted | LauncherEvent::PlaybackStopped => LauncherState::WaitingForSignal,
        LauncherEvent::DevicesChanged(devices) => state_after_devices_changed(state, devices),
        LauncherEvent::PlaybackStarted(device) => LauncherState::Playing(device),
        LauncherEvent::PlaybackFailed(failure) => LauncherState::Failed(failure),
    }
}

fn state_after_devices_changed(state: &LauncherState, devices: Vec<DeviceInfo>) -> LauncherState {
    match state {
        LauncherState::PermissionNeeded(_) | LauncherState::PermissionDenied(_) => state.clone(),
        LauncherState::Playing(device) if devices.contains(device) => state.clone(),
        LauncherState::Playing(_) | LauncherState::WaitingForSignal | LauncherState::Ready(_) | LauncherState::Failed(_) => {
            if devices.is_empty() {
                LauncherState::WaitingForSignal
            } else {
                LauncherState::Ready(devices)
            }
        }
    }
}
