using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;
using VL.Core.Import;

using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using MapsuiFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Separate from Pick because it is a different question with a different answer: Pick finds
// nothing in the sea, while this still says which sea. Measured - Mapsui fills in the world
// position even when the hit test finds no feature.
//
// Pure arithmetic on the viewport, so a static node: it holds nothing and there is nothing to
// rebuild.

/// <summary>
/// Converts between view pixels and longitude/latitude, whether or not anything is drawn there.
/// </summary>
[Name("Project")]
public static class ProjectNodes
{
    // x first, as everywhere else here. Both are 0 until the map has been drawn once, because a
    // viewport with no size has no coordinates to convert into - the same reason Home cannot run
    // before the first frame.

    /// <summary>
    /// Returns the longitude and latitude under a view pixel.
    /// </summary>
    /// <remarks>
    /// Both are 0 until the map has been drawn once.
    /// </remarks>
    /// <param name="map">The map whose view is used.</param>
    /// <param name="longitude">Degrees east, WGS84.</param>
    /// <param name="latitude">Degrees north, WGS84.</param>
    /// <param name="x">Pixels from the left of the view.</param>
    /// <param name="y">Pixels from the top of the view.</param>
    public static void ScreenToWorld(Map? map, out double longitude, out double latitude, float x = 0f, float y = 0f)
    {
        longitude = 0;
        latitude = 0;

        if (map is null) return;

        var viewport = map.Navigator.Viewport;
        if (!viewport.HasSize()) return;

        var world = viewport.ScreenToWorld(x, y);
        (longitude, latitude) = SphericalMercator.ToLonLat(world.X, world.Y);
    }

    // The inverse of ScreenToWorld, for putting something of your own on top of the map.

    /// <summary>
    /// Returns the view pixel a longitude/latitude is drawn at.
    /// </summary>
    /// <remarks>
    /// Use it to draw something of your own on top of the map.
    /// </remarks>
    /// <param name="map">The map whose view is used.</param>
    /// <param name="x">Pixels from the left of the view.</param>
    /// <param name="y">Pixels from the top of the view.</param>
    /// <param name="longitude">Degrees east, WGS84.</param>
    /// <param name="latitude">Degrees north, WGS84.</param>
    public static void WorldToScreen(Map? map, out float x, out float y, double longitude = 0, double latitude = 0)
    {
        x = 0;
        y = 0;

        if (map is null) return;

        var viewport = map.Navigator.Viewport;
        if (!viewport.HasSize()) return;

        var (mercatorX, mercatorY) = SphericalMercator.FromLonLat(longitude, latitude);
        var screen = viewport.WorldToScreen(mercatorX, mercatorY);
        x = (float)screen.X;
        y = (float)screen.Y;
    }
}
