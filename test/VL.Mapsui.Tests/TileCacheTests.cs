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

    /// <summary>
    /// The folder a FileCache really writes to. It keeps it in a private field and exposes it
    /// nowhere, and the whole point of these tests is not to take the folder on trust.
    /// </summary>
    static string? RootOf(FileCache? cache)
        => cache?.GetType()
            .GetField("_directory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(cache) as string;

    [Fact]
    public void The_tile_source_really_carries_a_disk_cache()
    {
        // Attaching happens through a type test that could silently not apply, so it is worth
        // asserting the result rather than the intent.
        using var node = new OpenStreetMapLayerNode();
        var layer = node.Update(out _, out _, enabled: true);

        Assert.NotNull(AttachedCache(layer));
    }

    [Fact]
    public void Nothing_connected_caches_to_the_default_folder_and_nowhere_else()
    {
        // THE regression test for 2026-08-14. 444 tiles were written next to two repositories
        // because the folder that reached the node was not the one anybody meant, and every check
        // in the package reported success - they all asked whether the folder was usable, and it
        // was. This asks the only question that would have failed: where does the cache that is
        // actually attached write?
        using var node = new OpenStreetMapLayerNode();

        var layer = node.Update(out _, out var status, enabled: true);

        Assert.Equal(TileCache.DefaultDirectory, RootOf(AttachedCache(layer)));
        Assert.StartsWith(TileCache.DefaultDirectory, status);
    }

    [Fact]
    public void A_cache_that_is_off_is_not_the_same_as_no_cache_at_all()
    {
        // An unconnected pin and a switched-off cache node would both arrive as null if "off" were
        // absence, and the node could not tell "nobody said anything" from "somebody said no".
        // That ambiguity is what an empty Path IOBox exploited one level down.
        using var connected = new OpenStreetMapLayerNode();
        var off = connected.Update(out _, out var offStatus, enabled: true, cache: TileCache.Off(TileCache.DefaultDirectory));

        Assert.Null(AttachedCache(off));
        Assert.StartsWith("off", offStatus);

        using var unconnected = new OpenStreetMapLayerNode();
        var byDefault = unconnected.Update(out _, out _, enabled: true, cache: null);

        Assert.NotNull(AttachedCache(byDefault));
    }

    [Fact]
    public void Changing_the_cache_rebuilds_the_layer_once()
    {
        // The cache is attached at construction, so it belongs to the layer's identity. Without
        // that, switching it on would appear to do nothing until something else changed.
        using var node = new OpenStreetMapLayerNode();
        var off = TileCache.Off(TileCache.DefaultDirectory);

        for (int frame = 0; frame < 10; frame++) node.Update(out _, out _, enabled: true);
        for (int frame = 0; frame < 10; frame++) node.Update(out _, out _, enabled: true, cache: off);

        Assert.Equal(2, node.LayersBuilt);
    }

    [Fact]
    public void The_same_cache_across_frames_builds_one_layer()
    {
        // A cache node hands out the same instance every frame; a layer compares by reference. If
        // either half of that were wrong this would climb, and every rebuild starts a fresh round
        // of tile requests.
        var cacheNode = new TileCacheNode();
        using var layerNode = new OpenStreetMapLayerNode();

        for (int frame = 0; frame < 100; frame++)
            layerNode.Update(out _, out _, enabled: true, cache: cacheNode.Update(out _, out _, out _));

        Assert.Equal(1, layerNode.LayersBuilt);
        Assert.Equal(1, cacheNode.CachesBuilt);
    }

    // ── The folder, which now lives on one node ───────────────────────────────

    [Fact]
    public void Only_an_unconnected_pin_means_the_default_location()
    {
        // Null is what an unconnected pin hands a node, and it is the only thing that means "the
        // default". The empty strings are kept here as a reminder rather than as an intention: a
        // Path IOBox never produces one. It produces the document's own folder, absolute, which is
        // indistinguishable from a deliberate choice - and that is what wrote 444 tiles into two
        // repositories on 2026-08-14 with every check in the package reporting success.
        Assert.Equal(TileCache.DefaultDirectory, TileCache.Resolve(null));
        Assert.Equal(TileCache.DefaultDirectory, TileCache.Resolve(""));
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
    public void Changing_the_folder_rebuilds_the_cache_and_the_layer()
    {
        // The cache is attached at construction, so a new folder is a new cache and a new layer.
        // Without that, retargeting it would appear to do nothing.
        var cacheNode = new TileCacheNode();
        using var layerNode = new OpenStreetMapLayerNode();
        var a = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-a");
        var b = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-b");

        for (int frame = 0; frame < 10; frame++)
            layerNode.Update(out _, out _, enabled: true, cache: cacheNode.Update(out _, out _, out _, folder: new VLPath(a)));
        for (int frame = 0; frame < 10; frame++)
            layerNode.Update(out _, out _, enabled: true, cache: cacheNode.Update(out _, out _, out _, folder: new VLPath(b)));

        Assert.Equal(2, cacheNode.CachesBuilt);
        Assert.Equal(2, layerNode.LayersBuilt);
    }

    [Fact]
    public void The_same_folder_written_differently_does_not_rebuild()
    {
        // Paths compare case-insensitively on Windows, and whitespace is trimmed. Treating those
        // as different folders would rebuild the layer and refetch every tile for nothing.
        var node = new TileCacheNode();
        var dir = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-case");

        node.Update(out _, out _, out _, folder: new VLPath(dir));
        node.Update(out _, out _, out _, folder: new VLPath(dir.ToUpperInvariant()));
        node.Update(out _, out _, out _, folder: new VLPath("  " + dir + " "));

        Assert.Equal(1, node.CachesBuilt);
    }

    [Fact]
    public void The_status_pin_names_the_folder_actually_in_use()
    {
        var cacheNode = new TileCacheNode();
        using var layerNode = new OpenStreetMapLayerNode();
        var mine = Path.Combine(Path.GetTempPath(), "vl-mapsui-cache-status");

        var cache = cacheNode.Update(out var cacheStatus, out _, out _, folder: new VLPath(mine));
        layerNode.Update(out _, out var layerStatus, enabled: true, cache: cache);

        // Both nodes report, and they cannot disagree: there is one folder and one cache object.
        Assert.Contains(mine, cacheStatus);
        Assert.Contains("tiles", cacheStatus);
        Assert.Contains(mine, layerStatus);
    }

    [Fact]
    public void An_impossible_folder_says_so_rather_than_falling_back()
    {
        // Falling back to the default would put files somewhere nobody asked for with nothing to
        // say so, which is the worse of the two failures.
        var cacheNode = new TileCacheNode();
        using var layerNode = new OpenStreetMapLayerNode();

        var cache = cacheNode.Update(out var status, out _, out _, folder: new VLPath(@"Z:\no\such\drive\tiles"));
        var layer = layerNode.Update(out _, out var layerStatus, enabled: true, cache: cache);

        Assert.False(cache.IsOn);
        Assert.NotNull(layer);                       // the map still works, just uncached
        Assert.StartsWith("cannot cache", status);
        Assert.Null(AttachedCache(layer));
        Assert.DoesNotContain(TileCache.DefaultDirectory, status);

        // And the layer does not quietly substitute the default for a folder that failed, which is
        // the whole point: a cache that could not be created is a *value* saying so, not a null
        // that reads the same as "nothing was connected".
        Assert.DoesNotContain(TileCache.DefaultDirectory, layerStatus);
    }

    [Fact]
    public void A_relative_folder_is_refused_rather_than_guessed_at()
    {
        // A Path IOBox stores a relative path whenever it can and hides that from you, which the
        // Gray Book states outright. If one reaches the node there is no honest way to root it, and
        // CreateDirectory would resolve it against whatever the working directory happens to be -
        // tiles written somewhere nobody could predict, silently.
        var node = new TileCacheNode();

        var cache = node.Update(out var status, out _, out _, folder: new VLPath(@"tiles\"));

        Assert.False(cache.IsOn);
        Assert.Contains("relative", status);
        Assert.DoesNotContain(TileCache.DefaultDirectory, status);
    }

    [Fact]
    public void A_folder_that_cannot_be_used_is_not_retried_every_frame()
    {
        // The failure has to be remembered like any other cache identity, or the node rebuilds
        // sixty times a second - hitting the disk each time - which is the shape of the bug this
        // package was rebuilt to undo, wearing an error message.
        var node = new TileCacheNode();

        for (int frame = 0; frame < 100; frame++)
            node.Update(out _, out _, out _, folder: new VLPath(@"Z:\no\such\drive\tiles"));

        Assert.Equal(1, node.CachesBuilt);
    }

    // ── Seeing where tiles go without switching a layer on ────────────────────

    [Fact]
    public void The_TileCache_node_reports_the_default_when_given_nothing()
    {
        // The reason this node exists: the default cannot be the pin's initial value, so it has to
        // be reachable some other way. Unconnected in, default out.
        var cache = new TileCacheNode().Update(out var status, out _, out _);

        Assert.Equal(TileCache.DefaultDirectory, cache.Folder);
        Assert.StartsWith(TileCache.DefaultDirectory, status);
    }

    [Fact]
    public void The_TileCache_node_measures_what_is_there_without_fetching()
    {
        // It reads the disk and never the network, which is what makes it safe to leave in a patch
        // with the layer switched off.
        var dir = Path.Combine(Path.GetTempPath(), "vl-mapsui-measured");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "tile.png"), new byte[2048]);

        new TileCacheNode().Update(out _, out var tiles, out var sizeMB, folder: new VLPath(dir));

        Assert.True(tiles > 0);
        Assert.True(sizeMB > 0f);
    }

    [Fact]
    public void A_cache_that_is_off_still_says_how_much_is_on_disk()
    {
        // Switching it off is not a reason to stop answering "how much have I got cached?", and it
        // is the question people switch it off to think about.
        var dir = Path.Combine(Path.GetTempPath(), "vl-mapsui-off-but-measured");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "tile.png"), new byte[4096]);

        var cache = new TileCacheNode().Update(out var status, out var tiles, out _,
            enabled: false, folder: new VLPath(dir));

        Assert.False(cache.IsOn);         // off is a value, not an absence
        Assert.Null(cache.Problem);       // and "off" is not the same as "that folder did not work"
        Assert.StartsWith("off", status);
        Assert.True(tiles > 0);
    }

    [Fact]
    public void The_TileCache_node_reads_the_disk_no_harder_than_the_status_pin_does()
    {
        // VL evaluates it on every frame of every patch it sits in. The throttle is what makes that
        // safe; without it this node would be a directory walk at 60fps.
        var node = new TileCacheNode();
        node.Update(out _, out _, out _);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int frame = 0; frame < 10_000; frame++) node.Update(out _, out _, out _);
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
