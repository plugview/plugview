using System.Numerics;

namespace Plugview.Core.Brand;

/// <summary>Which artwork a launcher screen shows.</summary>
public enum Art
{
    /// <summary>The still mark: the port with the bars triangle.</summary>
    Mark,

    /// <summary>The connector hovering beside a dashed port: no card has signal yet.</summary>
    Waiting,

    /// <summary>The plug-in intro, then the live loop: a card has signal.</summary>
    Signal,
}

/// <summary>A node of an artwork frame, in the kit's units (a <c>ViewBoxWidth</c>×64 box).</summary>
public abstract record ArtNode;

/// <summary>Children drawn with one transform, at one opacity (as a single layer), optionally clipped to a polygon.</summary>
/// <param name="Transform">Applied to the children and to <paramref name="Clip"/>.</param>
/// <param name="Opacity">The layer's opacity, 0…1.</param>
/// <param name="Clip">A polygon the children are clipped to, or <c>null</c>.</param>
/// <param name="Children">Drawn in order.</param>
public sealed record ArtGroup(Matrix3x2 Transform, float Opacity, IReadOnlyList<Vector2>? Clip, IReadOnlyList<ArtNode> Children) : ArtNode;

/// <summary>A closed polygon, filled or stroked with round joins.</summary>
/// <param name="Points">The corners.</param>
/// <param name="Color">The brand color.</param>
/// <param name="StrokeWidth">Stroked at this width; <c>null</c> fills it.</param>
/// <param name="Dash">Dash and gap lengths in stroke widths' units of the kit, or <c>null</c> for a solid stroke.</param>
public sealed record ArtPolygon(IReadOnlyList<Vector2> Points, BrandColor Color, float? StrokeWidth = null, IReadOnlyList<float>? Dash = null) : ArtNode;

/// <summary>A filled rectangle with rounded corners.</summary>
public sealed record ArtRectangle(float X, float Y, float Width, float Height, float CornerRadius, BrandColor Color) : ArtNode;

/// <summary>One frame of an artwork.</summary>
/// <param name="ViewBoxWidth">The frame's width in kit units; the height is always 64.</param>
/// <param name="Root">What to draw.</param>
public sealed record ArtFrame(float ViewBoxWidth, ArtGroup Root)
{
    /// <summary>The kit's height.</summary>
    public const float ViewBoxHeight = 64;
}

/// <summary>
/// The mark and its animations as frames, from the brand kit's 64-unit geometry and keyframes.
/// </summary>
/// <remarks>
/// Mirrors the Mac's <c>BrandGeometry</c>/<c>BrandAnimations</c> and the Linux <c>ui/art.rs</c>: the waiting loop (the
/// connector hovers beside a dashed port, its prongs blink), and the plug-in intro that ends in the live loop (bars
/// scrolling in the triangle). With animations off, every artwork rests on its final frame.
/// </remarks>
public static class ArtScenes
{
    /// <summary>The frame of <paramref name="art"/> <paramref name="seconds"/> after it appeared.</summary>
    /// <param name="art">The artwork.</param>
    /// <param name="seconds">Time since the artwork appeared.</param>
    /// <param name="isAnimated">False when Windows' animation effects are off: the resting frame.</param>
    /// <param name="brand">The brand kit, <see cref="BrandKit.Current"/> by default.</param>
    public static ArtFrame Frame(Art art, double seconds, bool isAnimated, BrandKit? brand = null)
    {
        brand ??= BrandKit.Current;
        return art switch
        {
            Art.Mark => new(ArtFrame.ViewBoxHeight, Group([Port(brand), .. Triangle(brand, barsOffset: null)])),
            Art.Waiting => Waiting(brand, isAnimated ? seconds : 0),
            Art.Signal => Signal(brand, isAnimated ? seconds : IntroLengthSeconds(brand)),
            _ => throw new ArgumentOutOfRangeException(nameof(art), art, "Unhandled artwork"),
        };
    }

    /// <summary>How long the intro runs before the live loop starts.</summary>
    public static double IntroLengthSeconds(BrandKit brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        return brand.Animations.Intro.DelaySeconds + brand.Animations.Intro.DurationSeconds;
    }

    /// <summary>Whether <paramref name="art"/> changes over time (so the window keeps redrawing it).</summary>
    public static bool IsMoving(Art art) => art != Art.Mark;

    private static ArtFrame Waiting(BrandKit brand, double seconds)
    {
        var waiting = brand.Animations.Waiting;
        var hover = waiting.Hover.Keyframes.ValueAt(seconds % waiting.HoverSeconds / waiting.HoverSeconds);
        // CSS steps(1): full brightness for the first part of each blink, dim for the rest.
        var isDim = seconds % waiting.BlinkSeconds >= waiting.DimFromSeconds;
        var connectorTransform = Matrix3x2.CreateTranslation((float)hover, 0)
            * Matrix3x2.CreateScale((float)waiting.ConnectorScale)
            * Matrix3x2.CreateTranslation((float)waiting.ConnectorTranslate[0], (float)waiting.ConnectorTranslate[1]);
        var connector = new ArtGroup(connectorTransform, 1, null, Connector(brand, isDim ? (float)waiting.DimOpacity : 1));
        var port = new ArtGroup(
            Matrix3x2.CreateTranslation((float)waiting.PortOffsetX, 0),
            (float)waiting.PortOpacity,
            null,
            [Port(brand) with { Dash = [.. waiting.PortDash.Select(length => (float)length)] }]);
        return new((float)waiting.ViewBoxWidth, Group([connector, port]));
    }

