using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using VL.NetTopologySuite;

namespace VL.Mapsui;

// The basemap under VL.NetTopologySuite's GeometryEditor window (2026-11-16, step 3 of that
// package's SRID plan). VL.NetTopologySuite fetches nothing and may reference nothing of ours, so it
// asks for an IBasemap and this package, when loaded, registers this one (see Initializer.cs) - the
// way VL.PROJ offers its CRS catalogue.
//
// The tiles are the OpenStreetMap layer's own: Mapsui's OSM source with this package's User-Agent,
// writing to and reading from the same disk folder as the OpenStreetMap layer (TileCache's default,
// "https://tile.openstreetmap.org/standard"), so a place seen on a map is there in the editor
// offline and the other way round. OSM's tile usage policy is kept the same way: a named
// User-Agent, at most two downloads at a time, nothing fetched that is not on screen, a week on
// disk. A tile that failed is not asked for again for half a minute, so an editor open without a
// network does not ask every frame.
//
// TryGetTile is called every frame by the window and never waits: a tile in memory is returned, any
// other is fetched in the background (BruTile looks in the disk cache first) and returned when the
// window asks again.

/// <summary>OpenStreetMap tiles for VL.NetTopologySuite's GeometryEditor window. Internal.</summary>
internal sealed class OsmBasemap : IBasemap
{
    public static readonly OsmBasemap Instance = new();

    private const int Kept = 512;                 // about 10 MB of tiles in memory
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly Lazy<ITileSource?> _source = new(CreateSource);
    private readonly ConcurrentDictionary<(int Z, int X, int Y), byte[]> _ready = new();
    private readonly ConcurrentQueue<(int Z, int X, int Y)> _order = new();
    private readonly ConcurrentDictionary<(int Z, int X, int Y), byte> _inFlight = new();
    private readonly ConcurrentDictionary<(int Z, int X, int Y), DateTime> _failed = new();
    private readonly SemaphoreSlim _twoAtATime = new(2, 2);

    public string Attribution => "© OpenStreetMap contributors";

    public int MaxZoom => 19;

    public byte[]? TryGetTile(int zoom, int x, int y)
    {
        if (zoom < 0 || zoom > MaxZoom || x < 0 || y < 0 || x >= 1 << zoom || y >= 1 << zoom) return null;
        var key = (zoom, x, y);
        if (_ready.TryGetValue(key, out var bytes)) return bytes;
        if (_failed.TryGetValue(key, out var until) && DateTime.UtcNow < until) return null;
        if (_inFlight.TryAdd(key, 0)) _ = FetchAsync(key);
        return null;
    }

    private async Task FetchAsync((int Z, int X, int Y) key)
    {
        try
        {
            if (_source.Value is not { } source) return;
            await _twoAtATime.WaitAsync().ConfigureAwait(false);
            try
            {
                var info = new TileInfo { Index = new TileIndex(key.X, key.Y, key.Z) };
                var bytes = await source.GetTileAsync(info).ConfigureAwait(false);
                if (bytes is { Length: > 0 }) Keep(key, bytes);
                else _failed[key] = DateTime.UtcNow + RetryAfter;
            }
            finally { _twoAtATime.Release(); }
        }
        catch (Exception)
        {
            _failed[key] = DateTime.UtcNow + RetryAfter;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private void Keep((int Z, int X, int Y) key, byte[] bytes)
    {
        if (!_ready.TryAdd(key, bytes)) return;
        _failed.TryRemove(key, out _);
        _order.Enqueue(key);
        while (_order.Count > Kept && _order.TryDequeue(out var old)) _ready.TryRemove(old, out _);
    }

    // Mapsui's own OSM definition with this package's User-Agent, and the OpenStreetMap layer's
    // default disk cache. Built on first use, never at load: registering costs nothing until an
    // editor window actually shows a basemap.
    private static ITileSource? CreateSource()
    {
        var source = global::Mapsui.Tiling.OpenStreetMap.CreateTileLayer(OpenStreetMapLayerNode.UserAgent).TileSource;
        if (source is BruTile.Web.HttpTileSource http && TileCache.Default() is { IsOn: true } cache)
            http.PersistentCache = cache.CacheFor(OpenStreetMapLayerNode.CacheKey)!;
        return source;
    }

    /// <summary>For tests: where a tile would be fetched from, without fetching it.</summary>
    internal static Uri? UriOf(int zoom, int x, int y)
        => Instance._source.Value is BruTile.Web.HttpTileSource http
            ? http.GetUri(new TileInfo { Index = new TileIndex(x, y, zoom) })
            : null;
}
