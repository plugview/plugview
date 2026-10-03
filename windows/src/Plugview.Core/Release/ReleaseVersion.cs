namespace Plugview.Core.Release;

/// <summary>Release version comparison.</summary>
public static class ReleaseVersion
{
    /// <summary>Whether <paramref name="candidate"/> (a tag such as <c>v0.3.0</c>) is later than <paramref name="current"/>.</summary>
    /// <remarks>Compares dot-separated whole numbers; a missing part counts as 0; malformed versions are never newer.</remarks>
    public static bool IsNewer(string candidate, string current)
    {
        if (Parts(candidate) is not { } candidateParts || Parts(current) is not { } currentParts) return false;
        var length = Math.Max(candidateParts.Count, currentParts.Count);
        for (var index = 0; index < length; index++)
        {
            var order = PartAt(candidateParts, index).CompareTo(PartAt(currentParts, index));
            if (order != 0) return order > 0;
        }
        return false;
    }

    private static long PartAt(List<long> parts, int index) => index < parts.Count ? parts[index] : 0;

    private static List<long>? Parts(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var parts = new List<long>();
        foreach (var part in (version.StartsWith('v') ? version[1..] : version).Split('.'))
        {
            if (Numbers.WholeNumber(part) is not { } number) return null;
            parts.Add(number);
        }
        return parts;
    }
}
