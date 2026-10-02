import AVFoundation
import PlugviewCore

/// Reads the capture devices macOS currently exposes.
public enum DeviceCatalog {
    public static func videoDevices() -> [DeviceInfo] {
        AVCaptureDevice.DiscoverySession(deviceTypes: [.external], mediaType: .video, position: .unspecified)
            .devices.map(makeDeviceInfo)
    }

    public static func audioDevices() -> [DeviceInfo] {
        AVCaptureDevice.DiscoverySession(deviceTypes: [.microphone, .external], mediaType: .audio, position: .unspecified)
            .devices.map(makeDeviceInfo)
    }

    /// The mode plugview would start a video device in (`choice`, or the best), without starting it.
    public static func mode(ofVideoDevice uniqueID: String, choice: ModeChoice? = nil) -> VideoMode? {
        let modes = videoModes(ofVideoDevice: uniqueID)
        return modeIndex(in: modes, choice: choice).map { modes[$0] }
    }

    /// Every format a video device offers; empty once it is unplugged.
    public static func videoModes(ofVideoDevice uniqueID: String) -> [VideoMode] {
        AVCaptureDevice(uniqueID: uniqueID)?.formats.map(VideoMode.init(format:)) ?? []
    }

    public static func printDevices() {
        printSection("Video devices", videoDevices())
        print("")
        printSection("Audio inputs", audioDevices())
    }

    private static func printSection(_ title: String, _ devices: [DeviceInfo]) {
        print("\(title):")
        if devices.isEmpty { print("  (none)") }
        for device in devices {
            print("  \(device.name)  [\(device.transport)]  id: \(device.uniqueID)")
        }
    }

    private static func makeDeviceInfo(_ device: AVCaptureDevice) -> DeviceInfo {
        let transport = device.isContinuityCamera ? .wireless : transport(fromFourCC: device.transportType)
        return DeviceInfo(uniqueID: device.uniqueID, name: device.localizedName, modelID: device.modelID, transport: transport)
    }

    /// Maps a Core Audio transport FourCC (the `transportType` property) to a transport.
    private static func transport(fromFourCC code: Int32) -> DeviceTransport {
        switch UInt32(bitPattern: code) {
        case kAudioDeviceTransportTypeUSB: .usb
        case kAudioDeviceTransportTypeBuiltIn: .builtIn
        case kAudioDeviceTransportTypeVirtual: .virtual
        default: .other
        }
    }
}
