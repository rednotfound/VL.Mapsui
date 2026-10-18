using System;
using Mapsui;
using VL.Core.Import;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A process node because it acts on the rising edge, and that is a safety property. Mapsui's
// ZoomIn animates over a duration, so a bang - true for a single frame - behaves exactly as you
// would want. A toggle left switched on would not: without edge detection it would zoom on every
// frame, which is the same runaway that took a home network down wearing different clothes, since
// each zoom asks the layers to refresh.
//
// Edge detection is mechanism, not policy. What counts as a trigger stays in the patch: a bang, a
// key, an OSC message, a footswitch.

/// <summary>
/// Zooms in one level each time Trigger turns on.
/// </summary>
/// <remarks>
/// Wire a bang, a key, an OSC message or a footswitch; holding Trigger on zooms only once.
/// </remarks>
[ProcessNode(Name = "ZoomIn", Category = "Mapsui.Navigate")]
public class ZoomInNode
{
    bool _was;

    /// <summary>
    /// How many times this node has actually zoomed. A test asserts on this rather than on the
    /// resolution, because Mapsui eases the zoom over a duration and reading the result would
    /// mean waiting for wall-clock time to pass.
    /// </summary>
    internal int Zooms { get; private set; }

    // Returns the map so this can sit in a chain with the other navigation nodes.

    /// <summary>
    /// Zooms in on the rising edge of Trigger.
    /// </summary>
    /// <param name="map">The map to zoom.</param>
    /// <param name="trigger">Zooms in once when this turns on.</param>
    /// <returns>The same map.</returns>
    public Map Update(Map map, bool trigger = false)
    {
        if (map is null) return map!;

        if (trigger && !_was)
        {
            map.Navigator.ZoomIn();
            NavigateNodes.Refresh(map, continuous: false);
            Zooms++;
        }
        _was = trigger;
        return map;
    }
}
