import Foundation

public enum DeviceTransport: Sendable {
    case builtIn
    case usb
    case virtual
    /// Continuity Camera: an iPhone used as a webcam.
    case wireless
    /// Thunderbolt, PCIe, and anything else wired.
    case other
}

/// Framework-free description of a capture device, so selection rules stay testable.
public struct DeviceInfo: Equatable, Sendable {
    public let uniqueID: String
    public let name: String
    public let modelID: String
    public let transport: DeviceTransport

    public init(uniqueID: String, name: String, modelID: String, transport: DeviceTransport) {
        self.uniqueID = uniqueID
        self.name = name
        self.modelID = modelID
        self.transport = transport
    }

    func matches(_ query: String) -> Bool {
        uniqueID == query || name.localizedCaseInsensitiveContains(query)
    }
}

/// Picks the video device to show.
///
/// Without a query only wired hardware qualifies,
/// USB first: virtual cameras (streaming tools) and a nearby iPhone are never auto-picked.
public func selectVideoDevice(from candidates: [DeviceInfo], matching query: String?) -> DeviceInfo? {
    if let query { return candidates.first { $0.matches(query) } }
    return candidates.first { $0.transport == .usb } ?? candidates.first { $0.transport == .other }
}

/// Picks the audio input that belongs to the chosen video device.
///
/// Capture cards expose video and audio as two devices; they usually share a name
/// or model ID, and otherwise the card is the only USB audio input present.
public func selectAudioDevice(from candidates: [DeviceInfo], for video: DeviceInfo, matching query: String?) -> DeviceInfo? {
    if let query { return candidates.first { $0.matches(query) } }
    if let sameName = candidates.first(where: { $0.name == video.name }) { return sameName }
    if !video.modelID.isEmpty, let sameModel = candidates.first(where: { $0.modelID == video.modelID }) { return sameModel }

    let usbCandidates = candidates.filter { $0.transport == .usb }
    if video.transport == .usb, usbCandidates.count == 1 { return usbCandidates[0] }
    return nil
}
