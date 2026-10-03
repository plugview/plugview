//! How Linux describes a capture device, read from sysfs (spec: "How a port describes devices", Linux column).

use std::fs;
use std::path::{Path, PathBuf};

use plugview_core::DeviceTransport;

/// A device node's place in the hardware: its transport and the physical device it belongs to.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Placement {
    pub transport: DeviceTransport,
    /// The sysfs path of the parent USB device, shared by a card's V4L2 node and its ALSA card; empty when not USB.
    pub model_id: String,
}

/// Where a V4L2 node (`video0`) or an ALSA card (`card1`) sits, from `/sys` (or a test tree standing in for it).
///
/// - virtual: under `/sys/devices/virtual`, or driven by `v4l2loopback`;
/// - USB: under a USB device (the directory with `idVendor`), built in when its port is `fixed` (laptop webcams);
/// - other: anything else wired, such as PCI capture cards.
#[must_use]
pub fn placement(sys_root: &Path, class: &str, node: &str) -> Placement {
    // Resolved like the node's path, so the comparisons below hold even when the root is reached through a link.
    let sys_root = fs::canonicalize(sys_root).unwrap_or_else(|_| sys_root.to_path_buf());
    let sys_root = sys_root.as_path();
    let class_link = sys_root.join("class").join(class).join(node);
    let Ok(node_path) = fs::canonicalize(&class_link) else {
        return Placement { transport: DeviceTransport::Other, model_id: String::new() };
    };
    let devices_virtual = sys_root.join("devices").join("virtual");
    if node_path.starts_with(&devices_virtual) || driver_name(&class_link).as_deref() == Some("v4l2loopback") {
        return Placement { transport: DeviceTransport::Virtual, model_id: String::new() };
    }
    match usb_parent(&node_path) {
        Some(usb_device) => {
            let is_fixed = fs::read_to_string(usb_device.join("removable")).is_ok_and(|removable| removable.trim() == "fixed");
            let model_id = usb_device.strip_prefix(sys_root).map_or_else(|_| usb_device.display().to_string(), |path| format!("/{}", path.display()));
            Placement { transport: if is_fixed { DeviceTransport::BuiltIn } else { DeviceTransport::Usb }, model_id }
        }
        None => Placement { transport: DeviceTransport::Other, model_id: String::new() },
    }
}

/// The nearest ancestor that is a USB device (it has `idVendor`), starting at `path` itself.
#[must_use]
pub fn usb_parent(path: &Path) -> Option<PathBuf> {
    path.ancestors().find(|ancestor| ancestor.join("idVendor").is_file()).map(Path::to_path_buf)
}

/// The USB vendor and product IDs of a USB device directory, e.g. `(0x534d, 0x2109)` for an MS2109.
#[must_use]
pub fn usb_ids(usb_device: &Path) -> Option<(u16, u16)> {
    let read = |name: &str| fs::read_to_string(usb_device.join(name)).ok().and_then(|text| u16::from_str_radix(text.trim(), 16).ok());
    Some((read("idVendor")?, read("idProduct")?))
}

fn driver_name(class_link: &Path) -> Option<String> {
    fs::canonicalize(class_link.join("device").join("driver")).ok()?.file_name()?.to_str().map(str::to_owned)
}

#[cfg(test)]
mod tests {
    #![allow(clippy::unwrap_used)]

    use std::os::unix::fs::symlink;

    use super::*;

    /// A fake `/sys` with a USB card (video0 + card1), a laptop webcam on a fixed port (video2), a loopback camera
    /// (video4) and a PCI card (video6).
    fn fake_sys() -> tempfile::TempDir {
        let root = tempfile::tempdir().unwrap();
        let sys = root.path();
        let usb_card = sys.join("devices/pci0000:00/0000:00:14.0/usb1/1-2");
        let webcam = sys.join("devices/pci0000:00/0000:00:14.0/usb1/1-5");
        let loopback = sys.join("devices/virtual/video4linux/video4");
        let pci_card = sys.join("devices/pci0000:00/0000:00:1c.0/0000:03:00.0");
        for (device, vendor, product, removable) in [(&usb_card, "534d", "2109", "removable"), (&webcam, "04f2", "b6dd", "fixed")] {
            fs::create_dir_all(device.join(format!("{}:1.0/video4linux", device.file_name().unwrap().to_str().unwrap()))).unwrap();
            fs::write(device.join("idVendor"), format!("{vendor}\n")).unwrap();
            fs::write(device.join("idProduct"), format!("{product}\n")).unwrap();
            fs::write(device.join("removable"), format!("{removable}\n")).unwrap();
        }
        fs::create_dir_all(usb_card.join("1-2:1.0/video4linux/video0")).unwrap();
        fs::create_dir_all(usb_card.join("1-2:1.2/sound/card1")).unwrap();
        fs::create_dir_all(webcam.join("1-5:1.0/video4linux/video2")).unwrap();
        fs::create_dir_all(&loopback).unwrap();
        fs::create_dir_all(pci_card.join("video4linux/video6")).unwrap();

        fs::create_dir_all(sys.join("class/video4linux")).unwrap();
        fs::create_dir_all(sys.join("class/sound")).unwrap();
        symlink(usb_card.join("1-2:1.0/video4linux/video0"), sys.join("class/video4linux/video0")).unwrap();
        symlink(webcam.join("1-5:1.0/video4linux/video2"), sys.join("class/video4linux/video2")).unwrap();
        symlink(&loopback, sys.join("class/video4linux/video4")).unwrap();
        symlink(pci_card.join("video4linux/video6"), sys.join("class/video4linux/video6")).unwrap();
        symlink(usb_card.join("1-2:1.2/sound/card1"), sys.join("class/sound/card1")).unwrap();
        root
    }

    #[test]
    fn a_usb_card_is_usb_and_its_sound_card_shares_its_model_id() {
        let sys = fake_sys();
        let video = placement(sys.path(), "video4linux", "video0");
        let sound = placement(sys.path(), "sound", "card1");
        assert_eq!(video.transport, DeviceTransport::Usb);
        assert_eq!(video.model_id, "/devices/pci0000:00/0000:00:14.0/usb1/1-2");
        assert_eq!(sound, video);
    }

    #[test]
    fn a_laptop_webcam_on_a_fixed_port_is_built_in() {
        assert_eq!(placement(fake_sys().path(), "video4linux", "video2").transport, DeviceTransport::BuiltIn);
    }

    #[test]
    fn a_loopback_camera_is_virtual() {
        assert_eq!(placement(fake_sys().path(), "video4linux", "video4").transport, DeviceTransport::Virtual);
    }

    #[test]
    fn a_pci_card_is_other_wired_hardware() {
        assert_eq!(placement(fake_sys().path(), "video4linux", "video6").transport, DeviceTransport::Other);
    }

    #[test]
    fn a_missing_node_is_other_with_no_model() {
        assert_eq!(placement(fake_sys().path(), "video4linux", "video9"), Placement { transport: DeviceTransport::Other, model_id: String::new() });
    }

    #[test]
    fn reads_the_usb_ids_of_the_card() {
        let sys = fake_sys();
        let usb = usb_parent(&fs::canonicalize(sys.path().join("class/video4linux/video0")).unwrap()).unwrap();
        assert_eq!(usb_ids(&usb), Some((0x534d, 0x2109)));
    }
}
