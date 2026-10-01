import PlugviewCore

/// Requests small Core Audio buffers (`Tuning.audioBufferFrames`) on the card input and the playback device.
///
/// Failure only costs latency, so it is reported, not thrown.
func requestLowLatencyAudioBuffers(inputUID: String) {
    do {
        try CoreAudioDevice.requestIOBufferFrames(Tuning.audioBufferFrames, on: CoreAudioDevice.deviceID(forUID: inputUID))
        try CoreAudioDevice.requestIOBufferFrames(Tuning.audioBufferFrames, on: CoreAudioDevice.defaultOutputDeviceID())
    } catch {
        printError("could not shorten audio buffers, sound may lag slightly: \(error)")
    }
}
