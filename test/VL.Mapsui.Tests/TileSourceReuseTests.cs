using System;
using System.Collections.Generic;
using System.IO;
using BruTile.Cache;
using BruTile.Web;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// A tile source is built once per URL and reused. Layers are cheap; sources are not.
/// </summary>
/// <remarks>
/// **THE regression test for 2026-08-17, second finding.** Asked whether destroying and rebuilding
/// the layer on every basemap switch was best practice, the answer turned out to be "that part is
/// fine — Leaflet does the same — but you are also rebuilding the source, and that leaks."
///
/// <code>
/// // BruTile 5.0.6, Web/HttpTileSource.cs:15
/// private readonly HttpClient _httpClient = HttpClientBuilder.Build();
/// </code>
///
/// <c>HttpTileSource</c> is not <c>IDisposable</c> and nothing releases that client, so every
/// switch left behind a connection pool for a finalizer to find, with pooled sockets outliving even
/// that. It is the 17,000-connection incident's mechanism at a slower clock — once per switch
/// instead of once per frame. A person clicking six times never notices; an installation, or a
/// preset index wired to an LFO, does.
///
/// These count OBJECTS rather than sockets, because a unit test cannot see a connection pool. The
/// socket count is checked by hand in vvvv; the object count is what can be defended automatically.
///
/// Negative-tested — remove the reuse in <c>XyzTileLayerNode.Build</c> and
/// <c>Switching_back_and_forth_builds_each_source_once</c> fails with 20 sources instead of 2.
/// </remarks>
public class TileSourceReuseTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(), "VL.Mapsui.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a temp folder that outlives the run is not a test failure */ }
    }

    const string Topo  = "https://tile.opentopomap.org/{z}/{x}/{y}.png";
    const string Cycle = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png";

    static HttpTileSource? SourceOf(global::Mapsui.Layers.ILayer? layer)
        => (layer as global::Mapsui.Tiling.Layers.TileLayer)?.TileSource as HttpTileSource;

    static string? RootOf(IPersistentCache<byte[]>? cache)
        => (cache as FileCache)?.GetType()
            .GetField("_directory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(cache) as string;

    [Fact]
    public void Switching_back_and_forth_builds_each_source_once()
    {
        // The leak, at the scale a patch reaches in a minute of play.
        var disk = TileCache.Create(_root);
        using var node = new XyzTileLayerNode();

        // Reference identity: two sources are "the same" only if they are the same object, because
        // it is the object that owns the HttpClient.
        var seen = new HashSet<HttpTileSource>(ReferenceEqualityComparer.Instance as IEqualityComparer<HttpTileSource>);

        for (var i = 0; i < 10; i++)
        {
            var url = i % 2 == 0 ? Topo : Cycle;
            var layer = node.Update(out _, out _, urlTemplate: url, enabled: true, cache: disk);
            var source = SourceOf(layer);
            Assert.NotNull(source);
            seen.Add(source!);
        }

        Assert.Equal(2, seen.Count);      // ← was 10 before the fix, one per switch
    }

    [Fact]
    public void A_rebuilt_layer_is_a_new_object_but_the_source_is_not()
    {
        // The asymmetry the whole fix rests on, asserted rather than assumed.
        var disk = TileCache.Create(_root);
        using var node = new XyzTileLayerNode();

        var first  = node.Update(out _, out _, urlTemplate: Topo,  enabled: true, cache: disk);
        node.Update(out _, out _, urlTemplate: Cycle, enabled: true, cache: disk);
        var third  = node.Update(out _, out _, urlTemplate: Topo,  enabled: true, cache: disk);

        Assert.NotSame(first, third);                       // the layer really was rebuilt
        Assert.Same(SourceOf(first), SourceOf(third));      // the expensive half was not
    }

    [Fact]
    public void A_reused_source_follows_the_cache_it_is_given_now()
    {
        // A source outlives the TileDiskCache it first met. If PersistentCache were only assigned
        // when the source is constructed, the second cache here would be ignored in silence.
        var first  = TileCache.Create(Path.Combine(_root, "one"));
        var second = TileCache.Create(Path.Combine(_root, "two"));
        using var node = new XyzTileLayerNode();

        var a = node.Update(out _, out _, urlTemplate: Topo, enabled: true, cache: first);
        var b = node.Update(out _, out _, urlTemplate: Topo, enabled: true, cache: second);

        Assert.Same(SourceOf(a), SourceOf(b));
        Assert.StartsWith(Path.Combine(_root, "two"), RootOf(SourceOf(b)!.PersistentCache)!);
    }

    [Fact]
    public void Switching_the_cache_off_detaches_it_from_a_reused_source()
    {
        // The trap inside the fix. A reused source arrives carrying last time's FileCache, so an
        // early return on "cache is off" would leave it attached: a setting that reports off and
        // keeps writing. Exactly the class of silent disagreement TileDiskCache exists to prevent.
        var disk = TileCache.Create(_root);
        using var node = new XyzTileLayerNode();

        var on  = node.Update(out _, out _, urlTemplate: Topo, enabled: true, cache: disk);
        Assert.IsType<FileCache>(SourceOf(on)!.PersistentCache);

        var off = node.Update(out _, out var status, urlTemplate: Topo, enabled: true,
            cache: TileCache.Off(_root));

        Assert.Same(SourceOf(on), SourceOf(off));
        Assert.IsNotType<FileCache>(SourceOf(off)!.PersistentCache);
        Assert.StartsWith("off", status);
    }

    [Fact]
    public void Toggling_Enabled_does_not_build_a_second_source()
    {
        // The OpenStreetMap half. Enabled off releases the layer, so turning it back on rebuilds -
        // and used to rebuild the source with it. Somebody automating that pin would have leaked a
        // connection pool per toggle.
        var disk = TileCache.Create(_root);
        using var node = new OpenStreetMapLayerNode();

        var first = node.Update(out _, out _, enabled: true, cache: disk);
        node.Update(out _, out _, enabled: false, cache: disk);
        var again = node.Update(out _, out _, enabled: true, cache: disk);

        Assert.NotNull(SourceOf(first));
        Assert.NotSame(first, again);
        Assert.Same(SourceOf(first), SourceOf(again));
    }
}
