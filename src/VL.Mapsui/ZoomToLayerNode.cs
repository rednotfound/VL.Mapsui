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
// The node that was missing. Without it an example has to hardcode a centre and a zoom that happen
// to match its data, and the first one that did got them 350 km apart - the shape was off screen
// and it read as a rendering fault. "Show me the data" is a thing a patch should be able to say.
//
// Triggered, not continuous, and that is the same safety property as ZoomIn's. Applied every frame
// it would pin the view to the layer and the map could not be moved at all - which is exactly what
// CenterOn running every frame did to a help patch. Edge detection is mechanism; what counts as a
// trigger (a bang, a key, whatever should mean "show me everything") stays in the patch.
//
// A layer with nothing in it has no extent, and nothing happens rather than the view jumping to
// some default. That matters because a feature layer is empty until its data arrives, and a bang
// that landed a frame early would otherwise send the view somewhere arbitrary.

/// <summary>
/// Frames a layer's extent, so the view shows where the data is.
/// </summary>
/// <remarks>
/// Acts once each time Trigger turns on, so the map can still be moved afterwards.
/// An empty layer leaves the view where it is.
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

    // Returns the map so this can sit in a chain with the other navigation nodes.
    //
    // Margin is a fraction of the extent left as breathing room - a shape flush against the window
    // edge reads as clipped even when it is not. It earns its pin: the alternative is every patch
    // growing the rectangle by hand before it can frame anything.

    /// <summary>
    /// Frames the layer on the rising edge of Trigger.
    /// </summary>
    /// <param name="map">The map to move.</param>
    /// <param name="layer">The layer to frame.</param>
    /// <param name="trigger">Frames the layer once when this turns on.</param>
    /// <param name="margin">Room left around the layer, as a fraction of its size.</param>
    /// <returns>The same map.</returns>
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
