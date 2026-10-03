using System.Globalization;

namespace Plugview.Core;

/// <summary>The number rules the spec fixes because standard libraries disagree on them.</summary>
internal static class Numbers
{
    /// <summary>The value of <paramref name="text"/> when it is only ASCII digits: no sign, no spaces, no separators.</summary>
    /// <remarks><c>int.Parse</c> accepts <c>+40</c> and <c> 40</c>; the spec refuses both.</remarks>
    public static long? WholeNumber(string text)
    {
        if (text.Length == 0 || !text.All(char.IsAsciiDigit)) return null;
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>Rounds half away from zero, as Swift and Rust do; <c>Math.Round</c> alone rounds halves to even.</summary>
    public static long Rounded(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Formats with the invariant culture, so numbers read the same in every language setting.</summary>
    public static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
