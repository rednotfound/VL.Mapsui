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
    /// Drag the map from one pixel to another. The Drag process node is usually what you want:
    /// it remembers the previous position for you. This is the stateless half, for a patch that
    /// already has both positions.
    /// </summary>
    /// <remarks>
    /// Refreshes continuously rather than discretely: during a gesture Mapsui uses that to fetch
    /// less aggressively, which is what keeps a drag from becoming a burst of tile requests.
    /// </remarks>
    public static Map DragBetween(Map map, float x, float y, float previousX, float previousY)
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
    /// **Mapsui reads only the sign, so one call is one zoom level whatever Steps says.** Read in
    /// Mapsui 4.1.9's MouseWheelAnimation.GetResolution: it compares the delta against an epsilon
    /// and asks ZoomHelper for the next resolution in or out, discarding the magnitude entirely.
    /// An earlier version of this multiplied by 120 on the theory that Mapsui wanted the raw
    /// Windows value; that was a guess, and it was wrong in a way no test would have caught,
    /// because the sign survived it.
    ///
    /// Mapsui also animates the change over MouseWheelAnimation.Duration, so the viewport's
    /// resolution does not move on the frame this is called. Something has to call
    /// Map.UpdateAnimations each frame, which the Map node does.
    /// </remarks>
    public static Map ZoomAt(Map map, float x, float y, int steps = 0)
    {
        if (steps == 0) return map;

        map.Navigator.MouseWheelZoom(Math.Sign(steps), new MPoint(x, y));
        Refresh(map, continuous: false);
        return map;
    }

    /// <summary>
    /// Zoom around a pixel from a mouse wheel reading, in wheel units rather than whole steps.
    /// </summary>
    /// <remarks>
    /// **Feed this the change in the wheel, not the wheel itself.** VL.Skia's MouseState reports
    /// Wheel State, which accumulates: it counts up and stays there. Put it through
    /// FrameDifference (Animation.FrameBased) and wire the result here. That is how vvvv's own
    /// patches read it, and it is the only place in the shipped documents that pin is connected
    /// to anything.
    ///
    /// Notch Size is a pin rather than a constant hidden in here, and the default is **1**
    /// because that is what VL reports: measured in vvvv 7.4, one notch of the wheel moves
    /// FrameDifference by exactly 1. The raw Windows convention is 120 per notch, and something
    /// feeding this from a native message or a MIDI controller may well use that, hence the pin.
    ///
    /// A node that guessed - "divide by 120 if the number looks big" - would silently do nothing
    /// for whoever fell on the wrong side of the guess. That is not hypothetical: this defaulted
    /// to 120 for exactly one round, and the symptom was a wheel that scrolled and a map that
    /// did not move, with nothing to indicate why.
    /// </remarks>
    public static Map ZoomByWheel(Map map, float x, float y, float wheelDelta = 0f, float notchSize = 1f)
    {
        var steps = WheelSteps(wheelDelta, notchSize);
        return steps == 0 ? map : ZoomAt(map, x, y, steps);
    }

    /// <summary>
    /// Wheel units to whole zoom steps. Separate and pure so it can be tested directly.
    /// </summary>
    /// <remarks>
    /// Testing this through the map instead would mean depending on Mapsui's zoom animation
    /// having advanced, which needs wall-clock time to pass and makes a test flaky. The
    /// arithmetic is ours; the animation is Mapsui's.
    /// </remarks>
    internal static int WheelSteps(float wheelDelta, float notchSize)
    {
        if (wheelDelta == 0f || notchSize == 0f) return 0;

        // AwayFromZero, not the default. Math.Round uses banker's rounding, so exactly half a
        // notch would round to zero and the input would vanish - surprising for a discrete
        // action someone just performed. Half a turn counts as a turn.
        return (int)Math.Round(wheelDelta / notchSize, MidpointRounding.AwayFromZero);
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
