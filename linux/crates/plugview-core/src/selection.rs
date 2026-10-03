/// How a capture device is attached; decides whether plugview may pick it by itself (spec: device classification).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DeviceTransport {
    BuiltIn,
    Usb,
    Virtual,
    /// A phone or network camera.
    Wireless,
    /// A PCI Express card, and anything else wired.
    Other,
}

/// Framework-free description of a capture device, so selection rules stay testable.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DeviceInfo {
    pub unique_id: String,
    pub name: String,
    /// Shared by the video and audio halves of one card: on Linux, the sysfs path of the parent USB device.
    pub model_id: String,
    pub transport: DeviceTransport,
}

impl DeviceInfo {
    fn matches(&self, query: &str) -> bool {
        self.unique_id == query || self.name.to_lowercase().contains(&query.to_lowercase())
    }
}

/// The video devices plugview may show, best first: without a query only wired hardware, USB first.
#[must_use]
pub fn playable_video_devices(candidates: &[DeviceInfo], query: Option<&str>) -> Vec<DeviceInfo> {
    if let Some(query) = query {
        return candidates.iter().filter(|device| device.matches(query)).cloned().collect();
    }
    let with = |transport| candidates.iter().filter(move |device| device.transport == transport);
    with(DeviceTransport::Usb).chain(with(DeviceTransport::Other)).cloned().collect()
}

/// The video device to show: the best of `playable_video_devices`.
#[must_use]
pub fn select_video_device(candidates: &[DeviceInfo], query: Option<&str>) -> Option<DeviceInfo> {
    playable_video_devices(candidates, query).into_iter().next()
}

/// The audio input that belongs to `video`: same name, then same model ID, then the only USB input.
#[must_use]
pub fn select_audio_device(candidates: &[DeviceInfo], video: &DeviceInfo, query: Option<&str>) -> Option<DeviceInfo> {
    if let Some(query) = query {
        return candidates.iter().find(|device| device.matches(query)).cloned();
    }
    if let Some(same_name) = candidates.iter().find(|device| device.name == video.name) {
        return Some(same_name.clone());
    }
    if !video.model_id.is_empty() {
        if let Some(same_model) = candidates.iter().find(|device| device.model_id == video.model_id) {
            return Some(same_model.clone());
        }
    }
    let usb: Vec<&DeviceInfo> = candidates.iter().filter(|device| device.transport == DeviceTransport::Usb).collect();
    match usb.as_slice() {
        [only] if video.transport == DeviceTransport::Usb => Some((*only).clone()),
        _ => None,
    }
}

/// Puts the device the user picked last in front, keeping the rest in their order.
#[must_use]
pub fn preferred_first(devices: &[DeviceInfo], preferred_id: Option<&str>) -> Vec<DeviceInfo> {
    let Some(preferred) = preferred_id.and_then(|id| devices.iter().find(|device| device.unique_id == id)) else {
        return devices.to_vec();
    };
    std::iter::once(preferred).chain(devices.iter().filter(|device| device.unique_id != preferred.unique_id)).cloned().collect()
}

/// Whether the launcher plays on its own: one device, or the one the user picked before.
#[must_use]
pub fn should_auto_play(devices: &[DeviceInfo], preferred_id: Option<&str>) -> bool {
    devices.len() == 1 || devices.iter().any(|device| Some(device.unique_id.as_str()) == preferred_id)
}
