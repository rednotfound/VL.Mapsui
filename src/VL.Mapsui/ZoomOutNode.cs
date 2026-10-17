using System;
using Mapsui;
using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// Zooming out a step at a time, triggered rather than continuous.
/// The counterpart to ZoomIn; see that node for why it watches the edge.
/// </summary>
[ProcessNode(Name = "ZoomOut", Category = "Mapsui.Navigate")]
public class ZoomOutNode
{
    bool _was;

    /// <summary>How many times this node has actually zoomed. See ZoomIn.</summary>
    internal int Zooms { get; private set; }

    /// <summary>The map, so this can sit in a chain with the other navigation nodes.</summary>
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
