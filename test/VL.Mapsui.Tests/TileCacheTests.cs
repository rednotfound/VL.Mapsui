using System;
using System.Linq;
using BruTile.Cache;
using BruTile.Web;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// The disk cache stores only tiles that were drawn, and these say so.
/// </summary>
/// <remarks>
/// OpenStreetMap's tile usage policy requires local caching — "Cache tiles locally according to
/// HTTP caching headers (or at least 7 days if your cache cannot read them)" — and forbids the
/// opposite thing, "any pre-emptive fetching of tiles other than those a user is actively
/// viewing". Nothing here or in the nodes fetches anything; the cache only keeps what the map
/// has already asked for.
///
/// These assert on **wiring**, not on files appearing: the assertion is that the tile source
/// really carries a FileCache, which is what the diagnostics overlay reports too. Writing a tile
/// would mean fetching one.
/// </remarks>
public class TileCacheTests
{
    static FileCache? AttachedCache(global::Mapsui.Layers.ILayer? layer)
        => ((layer as global::Mapsui.Tiling.Layers.TileLayer)?.TileSource as HttpTileSource)
            ?.PersistentCache as FileCache;

    [Fact]
    public void The_tile_source_really_carries_a_disk_cache()
    {
        // Attaching happens through a type test that could silently not apply, so it is worth
        // asserting the result rather than the intent.
        using var node = new OpenStreetMapLayerNode();
        var layer = node.Update(out _, enabled: true, cacheToDisk: true);

        Assert.NotNull(AttachedCache(layer));
    }

    [Fact]
    public void Turning_the_cache_off_leaves_the_source_without_one()
    {
        using var node = new OpenStreetMapLayerNode();
        var layer = node.Update(out _, enabled: true, cacheToDisk: false);

        Assert.Null(AttachedCache(layer));
    }

    [Fact]
    public void Changing_the_cache_setting_rebuilds_the_layer_once()
    {
        // The cache is attached at construction, so it belongs to the layer's identity. Without
        // that, switching it on would appear to do nothing until something else changed.
        using var node = new OpenStreetMapLayerNode();

        for (int frame = 0; frame < 10; frame++) node.Update(out _, enabled: true, cacheToDisk: true);
        for (int frame = 0; frame < 10; frame++) node.Update(out _, enabled: true, cacheToDisk: false);

        Assert.Equal(2, node.LayersBuilt);
    }

    [Fact]
    public void The_cache_lives_outside_the_repository()
    {
        // Under LOCALAPPDATA on purpose: it cannot be committed by accident, and deleting one
        // folder resets everything.
        var dir = TileCache.Directory;

        Assert.Contains("VL.Mapsui", dir);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), dir);
        Assert.False(dir.Contains("vl-mapsui", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Stats_survives_a_directory_that_does_not_exist_yet()
    {
        // Called from the render loop, so it has to survive the state before the first tile is
        // written rather than throwing into a frame.
        var (tiles, bytes) = TileCache.Stats();

        Assert.True(tiles >= 0);
        Assert.True(bytes >= 0);
    }

    [Fact]
    public void Stats_does_not_walk_the_disk_on_every_call()
    {
        // Reading a directory once is cheap and sixty times a second is not, which is the same
        // mistake that took a network down wearing different clothes.
        TileCache.Stats();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++) TileCache.Stats();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"10,000 calls took {sw.ElapsedMilliseconds} ms, which means it is hitting the disk");
    }
}
