namespace Plugview.Core.Launcher;

/// <summary>What the window does about the signal while a card is open.</summary>
/// <param name="ShowsNoSignal">Show "No signal" instead of a frozen last frame.</param>
/// <param name="ShouldReopen">Close and reopen the card now (it often stops after a console mode change or a wake).</param>
/// <param name="ShowsSignalHelp">Also show the console checklist: reopening alone has not brought the picture back.</param>
public readonly record struct SignalWatch(bool ShowsNoSignal, bool ShouldReopen, bool ShowsSignalHelp);

/// <summary>A likely cause the window can point out while video plays.</summary>
public enum SignalHint
{
    /// <summary>Frames arrive but are black: usually HDCP (PlayStation), or a mode the card cannot capture.</summary>
    BlackPicture,

    /// <summary>The picture is fine but the sound is digital silence: Dolby/DTS or surround output, or a headset on the controller.</summary>
    NoGameSound,
}

/// <summary>Watches a playing card's signal.</summary>
public static class SignalWatcher
{
    /// <summary>Decides, on each tick, what to do when frames stop while the card is still connected.</summary>
    /// <remarks>The caller counts its reopen attempts and resets them when a frame arrives.</remarks>
    public static SignalWatch Watch(double secondsSinceLastFrame, int reopenAttempts)
    {
        if (secondsSinceLastFrame < Tuning.NoSignalAfterSeconds) return new SignalWatch(false, false, false);
        var backoff = Tuning.ReopenBackoffSeconds;
        var waited = Enumerable.Range(0, Math.Max(0, reopenAttempts)).Sum(attempt => backoff[Math.Min(attempt, backoff.Count - 1)]);
        return new SignalWatch(
            ShowsNoSignal: true,
            ShouldReopen: secondsSinceLastFrame >= Tuning.NoSignalAfterSeconds + waited,
            ShowsSignalHelp: reopenAttempts >= Tuning.SignalHelpAfterAttempts);
    }

    /// <summary>Whether a sampled frame counts as black, from its brightest luma (0–255).</summary>
    public static bool IsBlackFrame(int maxLuma) => maxLuma <= Tuning.BlackLumaMax;

    /// <summary>The hints to show, from how long the picture has been black and the audio silent (<c>null</c> without card audio).</summary>
    public static IReadOnlyList<SignalHint> Hints(double blackPictureSeconds, double? silentAudioSeconds)
    {
        if (blackPictureSeconds >= Tuning.BlackPictureSeconds) return [SignalHint.BlackPicture];
        if (silentAudioSeconds >= Tuning.SilentAudioSeconds) return [SignalHint.NoGameSound];
        return [];
    }
}
