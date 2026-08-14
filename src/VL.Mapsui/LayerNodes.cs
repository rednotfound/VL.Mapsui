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
    // not acceptable for anything beyond a first trial. Shared with the XYZ node: any tile server
    // deserves to know who is asking, and a shared constant means one place to change it.
    internal const string UserAgent = "VL.Mapsui/0.0.1-alpha (+https://github.com/rednotfound/VL.Mapsui)";

    TileLayer? _layer;
    TileDiskCache? _attached;
    string _status = "off";

    /// <summary>Layers built by this node.</summary>
    internal int LayersBuilt { get; private set; }

    /// <summary>Where this layer's tiles go, or why they are not kept.</summary>
    /// <remarks>
    /// Read every frame rather than remembered from the build, so the count grows while tiles
    /// arrive. The disk walk behind it is throttled to once every couple of seconds.
    /// </remarks>
    internal string CacheStatus => _layer is not null && _attached is { IsOn: true }
        ? TileCache.Describe(_attached.Folder)
        : _status;

    /// <summary>
    /// The layer to put on a map, or nothing while Enabled is off.
    /// </summary>
    /// <remarks>
    /// Enabled defaults to off because opening a document in vvvv runs it. A layer that fetches
    /// the moment a patch is opened gives whoever opened it no chance to decline.
    ///
    /// **Cache takes the output of a <c>TileCache</c> node, and nothing else decides where tiles
    /// go.** Leave it unconnected for the default location; wire a TileCache node to move the
    /// folder or switch caching off. Keeping tiles that were drawn is what OSM's policy asks for;
    /// what it forbids is the opposite, fetching tiles nobody is looking at.
    ///
    /// There is no folder pin here on purpose. It used to be one, typed as a Path, with "leave it
    /// empty for the default" written on it — and an empty Path IOBox is not empty. VL resolves it
    /// against the document and hands the node the patch's own folder, so 444 tiles were written
    /// next to two repositories while every guard reported success. A folder is now decided in one
    /// place, by a node whose whole job that is.
    ///
    /// Cache Status reports the folder this layer actually writes to and how much is in it, or the
    /// reason nothing is kept. A folder that cannot be used does **not** silently fall back to the
    /// default: files appearing somewhere nobody asked for, with nothing to say so, is the worse
    /// outcome.
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
        TileDiskCache? cache = null)
    {
        if (!enabled)
        {
            Release();
            layersBuilt = LayersBuilt;
            cacheStatus = CacheStatus;
            return null;
        }

        // Nothing connected means the default cache, which is the policy-compliant answer: not
        // caching would refetch the same view on every restart. A TileCache node that is switched
        // off hands over a cache that is off rather than null, so the two cases stay apart.
        var wanted = cache ?? TileCache.Default();

        // Only a change to what the tile source *is* rebuilds, and the cache is part of that: it
        // is attached at construction. Compared by reference, because that is what a cache's
        // identity is now - a node hands out the same instance every frame. Where the map looks is
        // the navigator's business and never reaches this node.
        if (_layer is null || !ReferenceEquals(wanted, _attached))
        {
            Release();
            _layer = Build(wanted, out _status);
            _attached = wanted;
            LayersBuilt++;
        }

        layersBuilt = LayersBuilt;
        cacheStatus = CacheStatus;
        return _layer;
    }

    static TileLayer Build(TileDiskCache cache, out string status)
    {
        var layer = OpenStreetMap.CreateTileLayer(UserAgent);

        // Switched off, or a folder that did not work. Both arrive as a cache that is not on, and
        // both are reported rather than quietly replaced by the default.
        if (!cache.IsOn)
        {
            status = TileCache.Describe(cache);
            return layer;
        }

        // Mapsui's factory takes a user agent and nothing else, so the disk cache is attached
        // afterwards. Keeping Mapsui's own definition of the OSM source is worth more than
        // rebuilding one here out of BruTile primitives.
        if (layer.TileSource is not BruTile.Web.HttpTileSource http)
        {
            status = "cannot cache: the tile source is not an HttpTileSource";
            return layer;
        }

        http.PersistentCache = cache.Cache;
        status = cache.Folder;      // CacheStatus re-reads the size every frame from here on
        return layer;
    }

    void Release()
    {
        // AbortFetch before Dispose: a request in flight otherwise outlives the layer that asked
        // for it, which is how connections accumulate.
        _layer?.AbortFetch();
        _layer?.Dispose();
        _layer = null;
        _attached = null;
        _status = "off";
    }

    /// <summary>Releases the layer and aborts any tile fetch still in flight.</summary>
    public void Dispose() => Release();
}
