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
/// What is under a point on the map. The other direction of the whole package: geometry has been
/// going in, and this is the first node that hands something back.
/// </summary>
/// <remarks>
/// **It answers about a position, not about a click.** There is no Pressed pin here and that is
/// deliberate: what counts as a click belongs to the patch, exactly as it does for `Drag` and
/// `Click`. Wire the mouse position in and it reports continuously; put a `Sample and Hold` after
/// it, gated on a button, and it reports on click. Deciding here would take that choice away from
/// every patch, and the same instinct — swallowing the mouse inside a node — is what this package
/// was rebuilt to undo.
///
/// **A layer has to opt in, and nothing says so when it has not.** Mapsui only hit-tests layers
/// whose `IsMapInfoLayer` is true, and it defaults to **false** — measured: with it off, a click on
/// the dead centre of a square returns no feature and no error. `FeatureLayer` therefore switches
/// it on for every layer it builds. Tile layers stay off, which is right: there is no feature under
/// a tile to find.
///
/// **What comes back is NetTopologySuite's `Feature`, in WGS84** — the same type that went in, in
/// the same coordinates. It is a new object rather than the one the patch is holding: Mapsui was
/// handed a projected copy, so this projects back. Compare it by value, not by reference.
///
/// A process node because it owns a `MapRenderer`, which is what performs the hit test, and
/// building one per frame is the mistake this repository is named after.
/// </remarks>
[ProcessNode(Name = "Pick", Category = "Mapsui")]
public class PickNode
{
    // The renderer is the hit test. It needs no canvas and nothing to have been drawn - measured:
    // a fresh MapRenderer over a layer that has never been rendered returns hits.
    readonly MapRenderer _renderer = new();

    /// <summary>
    /// The feature under the given view pixel, or nothing when there is none there.
    /// </summary>
    /// <remarks>
    /// X and Y are pixels from the top-left of the view, the same ones `Drag` and `Click` take and
    /// the same ones `ToSkiaLayer` draws in — `MouseState`'s Position, split.
    ///
    /// **The hit is exact, with no tolerance.** Measured on a square spanning screen x 100..300: x
    /// 300 hits and 301 misses. Mapsui's `margin` parameter does not widen it for geometry — 0, 4,
    /// 8 and 32 all miss five pixels outside the edge — so it is not offered here rather than
    /// offered and useless. A patch wanting a fat finger has to grow the geometry, not the query.
    ///
    /// **When features overlap, the topmost wins**: last layer first, and within a layer the last
    /// feature. That is the same order they were drawn in, which is the only answer that matches
    /// what the eye picked.
    /// </remarks>
    public NtsFeature? Update(Map? map, out bool hit, out string layer, float x = 0f, float y = 0f)
    {
        hit = false;
        layer = string.Empty;

        if (map is null) return null;

        var viewport = map.Navigator.Viewport;

        // Before the first frame the viewport has no size, so every query would silently miss.
        // Saying so beats answering "nothing is there".
        if (!viewport.HasSize())
        {
            layer = "the map has not been drawn yet";
            return null;
        }

        var info = _renderer.GetMapInfo(x, y, viewport, map.Layers, margin: 0);
        if (info?.Feature is not { } found) return null;

        hit = true;
        layer = info.Layer?.Name ?? string.Empty;
        return ToNts(found);
    }

    /// <summary>
    /// A Mapsui feature as NetTopologySuite's neutral one: geometry unprojected, attributes copied.
    /// </summary>
    /// <remarks>
    /// The exact inverse of <c>FeatureLayer.ToMapsui</c>, and it lives here for the same reason that
    /// one lives there — the conversion belongs to whichever side is crossing the boundary.
    ///
    /// A feature Mapsui built from something other than geometry (a raster tile, a widget) has no
    /// NTS geometry to give back, so it gives back nothing rather than an empty shape.
    /// </remarks>
    internal static NtsFeature? ToNts(MapsuiFeature feature)
    {
        if (feature is not GeometryFeature { Geometry: { } geometry }) return null;

        var attributes = new AttributesTable();
        foreach (var name in feature.Fields)
            attributes.Add(name, feature[name]);

        return new NtsFeature(GeometryLayerNode.ToLonLat(geometry), attributes);
    }
}

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
