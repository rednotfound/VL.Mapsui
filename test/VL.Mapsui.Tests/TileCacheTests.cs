using System.IO;
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
/// viewing". Nothing here or in the node fetches anything; the cache only keeps what the map has
/// already asked for.
///
/// These tests assert on **wiring**, not on files appearing: the assertion is that the tile
/// source really carries a FileCache, which is what the diagnostics overlay reports too. Writing
/// a tile would mean fetching one.
/// </remarks>
public class TileCacheTests
{
    static FileCache? AttachedCache(OpenStreetMapNode node)
        => node.CurrentMap?.Layers
            .OfType<global::Mapsui.Tiling.Layers.TileLayer>()
            .Select(l => (l.TileSource as HttpTileSource)?.PersistentCache as FileCache)
            .FirstOrDefault();

    [Fact]
    public void The_tile_source_really_carries_a_disk_cache()
    {
        // The overlay reports whether a cache is attached rather than whether one was asked for,
        // because attaching happens through a type test that could silently not apply.
        using var node = new OpenStreetMapNode();
        node.Update(139.7, 35.68, 12, enabled: true, cacheToDisk: true);

        Assert.NotNull(AttachedCache(node));
    }

    [Fact]
    public void Turning_the_cache_off_leaves_the_source_without_one()
    {
        using var node = new OpenStreetMapNode();
        node.Update(139.7, 35.68, 12, enabled: true, cacheToDisk: false);

        Assert.Null(AttachedCache(node));
    }

    [Fact]
    public void Changing_the_cache_setting_rebuilds_once()
    {
        // The cache is attached at construction, so it belongs to the map's identity. Without
        // that, switching it on would appear to do nothing until something else changed.
        using var node = new OpenStreetMapNode();

        for (int frame = 0; frame < 10; frame++) node.Update(139.7, 35.68, 12, enabled: true, cacheToDisk: true);
        for (int frame = 0; frame < 10; frame++) node.Update(139.7, 35.68, 12, enabled: true, cacheToDisk: false);

        Assert.Equal(2, node.MapsBuiltHere);
    }

    [Fact]
    public void The_cache_lives_outside_the_repository()
    {
        // Under LOCALAPPDATA on purpose: it cannot be committed by accident, and deleting one
        // folder resets everything.
        var dir = TileCache.Directory;

        Assert.Contains("VL.Mapsui", dir);
        Assert.StartsWith(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            dir);
        Assert.False(dir.Contains("vl-mapsui", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Stats_on_a_directory_that_does_not_exist_yet_reports_nothing()
    {
        // Called from the render loop, so it has to survive the state before the first tile is
        // ever written rather than throwing into a frame.
        var (tiles, bytes) = TileCache.Stats();

        Assert.True(tiles >= 0);
        Assert.True(bytes >= 0);
    }

    [Fact]
    public void Stats_does_not_walk_the_disk_on_every_call()
    {
        // Reading a directory once is cheap and sixty times a second is not, which is the same
        // mistake that took a network down wearing different clothes. Repeated calls must be
        // served from the remembered answer.
        var first = TileCache.Stats();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++) TileCache.Stats();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"10,000 calls took {sw.ElapsedMilliseconds} ms, which means it is hitting the disk");
    }
}
