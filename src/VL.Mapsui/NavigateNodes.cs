using System;
using Mapsui;
using Mapsui.Projections;
using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// Moving the map. Positions are pixels from the top-left of the view, the same pixels
/// ToSkiaLayer draws in.
/// </summary>
/// <remarks>
/// These are separate nodes rather than pins on the map for a reason: **a patch decides what
/// drives them.** The mouse is the obvious answer and VL.Skia already has nodes for it, but so
/// are an LFO, an OSC message, a keyboard, or a timeline. Wiring the mouse inside the map node
/// would have taken all of that away.
///
/// Each one calls Refresh so the layers fetch what the new view needs. Mapsui expects its host
/// to drive that; without it the map moves and then sits on stale tiles, which looks exactly
/// like broken tile loading.
/// </remarks>
[Name("Navigate")]
public static class NavigateNodes
{
    /// <summary>
    /// Put a WGS84 coordinate at the centre of the view. x first: longitude, then latitude.
    /// </summary>
    public static Map CenterOn(Map map, double longitude, double latitude)
    {
        var center = SphericalMercator.FromLonLat(longitude, latitude);
        map.Navigator.CenterOn(center.x, center.y);
        Refresh(map, continuous: false);
        return map;
    }

    /// <summary>
    /// Go to a slippy-map zoom level: 0 is the whole world, 19 is a building.
    /// </summary>
    public static Map ZoomToLevel(Map map, int zoomLevel = 12)
    {
        map.Navigator.ZoomToLevel(Math.Clamp(zoomLevel, 0, 22));
        Refresh(map, continuous: false);
        return map;
    }

    /// <summary>
    /// Drag the map from one pixel to another, the way a mouse does. Wire a mouse position and
    /// its value from the previous frame, gated by whichever button should pan.
    /// </summary>
    /// <remarks>
    /// Refreshes continuously rather than discretely: during a gesture Mapsui uses that to fetch
    /// less aggressively, which is what keeps a drag from becoming a burst of tile requests.
    /// </remarks>
    public static Map Drag(Map map, float x, float y, float previousX, float previousY)
    {
        if (x == previousX && y == previousY) return map;

        map.Navigator.Drag(new MPoint(x, y), new MPoint(previousX, previousY));
        Refresh(map, continuous: true);
        return map;
    }

    /// <summary>
    /// Zoom by whole steps around a pixel, the way a scroll wheel does. One step is one zoom
    /// level; positive zooms in.
    /// </summary>
    /// <remarks>
    /// Steps, not a raw wheel delta. Windows reports 120 per notch and other sources report 1,
    /// and passing either straight to Mapsui makes the result depend on where the number came
    /// from. Take the sign here and the behaviour is the same whatever drives it.
    /// </remarks>
    public static Map ZoomAt(Map map, float x, float y, int steps = 0)
    {
        if (steps == 0) return map;

        // Mapsui reads this the way Windows sends it, so one notch is 120.
        map.Navigator.MouseWheelZoom(Math.Sign(steps) * 120 * Math.Abs(steps), new MPoint(x, y));
        Refresh(map, continuous: false);
        return map;
    }

    /// <summary>
    /// Ask the layers to fetch what the current view needs. The navigation nodes already do
    /// this; it is here for a patch that moves the map some other way.
    /// </summary>
    /// <remarks>
    /// Continuous is for mid-gesture, where Mapsui holds back on fetching; discrete is for
    /// when the view has settled and everything visible should be requested.
    /// </remarks>
    public static Map Refresh(Map map, bool continuous = false)
    {
        map.Refresh(continuous ? ChangeType.Continuous : ChangeType.Discrete);
        return map;
    }
}
