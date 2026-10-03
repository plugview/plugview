namespace Plugview.Core;

/// <summary>Stretches video range (16–235) to full range (0–255): the fix for a card that labels its range wrong.</summary>
public static class LimitedRangeExpansion
{
    /// <summary>Per-channel multiplier.</summary>
    public const double Scale = 255.0 / 219.0;

    /// <summary>Per-channel offset, applied after <see cref="Scale"/>.</summary>
    public const double Bias = -16.0 / 219.0;

    /// <summary>Where a channel value (0...1) lands after expansion, clamped to 0...1.</summary>
    public static double Expand(double value) => Math.Clamp((value * Scale) + Bias, 0, 1);
}

/// <summary>Screenshot naming.</summary>
public static class Screenshots
{
    /// <summary>The file name, e.g. <c>plugview 2026-10-01 at 19.05.12.png</c>, the same on every platform.</summary>
    public static string FileName(int year, int month, int day, int hour, int minute, int second) =>
        Numbers.Invariant($"plugview {year}-{month:00}-{day:00} at {hour:00}.{minute:00}.{second:00}.png");
}
