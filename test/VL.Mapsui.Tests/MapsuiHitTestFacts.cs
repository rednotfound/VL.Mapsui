using System.Collections.Generic;
using Xunit;

using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using ILayer = global::Mapsui.Layers.ILayer;
using IFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using VectorStyle = global::Mapsui.Styles.VectorStyle;
using Viewport = global::Mapsui.Viewport;
using WKTReader = NetTopologySuite.IO.WKTReader;

namespace VL.Mapsui.Tests;

/// <summary>
/// What Mapsui's hit test actually does, asserted rather than assumed.
/// </summary>
/// <remarks>
/// These are not tests of this package — they are the facts `Pick` is built on, and each one was
/// measured before a line of it was written. They belong in the suite because a Mapsui upgrade
/// changing any of them would otherwise change `Pick`'s behaviour silently: no compile error, no
/// exception, just a map that answers differently.
/// </remarks>
public class MapsuiHitTestFacts
{
    static List<ILayer> SquareLayer(double halfWidth = 100)
    {
        var wkt = $"POLYGON (({-halfWidth} {-halfWidth}, {halfWidth} {-halfWidth}, " +
                  $"{halfWidth} {halfWidth}, {-halfWidth} {halfWidth}, {-halfWidth} {-halfWidth}))";

        var feature = new GeometryFeature { Geometry = new WKTReader().Read(wkt) };
        feature["name"] = "probe";

        return new List<ILayer>
        {
            new MemoryLayer { Features = new[] { (IFeature)feature }, Style = new VectorStyle(), IsMapInfoLayer = true },
        };
    }

    // 400x400 pixels over 400 world units centred on the origin: one unit per pixel, so the square
    // above spans screen x 100..300.
    static Viewport Centred() => new(0, 0, 1, 0, 400, 400);

    /// <summary>
    /// The edge is exact: no tolerance, no rounding outward.
    /// </summary>
    /// <remarks>
    /// This is why `Pick` promises nothing about a fat finger. A patch wanting one has to grow the
    /// geometry it hit-tests against, which is honest, rather than have a node quietly widen it.
    /// </remarks>
    [Theory]
    [InlineData(290, true)]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    [InlineData(310, false)]
    public void The_edge_of_a_hit_is_exactly_the_geometry(int screenX, bool expectHit)
    {
        var info = new MapRenderer().GetMapInfo(screenX, 200, Centred(), SquareLayer(), 0);
        Assert.Equal(expectHit, info?.Feature is not null);
    }

    /// <summary>
    /// Mapsui's <c>margin</c> does not widen a geometry hit, which is why `Pick` has no such pin.
    /// </summary>
    /// <remarks>
    /// Offering it would have been worse than omitting it: a tolerance pin that does nothing is a
    /// promise the node cannot keep, and the patch would blame its own numbers.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(32)]
    public void The_margin_does_not_widen_a_geometry_hit(int margin)
    {
        // Five pixels outside the right edge - well within any of these margins, if they applied.
        var info = new MapRenderer().GetMapInfo(305, 200, Centred(), SquareLayer(), margin);
        Assert.Null(info?.Feature);
    }

    /// <summary>
    /// A miss still says where in the world it missed, which is what `ScreenToWorld` relies on.
    /// </summary>
    [Fact]
    public void A_miss_still_carries_a_world_position()
    {
        var info = new MapRenderer().GetMapInfo(10, 10, Centred(), SquareLayer(), 0);

        Assert.Null(info?.Feature);
        Assert.NotNull(info?.WorldPosition);
        Assert.Equal(-190, info!.WorldPosition!.X, precision: 3);
        Assert.Equal(190, info.WorldPosition.Y, precision: 3);
    }

    /// <summary>
    /// Everything under the point is reported, topmost first — not only the winner.
    /// </summary>
    /// <remarks>
    /// `Pick` returns the top one today. This asserts the rest is there to expose the day a patch
    /// needs "everything under the cursor", so that turns out to be a pin rather than a rewrite.
    /// </remarks>
    [Fact]
    public void Every_feature_under_the_point_is_listed_topmost_first()
    {
        var big = new GeometryFeature { Geometry = new WKTReader().Read("POLYGON ((-100 -100, 100 -100, 100 100, -100 100, -100 -100))") };
        big["name"] = "big";
        var small = new GeometryFeature { Geometry = new WKTReader().Read("POLYGON ((-30 -30, 30 -30, 30 30, -30 30, -30 -30))") };
        small["name"] = "small";

        var layers = new List<ILayer>
        {
            new MemoryLayer { Features = new[] { (IFeature)big, small }, Style = new VectorStyle(), IsMapInfoLayer = true },
        };

        var info = new MapRenderer().GetMapInfo(200, 200, Centred(), layers, 0);

        Assert.Equal("small", info!.Feature!["name"]);
        Assert.Equal(2, info.MapInfoRecords.Count);
        Assert.Equal("small", info.MapInfoRecords[0].Feature["name"]);
        Assert.Equal("big", info.MapInfoRecords[1].Feature["name"]);
    }
}
