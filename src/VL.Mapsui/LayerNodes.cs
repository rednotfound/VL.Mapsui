using System;
using VL.Core.Import;

// global:: throughout this file: our own namespace is VL.Mapsui, so a bare "Mapsui.Tiling"
// binds to VL.Mapsui.Tiling and does not exist.
using ILayer = global::Mapsui.Layers.ILayer;
using TileLayer = global::Mapsui.Tiling.Layers.TileLayer;
using OpenStreetMap = global::Mapsui.Tiling.OpenStreetMap;

// A path in VL is its own type, not a string: 54 members of VL.CoreLib take VL.Lib.IO.Path, and a
// Path IOBox opens a directory chooser on SHIFT+rightclick. A string pin would make the author type
// it out.
using VLPath = VL.Lib.IO.Path;

namespace VL.Mapsui;

/// <summary>
/// An OpenStreetMap tile layer. Nothing is fetched until Enabled is switched on.
/// Uses Mapsui and BruTile; tiles are © OpenStreetMap contributors (ODbL).
/// </summary>
/// <remarks>
/// **A process node, and that is not a style choice.** A tile layer owns HTTP connections and a
/// disk cache. As a plain static method it would be evaluated once per frame, which is how an
/// earlier version of this package built sixty tile layers a second, exhausted a machine's
/// ephemeral ports and took a home network down.
///
/// Hand the output to a <c>Map</c> node. Two of them can go into one map, and a patch can put
/// its own layers alongside; that is the point of it being a separate node rather than
/// something hidden inside a single map-shaped thing.
/// </remarks>
[ProcessNode(Name = "OpenStreetMap", Category = "Mapsui.Layers")]
public class OpenStreetMapLayerNode : IDisposable
{
    // OSM's tile usage policy requires a User-Agent naming the application. The default one is
    // not acceptable for anything beyond a first trial.
    const string UserAgent = "VL.Mapsui/0.0.1-alpha (+https://github.com/rednotfound/VL.Mapsui)";

    TileLayer? _layer;
    bool _cache;
    string _folder = string.Empty;
    string _status = "off";

    /// <summary>Layers built by this node.</summary>
    internal int LayersBuilt { get; private set; }

    /// <summary>Where the cache went and how big it is, or why it is off.</summary>
    internal string CacheStatus => _cache && _layer is not null
        ? (_status.StartsWith("cannot", StringComparison.Ordinal) ? _status : TileCache.Describe(_status))
        : _status;

    /// <summary>
    /// The layer to put on a map, or nothing while Enabled is off.
    /// </summary>
    /// <remarks>
    /// Enabled defaults to off because opening a document in vvvv runs it. A layer that fetches
    /// the moment a patch is opened gives whoever opened it no chance to decline.
    ///
    /// Cache To Disk keeps tiles that were drawn, which is what OSM's policy asks for; what it
    /// forbids is the opposite, fetching tiles nobody is looking at. **Cache Folder is a pin
    /// because where a node writes files is the patch author's business** — beside the project so
    /// it travels with it, on a fast disk, shared between patches. Leave it empty for
    /// %LOCALAPPDATA%\VL.Mapsui\tiles; <c>CacheFolder</c> shows what that resolves to.
    ///
    /// It is a Path rather than a string so the IOBox offers a directory chooser on
    /// SHIFT+rightclick. The default is deliberately *not* the pin's initial value: it cannot be
    /// (a C# default has to be a compile-time constant, CS1736) and it should not be, since a
    /// literal would ship one machine's path inside the node.
    ///
    /// Cache Status reports the folder actually in use and how much is in it, or the reason the
    /// cache is off. A path that cannot be used does **not** silently fall back to the default:
    /// files appearing somewhere nobody asked for, with nothing to say so, is the worse outcome.
    ///
    /// **Watch Layers Built.** It should settle at 1 and stay there. A number that climbs frame
    /// after frame means an input is changing every frame, and every rebuild starts a fresh
    /// round of tile requests — the failure that once exhausted a machine's ephemeral ports and
    /// took a home network down. It is an output pin rather than something hidden in an overlay
    /// so a patch can watch it, or act on it.
    /// </remarks>
    public ILayer? Update(
        out int layersBuilt,
        out string cacheStatus,
        bool enabled = false,
        bool cacheToDisk = true,
        VLPath? cacheFolder = null)
    {
        if (!enabled)
        {
            Release();
            layersBuilt = LayersBuilt;
            cacheStatus = CacheStatus;
            return null;
        }

        // Only a change to what the tile source *is* rebuilds, and the cache folder is part of
        // that: it is attached at construction. Where the map looks is the navigator's business
        // and never reaches this node.
        var folder = TileCache.Resolve(cacheFolder?.Value);
        if (_layer is null || cacheToDisk != _cache || !string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
        {
            Release();
            _layer = Build(cacheToDisk, folder, out _status);
            _cache = cacheToDisk;
            _folder = folder;
            LayersBuilt++;
        }

        layersBuilt = LayersBuilt;
        cacheStatus = CacheStatus;
        return _layer;
    }

    static TileLayer Build(bool cache, string folder, out string status)
    {
        var layer = OpenStreetMap.CreateTileLayer(UserAgent);
        status = "off";

        // Mapsui's factory takes a user agent and nothing else, so the disk cache is attached
        // afterwards. Keeping Mapsui's own definition of the OSM source is worth more than
        // rebuilding one here out of BruTile primitives.
        if (!cache) return layer;

        if (layer.TileSource is not BruTile.Web.HttpTileSource http)
        {
            status = "cannot cache: the tile source is not an HttpTileSource";
            return layer;
        }

        var fileCache = TileCache.TryCreate(folder, out var problem);
        if (fileCache is null)
        {
            status = $"cannot cache to {folder}: {problem}";
            return layer;
        }

        http.PersistentCache = fileCache;
        status = folder;
        return layer;
    }

    void Release()
    {
        // AbortFetch before Dispose: a request in flight otherwise outlives the layer that asked
        // for it, which is how connections accumulate.
        _layer?.AbortFetch();
        _layer?.Dispose();
        _layer = null;
        _cache = false;
        _folder = string.Empty;
        _status = "off";
    }

    /// <summary>Releases the layer and aborts any tile fetch still in flight.</summary>
    public void Dispose() => Release();
}
