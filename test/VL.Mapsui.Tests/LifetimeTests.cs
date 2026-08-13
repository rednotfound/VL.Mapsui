using System.Linq;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// Locks down the bug that took a home network offline.
/// </summary>
/// <remarks>
/// The map used to be one node written as a public static method, which VL evaluates on every
/// frame. At 60fps it built a fresh map and tile layer sixty times a second and released none:
/// 17,085 TCP connections in 13 minutes, the machine's 16,384 ephemeral ports exhausted, DNS
/// dead for every program on it. The same bug is why nothing ever rendered, since tiles kept
/// arriving for maps that had already been discarded.
///
/// The package is now several nodes rather than one, so the same property has to hold in more
/// places: **a frame loop with unchanged inputs must build nothing after the first frame**, in
/// the layer, in the map, and across navigation.
///
/// Every test here is a frame loop, because that is the shape of the failure.
/// </remarks>
public class LifetimeTests
{
    // ── The tile layer ────────────────────────────────────────────────────────

    [Fact]
    public void A_hundred_frames_with_unchanged_inputs_build_one_layer()
    {
        // The regression test. Under the old code this number was 100.
        using var layer = new OpenStreetMapLayerNode();

        for (int frame = 0; frame < 100; frame++)
            layer.Update(out _, enabled: true);

        Assert.Equal(1, layer.LayersBuilt);
    }

    [Fact]
    public void The_same_layer_instance_comes_back_every_frame()
    {
        // Identity matters as much as the count: a new layer each frame would discard every
        // tile already fetched, even if the count somehow stayed low.
        using var node = new OpenStreetMapLayerNode();

        var first = node.Update(out _, enabled: true);
        for (int frame = 0; frame < 10; frame++)
            Assert.Same(first, node.Update(out _, enabled: true));
    }

    [Fact]
    public void Nothing_is_built_until_enabled()
    {
        // Opening a document in vvvv runs it. A layer that fetches on open gives whoever opened
        // it no chance to decline, so the default has to cost nothing.
        using var node = new OpenStreetMapLayerNode();

        for (int frame = 0; frame < 50; frame++)
            Assert.Null(node.Update(out _, enabled: false));

        Assert.Equal(0, node.LayersBuilt);
    }

    [Fact]
    public void Layers_built_is_reported_on_the_output_pin()
    {
        // It is a pin rather than something hidden in an overlay so a patch can watch it. This
        // failure was first noticed when a network died; a number that climbs shows it in
        // seconds.
        using var node = new OpenStreetMapLayerNode();

        node.Update(out var atStart, enabled: false);
        Assert.Equal(0, atStart);

        node.Update(out var afterEnabling, enabled: true);
        Assert.Equal(1, afterEnabling);
    }

    [Fact]
    public void Disabling_releases_the_layer_and_re_enabling_builds_a_fresh_one()
    {
        // Switching off has to actually let go, or the gate only stops new work while the old
        // layer keeps its connections.
        using var node = new OpenStreetMapLayerNode();

        node.Update(out _, enabled: true);
        for (int frame = 0; frame < 10; frame++)
            Assert.Null(node.Update(out _, enabled: false));
        node.Update(out var built, enabled: true);

        Assert.Equal(2, built);
    }

    [Fact]
    public void Dispose_is_safe_to_call_twice()
    {
        // VL disposes a process node when it leaves the patch, and a patch can be edited while
        // running. Throwing there would take the whole document down.
        var node = new OpenStreetMapLayerNode();
        node.Update(out _, enabled: true);

        node.Dispose();
        node.Dispose();
    }

    // ── The map ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_hundred_frames_build_one_map()
    {
        using var map = new MapNode();

        for (int frame = 0; frame < 100; frame++)
            map.Update();

        Assert.Equal(1, map.MapsBuilt);
    }

    [Fact]
    public void The_same_map_instance_comes_back_every_frame()
    {
        using var node = new MapNode();

        var first = node.Update();
        for (int frame = 0; frame < 10; frame++)
            Assert.Same(first, node.Update());
    }

    [Fact]
    public void The_layer_collection_is_left_alone_while_the_layers_are_the_same()
    {
        // Clearing and re-adding every frame would throw away each layer's fetched tiles, which
        // is the per-frame-rebuild mistake one level up.
        using var layerNode = new OpenStreetMapLayerNode();
        using var mapNode = new MapNode();

        var layer = layerNode.Update(out _, enabled: true);
        var layers = new[] { layer! };

        var map = mapNode.Update(layers);
        var before = map.Layers.First();

        for (int frame = 0; frame < 20; frame++) mapNode.Update(layers);

        Assert.Single(map.Layers);
        Assert.Same(before, map.Layers.First());
    }

    [Fact]
    public void A_changed_layer_set_is_taken_up()
    {
        using var layerNode = new OpenStreetMapLayerNode();
        using var mapNode = new MapNode();

        var map = mapNode.Update(System.Array.Empty<global::Mapsui.Layers.ILayer>());
        Assert.Empty(map.Layers);

        var layer = layerNode.Update(out _, enabled: true);
        mapNode.Update(new[] { layer! });

        Assert.Single(map.Layers);
    }

    [Fact]
    public void A_null_layer_is_ignored_rather_than_added()
    {
        // The layer node returns null while disabled, and that arrives here as a null in the
        // spread. Adding it would throw inside Mapsui on the next render.
        using var mapNode = new MapNode();

        var map = mapNode.Update(new global::Mapsui.Layers.ILayer?[] { null }!);

        Assert.Empty(map.Layers);
    }
}
