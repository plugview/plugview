namespace Plugview.Core.Audio;

/// <summary>What the playback side does with the card's audio this period.</summary>
public abstract record DriftAction
{
    private DriftAction() { }

    /// <summary>Play, reading <paramref name="InputFramesPerOutputFrame"/> card frames per output frame (1 ± a few hundred ppm).</summary>
    public sealed record Play(double InputFramesPerOutputFrame) : DriftAction;

    /// <summary>The buffer grew past its cap (a stall, a sleep): drop the oldest frames down to <paramref name="TargetFrames"/>.</summary>
    public sealed record DropTo(int TargetFrames) : DriftAction;

    /// <summary>The buffer ran dry: play silence until it holds <paramref name="TargetFrames"/> again.</summary>
    public sealed record Refill(int TargetFrames) : DriftAction;
}

/// <summary>
/// Keeps the buffer between the card's audio clock and the speakers' clock at a small, fixed delay.
/// </summary>
/// <remarks>
/// The two clocks differ by up to a few hundred ppm, so a fixed buffer slowly fills (sound falls behind the picture) or
/// drains (crackles); this is the classic "audio drifts after an hour" report. A PI controller on the smoothed fill
/// level nudges the resampling ratio, clamped to ±<see cref="MaxCorrectionPpm"/> ppm, far below what anyone can hear.
/// </remarks>
public sealed class AudioDriftController
{
    /// <summary>Largest rate correction, in parts per million. Card and output clocks are within ±300 ppm.</summary>
    public const double MaxCorrectionPpm = 1000;

    private const double ProportionalGainPerSecond = 0.02;
    private const double IntegralGainPerSecondSquared = 0.002;
    private const double SmoothingSeconds = 0.5;

    private readonly int targetFrames;
    private readonly int capFrames;
    private readonly double sampleRate;
    private double smoothedFillFrames;
    private double integralSeconds;
    private bool isPrimed;

    /// <summary>Creates a controller for one stream.</summary>
    /// <param name="sampleRate">Card frames per second.</param>
    /// <param name="targetMilliseconds">The delay to hold: enough for one capture packet plus one render period.</param>
    /// <param name="capMilliseconds">Above this, frames are dropped instead of slowly drained.</param>
    public AudioDriftController(int sampleRate, double targetMilliseconds, double capMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capMilliseconds, targetMilliseconds);
        this.sampleRate = sampleRate;
        targetFrames = (int)Math.Round(sampleRate * targetMilliseconds / 1000, MidpointRounding.AwayFromZero);
        capFrames = (int)Math.Round(sampleRate * capMilliseconds / 1000, MidpointRounding.AwayFromZero);
    }

    /// <summary>Decides this period's action from the frames buffered now and the time since the last call.</summary>
    public DriftAction Update(int bufferedFrames, double elapsedSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bufferedFrames);
        if (bufferedFrames > capFrames) return Restart(new DriftAction.DropTo(targetFrames));
        if (bufferedFrames == 0) return Restart(new DriftAction.Refill(targetFrames));
        if (!isPrimed)
        {
            smoothedFillFrames = bufferedFrames;
            isPrimed = true;
        }

        var weight = Math.Clamp(elapsedSeconds / SmoothingSeconds, 0, 1);
        smoothedFillFrames += (bufferedFrames - smoothedFillFrames) * weight;
        var errorSeconds = (smoothedFillFrames - targetFrames) / sampleRate;
        var maxCorrection = MaxCorrectionPpm / 1_000_000;
        // Anti-windup: the integral alone may never ask for more than the clamp allows.
        integralSeconds = Math.Clamp(integralSeconds + (errorSeconds * elapsedSeconds), -maxCorrection / IntegralGainPerSecondSquared, maxCorrection / IntegralGainPerSecondSquared);
        var correction = (ProportionalGainPerSecond * errorSeconds) + (IntegralGainPerSecondSquared * integralSeconds);
        return new DriftAction.Play(1 + Math.Clamp(correction, -maxCorrection, maxCorrection));
    }

    private DriftAction Restart(DriftAction action)
    {
        isPrimed = false;
        return action;
    }
}
