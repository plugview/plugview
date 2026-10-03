namespace Plugview.Core.Measurement;

/// <summary>Facts gathered by <c>plugview --measure</c>, reduced to what the advice depends on.</summary>
/// <param name="RequestedFrameRate">The mode's frame rate.</param>
/// <param name="DeliveredFrameRate">Derived from the median interval between delivered frames.</param>
/// <param name="DisplayRefreshRate">The display's refresh rate, when known.</param>
/// <param name="OutputLatencyMilliseconds">What the playback device reports, when known.</param>
/// <param name="IsOutputBluetooth">Whether sound plays over Bluetooth.</param>
public sealed record LatencyFacts(
    double RequestedFrameRate, double DeliveredFrameRate, double? DisplayRefreshRate, double? OutputLatencyMilliseconds, bool IsOutputBluetooth);

/// <summary>A latency problem <c>--measure</c> found, with the rounded numbers its advice shows.</summary>
public abstract record LatencyIssue
{
    private LatencyIssue() { }

    /// <summary>The card delivers fewer frames than the mode promises.</summary>
    public sealed record LowFrameRate(long Delivered, long Requested) : LatencyIssue;

    /// <summary>Sound plays over Bluetooth, which adds 100–300 ms whatever the device reports.</summary>
    public sealed record BluetoothOutput : LatencyIssue;

    /// <summary>A wired playback device that reports more than <see cref="Tuning.SlowOutputLatencyMilliseconds"/>.</summary>
    public sealed record SlowOutput(long Milliseconds) : LatencyIssue;

    /// <summary>The refresh rate is not a whole multiple of the frame rate, so frames show for uneven times.</summary>
    public sealed record UnevenRefresh(long RefreshRate, long FrameRate) : LatencyIssue;
}

/// <summary>What <c>--measure</c> flags and how it words it on Windows.</summary>
public static class LatencyAdvice
{
    /// <summary>The latency problems the facts reveal, most important first.</summary>
    public static IReadOnlyList<LatencyIssue> Issues(LatencyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var issues = new List<LatencyIssue>();
        if (facts.DeliveredFrameRate < facts.RequestedFrameRate * 0.9)
        {
            issues.Add(new LatencyIssue.LowFrameRate(Numbers.Rounded(facts.DeliveredFrameRate), Numbers.Rounded(facts.RequestedFrameRate)));
        }
        if (facts.IsOutputBluetooth)
        {
            issues.Add(new LatencyIssue.BluetoothOutput());
        }
        else if (facts.OutputLatencyMilliseconds is { } latency && latency > Tuning.SlowOutputLatencyMilliseconds)
        {
            issues.Add(new LatencyIssue.SlowOutput(Numbers.Rounded(latency)));
        }
        if (facts.DisplayRefreshRate is { } refreshRate && facts.DeliveredFrameRate > 0 && !IsWholeMultiple(refreshRate, facts.DeliveredFrameRate))
        {
            issues.Add(new LatencyIssue.UnevenRefresh(Numbers.Rounded(refreshRate), Numbers.Rounded(facts.DeliveredFrameRate)));
        }
        return issues;
    }

    /// <summary>Plain-language fixes for the issues, worded for a Windows PC.</summary>
    public static IReadOnlyList<string> Advice(LatencyFacts facts) => [.. Issues(facts).Select(Text)];

    private static string Text(LatencyIssue issue) => issue switch
    {
        LatencyIssue.LowFrameRate low => Numbers.Invariant(
            $"The card delivers {low.Delivered} fps instead of {low.Requested}. Plug it straight into a USB 3 port (no hub) and check the console's video output setting."),
        LatencyIssue.BluetoothOutput => "Sound plays over Bluetooth, which adds 100–300 ms. Use the PC speakers or wired headphones for games.",
        LatencyIssue.SlowOutput slow => Numbers.Invariant($"The playback device adds {slow.Milliseconds} ms. Wired headphones or the PC speakers are faster."),
        LatencyIssue.UnevenRefresh uneven => Numbers.Invariant(
            $"The display runs at {uneven.RefreshRate} Hz, not a multiple of {uneven.FrameRate} fps, so frames are shown for uneven times. A refresh rate of 60 or 120 Hz gives even motion."),
        _ => throw new ArgumentOutOfRangeException(nameof(issue), issue, "Unhandled latency issue"),
    };

    private static bool IsWholeMultiple(double value, double baseValue)
    {
        var ratio = value / baseValue;
        return ratio >= 0.98 && Math.Abs(ratio - Math.Round(ratio, MidpointRounding.AwayFromZero)) < 0.02;
    }

    /// <summary>Nearest-rank percentile of <paramref name="samples"/>, with <paramref name="fraction"/> clamped to 0...1; <c>null</c> for no samples.</summary>
    public static double? Percentile(IEnumerable<double> samples, double fraction)
    {
        var sorted = samples.Order().ToList();
        if (sorted.Count == 0) return null;
        var rank = (int)Numbers.Rounded((sorted.Count - 1) * Math.Clamp(fraction, 0, 1));
        return sorted[rank];
    }
}
