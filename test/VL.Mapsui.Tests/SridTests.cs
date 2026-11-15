using System;
using System.Linq;
using NetTopologySuite.Geometries;
using Xunit;

using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;

namespace VL.Mapsui.Tests;

/// <summary>
/// The SRID on a geometry decides what the layer does with it.
/// </summary>
/// <remarks>
/// Until 2026-11-15 nothing here read it, and nothing needed to: no node in the family produced
/// geometry that was not longitude and latitude. VL.PROJ and SetSRID changed that. A geometry on
/// the Dutch grid wired straight into a layer was projected as if 85065 were a longitude, drew
/// nothing, and no readout said why.
///
/// 0 and 4326 are projected, 3857 is the map's own system and is taken as it is, anything else is
/// left off the layer and named in Status.
/// </remarks>
public class SridTests
{
    // Tokyo station: longitude and latitude, and the same place in EPSG:3857 (GeometryLayerTests
    // derives the numbers).
    const double Lon = 139.7671, Lat = 35.6812;

    static Point PointIn(int srid, double x, double y)
        => new GeometryFactory(new PrecisionModel(), srid).CreatePoint(new Coordinate(x, y));

    static NtsFeature Feature(Geometry geometry) => new(geometry, new AttributesTable());

    static Coordinate[] Drawn(global::Mapsui.Layers.ILayer? layer)
        => ((MemoryLayer)layer!).Features.Cast<GeometryFeature>().Select(f => f.Geometry!.Coordinate).ToArray();

    // ── What is drawn where ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4326)]
    public void Longitude_and_latitude_is_projected_whether_or_not_it_says_so(int srid)
    {
        using var node = new FeatureLayerNode();

        var layer = node.Update(out _, out var status, new[] { Feature(PointIn(srid, Lon, Lat)) });

        var at = Assert.Single(Drawn(layer));
        Assert.InRange(at.X, 15558800, 15558805);
        Assert.InRange(at.Y, 4256841, 4256846);
        Assert.Equal("1 feature", status);
    }

    [Fact]
    public void Web_mercator_is_drawn_where_it_already_is()
    {
        // The mechanism by which this could go wrong: projecting it AGAIN. FromLonLat of fifteen
        // million "degrees" is not these numbers, so an exact match means the coordinates were
        // left alone.
        using var node = new FeatureLayerNode();

        var layer = node.Update(out _, out var status, new[] { Feature(PointIn(3857, 15558802.4, 4256843.2)) });

        var at = Assert.Single(Drawn(layer));
        Assert.Equal(15558802.4, at.X, 6);
        Assert.Equal(4256843.2, at.Y, 6);
        Assert.Equal("1 feature", status);
    }

    [Fact]
    public void The_same_place_said_both_ways_lands_on_the_same_spot()
    {
        using var node = new FeatureLayerNode();
        var mercator = GeometryLayerNode.ToMercator(PointIn(4326, Lon, Lat));

        var layer = node.Update(out _, out _, new[]
        {
            Feature(PointIn(4326, Lon, Lat)),
            Feature(PointIn(3857, mercator.Coordinate.X, mercator.Coordinate.Y)),
        });

        var drawn = Drawn(layer);
        Assert.Equal(2, drawn.Length);
        Assert.Equal(drawn[0].X, drawn[1].X, 6);
        Assert.Equal(drawn[0].Y, drawn[1].Y, 6);
    }

    // ── What is not drawn, and says so ───────────────────────────────────────

    [Fact]
    public void A_geometry_in_another_crs_gives_no_layer_and_a_reason()
    {
        // TU Delft on the Dutch grid, the coordinates VL.PROJ's acceptance used.
        using var node = new FeatureLayerNode();

        var layer = node.Update(out var built, out var status, new[] { Feature(PointIn(28992, 85065, 446394)) });

        Assert.Null(layer);
        Assert.Equal(0, built);
        Assert.Contains("EPSG:28992", status);
        Assert.Contains("NOT drawn", status);
        Assert.Contains("Reproject", status);
    }

