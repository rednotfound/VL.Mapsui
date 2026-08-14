using System;
using System.Linq;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// The node where VL.GIS and VL.Mapsui meet.
/// </summary>
/// <remarks>
/// They meet through NetTopologySuite rather than through each other: VL.GIS computes geometry in
/// WGS84 and this draws it. So the two things that can silently go wrong are the projection —
/// Mapsui works in spherical mercator — and the lifetime, which is the failure this whole package
/// was rebuilt around.
///
/// No network: a MemoryLayer holds features and nothing else.
/// </remarks>
public class GeometryLayerTests
{
    static readonly GeometryFactory Factory = new();

    // NetTopologySuite.Geometries.Point and Stride.Core.Mathematics.Point are both in scope here,
    // and both are called Point.
    static NetTopologySuite.Geometries.Point Tokyo()
        => Factory.CreatePoint(new Coordinate(139.7671, 35.6812));

    // ── Projection ────────────────────────────────────────────────────────────

    [Fact]
    public void Longitude_and_latitude_become_mercator_metres()
    {
        // Tokyo station in EPSG:3857. Computed rather than remembered, because the first version
        // of this test asserted numbers from memory and both were wrong:
        //     x = lon * pi/180 * 6378137                       = 15558802.4
        //     y = 6378137 * ln(tan(pi/4 + (lat * pi/180) / 2))  =  4256843.2
        // Metres, so a few either way is tolerance; the point is that these are mercator
        // coordinates and the right ones.
        var projected = GeometryLayerNode.ToMercator(Tokyo());

        Assert.InRange(projected.Coordinate.X, 15558800, 15558805);
        Assert.InRange(projected.Coordinate.Y, 4256841, 4256846);
    }

    [Fact]
    public void The_origin_stays_at_the_origin()
    {
        var projected = GeometryLayerNode.ToMercator(Factory.CreatePoint(new Coordinate(0, 0)));

        Assert.Equal(0, projected.Coordinate.X, 6);
        Assert.Equal(0, projected.Coordinate.Y, 6);
    }

    [Fact]
    public void Projecting_does_not_touch_the_geometry_it_was_given()
    {
        // The one that would be found late and blamed on something else. NTS geometries are
        // mutable through coordinate filters, so projecting in place would hand the patch back
        // its own shapes silently converted to metres -- and every later use, including a second
        // pass through this node, would compound it.
        var original = Tokyo();

        GeometryLayerNode.ToMercator(original);

        Assert.Equal(139.7671, original.Coordinate.X, 6);
        Assert.Equal(35.6812, original.Coordinate.Y, 6);
    }

    [Fact]
    public void Every_vertex_of_a_polygon_is_projected()
    {
        // A filter applied to only the first coordinate would still produce a plausible-looking
        // shape somewhere near the right place.
        var ring = Factory.CreatePolygon(new[]
        {
            new Coordinate(139.0, 35.0), new Coordinate(140.0, 35.0),
            new Coordinate(140.0, 36.0), new Coordinate(139.0, 36.0),
            new Coordinate(139.0, 35.0),
        });

        var projected = GeometryLayerNode.ToMercator(ring);

        Assert.All(projected.Coordinates, c =>
        {
            Assert.True(Math.Abs(c.X) > 1000, $"X={c.X} still looks like degrees");
            Assert.True(Math.Abs(c.Y) > 1000, $"Y={c.Y} still looks like degrees");
        });
    }

    [Fact]
    public void A_buffered_point_keeps_its_shape_through_the_projection()
    {
        // What the example patch actually draws: VL.GIS buffers a point, this projects the ring.
        var ring = Tokyo().Buffer(0.002);
        var projected = GeometryLayerNode.ToMercator(ring);

        Assert.Equal(ring.Coordinates.Length, projected.Coordinates.Length);
        Assert.True(projected.Area > 0);
    }

