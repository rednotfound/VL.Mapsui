using Mapsui;
using Mapsui.Projections;
using Mapsui.Tiling;
using VL.Core.Import;
using VL.Skia;

namespace VL.Mapsui;

/// <summary>
/// A Mapsui map, and the layer that draws it.
/// </summary>
/// <remarks>
/// Spike scope: enough nodes to prove a map reaches the screen inside vvvv. Node names and
/// categories are not settled and will change.
/// </remarks>
[Name("Map")]
public static class MapNodes
{
    /// <summary>
    /// A map with OpenStreetMap tiles, centred on a WGS84 coordinate at a slippy-map zoom
    /// level. Uses Mapsui, and OpenStreetMap tiles are © OpenStreetMap contributors (ODbL).
    /// </summary>
    /// <remarks>
    /// Coordinate order is (longitude, latitude) - x first. Mapsui works internally in
    /// Spherical Mercator metres, so the centre is converted on the way in.
    /// </remarks>
    public static Map CreateOpenStreetMap(
        double centerLongitude = 139.7,
        double centerLatitude = 35.68,
        int zoomLevel = 12)
    {
        var map = new Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer());

        var center = SphericalMercator.FromLonLat(centerLongitude, centerLatitude);

        // Home runs once the viewport has a size, which is the earliest moment a zoom level
        // means anything. Calling the navigator directly here would be a no-op.
        map.Home = navigator =>
        {
            navigator.CenterOn(center.x, center.y);
            navigator.ZoomToLevel(zoomLevel);
        };

        return map;
    }

    /// <summary>
    /// Wrap a Mapsui map as a VL.Skia layer, ready for a Renderer.
    /// The Renderer's Space pin does not need setting: the layer resets the canvas matrix to
    /// pixels itself, because a wrong Space value fails silently.
    /// </summary>
    public static global::VL.Skia.ILayer ToSkiaLayer(Map map) => new MapsuiLayer(map);

    /// <summary>
    /// A layer that draws a fixed 200x120 pixel box and prints what it measured about the
    /// space it was given. Scaffolding for bringing this package up; not a feature.
    /// </summary>
    /// <remarks>
    /// Put this into a Renderer before wiring the map. If the box appears at the right size,
    /// pixel-space handling works and any remaining problem is Mapsui's. If it does not, there
    /// is no point looking at Mapsui yet.
    /// </remarks>
    public static global::VL.Skia.ILayer DiagnosticsLayer() => new DiagnosticsLayer();
}
