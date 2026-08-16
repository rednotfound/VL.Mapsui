using System;
using System.Linq;
using Xunit;

using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;
using WKTReader = NetTopologySuite.IO.WKTReader;
using ILayer = global::Mapsui.Layers.ILayer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;

namespace VL.Mapsui.Tests;

/// <summary>
/// Picking a feature off the map — the first thing this package hands back rather than takes in.
/// </summary>
/// <remarks>
/// Shaped like a frame loop, as everything here is: a pick runs every frame, so what matters is
/// that it stays cheap and stays correct across many of them.
/// </remarks>
public class PickTests
{
    // A square around the origin in WGS84, half a degree each way. In mercator that is roughly
    // 55 km, so at 400x400 pixels and a resolution of 400 it sits well inside the view.
    static NtsFeature Square(string name, double halfWidth = 0.5)
    {
        var wkt = $"POLYGON (({-halfWidth} {-halfWidth}, {halfWidth} {-halfWidth}, " +
                  $"{halfWidth} {halfWidth}, {-halfWidth} {halfWidth}, {-halfWidth} {-halfWidth}))";

        var attributes = new AttributesTable { { "name", name } };
        return new NtsFeature(new WKTReader().Read(wkt), attributes);
    }

    /// <summary>
    /// A map with the layer on it, drawn once so the viewport has a size — which is what
    /// MapsuiLayer.Render does on the first frame, and what a pick cannot work without.
    /// </summary>
    static (MapNode map, global::Mapsui.Map built) MapOver(ILayer layer, double resolution = 400)
    {
        var node = new MapNode();
        var built = node.Update(new[] { layer });
        built.Navigator.SetSize(400, 400);
        built.Navigator.CenterOn(0, 0);
        built.Navigator.ZoomTo(resolution);
        return (node, built);
    }

    [Fact]
    public void A_pick_over_a_shape_returns_it_with_its_attributes()
    {
        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, out _, new[] { Square("probe") })!;

        var (_, map) = MapOver(layer);
        var pick = new PickNode();

        var found = pick.Update(map, out var hit, out var layerName, 200, 200);

        Assert.True(hit);
        Assert.Equal("Features", layerName);
        Assert.NotNull(found);
        Assert.Equal("probe", found!.Attributes["name"]);
    }

    [Fact]
    public void A_pick_over_nothing_returns_nothing()
    {
        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, out _, new[] { Square("probe") })!;

        var (_, map) = MapOver(layer);
        var pick = new PickNode();

        // Top-left corner: the square is centred, so this is well clear of it.
        var found = pick.Update(map, out var hit, out _, 5, 5);

        Assert.False(hit);
        Assert.Null(found);
    }

    /// <summary>
    /// The whole reason FeatureLayer sets IsMapInfoLayer, expressed as the failure it prevents.
    /// </summary>
    /// <remarks>
    /// Negative test in the strong sense: this is the exact state the package would be in if the
    /// line in FeatureLayerNode were removed, and it fails silently — no exception, no message, an
    /// empty result indistinguishable from an honest miss. Confirmed by reverting the line: the
    /// two tests above go red while nothing else does.
    /// </remarks>
    [Fact]
    public void A_layer_that_did_not_opt_in_is_never_hit()
    {
        using var layerNode = new FeatureLayerNode();
        var layer = (MemoryLayer)layerNode.Update(out _, out _, new[] { Square("probe") })!;

        layer.IsMapInfoLayer = false;      // what Mapsui's own default would have left it as

        var (_, map) = MapOver(layer);
        var pick = new PickNode();

        var found = pick.Update(map, out var hit, out _, 200, 200);

        Assert.False(hit);
        Assert.Null(found);
    }

    [Fact]
    public void What_comes_back_is_in_the_coordinates_that_went_in()
    {
        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, out _, new[] { Square("probe") })!;

        var (_, map) = MapOver(layer);
        var pick = new PickNode();

        var found = pick.Update(map, out _, out _, 200, 200)!;

        // Degrees, not the millions of metres a mercator coordinate would be.
        var envelope = found.Geometry.EnvelopeInternal;
        Assert.Equal(-0.5, envelope.MinX, precision: 6);
        Assert.Equal(0.5, envelope.MaxY, precision: 6);
    }

    [Fact]
    public void The_topmost_layer_wins_when_shapes_overlap()
    {
        using var lowerNode = new FeatureLayerNode();
        using var upperNode = new FeatureLayerNode();

        var lower = lowerNode.Update(out _, out _, new[] { Square("big", 0.5) }, name: "lower")!;
        var upper = upperNode.Update(out _, out _, new[] { Square("small", 0.1) }, name: "upper")!;

        var mapNode = new MapNode();
        var map = mapNode.Update(new[] { lower, upper });
        map.Navigator.SetSize(400, 400);
        map.Navigator.CenterOn(0, 0);
        map.Navigator.ZoomTo(400);

        var pick = new PickNode();
        var found = pick.Update(map, out _, out var layerName, 200, 200)!;

        Assert.Equal("small", found.Attributes["name"]);
        Assert.Equal("upper", layerName);
    }

    [Fact]
    public void A_hundred_frames_of_picking_build_one_renderer_and_one_layer()
    {
        using var layerNode = new FeatureLayerNode();
        var pick = new PickNode();

        global::Mapsui.Map? map = null;
        var mapNode = new MapNode();
        NtsFeature? last = null;

        for (var frame = 0; frame < 100; frame++)
        {
            // Fresh feature objects every frame, which is what a patch actually does.
            var layer = layerNode.Update(out _, out _, new[] { Square("probe") })!;
            map = mapNode.Update(new[] { layer });
            map.Navigator.SetSize(400, 400);
            map.Navigator.CenterOn(0, 0);
            map.Navigator.ZoomTo(400);

            last = pick.Update(map, out var hit, out _, 200, 200);
            Assert.True(hit);
        }

        Assert.Equal(1, layerNode.LayersBuilt);
        Assert.Equal(1, layerNode.FeatureSetsBuilt);
        Assert.Equal("probe", last!.Attributes["name"]);
    }

    [Fact]
    public void Before_the_first_frame_a_pick_says_so_rather_than_missing()
    {
        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, out _, new[] { Square("probe") })!;

        var mapNode = new MapNode();
        var map = mapNode.Update(new[] { layer });   // never sized: nothing has been drawn

        var pick = new PickNode();
        var found = pick.Update(map, out var hit, out var layerName, 200, 200);

        Assert.False(hit);
        Assert.Null(found);
        Assert.Contains("not been drawn", layerName);
    }

    [Fact]
    public void Screen_to_world_answers_where_there_is_no_feature()
    {
        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, out _, new[] { Square("probe") })!;
        var (_, map) = MapOver(layer);

        ProjectNodes.ScreenToWorld(map, out var longitude, out var latitude, 5, 5);

        // Nothing is drawn in the corner, but it still has coordinates - and they are north-west
        // of the centre, because screen y counts down and latitude counts up.
        Assert.True(longitude < 0, $"expected west of centre, got {longitude}");
        Assert.True(latitude > 0, $"expected north of centre, got {latitude}");
    }

    [Fact]
    public void Screen_and_world_are_inverses_of_each_other()
    {
        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, out _, new[] { Square("probe") })!;
        var (_, map) = MapOver(layer);

        ProjectNodes.ScreenToWorld(map, out var longitude, out var latitude, 137, 251);
        ProjectNodes.WorldToScreen(map, out var x, out var y, longitude, latitude);

        Assert.Equal(137f, x, precision: 2);
        Assert.Equal(251f, y, precision: 2);
    }
}