    // ── Lifetime ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_hundred_frames_with_unchanged_inputs_build_one_layer()
    {
        using var node = new GeometryLayerNode();
        var geometries = new Geometry[] { Tokyo().Buffer(0.002) };

        for (int frame = 0; frame < 100; frame++)
            node.Update(out _, geometries);

        Assert.Equal(1, node.LayersBuilt);
    }

    [Fact]
    public void The_same_layer_instance_comes_back_every_frame()
    {
        using var node = new GeometryLayerNode();
        var geometries = new Geometry[] { Tokyo() };

        var first = node.Update(out _, geometries);
        for (int frame = 0; frame < 10; frame++)
            Assert.Same(first, node.Update(out _, geometries));
    }

    [Fact]
    public void Nothing_is_built_for_nothing_to_draw()
    {
        // A Map should be wireable before there is any data, without an empty layer appearing in
        // its layer list.
        using var node = new GeometryLayerNode();

        Assert.Null(node.Update(out _, null));
        Assert.Null(node.Update(out _, Array.Empty<Geometry>()));
        Assert.Equal(0, node.LayersBuilt);
    }

    [Fact]
    public void Rebuilding_the_same_shape_every_frame_does_not_rebuild_the_layer()
    {
        // Ten frames, each handing over a brand new array holding a brand new Geometry object,
        // and only two layers come out of it -- one per distinct shape.
        //
        // That is better than it was designed to be, and worth knowing why: SequenceEqual uses
        // NTS's own Equals, which compares geometries by value rather than by reference. So a
        // patch that recomputes its shapes every frame -- which is what a patch driven by an LFO
        // or a slider does -- costs a comparison rather than a rebuild. The comparison walks the
        // coordinates, so it is not free on a large geometry, but it never re-styles or
        // re-uploads a layer for a shape that has not moved.
        //
        // This test asserted 10 first, on the assumption that new objects meant new inputs. The
        // code was right and the expectation was wrong.
        //
        // Updated 2026-08-14 for the same reason one level along: a different shape no longer
        // rebuilds the LAYER either, it replaces the layer's contents. The layer's identity is what
        // a Map compares, and handing out a new one made the whole map flicker.
        using var node = new GeometryLayerNode();

        for (int frame = 0; frame < 5; frame++) node.Update(out _, new Geometry[] { Tokyo() });
        for (int frame = 0; frame < 5; frame++)
            node.Update(out _, new Geometry[] { Factory.CreatePoint(new Coordinate(1, 1)) });

        Assert.Equal(1, node.LayersBuilt);        // one layer, kept
        Assert.Equal(2, node.FeatureSetsBuilt);   // two distinct shapes, two replacements
    }

    [Fact]
    public void Changing_only_the_colour_takes_effect_without_rebuilding()
    {
        // A colour change has to actually reach the map - that is what this has always guarded.
        // What changed is how: the style is now set on the layer rather than baked in at
        // construction, so the layer keeps its identity and the Map holding it is left alone.
        using var node = new GeometryLayerNode();
        var geometries = new Geometry[] { Tokyo() };

        var first = node.Update(out _, geometries);
        var firstStyle = ((global::Mapsui.Layers.MemoryLayer)first!).Style;

        var second = node.Update(out _, geometries, fillColor: new Color4(0f, 1f, 0f, 1f));

        Assert.Same(first, second);                                                   // same layer
        Assert.NotSame(firstStyle, ((global::Mapsui.Layers.MemoryLayer)second!).Style); // new style on it
        Assert.Equal(1, node.LayersBuilt);
    }

    [Fact]
    public void A_null_in_the_spread_is_ignored_rather_than_thrown_on()
    {
        // VL hands out nulls where a node upstream had nothing to give.
        using var node = new GeometryLayerNode();

        var layer = node.Update(out _, new Geometry?[] { null, Tokyo() }!);

        Assert.NotNull(layer);
    }

    [Fact]
    public void Dispose_is_safe_to_call_twice()
    {
        var node = new GeometryLayerNode();
        node.Update(out _, new Geometry[] { Tokyo() });

        node.Dispose();
        node.Dispose();
    }
}