    [Fact]
    public void Only_the_features_the_map_can_take_are_on_the_layer()
    {
        using var node = new FeatureLayerNode();

        var layer = node.Update(out _, out var status, new[]
        {
            Feature(PointIn(4326, Lon, Lat)),
            Feature(PointIn(28992, 85065, 446394)),
            Feature(PointIn(2263, 987000, 211000)),
            Feature(PointIn(28992, 85100, 446400)),
        });

        Assert.Single(Drawn(layer));
        Assert.StartsWith("4 features, but 3 of them are in EPSG:2263, EPSG:28992 and NOT drawn.", status);
    }

    [Fact]
    public void The_srid_warning_comes_before_any_style_warning()
    {
        // A polygon under a SymbolStyle is also drawn as nothing, but a feature that is not on the
        // layer at all is the more complete absence and the one to fix first.
        using var node = new FeatureLayerNode();
        var polygon = new GeometryFactory(new PrecisionModel(), 28992)
            .CreatePolygon(new[] { new Coordinate(0, 0), new Coordinate(1, 0), new Coordinate(1, 1), new Coordinate(0, 0) });

        node.Update(out _, out var status, new[] { Feature(polygon) }, new SymbolStyleNode().Update());

        Assert.Contains("EPSG:28992", status);
        Assert.DoesNotContain("SymbolStyle", status);
    }

    // ── The frame loop ───────────────────────────────────────────────────────

    [Fact]
    public void A_hundred_frames_of_a_foreign_crs_build_nothing()
    {
        using var node = new FeatureLayerNode();
        var built = -1;

        // New objects every frame, as a static node upstream would hand them over.
        for (int frame = 0; frame < 100; frame++)
            node.Update(out built, out _, new[] { Feature(PointIn(28992, 85065, 446394)) });

        Assert.Equal(0, built);
        Assert.Equal(0, node.FeatureSetsBuilt);
    }

    [Fact]
    public void A_hundred_frames_of_mixed_crs_build_one_layer_and_one_feature_set()
    {
        using var node = new FeatureLayerNode();
        var built = -1;

        for (int frame = 0; frame < 100; frame++)
            node.Update(out built, out _, new[]
            {
                Feature(PointIn(3857, 15558802.4, 4256843.2)),
                Feature(PointIn(28992, 85065, 446394)),
            });

        Assert.Equal(1, built);
        Assert.Equal(1, node.FeatureSetsBuilt);
    }

    [Fact]
    public void Changing_only_the_srid_moves_the_shape()
    {
        // EqualsExact does not look at the SRID. The same two numbers are a different place in
        // 4326 and in 3857, so comparing coordinates alone would keep drawing the old one.
        using var node = new FeatureLayerNode();

        var asDegrees = Drawn(node.Update(out _, out _, new[] { Feature(PointIn(4326, 10, 20)) })).Single();
        var asMetres = Drawn(node.Update(out _, out _, new[] { Feature(PointIn(3857, 10, 20)) })).Single();

        Assert.Equal(2, node.FeatureSetsBuilt);
        Assert.Equal(10, asMetres.X, 6);
        Assert.Equal(20, asMetres.Y, 6);
        Assert.True(asDegrees.X > 1_000_000);
    }

    [Fact]
    public void An_srid_set_in_place_on_a_geometry_the_layer_already_holds_is_noticed()
    {
        // A geometry is mutable. The same object compares equal to itself whatever was done to it,
        // which is why the layer keeps the SRIDs it was built from as numbers.
        using var node = new FeatureLayerNode();
        var point = PointIn(4326, 10, 20);
        var features = new[] { Feature(point) };

        node.Update(out _, out _, features);
        point.SRID = 3857;
        var at = Drawn(node.Update(out _, out _, features)).Single();

        Assert.Equal(2, node.FeatureSetsBuilt);
        Assert.Equal(10, at.X, 6);

        point.SRID = 28992;
        Assert.Null(node.Update(out _, out var status, features));
        Assert.Contains("EPSG:28992", status);
    }

