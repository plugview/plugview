using System.Numerics;
using Plugview.Core.Brand;

namespace Plugview.Core.Tests;

public sealed class BrandTests
{
    [Fact]
    public void BrandKitMatchesTheFixture()
    {
        var fixture = Fixture.Load("brand.json");
        var brand = BrandKit.Current;
        var palette = fixture["palette"]!;
        Assert.Equal(palette["cathode"].Text(), brand.Palette.Cathode);
        Assert.Equal(palette["bone"].Text(), brand.Palette.Bone);
        Assert.Equal(palette["signal"].Text(), brand.Palette.Signal);
        Assert.Equal(palette["tally"].Text(), brand.Palette.Tally);
        Assert.Equal(fixture["bars"].Items().Select(bar => bar.Text()).ToList(), brand.Bars.ToList());
        Assert.Equal(6, brand.Geometry.Port.Count);
        Assert.Equal(fixture["geometry"]!["portStrokeWidth"].Number(), brand.Geometry.PortStrokeWidth);
        Assert.Equal(fixture["animations"]!["live"]!["scrollSeconds"].Number(), brand.Animations.Live.ScrollSeconds);
    }

    [Fact]
    public void ColorsParseFromHex()
    {
        Assert.Equal(new BrandColor(1, 176 / 255f, 0), BrandColor.Parse("#FFB000"));
    }

    [Fact]
    public void TheMarkIsThePortOverTheBarsTriangle()
    {
        var frame = ArtScenes.Frame(Art.Mark, 0, isAnimated: true);
        Assert.Equal(64f, frame.ViewBoxWidth);
        var children = frame.Root.Children;
        Assert.IsType<ArtPolygon>(children[0]);
        var bars = (ArtGroup)children[1];
        Assert.NotNull(bars.Clip);
        Assert.Equal(7, bars.Children.Count);
        Assert.Equal((float)BrandKit.Current.Geometry.TriangleStrokeWidth, ((ArtPolygon)children[2]).StrokeWidth);
    }

    [Fact]
    public void TheWaitingConnectorHoversHalfwayThroughTheLoop()
    {
        var waiting = BrandKit.Current.Animations.Waiting;
        var atRest = (ArtGroup)ArtScenes.Frame(Art.Waiting, 0, isAnimated: true).Root.Children[0];
        var halfway = (ArtGroup)ArtScenes.Frame(Art.Waiting, waiting.HoverSeconds / 2, isAnimated: true).Root.Children[0];
        var moved = halfway.Transform.Translation.X - atRest.Transform.Translation.X;
        Assert.Equal(6 * waiting.ConnectorScale, moved, 1e-4);
    }

    [Fact]
    public void WithoutAnimationsEveryArtworkRestsOnItsFinalFrame()
    {
        var resting = ArtScenes.Frame(Art.Signal, 0, isAnimated: false);
        // The live loop at scroll 0: two copies of the bars fill the triangle.
        var bars = (ArtGroup)resting.Root.Children[1];
        Assert.Equal(14, bars.Children.Count);
        var waiting = ArtScenes.Frame(Art.Waiting, 5, isAnimated: false);
        Assert.Equal(Matrix3x2.Identity.M11 * (float)BrandKit.Current.Animations.Waiting.ConnectorScale, ((ArtGroup)waiting.Root.Children[0]).Transform.M11, 1e-6);
    }

    [Fact]
    public void TheIntroStartsWithTheConnectorHiddenAndThePortDim()
    {
        var frame = ArtScenes.Frame(Art.Signal, 0, isAnimated: true);
        var port = (ArtGroup)frame.Root.Children[0];
        Assert.Equal(0.28f, port.Opacity, 1e-6);
        var connector = frame.Root.Children.OfType<ArtGroup>().Single(group => group.Children.Count == 3);
        Assert.Equal(0f, connector.Opacity, 1e-6);
    }
}
