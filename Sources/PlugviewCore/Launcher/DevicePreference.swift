/// Puts the device the user picked last in front, keeping the rest in their order.
public func preferredFirst(_ devices: [DeviceInfo], preferredID: String?) -> [DeviceInfo] {
    guard let preferredID, let preferred = devices.first(where: { $0.uniqueID == preferredID }) else { return devices }
    return [preferred] + devices.filter { $0.uniqueID != preferredID }
}

/// Whether the launcher plays on its own: one device, or the one the user picked before.
///
/// Several devices and no earlier pick: the launcher waits for the user to choose.
public func shouldAutoPlay(_ devices: [DeviceInfo], preferredID: String?) -> Bool {
    devices.count == 1 || devices.contains { $0.uniqueID == preferredID }
}