    private static ArtFrame Signal(BrandKit brand, double seconds)
    {
        var intro = brand.Animations.Intro;
        var live = brand.Animations.Live;
        var length = IntroLengthSeconds(brand);
        if (seconds >= length)
        {
            var scroll = (seconds - length) % live.ScrollSeconds / live.ScrollSeconds * live.ScrollDistance;
            return new(ArtFrame.ViewBoxHeight, Group([Port(brand), .. Triangle(brand, (float)scroll)]));
        }

        var progress = (seconds - intro.DelaySeconds) / intro.DurationSeconds;
        var tracks = intro.Tracks;
        float Value(BrandTrack track) => (float)track.Keyframes.ValueAt(progress);

        var children = new List<ArtNode> { new ArtGroup(Matrix3x2.Identity, Value(tracks.PortOpacity), null, [Port(brand)]) };

        var (flashScaleX, flashScaleY) = (Value(tracks.FlashScaleX), Value(tracks.FlashScaleY));
        if (MathF.Abs(flashScaleX) > 1e-6f && MathF.Abs(flashScaleY) > 1e-6f)
        {
            var rectangle = intro.FlashRect;
            var flash = new ArtRectangle((float)rectangle[0], (float)rectangle[1], (float)rectangle[2], (float)rectangle[3], (float)intro.FlashCornerRadius, BrandColor.Parse(brand.Palette.Signal));
            children.Add(new ArtGroup(ScaleAbout(flashScaleX, flashScaleY, intro.FlashCenter), Value(tracks.FlashOpacity), null, [flash]));
        }

        var connectorScale = Value(tracks.ConnectorScale);
        children.Add(new ArtGroup(
            ScaleAbout(connectorScale, connectorScale, intro.ConnectorCenter) * Matrix3x2.CreateTranslation(Value(tracks.ConnectorX), 0),
            Value(tracks.ConnectorOpacity),
            null,
            Connector(brand, 1)));

        var triangleScale = Value(tracks.TriangleScale);
        if (MathF.Abs(triangleScale) > 1e-6f)
        {
            children.Add(new ArtGroup(ScaleAbout(triangleScale, triangleScale, intro.TriangleCenter), Value(tracks.TriangleOpacity), null, Triangle(brand, barsOffset: null)));
        }
        return new(ArtFrame.ViewBoxHeight, Group(children));
    }

    private static ArtGroup Group(IReadOnlyList<ArtNode> children) => new(Matrix3x2.Identity, 1, null, children);

    /// <summary>Scales by (<paramref name="x"/>, <paramref name="y"/>) around <paramref name="center"/>.</summary>
    private static Matrix3x2 ScaleAbout(float x, float y, double[] center) =>
        Matrix3x2.CreateScale(x, y, new Vector2((float)center[0], (float)center[1]));

    private static IReadOnlyList<Vector2> Points(IReadOnlyList<double[]> points) => [.. points.Select(point => new Vector2((float)point[0], (float)point[1]))];

    /// <summary>The HDMI port outline in bone.</summary>
    private static ArtPolygon Port(BrandKit brand) =>
        new(Points(brand.Geometry.Port), BrandColor.Parse(brand.Palette.Bone), (float)brand.Geometry.PortStrokeWidth);

    /// <summary>The seven SMPTE bars, 64 units wide before <paramref name="scaleX"/>, starting at <paramref name="x"/>.</summary>
    private static IEnumerable<ArtNode> Bars(BrandKit brand, float x, float scaleX) =>
        brand.Geometry.BarStarts.Zip(brand.Bars, (start, color) =>
            (ArtNode)new ArtRectangle(x + ((float)start * scaleX), 0, (float)brand.Geometry.BarWidth * scaleX, ArtFrame.ViewBoxHeight, 0, BrandColor.Parse(color)));

    /// <summary>The connector with its bars and amber prongs at <paramref name="prongOpacity"/>.</summary>
    private static IReadOnlyList<ArtNode> Connector(BrandKit brand, float prongOpacity)
    {
        var geometry = brand.Geometry;
        var signal = BrandColor.Parse(brand.Palette.Signal);
        IReadOnlyList<ArtNode> prongs = [.. geometry.Prongs.Select(prong =>
            (ArtNode)new ArtRectangle((float)prong[0], (float)prong[1], (float)prong[2], (float)prong[3], (float)geometry.ProngCornerRadius, signal))];
        return
        [
            new ArtGroup(Matrix3x2.Identity, 1, Points(geometry.ConnectorWindow), [.. Bars(brand, (float)geometry.ConnectorBars.X, (float)geometry.ConnectorBars.ScaleX)]),
            new ArtPolygon(Points(geometry.Connector), BrandColor.Parse(brand.Palette.Bone), (float)geometry.PortStrokeWidth),
            new ArtGroup(Matrix3x2.Identity, prongOpacity, null, prongs),
        ];
    }

    /// <summary>The play triangle filled with bars (scrolled by <paramref name="barsOffset"/> in the live loop) and its outline.</summary>
    private static IReadOnlyList<ArtNode> Triangle(BrandKit brand, float? barsOffset)
    {
        var geometry = brand.Geometry;
        var bars = geometry.TriangleBars;
        var scaleX = (float)bars.ScaleX;
        IReadOnlyList<ArtNode> fill = barsOffset is { } offset
            ? [.. Bars(brand, (float)bars.LiveSecondCopyX + offset, scaleX), .. Bars(brand, (float)bars.X + offset, scaleX)]
            : [.. Bars(brand, (float)bars.X, scaleX)];
        return
        [
            new ArtGroup(Matrix3x2.Identity, 1, Points(geometry.Triangle), fill),
            new ArtPolygon(Points(geometry.Triangle), BrandColor.Parse(brand.Palette.Bone), (float)geometry.TriangleStrokeWidth),
        ];
    }
}
