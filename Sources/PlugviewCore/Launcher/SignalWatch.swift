/// What the window does about the signal while a card is open.
public struct SignalWatch: Equatable, Sendable {
    /// Show "No signal" instead of a frozen last frame.
    public var showsNoSignal: Bool
    /// Close and reopen the card now (it often stops after a console mode change, a wake, or an HDMI replug).
    public var shouldReopen: Bool
    /// Also show the console checklist: reopening alone has not brought the picture back.
    public var showsSignalHelp: Bool

    public init(showsNoSignal: Bool, shouldReopen: Bool, showsSignalHelp: Bool) {
        self.showsNoSignal = showsNoSignal
        self.shouldReopen = shouldReopen
        self.showsSignalHelp = showsSignalHelp
    }
}

/// Decides, on each tick, what to do when frames stop while the card is still connected.
///
/// The first reopen is due when the signal is declared lost; later ones wait `Tuning.reopenBackoffSeconds` more each.
/// The caller counts its reopen attempts and resets them when a frame arrives.
public func signalWatch(secondsSinceLastFrame: Double, reopenAttempts: Int) -> SignalWatch {
    guard secondsSinceLastFrame >= Tuning.noSignalAfterSeconds else {
        return SignalWatch(showsNoSignal: false, shouldReopen: false, showsSignalHelp: false)
    }
    let backoff = Tuning.reopenBackoffSeconds
    let waited = (0..<max(0, reopenAttempts)).reduce(0.0) { total, attempt in total + backoff[min(attempt, backoff.count - 1)] }
    return SignalWatch(
        showsNoSignal: true,
        shouldReopen: secondsSinceLastFrame >= Tuning.noSignalAfterSeconds + waited,
        showsSignalHelp: reopenAttempts >= Tuning.signalHelpAfterAttempts)
}

/// A likely cause the window can point out while video plays.
public enum SignalHint: Equatable, Sendable {
    /// Frames arrive but are black: usually HDCP (PlayStation), or a console sending a mode the card cannot capture.
    case blackPicture
    /// The picture is fine but the sound is digital silence: Dolby/DTS or surround output, or a headset on the controller.
    case noGameSound
}

/// Whether a sampled frame counts as black, from its brightest luma (0–255).
public func isBlackFrame(maxLuma: Int) -> Bool {
    maxLuma <= Tuning.blackLumaMax
}

/// The hints to show, from how long the picture has been black and the audio silent (`nil` without card audio).
///
/// A black picture explains silence too, so only one hint shows at a time.
public func signalHints(blackPictureSeconds: Double, silentAudioSeconds: Double?) -> [SignalHint] {
    if blackPictureSeconds >= Tuning.blackPictureSeconds { return [.blackPicture] }
    if let silentAudioSeconds, silentAudioSeconds >= Tuning.silentAudioSeconds { return [.noGameSound] }
    return []
}
