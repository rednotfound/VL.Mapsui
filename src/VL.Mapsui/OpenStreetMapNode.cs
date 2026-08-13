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
    // only found out when the network died. Shown in the diagnostics overlay.
    static int _mapsBuilt;

    Map? _map;
    MapsuiLayer? _layer;

    // NaN and -1 cannot equal any first input, so the first enabled frame always builds.
    double _lon = double.NaN;
    double _lat = double.NaN;
    int _zoom = -1;

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
        bool diagnostics = false)
    {
        if (!enabled)
        {
            Release();
            return null;
        }

        if (_map is null || centerLongitude != _lon || centerLatitude != _lat || zoomLevel != _zoom)
        {
            Release();

            _map = BuildMap(centerLongitude, centerLatitude, zoomLevel);
            _layer = new MapsuiLayer(_map);
            _lon = centerLongitude;
            _lat = centerLatitude;
            _zoom = zoomLevel;
        }

        // Not part of the identity: toggling the overlay must not rebuild the map and throw
        // away every tile already fetched.
        _layer!.Diagnostics = diagnostics;
        _layer.MapsBuilt = _mapsBuilt;
        return _layer;
    }

    static Map BuildMap(double centerLongitude, double centerLatitude, int zoomLevel)
    {
        var map = new Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer(UserAgent));
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
    }

    /// <summary>Releases the map and aborts any tile fetch still in flight.</summary>
    public void Dispose() => Release();
}
