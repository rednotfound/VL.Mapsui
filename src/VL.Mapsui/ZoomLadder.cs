using System;
using Mapsui;

using GlobalSphericalMercator = BruTile.Predefined.GlobalSphericalMercator;

namespace VL.Mapsui;

/// <summary>
/// What a slippy-map zoom level means as a resolution, whether or not a tile layer is present.
/// </summary>
/// <remarks>
/// Mapsui's <c>Navigator.ZoomToLevel</c> looks the level up in a resolutions list that only a tile
/// schema fills. On a map with no tile layer the list is empty, and Mapsui logs a warning and does
/// not zoom - silently, from the patch's point of view. That hit twice: <c>Map</c>'s Initial Zoom
/// Level on 2026-08-23 (fixed in Home only), and <c>ZoomToLevel</c> on 2026-09-25, found by the user
/// turning its Zoom Level in HowTo Draw a graticule and seeing nothing happen. One helper now, so
/// the two cannot drift apart again.
///
/// ZoomIn, ZoomOut and the wheel were measured the same day and do NOT need this: with an empty
/// list Mapsui falls back to halving or doubling the current resolution.
/// </remarks>
internal static class ZoomLadder
{
    /// <summary>
    /// Map units per pixel at level 0. Taken from BruTile's own <c>GlobalSphericalMercator</c> - the
    /// library that decides which tile a level means - rather than from the widely copied
    /// 156543.03392804 literal. Same number, checked.
    /// </summary>
    internal static readonly double TopResolution = new GlobalSphericalMercator().Resolutions[0].UnitsPerPixel;

    /// <summary>
    /// Map units per pixel at a zoom level. Halving from level 0 does not stop where a tile schema's
    /// table does (19): a data layer has no reason to stop there.
    /// </summary>
    internal static double Resolution(int zoomLevel) => TopResolution / Math.Pow(2, Math.Max(0, zoomLevel));

    /// <summary>
    /// Zoom to a level: through the navigator's own list when it has that level, otherwise to the
    /// number the list would have held.
    /// </summary>
    internal static void ZoomToLevel(Navigator navigator, int zoomLevel)
    {
        if (zoomLevel >= 0 && navigator.Resolutions.Count > zoomLevel)
            navigator.ZoomToLevel(zoomLevel);
        else
            navigator.ZoomTo(Resolution(zoomLevel));
    }
}
