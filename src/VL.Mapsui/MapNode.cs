using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// A Mapsui map: a stack of layers, a viewport, and a navigator.
/// Feed it layers, move it with the nodes in Mapsui.Navigate, and draw it with ToSkiaLayer.
/// </summary>
/// <remarks>
/// A process node because the map owns its layers, and those own connections and caches.
///
/// The initial centre and zoom are applied once, when the viewport first has a size — a zoom
/// level means nothing before then. **Afterwards they are ignored**, so that navigating the map
/// is not immediately undone by the pins on this node. To move it later, use the navigation
/// nodes; that is also what a patch would wire the mouse to.
/// </remarks>
[ProcessNode(Name = "Map", Category = "Mapsui")]
public class MapNode : IDisposable
{
    readonly Map _map = new();
    ILayer[] _current = Array.Empty<ILayer>();

    /// <summary>Maps built by this node. Always one: the map outlives every layer change.</summary>
    internal int MapsBuilt { get; private set; }

    /// <summary>
    /// The map, ready for the navigation and drawing nodes.
    /// </summary>
    public Map Update(
        IEnumerable<ILayer>? layers = null,
        double centerLongitude = 139.7,
        double centerLatitude = 35.68,
        int zoomLevel = 12)
    {
        if (MapsBuilt == 0)
        {
            MapsBuilt = 1;

            // Home runs once the viewport has a size, which the renderer sets. Calling the
            // navigator here would be a no-op, and skipping Home altogether leaves the map at a
            // default resolution that shows nothing at all - no error, just an empty window.
            var center = SphericalMercator.FromLonLat(centerLongitude, centerLatitude);
            _map.Home = navigator =>
            {
                navigator.CenterOn(center.x, center.y);
                navigator.ZoomToLevel(zoomLevel);
            };
        }

        // Only touch the collection when the set of layers actually changed. Clearing and
        // re-adding every frame would discard each layer's fetched tiles, which is the same
        // per-frame-rebuild mistake one level up.
        var incoming = (layers ?? Enumerable.Empty<ILayer>()).Where(l => l is not null).ToArray();
        if (!incoming.SequenceEqual(_current))
        {
            _map.Layers.Clear();
            foreach (var l in incoming) _map.Layers.Add(l);
            _current = incoming;
        }

        // The frame tick Mapsui's own hosts give it; drives fly-to and easing.
        _map.UpdateAnimations();

        return _map;
    }

    /// <summary>
    /// Disposes the map, but not the layers: those belong to the nodes that made them, and
    /// VL disposes those separately.
    /// </summary>
    public void Dispose()
    {
        _map.Layers.Clear();
        _map.Dispose();
    }
}

/// <summary>
/// Reading a map's state back out.
/// </summary>
/// <remarks>
/// A Map is opaque in a patch: without a reader, an author holds a value they cannot inspect
/// and cannot debug. Every opaque type in this repository gets one.
///
/// **SkipCategory, not [Name("Map")].** A named static class becomes a category level, so
/// [Name("Map")] would create the category "Mapsui.Map" alongside the node "Map" in category
/// "Mapsui" - and the node then silently fails to resolve, taking every link to it with it.
/// SkipCategory drops the type level so these land in "Mapsui" beside the node they read.
/// </remarks>
[SkipCategory]
public static class MapInfoNodes
{
    /// <summary>
    /// Where the map is currently looking, and how big its viewport is.
    /// Longitude and latitude are WGS84, x first. Resolution is ground metres per pixel.
    /// </summary>
    public static void ViewportInfo(
        Map map,
        out double centerLongitude,
        out double centerLatitude,
        out double resolution,
        out float width,
        out float height)
    {
        var v = map.Navigator.Viewport;
        var (lon, lat) = SphericalMercator.ToLonLat(v.CenterX, v.CenterY);

        centerLongitude = lon;
        centerLatitude  = lat;
        resolution      = v.Resolution;
        width           = (float)v.Width;
        height          = (float)v.Height;
    }

    /// <summary>
    /// What the map is made of: how many layers it carries, and whether any is still fetching.
    /// Busy staying true forever usually means tiles are not arriving.
    /// </summary>
    public static void LayerInfo(Map map, out int layerCount, out bool busy, out string names)
    {
        layerCount = map.Layers.Count;
        busy       = map.Layers.Any(l => l.Busy);
        names      = string.Join(", ", map.Layers.Select(l => l.Name));
    }
}
