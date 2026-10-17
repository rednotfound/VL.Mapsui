using System;
using System.Linq;
using Mapsui;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;

namespace VL.Mapsui;

/// <summary>
/// Frames everything on the map at once — every layer that has an extent.
/// </summary>
/// <remarks>
/// The same node one step up: a tile layer covers the world, so this is worth reaching for when a
/// map holds only data layers. With a world-covering basemap on it does what it says and shows the
/// world, which is honest but rarely what anyone wants — frame the layer that matters instead.
/// </remarks>
[ProcessNode(Name = "ZoomToLayers", Category = "Mapsui.Navigate")]
public class ZoomToLayersNode
{
    bool _was;

    /// <summary>How many times this node has actually framed something.</summary>
    internal int Framings { get; private set; }

    /// <summary>The map, so this can sit in a chain with the other navigation nodes.</summary>
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
