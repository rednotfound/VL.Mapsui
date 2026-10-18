using System;
using System.Linq;
using Mapsui;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// ZoomToLayer one step up: a tile layer covers the world, so this is worth reaching for when a map
// holds only data layers. With a world-covering basemap on it does what it says and shows the
// world, which is honest but rarely what anyone wants - frame the layer that matters instead.

/// <summary>
/// Frames every layer on the map at once.
/// </summary>
/// <remarks>
/// A tile layer covers the world, so with one on the map use ZoomToLayer on your data layer instead.
/// </remarks>
[ProcessNode(Name = "ZoomToLayers", Category = "Mapsui.Navigate")]
public class ZoomToLayersNode
{
    bool _was;

    /// <summary>How many times this node has actually framed something.</summary>
    internal int Framings { get; private set; }

    // Returns the map so this can sit in a chain with the other navigation nodes.

    /// <summary>
    /// Frames all layers on the rising edge of Trigger.
    /// </summary>
    /// <param name="map">The map to move.</param>
    /// <param name="trigger">Frames the layers once when this turns on.</param>
    /// <param name="margin">Room left around the layers, as a fraction of their size.</param>
    /// <returns>The same map.</returns>
    public Map Update(Map map, bool trigger = false, float margin = 0.1f)
    {
        if (map is null) return map!;

        if (trigger && !_was)
        {
            var extents = map.Layers.Select(l => l.Extent).OfType<MRect>().ToArray();
            if (extents.Length > 0)
            {
                map.Navigator.ZoomToBox(ZoomToLayerNode.Grown(new MRect(extents), margin));
                NavigateNodes.Refresh(map, continuous: false);
                Framings++;
            }
        }

        _was = trigger;
        return map;
    }
}
