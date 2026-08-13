using System;
using VL.Core.Import;

// global:: throughout this file: our own namespace is VL.Mapsui, so a bare "Mapsui.Tiling"
// binds to VL.Mapsui.Tiling and does not exist.
using ILayer = global::Mapsui.Layers.ILayer;
using TileLayer = global::Mapsui.Tiling.Layers.TileLayer;
using OpenStreetMap = global::Mapsui.Tiling.OpenStreetMap;

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

    /// <summary>Layers built by this node.</summary>
    internal int LayersBuilt { get; private set; }

    /// <summary>
    /// The layer to put on a map, or nothing while Enabled is off.
    /// </summary>
    /// <remarks>
    /// Enabled defaults to off because opening a document in vvvv runs it. A layer that fetches
    /// the moment a patch is opened gives whoever opened it no chance to decline.
    ///
    /// Cache To Disk keeps tiles that were drawn under %LOCALAPPDATA%\VL.Mapsui\tiles, which is
    /// what OSM's policy asks for. What it forbids is the opposite: fetching tiles nobody is
    /// looking at.
    ///
    /// **Watch Layers Built.** It should settle at 1 and stay there. A number that climbs frame
    /// after frame means an input is changing every frame, and every rebuild starts a fresh
    /// round of tile requests — the failure that once exhausted a machine's ephemeral ports and
    /// took a home network down. It is an output pin rather than something hidden in an overlay
    /// so a patch can watch it, or act on it.
    /// </remarks>
    public ILayer? Update(out int layersBuilt, bool enabled = false, bool cacheToDisk = true)
    {
        if (!enabled)
        {
            Release();
            layersBuilt = LayersBuilt;
            return null;
        }

        // Only a change to what the tile source *is* rebuilds. Where the map looks is the
        // navigator's business and never reaches this node.
        if (_layer is null || cacheToDisk != _cache)
        {
            Release();
            _layer = Build(cacheToDisk);
            _cache = cacheToDisk;
            LayersBuilt++;
        }

        layersBuilt = LayersBuilt;
        return _layer;
    }

    static TileLayer Build(bool cache)
    {
        var layer = OpenStreetMap.CreateTileLayer(UserAgent);

        // Mapsui's factory takes a user agent and nothing else, so the disk cache is attached
        // afterwards. Keeping Mapsui's own definition of the OSM source is worth more than
        // rebuilding one here out of BruTile primitives.
        if (cache && layer.TileSource is BruTile.Web.HttpTileSource http)
            http.PersistentCache = TileCache.Create();

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
    }

    /// <summary>Releases the layer and aborts any tile fetch still in flight.</summary>
    public void Dispose() => Release();
}
