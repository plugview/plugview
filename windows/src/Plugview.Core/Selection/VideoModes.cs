namespace Plugview.Core.Selection;

/// <summary>How much work a frame needs before it can be shown; later values reach the screen sooner.</summary>
public enum FrameEncoding
{
    /// <summary>MJPEG, H.264 or HEVC: every frame needs a decode step.</summary>
    Compressed,

    /// <summary>Uncompressed, but not the card's wire layout, so every frame is converted.</summary>
    Converted,

    /// <summary>What the card sends natively (packed 4:2:2, YUY2, on USB capture cards).</summary>
    Native,
}

/// <summary>Framework-free description of one capture format.</summary>
public readonly record struct VideoMode(long Width, long Height, double MaxFrameRate, FrameEncoding Encoding)
{
    /// <summary>Human-readable summary, e.g. <c>1920×1080 @ 60 fps</c>.</summary>
    public string Summary => Numbers.Invariant($"{Width}×{Height} @ {Numbers.Rounded(MaxFrameRate)} fps");
}

/// <summary>A size and frame rate the user picked for a device, saved as text such as <c>1920x1080@60</c>.</summary>
public readonly record struct ModeChoice(long Width, long Height, long FrameRate)
{
    /// <summary>The choice that names <paramref name="mode"/>, with its frame rate rounded.</summary>
    public static ModeChoice From(VideoMode mode) => new(mode.Width, mode.Height, Numbers.Rounded(mode.MaxFrameRate));

    /// <summary>Reads a saved choice: <c>&lt;width&gt;x&lt;height&gt;@&lt;fps&gt;</c> with positive whole numbers, else <c>null</c>.</summary>
    public static ModeChoice? FromKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var sizeAndRate = key.Split('@');
        if (sizeAndRate.Length != 2) return null;
        var size = sizeAndRate[0].Split('x');
        if (size.Length != 2) return null;
        if (Numbers.WholeNumber(size[0]) is not { } width || Numbers.WholeNumber(size[1]) is not { } height) return null;
        if (Numbers.WholeNumber(sizeAndRate[1]) is not { } frameRate) return null;
        return width > 0 && height > 0 && frameRate > 0 ? new ModeChoice(width, height, frameRate) : null;
    }

    /// <summary>The saved form, e.g. <c>1920x1080@60</c>.</summary>
    public string Key => Numbers.Invariant($"{Width}x{Height}@{FrameRate}");

    /// <summary>Menu text, e.g. <c>1920×1080 · 60 fps</c>.</summary>
    public string Title => Numbers.Invariant($"{Width}×{Height} · {FrameRate} fps");
}

/// <summary>Which capture mode plays.</summary>
public static class VideoModes
{
    /// <summary>The index of the best mode for live play: smooth frame rate, then resolution, then frame rate, then the least work.</summary>
    /// <remarks>1080p60 beats 4K30. The first of equal modes wins, as in Swift.</remarks>
    public static int? BestModeIndex(IReadOnlyList<VideoMode> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        return FirstMaxIndex(Enumerable.Range(0, modes.Count), (a, b) => CompareForPlay(modes[a], modes[b]));
    }

    /// <summary>The modes worth offering in a menu: one per size and frame rate, largest and fastest first, then widest.</summary>
    public static IReadOnlyList<ModeChoice> PickableModes(IEnumerable<VideoMode> modes) =>
        [.. modes.Where(mode => mode.MaxFrameRate >= 1)
            .Select(ModeChoice.From)
            .Distinct()
            .OrderByDescending(choice => choice.Width * choice.Height)
            .ThenByDescending(choice => choice.FrameRate)
            .ThenByDescending(choice => choice.Width)];

    /// <summary>The mode to start: the user's choice when the device still offers it (cheapest encoding), otherwise the best.</summary>
    public static int? ModeIndex(IReadOnlyList<VideoMode> modes, ModeChoice? choice)
    {
        ArgumentNullException.ThrowIfNull(modes);
        if (choice is not { } chosen) return BestModeIndex(modes);
        var matching = Enumerable.Range(0, modes.Count).Where(index => ModeChoice.From(modes[index]) == chosen);
        return FirstMaxIndex(matching, (a, b) => modes[a].Encoding.CompareTo(modes[b].Encoding)) ?? BestModeIndex(modes);
    }

    private static int CompareForPlay(VideoMode a, VideoMode b)
    {
        static bool IsSmooth(VideoMode mode) => mode.MaxFrameRate >= Tuning.SmoothFrameRate;
        var order = IsSmooth(a).CompareTo(IsSmooth(b));
        if (order == 0) order = (a.Width * a.Height).CompareTo(b.Width * b.Height);
        if (order == 0) order = a.MaxFrameRate.CompareTo(b.MaxFrameRate);
        return order != 0 ? order : a.Encoding.CompareTo(b.Encoding);
    }

    /// <summary>The greatest index by <paramref name="compare"/>; the first of equals wins (LINQ's <c>MaxBy</c> does too, but not on indices).</summary>
    private static int? FirstMaxIndex(IEnumerable<int> indices, Func<int, int, int> compare)
    {
        int? best = null;
        foreach (var index in indices)
        {
            if (best is not { } current || compare(index, current) > 0) best = index;
        }
        return best;
    }
}
