using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using VL.Core.Import;

namespace VL.Mapsui;

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
