using System;
using System.IO;
using System.Linq;
using BruTile.Cache;
using BruTile.Web;
using VL.Mapsui;

using VLPath = VL.Lib.IO.Path;

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
        var layer = node.Update(out _, out _, enabled: true, cacheToDisk: true);

        Assert.NotNull(AttachedCache(layer));
    }

    [Fact]
    public void Turning_the_cache_off_leaves_the_source_without_one()
    {
        using var node = new OpenStreetMapLayerNode();
        var layer = node.Update(out _, out var status, enabled: true, cacheToDisk: false);

        Assert.Null(AttachedCache(layer));
        Assert.Equal("off", status);
    }

    [Fact]
    public void Changing_the_cache_setting_rebuilds_the_layer_once()
    {
        // The cache is attached at construction, so it belongs to the layer's identity. Without
        // that, switching it on would appear to do nothing until something else changed.
        using var node = new OpenStreetMapLayerNode();

        for (int frame = 0; frame < 10; frame++) node.Update(out _, out _, enabled: true, cacheToDisk: true);
        for (int frame = 0; frame < 10; frame++) node.Update(out _, out _, enabled: true, cacheToDisk: false);

        Assert.Equal(2, node.LayersBuilt);
    }

    // ── The folder pin ────────────────────────────────────────────────────────

    [Fact]
    public void An_empty_folder_pin_means_the_default_location()
    {
        // Empty has to keep working out of the box, or the pin becomes a chore rather than a
        // choice.
        Assert.Equal(TileCache.DefaultDirectory, TileCache.Resolve(""));
        Assert.Equal(TileCache.DefaultDirectory, TileCache.Resolve(null));
        Assert.Equal(TileCache.DefaultDirectory, TileCache.Resolve("   "));
    }

    [Fact]
    public void A_folder_on_the_pin_is_used_verbatim()
    {
        var mine = Path.Combine(Path.GetTempPath(), "vl-mapsui-test-cache");
        Assert.Equal(mine, TileCache.Resolve(mine));
        Assert.Equal(mine, TileCache.Resolve("  " + mine + "  "));   // trimmed, not rejected
    }

    [Fact]
    public void The_default_lives_outside_any_repository()
    {
        // Under LOCALAPPDATA on purpose: it cannot be committed by accident, and deleting one
        // folder resets everything.
        var dir = TileCache.DefaultDirectory;

        Assert.Contains("VL.Mapsui", dir);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), dir);
        Assert.False(dir.Contains("vl-mapsui", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Changing_the_folder_rebuilds_the_layer()
    {
        // The cache is attached at construction, so the folder is part of the layer's identity.
        // Without that, retargeting it would appear to do nothing.
        using var node = new OpenStreetMapLayerNode();
        var a = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-a");
        var b = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-b");

        for (int frame = 0; frame < 10; frame++) node.Update(out _, out _, enabled: true, cacheFolder: new VLPath(a));
        for (int frame = 0; frame < 10; frame++) node.Update(out _, out _, enabled: true, cacheFolder: new VLPath(b));

        Assert.Equal(2, node.LayersBuilt);
    }

    [Fact]
    public void The_same_folder_written_differently_does_not_rebuild()
    {
        // Paths compare case-insensitively on Windows, and whitespace is trimmed. Treating those
        // as different folders would rebuild the layer and refetch every tile for nothing.
        using var node = new OpenStreetMapLayerNode();
        var dir = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-case");

        node.Update(out _, out _, enabled: true, cacheFolder: new VLPath(dir));
        node.Update(out _, out _, enabled: true, cacheFolder: new VLPath(dir.ToUpperInvariant()));
        node.Update(out _, out _, enabled: true, cacheFolder: new VLPath("  " + dir + " "));

        Assert.Equal(1, node.LayersBuilt);
    }

    [Fact]
    public void The_status_pin_names_the_folder_actually_in_use()
    {
        using var node = new OpenStreetMapLayerNode();
        var mine = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-status");

        node.Update(out _, out var status, enabled: true, cacheFolder: new VLPath(mine));

        Assert.Contains(mine, status);
        Assert.Contains("tiles", status);
    }

    [Fact]
    public void An_impossible_folder_says_so_rather_than_falling_back()
    {
        // Falling back to the default would put files somewhere nobody asked for with nothing to
        // say so, which is the worse of the two failures.
        using var node = new OpenStreetMapLayerNode();

        var layer = node.Update(out _, out var status, enabled: true, cacheFolder: new VLPath(@"Z:\no\such\drive\tiles"));

        Assert.NotNull(layer);                       // the map still works, just uncached
        Assert.StartsWith("cannot cache", status);
        Assert.Null(AttachedCache(layer));
        Assert.DoesNotContain(TileCache.DefaultDirectory, status);
    }

    [Fact]
    public void A_relative_folder_is_refused_rather_than_guessed_at()
    {
        // A Path IOBox stores a relative path whenever it can and hides that from you, which the
        // Gray Book states outright. If one reaches the node there is no honest way to root it, and
        // CreateDirectory would resolve it against whatever the working directory happens to be -
        // tiles written somewhere nobody could predict, silently.
        using var node = new OpenStreetMapLayerNode();

        var layer = node.Update(out _, out var status, enabled: true, cacheFolder: new VLPath(@"tiles\"));

        Assert.NotNull(layer);                       // the map still works, just uncached
        Assert.Contains("relative", status);
        Assert.DoesNotContain(TileCache.DefaultDirectory, status);
    }

    // ── Seeing the default without switching a layer on ───────────────────────

    [Fact]
    public void The_CacheFolder_node_reports_the_default_when_given_nothing()
    {
        // The reason this node exists: the default cannot be the pin's initial value, so it has to
        // be reachable some other way. Empty in, default out - the same rule the layer node uses.
        var shown = CacheNodes.CacheFolder(null, out _, out _);

        Assert.Equal(TileCache.DefaultDirectory, shown.Value);
    }

    [Fact]
    public void The_CacheFolder_node_echoes_a_folder_it_is_given()
    {
        var mine = Path.Combine(Path.GetTempPath(), "vl-mapsui-shown");

        var shown = CacheNodes.CacheFolder(new VLPath(mine), out _, out _);

        Assert.Equal(mine, shown.Value);
    }

    [Fact]
    public void The_CacheFolder_node_measures_what_is_there()
    {
        // Same numbers as the status pin, from the same throttled read, so the two cannot disagree.
        var dir = Path.Combine(Path.GetTempPath(), "vl-mapsui-measured");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "tile.png"), new byte[2048]);

        CacheNodes.CacheFolder(new VLPath(dir), out var tiles, out var sizeMB);

        Assert.True(tiles > 0);
        Assert.True(sizeMB > 0f);
    }

    [Fact]
    public void The_CacheFolder_node_reads_the_disk_no_harder_than_the_status_pin_does()
    {
        // It is a static method, so VL evaluates it on every frame of every patch it sits in. The
        // throttle is what makes that safe; without it this node would be a directory walk at 60fps.
        CacheNodes.CacheFolder(null, out _, out _);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int frame = 0; frame < 10_000; frame++) CacheNodes.CacheFolder(null, out _, out _);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 300,
            $"10,000 frames took {sw.ElapsedMilliseconds} ms, which means it is hitting the disk");
    }

    [Fact]
    public void Stats_survives_a_directory_that_does_not_exist_yet()
    {
        // Called from the render loop, so it has to survive the state before the first tile is
        // written rather than throwing into a frame.
        var (tiles, bytes) = TileCache.Stats(Path.Combine(Path.GetTempPath(), "vl-mapsui-never-created"));

        Assert.Equal(0, tiles);
        Assert.Equal(0L, bytes);
    }

    [Fact]
    public void Stats_does_not_walk_the_disk_on_every_call()
    {
        // Reading a directory once is cheap and sixty times a second is not, which is the same
        // mistake that took a network down wearing different clothes.
        var dir = TileCache.DefaultDirectory;
        TileCache.Stats(dir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++) TileCache.Stats(dir);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"10,000 calls took {sw.ElapsedMilliseconds} ms, which means it is hitting the disk");
    }

    [Fact]
    public void Two_folders_do_not_read_each_others_numbers()
    {
        // The throttle remembers per folder. A single remembered value would have two nodes
        // pointing at different caches reporting the same size, which is exactly the kind of
        // plausible-looking wrong number that is hard to notice.
        var withFiles = Path.Combine(Path.GetTempPath(), "vl-mapsui-two-a");
        var empty = Path.Combine(Path.GetTempPath(), "vl-mapsui-two-b");
        Directory.CreateDirectory(withFiles);
        Directory.CreateDirectory(empty);
        File.WriteAllBytes(Path.Combine(withFiles, "tile.png"), new byte[1024]);

        var a = TileCache.Stats(withFiles);
        var b = TileCache.Stats(empty);

        Assert.True(a.tiles > 0);
        Assert.Equal(0, b.tiles);
    }
}
