import PlugviewCore

/// Requests a small Core Audio buffer (`Tuning.audioBufferFrames`) on the playback device.
///
/// The card's own input keeps the macOS default: with a resized input buffer, unplugging the card while it plays
/// crashed inside Core Audio's teardown on macOS 27 (CMIOBaseUnits property listener on freed memory).
///
/// Failure only costs latency, so it is reported, not thrown.
func requestLowLatencyAudioBuffers() {
    do {
        try CoreAudioDevice.requestIOBufferFrames(Tuning.audioBufferFrames, on: CoreAudioDevice.defaultOutputDeviceID())
    } catch {
        printError("could not shorten audio buffers, sound may lag slightly: \(error)")
    }
}
