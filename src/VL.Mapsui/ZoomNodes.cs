using System;
using Mapsui;
using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// Zooming a step at a time, triggered rather than continuous.
/// Wire a bang, a key, an OSC message, a footswitch.
/// </summary>
/// <remarks>
/// **A process node because it acts on the rising edge, and that is a safety property.**
/// Mapsui's ZoomIn animates over a duration, so a bang - true for a single frame - behaves
/// exactly as you would want. A toggle left switched on would not: without edge detection it
/// would zoom on every frame, which is the same runaway that took a home network down wearing
/// different clothes, since each zoom asks the layers to refresh.
///
/// Edge detection is mechanism, not policy. What counts as a trigger stays in the patch.
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

    /// <summary>The map, so this can sit in a chain with the other navigation nodes.</summary>
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
