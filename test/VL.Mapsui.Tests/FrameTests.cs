using System;
using System.Linq;
using Mapsui;
using NetTopologySuite.Geometries;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// Framing a layer: the node that answers "put the view where the data is".
/// </summary>
/// <remarks>
/// Asserted on the count of framings rather than on the resulting resolution, the same way the
/// zoom nodes are: Mapsui eases the move over a duration, so reading the viewport back would mean
/// waiting for wall-clock time to pass and would test the animation rather than the node.
///
/// No network: a MemoryLayer and a Map with no size.
/// </remarks>
public class FrameTests
{
    static readonly GeometryFactory Factory = new();

    static global::Mapsui.Layers.ILayer LayerAround(double lon, double lat)
    {
        var node = new FeatureLayerNode();
        var feature = FeatureNodes.Feature(Factory.CreatePoint(new Coordinate(lon, lat)));
        return node.Update(out _, out _, new[] { feature })!;
    }

    [Fact]
    public void A_held_trigger_frames_once()
    {
        // The safety property, and the reason this is a process node: every framing asks the layers
        // to refresh, so one per frame is a tile request per frame - the shape of the failure that
        // took a home network down.
        var map = new global::Mapsui.Map();
        var layer = LayerAround(139.7671, 35.6812);
        var node = new ZoomToLayerNode();

        for (int frame = 0; frame < 100; frame++) node.Update(map, layer, trigger: true);

        Assert.Equal(1, node.Framings);
    }

    [Fact]
    public void Releasing_and_pressing_again_frames_again()
    {
        var map = new global::Mapsui.Map();
        var layer = LayerAround(139.7671, 35.6812);
        var node = new ZoomToLayerNode();

        foreach (var t in new[] { true, true, false, false, true }) node.Update(map, layer, trigger: t);

        Assert.Equal(2, node.Framings);
    }

    [Fact]
    public void An_empty_or_unconnected_layer_moves_nothing()
    {
        // A feature layer is empty until its data arrives, so a bang can land a frame early. Jumping
        // to some default extent then would send the view somewhere arbitrary and look like a bug
        // in whatever produced the data.
        var map = new global::Mapsui.Map();
        var node = new ZoomToLayerNode();

        node.Update(map, layer: null, trigger: true);
        node.Update(map, layer: new global::Mapsui.Layers.MemoryLayer("empty"), trigger: true);

        Assert.Equal(0, node.Framings);
    }

    [Fact]
    public void The_margin_grows_the_box_on_every_side()
    {
        var box = new MRect(0, 0, 100, 200);

        var grown = ZoomToLayerNode.Grown(box, 0.1f);

        // To a precision, not exactly: the margin is a float, and 0.1f is 0.100000001490116... so
        // ten per cent of a hundred is 10.0000001. Asserting exact equality here failed on the
        // first run, and the expectation was the wrong one - a map margin is not a place where the
        // seventh decimal means anything.
        Assert.Equal(-10, grown.MinX, 3);
        Assert.Equal(110, grown.MaxX, 3);
        Assert.Equal(-20, grown.MinY, 3);
        Assert.Equal(220, grown.MaxY, 3);
    }

    [Fact]
    public void A_single_point_still_gets_a_box_with_area()
    {
        // A layer holding one point has a zero-sized extent, and a fraction of zero is zero. Mapsui
        // would then be asked to fit nothing into a viewport, so a degenerate box gets a small
        // absolute margin instead - the one case where a fraction cannot work.
        var point = new MRect(1000, 2000, 1000, 2000);

        var grown = ZoomToLayerNode.Grown(point, 0.1f);

        Assert.True(grown.Width > 0);
        Assert.True(grown.Height > 0);
    }

    [Fact]
    public void A_negative_margin_is_treated_as_none_rather_than_shrinking()
    {
        var grown = ZoomToLayerNode.Grown(new MRect(0, 0, 100, 100), -5f);

        Assert.Equal(0, grown.MinX);
        Assert.Equal(100, grown.MaxX);
    }

    [Fact]
    public void Framing_every_layer_takes_them_all_in()
    {
        // Two layers far apart: the box has to cover both, which is what makes this different from
        // framing one of them.
        var map = new global::Mapsui.Map();
        map.Layers.Add(LayerAround(139.7671, 35.6812));   // Tokyo
        map.Layers.Add(LayerAround(135.7581, 34.9859));   // Kyoto
        var node = new ZoomToLayersNode();

        node.Update(map, trigger: true);

        Assert.Equal(1, node.Framings);
    }

    [Fact]
    public void Framing_a_map_with_nothing_on_it_moves_nothing()
    {
        var node = new ZoomToLayersNode();

        node.Update(new global::Mapsui.Map(), trigger: true);

        Assert.Equal(0, node.Framings);
    }
}
