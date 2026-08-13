using System;
using Mapsui;
using Mapsui.Projections;
using Mapsui.Tiling;
using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// An OpenStreetMap map, ready to hand to a Renderer. Centre is WGS84, x first.
/// Nothing is fetched until Enabled is switched on.
/// Uses Mapsui; tiles are © OpenStreetMap contributors (ODbL).
/// </summary>
/// <remarks>
/// **This is a process node, and that is not a style choice.** A map owns a tile layer, which
/// owns HTTP connections. As a plain static method it was evaluated once per frame, so at 60fps
/// it built sixty tile layers a second, each starting its own fetches and none of them ever
/// released. That took out a home network: 17,000 TCP connections, the machine's 16,384
/// ephemeral ports exhausted, and DNS failing for every other program on it.
///
/// The same bug is why nothing was ever drawn. Tiles requested on frame N arrived after frame
/// N's map had already become garbage, so the layer was permanently busy and permanently empty.
/// One cause, both symptoms.
///
/// So the map is built once and rebuilt only when an input actually changes. Anything in this
/// package that acquires a connection, a file handle or a cache belongs in a class like this
/// one, never in a static method.
/// </remarks>
[ProcessNode(Name = "OpenStreetMap", Category = "Mapsui")]
public class OpenStreetMapNode : IDisposable
{
    // OSM's tile usage policy requires a User-Agent that identifies the application and offers
    // a way to get in touch. The default one is not acceptable for anything but a quick trial.
    const string UserAgent = "VL.Mapsui/0.0.1-spike (+https://github.com/rednotfound/vl-mapsui)";

    // Counts every map ever built in this process. The whole failure above would have been
    // obvious in three seconds from a number that climbed instead of stopping at one, and we
    // only found out when the network died. Shown in the diagnostics overlay, where a
    // process-wide total also catches a second node instance nobody noticed.
    static int _mapsBuilt;

    /// <summary>
    /// Maps built by this node alone. The overlay shows the process-wide total; this one is
    /// per instance so a test can assert on it without depending on what ran before.
    /// </summary>
    internal int MapsBuiltHere { get; private set; }

    /// <summary>The map currently held, so a test can inspect how it was wired up.</summary>
    internal Map? CurrentMap => _map;

    /// <summary>
    /// True once a rebuild has happened on two consecutive frames, which no deliberate action
    /// produces and every runaway does.
    /// </summary>
    /// <remarks>
    /// A plain count is the wrong alarm: switching Enabled off and on rebuilds, quite correctly,
    /// so a counter above one says nothing on its own. What cannot be done by hand is rebuilding
    /// twice in two frames.
    /// </remarks>
    internal bool RebuiltOnConsecutiveFrames { get; private set; }

    int _framesSinceBuild = int.MaxValue / 2;

    Map? _map;
    MapsuiLayer? _layer;

    // NaN and -1 cannot equal any first input, so the first enabled frame always builds.
    double _lon = double.NaN;
    double _lat = double.NaN;
    int _zoom = -1;
    bool _cache;

    /// <summary>
    /// Returns the layer to draw, or nothing while Enabled is off.
    /// Turn on Diagnostics to print Mapsui's viewport and layer state over the map.
    /// </summary>
    /// <remarks>
    /// Enabled defaults to off because in vvvv opening a document runs it. A map node that
    /// fetches the moment a patch is opened gives whoever opens it no chance to decline.
    /// </remarks>
    public global::VL.Skia.ILayer? Update(
        double centerLongitude = 139.7,
        double centerLatitude = 35.68,
        int zoomLevel = 12,
        bool enabled = false,
        bool diagnostics = false,
        bool cacheToDisk = true)
    {
        if (!enabled)
        {
            Release();
            return null;
        }

        _framesSinceBuild++;

        // Only things that change what the tile source *is* justify a rebuild. Where the map is
        // looking does not: moving the navigator keeps the layer, its memory cache and every
        // tile already fetched.
        //
        // This is not just tidiness. Dragging a map changes the centre on every frame, so a
        // rebuild-on-centre-change design would rebuild sixty times a second the moment
        // interaction is added - the same failure that took a network down, arriving by a
        // different route.
        if (_map is null || cacheToDisk != _cache)
        {
            Release();

            // Two builds on consecutive frames is the signature of a runaway. Toggling Enabled
            // by hand also rebuilds, and that is fine, so the alarm has to tell them apart
            // rather than counting.
            if (_framesSinceBuild <= 1) RebuiltOnConsecutiveFrames = true;
            _framesSinceBuild = 0;

            _map = BuildMap(centerLongitude, centerLatitude, zoomLevel, cacheToDisk);
            _layer = new MapsuiLayer(_map);
            MapsBuiltHere++;
            _lon = centerLongitude;
            _lat = centerLatitude;
            _zoom = zoomLevel;
            _cache = cacheToDisk;
        }
        else if (centerLongitude != _lon || centerLatitude != _lat || zoomLevel != _zoom)
        {
            var center = SphericalMercator.FromLonLat(centerLongitude, centerLatitude);
            _map.Navigator.CenterOn(center.x, center.y);
            if (zoomLevel != _zoom) _map.Navigator.ZoomToLevel(zoomLevel);

            _lon = centerLongitude;
            _lat = centerLatitude;
            _zoom = zoomLevel;
        }

        // Not part of the identity: toggling the overlay must not rebuild the map and throw
        // away every tile already fetched.
        _layer!.Diagnostics = diagnostics;
        _layer.MapsBuilt = _mapsBuilt;
        _layer.Runaway = RebuiltOnConsecutiveFrames;
        return _layer;
    }

    static Map BuildMap(double centerLongitude, double centerLatitude, int zoomLevel, bool cache)
    {
        var map = new Map();
        var layer = OpenStreetMap.CreateTileLayer(UserAgent);

        // Mapsui's factory takes a user agent and nothing else, so the disk cache is attached
        // afterwards. Mapsui keeps its own definition of what the OSM source is, which is worth
        // more than rebuilding one here from BruTile primitives.
        //
        // If the source is ever not an HttpTileSource this quietly does nothing, so the overlay
        // reports whether a cache is actually attached rather than whether one was requested.
        if (cache && layer.TileSource is BruTile.Web.HttpTileSource http)
            http.PersistentCache = TileCache.Create();

        map.Layers.Add(layer);
        _mapsBuilt++;

        var center = SphericalMercator.FromLonLat(centerLongitude, centerLatitude);

        // Home runs once the viewport has a size, which is the earliest moment a zoom level
        // means anything. Calling the navigator here would be a no-op. MapsuiLayer invokes it.
        map.Home = navigator =>
        {
            navigator.CenterOn(center.x, center.y);
            navigator.ZoomToLevel(zoomLevel);
        };

        return map;
    }

    void Release()
    {
        // AbortFetch before Dispose: a tile request in flight otherwise outlives the map that
        // asked for it, which is how connections accumulated in the first place.
        _map?.AbortFetch();
        _map?.Dispose();
        _map = null;
        _layer = null;
        _lon = double.NaN;
        _lat = double.NaN;
        _zoom = -1;
        _cache = false;
    }

    /// <summary>Releases the map and aborts any tile fetch still in flight.</summary>
    public void Dispose() => Release();
}
