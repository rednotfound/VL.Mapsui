using System;
using Mapsui;
using VL.Core.Import;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// The counterpart to ZoomIn; see that node for why it watches the edge rather than the level.

/// <summary>
/// Zooms out one level each time Trigger turns on.
/// </summary>
[ProcessNode(Name = "ZoomOut", Category = "Mapsui.Navigate")]
public class ZoomOutNode
{
    bool _was;

    /// <summary>How many times this node has actually zoomed. See ZoomIn.</summary>
    internal int Zooms { get; private set; }

    // Returns the map so this can sit in a chain with the other navigation nodes.

    /// <summary>
    /// Zooms out on the rising edge of Trigger.
    /// </summary>
    /// <param name="map">The map to zoom.</param>
    /// <param name="trigger">Zooms out once when this turns on.</param>
    /// <returns>The same map.</returns>
    public Map Update(Map map, bool trigger = false)
    {
        if (map is null) return map!;

        if (trigger && !_was)
        {
            map.Navigator.ZoomOut();
            NavigateNodes.Refresh(map, continuous: false);
            Zooms++;
        }
        _was = trigger;
        return map;
    }
}
