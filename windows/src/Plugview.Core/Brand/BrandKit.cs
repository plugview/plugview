using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugview.Core.Brand;

/// <summary>
/// The brand, read from <c>spec/fixtures/brand.json</c> (embedded at build time), so Windows draws with exactly the
/// numbers the Mac's tests check. Nothing brand-related is typed by hand here.
/// </summary>
public sealed record BrandKit(BrandPalette Palette, IReadOnlyList<string> Bars, BrandGeometry Geometry, BrandAnimations Animations)
{
    private static readonly Lazy<BrandKit> Shared = new(Load);

    /// <summary>The brand, parsed once.</summary>
    public static BrandKit Current => Shared.Value;

    private static BrandKit Load()
    {
        using var stream = typeof(BrandKit).Assembly.GetManifestResourceStream("Plugview.Core.brand.json")
            ?? throw new InvalidOperationException("brand.json is not embedded in Plugview.Core");
        return JsonSerializer.Deserialize(stream, BrandJsonContext.Default.BrandKit)
            ?? throw new InvalidDataException("brand.json is empty");
    }
}

/// <summary>The palette, as <c>#RRGGBB</c> strings.</summary>
public sealed record BrandPalette(string Cathode, string Surface, string Raised, string Line, string Bone, string Muted, string Signal, string Tally, string Ok, string Glow);

/// <summary>The mark's geometry in the kit's 64-unit box.</summary>
public sealed record BrandGeometry(
    IReadOnlyList<double[]> Port,
    IReadOnlyList<double[]> Triangle,
    IReadOnlyList<double[]> Connector,
    IReadOnlyList<double[]> ConnectorWindow,
    IReadOnlyList<double[]> Prongs,
    double ProngCornerRadius,
    double PortStrokeWidth,
    double TriangleStrokeWidth,
    IReadOnlyList<double> BarStarts,
    double BarWidth,
    BrandBars ConnectorBars,
    BrandBars TriangleBars);

/// <summary>Where a set of the seven bars starts and how much it is squeezed.</summary>
public sealed record BrandBars(double X, double ScaleX, double LiveSecondCopyX = 0);

/// <summary>The three brand animations.</summary>
public sealed record BrandAnimations(WaitingAnimation Waiting, IntroAnimation Intro, LiveAnimation Live);

/// <summary>One animated value, as in the brand SVGs' CSS.</summary>
public sealed record BrandTrack(IReadOnlyList<double[]> Frames, double[] Easing)
{
    /// <summary>The track as the shared easing maths evaluates it (<c>easing.json</c> pins the curve).</summary>
    public Keyframes Keyframes { get; } = new([.. Frames.Select(frame => (frame[0], frame[1]))], new CubicBezier(Easing[0], Easing[1], Easing[2], Easing[3]));
}

/// <summary>The connector hovering beside a dashed port while no card has signal.</summary>
public sealed record WaitingAnimation(
    double ViewBoxWidth,
    double[] ConnectorTranslate,
    double ConnectorScale,
    double HoverSeconds,
    BrandTrack Hover,
    double BlinkSeconds,
    double DimFromSeconds,
    double DimOpacity,
    double PortOffsetX,
    double PortOpacity,
    double[] PortDash);

/// <summary>The plug-in intro played when a card shows a picture.</summary>
public sealed record IntroAnimation(
    double DelaySeconds,
    double DurationSeconds,
    double[] FlashRect,
    double FlashCornerRadius,
    double[] FlashCenter,
    double[] ConnectorCenter,
    double[] TriangleCenter,
    IntroTracks Tracks);

/// <summary>Every animated value of the intro.</summary>
public sealed record IntroTracks(
    BrandTrack PortOpacity,
    BrandTrack FlashScaleX,
    BrandTrack FlashScaleY,
    BrandTrack FlashOpacity,
    BrandTrack ConnectorX,
    BrandTrack ConnectorScale,
    BrandTrack ConnectorOpacity,
    BrandTrack TriangleScale,
    BrandTrack TriangleOpacity);

/// <summary>The bars scrolling in the triangle after the intro.</summary>
public sealed record LiveAnimation(double ScrollSeconds, double ScrollDistance);

/// <summary>An opaque color from an <c>#RRGGBB</c> string, channels 0…1.</summary>
public readonly record struct BrandColor(float Red, float Green, float Blue)
{
    /// <summary>Parses <c>#RRGGBB</c>.</summary>
    public static BrandColor Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var value = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
    }
}

/// <summary>Source-generated reading of <c>brand.json</c>, which NativeAOT needs (no reflection).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrandKit))]
internal sealed partial class BrandJsonContext : JsonSerializerContext;
