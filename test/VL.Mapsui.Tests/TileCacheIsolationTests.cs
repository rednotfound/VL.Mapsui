using System;
using System.IO;
using BruTile;
using BruTile.Cache;
using BruTile.Web;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// Two tile sources must never read each other's tiles.
/// </summary>
/// <remarks>
/// **THE regression test for 2026-08-17.** BruTile's <c>FileCache</c> keys a tile on nothing but
/// <c>{level}/{col}/{row}.png</c>, and every layer node was handed the same one. So the second
/// service you pointed at never fetched anything: it asked for tile 7/63/41, the first service's
/// copy was already on disk, and it got that instead.
///
/// What made it expensive to see is that **everything reported success**. The layer really was
/// rebuilt, `Layers Built` counted up, `Cache Status` named a real folder, no exception, no warning
/// — and the picture did not change, because no request was ever made. It was found by writing a
/// tutorial whose whole lesson was switching basemaps, not by any of the 218 tests, none of which
/// had ever used two sources and one cache together.
///
/// It is also a licensing fault rather than only a visual one: `Attribution` says one provider
/// while the pixels came from another, so a patch can credit the wrong service in good faith.
///
/// Negative-tested — revert <c>CacheFor</c> to hand back the shared <c>Cache</c> and
/// <c>Two_sources_never_read_each_others_tiles</c> fails on the first assertion.
/// </remarks>
public class TileCacheIsolationTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(), "VL.Mapsui.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a temp folder that outlives the run is not a test failure */ }
    }

    static string? RootOf(FileCache? cache)
        => cache?.GetType()
            .GetField("_directory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(cache) as string;

    static FileCache? AttachedCache(global::Mapsui.Layers.ILayer? layer)
        => ((layer as global::Mapsui.Tiling.Layers.TileLayer)?.TileSource as HttpTileSource)
            ?.PersistentCache as FileCache;

    const string Topo  = "https://tile.opentopomap.org/{z}/{x}/{y}.png";
    const string Cycle = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png";

    [Fact]
    public void Two_sources_never_read_each_others_tiles()
    {
        // The bug, reproduced at the level it actually happened: write a tile as one source, then
        // ask for the same tile as another. Nothing here touches the network - the bytes are made
        // up, which is the point. A cache hit is a cache hit whatever the bytes mean.
        var disk = TileCache.Create(_root);

        var topo  = disk.CacheFor(Topo);
        var cycle = disk.CacheFor(Cycle);
        Assert.NotNull(topo);
        Assert.NotNull(cycle);

        var tile = new TileIndex(63, 41, 7);
        topo!.Add(tile, new byte[] { 1, 2, 3, 4 });

        Assert.Null(cycle!.Find(tile));        // ← the whole bug. Was: the four bytes above.
        Assert.NotNull(topo.Find(tile));       // and the source that wrote it still gets it back
    }

    [Fact]
    public void The_same_source_lands_in_the_same_folder_every_time()
    {
        // Guards the choice of SHA-256 over string.GetHashCode. .NET randomises string hash codes
        // PER PROCESS, so a GetHashCode-derived folder name would change on every launch: the cache
        // would grow for ever and never once hit, while reporting a perfectly healthy folder. This
        // test cannot span processes, so it also pins the expected shape below.
        var disk = TileCache.Create(_root);

        Assert.Equal(RootOf(disk.CacheFor(Topo)), RootOf(disk.CacheFor(Topo)));
        Assert.NotEqual(RootOf(disk.CacheFor(Topo)), RootOf(disk.CacheFor(Cycle)));
    }

    [Fact]
    public void A_folder_name_is_readable_at_the_front_and_legal_throughout()
    {
        // Readable, because the answer to "whose tiles are these?" should be visible in Explorer.
        // Legal, because a URL contains { } / : and a folder name may not.
        var slug = TileCache.Slug(Topo);

        Assert.StartsWith("tile.opentopomap.org-", slug);
        Assert.Equal(-1, slug.IndexOfAny(Path.GetInvalidFileNameChars()));

        // One host, two styles: the host alone would collide, so the hash is doing real work.
        Assert.NotEqual(
            TileCache.Slug("https://a.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png"),
            TileCache.Slug("https://a.tile.openstreetmap.fr/osmfr/{z}/{x}/{y}.png"));
    }

    [Fact]
    public void OpenStreetMap_and_XYZ_do_not_share_a_folder()
    {
        // Through the nodes rather than the helper, because the fix is only worth anything if both
        // consumers actually call it. One of them not calling it is exactly how this shipped.
        var disk = TileCache.Create(_root);

        using var osm = new OpenStreetMapLayerNode();
        var osmLayer = osm.Update(out _, out _, enabled: true, cache: disk);

        using var xyz = new XyzTileLayerNode();
        var xyzLayer = xyz.Update(out _, out _, urlTemplate: Topo, enabled: true, cache: disk);

        var a = RootOf(AttachedCache(osmLayer));
        var b = RootOf(AttachedCache(xyzLayer));

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
        Assert.StartsWith(_root, a!);
        Assert.StartsWith(_root, b!);
    }

    [Fact]
    public void Two_XYZ_layers_with_different_templates_do_not_share_a_folder()
    {
        // The case the tutorial hit: one node, the template changed. The layer rebuilds, so this
        // is two Build calls, and they must not land in the same place.
        var disk = TileCache.Create(_root);

        using var node = new XyzTileLayerNode();

        node.Update(out _, out _, urlTemplate: Topo, enabled: true, cache: disk);
        var first = RootOf(AttachedCache(node.Update(out _, out _, urlTemplate: Topo, enabled: true, cache: disk)));

        var second = RootOf(AttachedCache(node.Update(out _, out _, urlTemplate: Cycle, enabled: true, cache: disk)));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_cache_that_is_off_hands_out_nothing_for_any_source()
    {
        // "Off" stays off per source too. Otherwise switching a basemap would quietly switch the
        // cache back on, which is the kind of thing nobody would look for.
        var off = TileCache.Off(_root);

        Assert.False(off.IsOn);
        Assert.Null(off.CacheFor(Topo));
        Assert.Null(off.CacheFor(Cycle));
    }
}
