using System;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using BaseLayer = global::Mapsui.Layers.BaseLayer;
using GlobalSphericalMercator = BruTile.Predefined.GlobalSphericalMercator;

namespace VL.Mapsui;

/// <summary>
/// Which zoom levels a layer is drawn at.
/// </summary>
/// <remarks>
/// **This is what keeps a map with many layers readable, and it is the only thing that does.**
/// Draw order decides what covers what; it does not reduce anything. A city-block layer at country
/// zoom is a grey smear, a country-boundary layer at street zoom is a line off the edge of the
/// screen, and both are still being drawn. Every GIS answers this the same way: a layer declares
/// the scales it belongs to, and outside them it is skipped.
///
/// **Measured 2026-08-16, because the property reads like a dead one.** `get_MinVisible` occurs
/// **zero times** in `Mapsui.Rendering.Skia.dll` — the same reading as `LabelColumn`, which works,
/// and as `UnitType`, which does nothing. Rendered: a layer with `MinVisible` 0.5 and `MaxVisible`
/// 2.0 draws 91204 pixels at resolution 1 and **0** at both 0.25 and 4.0. It is a hard cut, and it
/// is honoured.
///
/// It also answers the dense-label problem from earlier the same day: two hundred labels are noise
/// at zoom 2 and useful at zoom 14, and this is how a patch says so.
/// </remarks>
[SkipCategory]
public static class VisibleRangeNodes
{
    /// <summary>
    /// The same layer, drawn only between two zoom levels.
    /// </summary>
    /// <remarks>
    /// **The same instance comes back, deliberately.** A layer's identity is what `Map` compares,
    /// so returning a copy would rebuild the map every frame — this sets two properties on the
    /// layer it was handed and returns it, which chains as
    /// `FeatureLayer → VisibleRange → Cons` and costs nothing.
    ///
    /// **Zoom levels, not resolutions, and the two run in opposite directions.** Mapsui stores
    /// `MinVisible` and `MaxVisible` as *resolutions* — map units per pixel — where a higher zoom
    /// level is a **smaller** number. So `From Zoom` sets `MaxVisible` and `To Zoom` sets
    /// `MinVisible`, which reads backwards and is exactly the sort of inversion that should live
    /// inside a node rather than in everyone's patch.
    ///
    /// Both named levels are inside the range: each end is widened by half a zoom level, so a layer
    /// asked for 10 to 14 is visible at 10 and at 14 and gone at 9 and 15. Without that a level
    /// would sit exactly on the boundary, and whether the comparison is inclusive is not something
    /// a patch author should have to know.
    ///
    /// **A layer that is not a `BaseLayer` cannot be ranged** — `MinVisible` and `MaxVisible` are
    /// read-only on the `ILayer` interface and settable only on the base class. Every layer this
    /// package makes is one; a foreign one might not be, and `Status` says so rather than the
    /// range quietly doing nothing.
    /// </remarks>
    [Name("VisibleRange")]
    public static ILayer? VisibleRange(
        ILayer? layer,
        out string status,
        int fromZoom = 0,
        int toZoom = 30)
    {
        if (layer is null)
        {
            status = "nothing connected";
            return null;
        }

        if (layer is not BaseLayer basic)
        {
            status = $"{layer.GetType().Name} is not a BaseLayer, so its visible range cannot be set. "
                   + "It will keep drawing at every zoom.";
            return layer;
        }

        if (fromZoom > toZoom)
        {
            status = $"From Zoom {fromZoom} is past To Zoom {toZoom}, so the range is empty and the "
                   + "layer would never draw. Left unchanged.";
            return layer;
        }

        // Half a level of slack at each end, so both named levels are inside the band.
        basic.MaxVisible = Resolution(fromZoom) * HalfLevel;
        basic.MinVisible = Resolution(toZoom) / HalfLevel;

        status = fromZoom == toZoom
            ? $"only at zoom {fromZoom}"
            : $"zoom {fromZoom} to {toZoom}";
        return layer;
    }

    /// <summary>One half of a zoom level, as a resolution factor. Each level halves the number.</summary>
    static readonly double HalfLevel = Math.Sqrt(2);

    /// <summary>
    /// Map units per pixel at a zoom level, on the same ladder the tile layers use.
    /// </summary>
    /// <remarks>
    /// Derived from BruTile's own <c>GlobalSphericalMercator</c> rather than from the widely-copied
    /// 156543.03392804 literal — same number, checked, but taken from the library that also decides
    /// which tile a zoom level means. The schema's own dictionary stops at 19; halving from level 0
    /// does not, and a data layer has no reason to stop where a tile source does.
    /// </remarks>
    static double Resolution(int zoomLevel) => TopResolution / Math.Pow(2, Math.Max(0, zoomLevel));

    static readonly double TopResolution = new GlobalSphericalMercator().Resolutions[0].UnitsPerPixel;
}