    // ── The copy ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(4326)]
    [InlineData(3857)]
    public void The_geometry_it_was_given_keeps_its_coordinates_and_its_srid(int srid)
    {
        var original = PointIn(srid, 10, 20);

        var copy = GeometryLayerNode.ToMercator(original);

        Assert.NotSame(original, copy);
        Assert.Equal(10, original.Coordinate.X, 6);
        Assert.Equal(20, original.Coordinate.Y, 6);
        Assert.Equal(srid, original.SRID);
        Assert.Equal(3857, copy.SRID);
    }

    // ── The shortcut node ────────────────────────────────────────────────────

    [Fact]
    public void The_geometry_node_says_it_too()
    {
        using var node = new GeometryLayerNode();

        var layer = node.Update(out _, out var status, new Geometry[] { PointIn(28992, 85065, 446394) });

        Assert.Null(layer);
        Assert.Contains("EPSG:28992", status);

        var drawn = node.Update(out _, out status, new Geometry[] { PointIn(3857, 15558802.4, 4256843.2) });
        Assert.Equal(15558802.4, Drawn(drawn).Single().X, 6);
        Assert.Equal("1 feature", status);
    }

    [Fact]
    public void The_geometry_node_notices_an_srid_that_changed_under_the_same_coordinates()
    {
        // What a patch does when someone types another number into SetSRID: new objects, the same
        // coordinates, a different SRID. The node keeps its features while the geometry compares
        // equal, and NTS's equality does not look at the SRID.
        using var node = new GeometryLayerNode();

        var asDegrees = Drawn(node.Update(out _, out _, new Geometry[] { PointIn(4326, 10, 20) })).Single();
        var asMetres = Drawn(node.Update(out _, out _, new Geometry[] { PointIn(3857, 10, 20) })).Single();
        Assert.True(asDegrees.X > 1_000_000);
        Assert.Equal(10, asMetres.X, 6);

        Assert.Null(node.Update(out _, out var status, new Geometry[] { PointIn(28992, 10, 20) }));
        Assert.Contains("EPSG:28992", status);

        // And a hundred frames of the same thing build once.
        for (int frame = 0; frame < 100; frame++)
            node.Update(out _, out _, new Geometry[] { PointIn(3857, 10, 20) });
        Assert.Equal(3, node.FeatureSetsBuilt);
    }

    // ── Handed back ──────────────────────────────────────────────────────────

    [Fact]
    public void A_shape_picked_off_the_map_comes_back_as_longitude_and_latitude_and_says_so()
    {
        // Drawn from EPSG:3857, handed back in 4326 and labelled 4326: what leaves this package is
        // always longitude and latitude, and a label of 0 is one VL.PROJ's Reproject refuses.
        var factory = new GeometryFactory(new PrecisionModel(), 3857);
        var half = 55_000.0;
        var square = factory.CreatePolygon(new[]
        {
            new Coordinate(-half, -half), new Coordinate(half, -half), new Coordinate(half, half),
            new Coordinate(-half, half), new Coordinate(-half, -half),
        });

        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, out _, new[] { Feature(square) })!;

        var mapNode = new MapNode();
        var map = mapNode.Update(new[] { layer });
        map.Navigator.SetSize(400, 400);
        map.Navigator.CenterOn(0, 0);
        map.Navigator.ZoomTo(400);

        var found = new PickNode().Update(map, out var hit, out _, 200, 200);

        Assert.True(hit);
        Assert.Equal(4326, found!.Geometry.SRID);
        Assert.InRange(found.Geometry.EnvelopeInternal.MaxX, 0.49, 0.50);
        Assert.Equal(3857, square.SRID);
    }
}
