namespace Plugview.Core;

/// <summary>Tuning values shared with every platform (<c>spec/fixtures/tuning.json</c>).</summary>
public static class Tuning
{
    /// <summary>The released version; the same number on every platform (the Mac's <c>Constants.swift</c> is the source).</summary>
    public const string PlugviewVersion = "0.3.1";

    /// <summary>Frame rate at which motion counts as smooth for gaming.</summary>
    public const double SmoothFrameRate = 50;

    /// <summary>Longest wait for a card's audio input after its video appears, so playback starts once, with sound.</summary>
    public const double CardAudioWaitSeconds = 1;

    /// <summary>Volume change per arrow-key press, in percent.</summary>
    public const int VolumeStepPercent = 10;

    /// <summary>Seconds without mouse movement before the controls fade out over the video.</summary>
    public const double ControlsHideSeconds = 2.5;

    /// <summary>Seconds between automatic update checks (one a day).</summary>
    public const double UpdateCheckIntervalSeconds = 24 * 60 * 60;

    /// <summary>How long <c>plugview --measure</c> samples the capture pipeline.</summary>
    public const double MeasurementSeconds = 5;

    /// <summary>Playback-device latency above which <c>--measure</c> warns.</summary>
    public const double SlowOutputLatencyMilliseconds = 40;
}
