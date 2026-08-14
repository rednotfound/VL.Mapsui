using System;
using System.Linq;
using Mapsui;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;

namespace VL.Mapsui;

/// <summary>
/// Puts the view where the data is: frames a layer's extent.
/// Wire a bang, a key, or whatever else should mean "show me everything".
/// </summary>
/// <remarks>
/// **The node that was missing.** Without it an example has to hardcode a centre and a zoom that
/// happen to match its data, and the first one that did got them 350 km apart — the shape was off
/// screen and it read as a rendering fault (NOTES.md, 2026-08-14). "Show me the data" is a thing a
/// patch should be able to say.
///
/// **Triggered, not continuous, and that is the same safety property as ZoomIn's.** Applied every
/// frame it would pin the view to the layer and the map could not be moved at all — which is
/// exactly what `CenterOn` running every frame did to a help patch. Edge detection is mechanism;
/// what counts as a trigger stays in the patch.
///
/// A layer with nothing in it has no extent, and nothing happens rather than the view jumping to
/// some default. That matters because a feature layer is empty until its data arrives, and a bang
/// that landed a frame early would otherwise send the view somewhere arbitrary.
/// </remarks>
[ProcessNode(Name = "ZoomToLayer", Category = "Mapsui.Navigate")]
public class ZoomToLayerNode
{
    bool _was;

    /// <summary>
    /// How many times this node has actually framed something. A test asserts on this rather than
    /// on the resolution, because Mapsui eases the move over a duration.
    /// </summary>
    internal int Framings { get; private set; }

    /// <summary>The map, so this can sit in a chain with the other navigation nodes.</summary>
    /// <remarks>
    /// Margin is a fraction of the extent left as breathing room — a shape flush against the window
    /// edge reads as clipped even when it is not. It earns its pin: the alternative is every patch
    /// growing the rectangle by hand before it can frame anything.
    /// </remarks>
    public Map Update(Map map, ILayer? layer = null, bool trigger = false, float margin = 0.1f)
    {
        if (map is null) return map!;

        if (trigger && !_was && layer?.Extent is { } extent)
        {
            map.Navigator.ZoomToBox(Grown(extent, margin));
            NavigateNodes.Refresh(map, continuous: false);
            Framings++;
        }

        _was = trigger;
        return map;
    }

    /// <summary>The same box with a fraction of breathing room on every side.</summary>
    /// <remarks>
    /// A zero-width extent is possible and is not an error: a layer holding a single point has one.
    /// Growing it by a fraction of zero leaves zero, which Mapsui would have to fit into a viewport
    /// of some size, so a degenerate box gets a small absolute margin in map units instead.
    /// </remarks>
    internal static MRect Grown(MRect extent, float margin)
    {
        var m = Math.Max(0f, margin);
        var padX = extent.Width * m;
        var padY = extent.Height * m;

        if (padX <= 0) padX = extent.Width > 0 ? 0 : 100;
        if (padY <= 0) padY = extent.Height > 0 ? 0 : 100;

        return new MRect(
            extent.MinX - padX, extent.MinY - padY,
            extent.MaxX + padX, extent.MaxY + padY);
    }
}

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
