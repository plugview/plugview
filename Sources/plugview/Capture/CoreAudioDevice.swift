import CoreAudio

/// Core Audio failure with the operation that failed and its OSStatus.
struct CoreAudioError: Error, CustomStringConvertible {
    let operation: String
    let status: OSStatus

    var description: String { "\(operation) failed (OSStatus \(status))" }
}

/// The few Core Audio knobs AVFoundation does not expose: I/O buffer size and reported latency.
enum CoreAudioDevice {
    static func deviceID(forUID uid: String) throws(CoreAudioError) -> AudioObjectID {
        var address = address(kAudioHardwarePropertyTranslateUIDToDevice)
        var qualifier: CFString = uid as CFString
        var deviceID = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        let status = withUnsafeMutablePointer(to: &qualifier) { qualifierPointer in
            AudioObjectGetPropertyData(
                AudioObjectID(kAudioObjectSystemObject), &address, UInt32(MemoryLayout<CFString>.size), qualifierPointer, &size, &deviceID)
        }
        guard status == noErr, deviceID != kAudioObjectUnknown else { throw CoreAudioError(operation: "finding audio device \(uid)", status: status) }
        return deviceID
    }

    static func defaultOutputDeviceID() throws(CoreAudioError) -> AudioObjectID {
        try read(kAudioHardwarePropertyDefaultOutputDevice, of: AudioObjectID(kAudioObjectSystemObject), initial: AudioObjectID(kAudioObjectUnknown))
    }

    /// Requests a smaller I/O buffer for this process only (clamped to what the device allows).
    ///
    /// Core Audio drops the request when plugview exits, so other apps are unaffected.
    @discardableResult
    static func requestIOBufferFrames(_ frames: UInt32, on device: AudioObjectID) throws(CoreAudioError) -> UInt32 {
        let range = try read(kAudioDevicePropertyBufferFrameSizeRange, of: device, initial: AudioValueRange())
        var clampedFrames = UInt32(min(range.mMaximum, max(range.mMinimum, Double(frames))))
        var address = address(kAudioDevicePropertyBufferFrameSize)
        let status = AudioObjectSetPropertyData(device, &address, 0, nil, UInt32(MemoryLayout<UInt32>.size), &clampedFrames)
        guard status == noErr else { throw CoreAudioError(operation: "setting the audio buffer size", status: status) }
        return clampedFrames
    }

    static func name(of device: AudioObjectID) throws(CoreAudioError) -> String {
        let name: Unmanaged<CFString>? = try read(kAudioObjectPropertyName, of: device, initial: nil)
        return name?.takeRetainedValue() as String? ?? "unknown device"
    }

    static func isBluetooth(_ device: AudioObjectID) throws(CoreAudioError) -> Bool {
        let transport = try read(kAudioDevicePropertyTransportType, of: device, initial: UInt32(0))
        return transport == kAudioDeviceTransportTypeBluetooth || transport == kAudioDeviceTransportTypeBluetoothLE
    }

    /// Delay the device itself reports between a sample being played and it being heard:
    /// device + first stream latency, safety offset, and one I/O buffer.
    static func outputLatencyMilliseconds(of device: AudioObjectID) throws(CoreAudioError) -> Double {
        let output = kAudioObjectPropertyScopeOutput
        let sampleRate = try read(kAudioDevicePropertyNominalSampleRate, of: device, initial: Float64(0))
        guard sampleRate > 0 else { throw CoreAudioError(operation: "reading the output sample rate", status: kAudioHardwareUnspecifiedError) }

        let streams = try readArray(kAudioDevicePropertyStreams, of: device, scope: output, element: AudioStreamID(0))
        let streamFrames = try streams.first.map { stream throws(CoreAudioError) in try read(kAudioStreamPropertyLatency, of: stream, initial: UInt32(0)) } ?? 0
        let frames = [
            try read(kAudioDevicePropertyLatency, of: device, scope: output, initial: UInt32(0)),
            try read(kAudioDevicePropertySafetyOffset, of: device, scope: output, initial: UInt32(0)),
            try read(kAudioDevicePropertyBufferFrameSize, of: device, initial: UInt32(0)),
            streamFrames,
        ]
        return Double(frames.reduce(0, +)) / sampleRate * 1000
    }

    // MARK: - Property access

    private static func address(
        _ selector: AudioObjectPropertySelector, scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal
    ) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }

    private static func read<Value>(
        _ selector: AudioObjectPropertySelector, of object: AudioObjectID, scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal,
        initial: Value
    ) throws(CoreAudioError) -> Value {
        var address = address(selector, scope: scope)
        var value = initial
        var size = UInt32(MemoryLayout<Value>.size)
        let status = withUnsafeMutablePointer(to: &value) { AudioObjectGetPropertyData(object, &address, 0, nil, &size, $0) }
        guard status == noErr else { throw CoreAudioError(operation: "reading audio property \(selector)", status: status) }
        return value
    }

    private static func readArray<Element>(
        _ selector: AudioObjectPropertySelector, of object: AudioObjectID, scope: AudioObjectPropertyScope, element: Element
    ) throws(CoreAudioError) -> [Element] {
        var address = address(selector, scope: scope)
        var size: UInt32 = 0
        var status = AudioObjectGetPropertyDataSize(object, &address, 0, nil, &size)
        guard status == noErr else { throw CoreAudioError(operation: "sizing audio property \(selector)", status: status) }

        var elements = [Element](repeating: element, count: Int(size) / MemoryLayout<Element>.stride)
        guard !elements.isEmpty else { return [] }
        status = elements.withUnsafeMutableBytes { buffer in
            guard let baseAddress = buffer.baseAddress else { return kAudioHardwareBadPropertySizeError }
            return AudioObjectGetPropertyData(object, &address, 0, nil, &size, baseAddress)
        }
        guard status == noErr else { throw CoreAudioError(operation: "reading audio property \(selector)", status: status) }
        return Array(elements.prefix(Int(size) / MemoryLayout<Element>.stride))
    }
}
