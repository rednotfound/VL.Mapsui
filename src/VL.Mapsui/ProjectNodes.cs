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

/// <summary>
/// Where a point on the view is in the world, whether or not anything is drawn there.
/// </summary>
/// <remarks>
/// Separate from `Pick` because it is a different question with a different answer: `Pick` finds
/// nothing in the sea, while this still says which sea. Measured — Mapsui fills in the world
/// position even when the hit test finds no feature.
///
/// Pure arithmetic on the viewport, so a static node: it holds nothing and there is nothing to
/// rebuild.
/// </remarks>
[Name("Project")]
public static class ProjectNodes
{
    /// <summary>
    /// The WGS84 longitude and latitude under a view pixel. x first, as everywhere else here.
    /// </summary>
    /// <remarks>
    /// Both are 0 until the map has been drawn once, because a viewport with no size has no
    /// coordinates to convert into — the same reason `Home` cannot run before the first frame.
    /// </remarks>
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

    /// <summary>
    /// The view pixel a WGS84 coordinate is drawn at — the inverse, for putting something of your
    /// own on top of the map.
    /// </summary>
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
