using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using VL.Core.Import;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A Map is opaque in a patch: without a reader, an author holds a value they cannot inspect and
// cannot debug. Every opaque type in this repository gets one.
//
// SkipCategory, not [Name("Map")]. A named static class becomes a category level, so [Name("Map")]
// would create the category "Mapsui.Map" alongside the node "Map" in category "Mapsui" - and the
// node then silently fails to resolve, taking every link to it with it. SkipCategory drops the type
// level so these land in "Mapsui" beside the node they read.

/// <summary>
/// Reads a map's state back out.
/// </summary>
[SkipCategory]
public static class MapInfoNodes
{
    /// <summary>
    /// Returns where the map is looking and how big its viewport is.
    /// </summary>
    /// <param name="map">The map to read.</param>
    /// <param name="centerLongitude">Longitude of the view centre, WGS84.</param>
    /// <param name="centerLatitude">Latitude of the view centre, WGS84.</param>
    /// <param name="resolution">Ground metres per pixel.</param>
    /// <param name="width">Viewport width in pixels.</param>
    /// <param name="height">Viewport height in pixels.</param>
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
    /// Returns how many layers the map carries and whether any is still fetching.
    /// </summary>
    /// <param name="map">The map to read.</param>
    /// <param name="layerCount">The number of layers.</param>
    /// <param name="busy">True while a layer is fetching. Staying true forever usually means tiles are not arriving.</param>
    /// <param name="names">The layer names, comma separated.</param>
    public static void LayerInfo(Map map, out int layerCount, out bool busy, out string names)
    {
        layerCount = map.Layers.Count;
        busy       = map.Layers.Any(l => l.Busy);
        names      = string.Join(", ", map.Layers.Select(l => l.Name));
    }
}
