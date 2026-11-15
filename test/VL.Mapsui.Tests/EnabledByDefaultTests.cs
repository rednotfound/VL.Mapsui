using Xunit;

using TileLayer = global::Mapsui.Tiling.Layers.TileLayer;

namespace VL.Mapsui.Tests;

/// <summary>
/// A tile layer placed in a patch is a tile layer: Enabled is on unless the patch says otherwise.
/// </summary>
/// <remarks>
/// Decided 2026-11-15, reversing "off by default on anything that fetches". What that rule
/// protected is asserted elsewhere and still holds - one tile source per node
/// (TileSourceReuseTests), one layer per hundred frames (LifetimeTests), a disk cache
/// (TileCacheTests). No network here: a layer requests nothing until it is on a map that has a
/// size and is refreshed, and none of these is ever put on one.
/// </remarks>
public class EnabledByDefaultTests
{
    [Fact]
    public void OpenStreetMap_with_nothing_set_gives_a_layer_with_its_credit_and_a_cache()
    {
        using var node = new OpenStreetMapLayerNode();

        var layer = Assert.IsType<TileLayer>(node.Update(out var built, out _));

        Assert.Equal(1, built);
        Assert.Contains("OpenStreetMap", layer.Attribution.Text);
        var http = Assert.IsType<BruTile.Web.HttpTileSource>(layer.TileSource);
        Assert.IsType<BruTile.Cache.FileCache>(http.PersistentCache);
    }

    [Fact]
    public void A_hundred_frames_of_defaults_build_one_layer()
    {
        using var node = new OpenStreetMapLayerNode();

        var first = node.Update(out _, out _);
        var built = 0;
        for (int frame = 0; frame < 100; frame++)
            Assert.Same(first, node.Update(out built, out _));

        Assert.Equal(1, built);
    }

    [Fact]
    public void Off_is_still_off_and_still_gives_no_layer()
    {
        using var osm = new OpenStreetMapLayerNode();
        using var xyz = new XyzTileLayerNode();

        Assert.Null(osm.Update(out _, out _, enabled: false));
        Assert.Null(xyz.Update(out _, out _, urlTemplate: "https://tile.example/{z}/{x}/{y}.png", enabled: false));
    }
}
