using System;
using System.Linq;
using VL.Core.Import;

// global:: throughout: our own namespace is VL.Mapsui, so a bare "Mapsui.Widgets" would bind to
// VL.Mapsui.Widgets and not exist.
using Map = global::Mapsui.Map;
using IWidget = global::Mapsui.Widgets.IWidget;
using HAlign = global::Mapsui.Widgets.HorizontalAlignment;
using VAlign = global::Mapsui.Widgets.VerticalAlignment;
using ScaleBarWidget = global::Mapsui.Widgets.ScaleBar.ScaleBarWidget;
using ZoomInOutWidget = global::Mapsui.Widgets.Zoom.ZoomInOutWidget;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// The buttons draw as soon as they are on the map, but a press has to be wired to them like
// anything else in this package: Click [Mapsui.Widgets] takes a position and a pressed gate,
// exactly as Drag does. Nothing here decides that the left button means clicking a widget.

/// <summary>
/// Shows plus and minus buttons that zoom the map.
/// </summary>
/// <remarks>
/// The buttons respond only to presses wired through a Click node.
/// </remarks>
[ProcessNode(Name = "ZoomButtons", Category = "Mapsui.Widgets")]
public class ZoomButtonsWidgetNode
{
    readonly WidgetSlot<ZoomInOutWidget> _slot = new();

    /// <summary>Widgets this node has added. It should reach 1 and stay there.</summary>
    internal int WidgetsAdded => _slot.Added;

    /// <summary>
    /// Configures the zoom buttons on the map.
    /// </summary>
    /// <param name="map">The map to put them on.</param>
    /// <param name="enabled">Nothing is drawn while off.</param>
    /// <param name="corner">The corner of the map they sit in.</param>
    /// <returns>The same map, for the chain between Map and ToSkiaLayer.</returns>
    public Map? Update(
        Map? map,
        bool enabled = true,
        WidgetCorner corner = WidgetCorner.TopRight)
    {
        if (map is null) return null;

        var widget = _slot.Ensure(map, _ => new ZoomInOutWidget());

        widget.Enabled = enabled;
        WidgetSlot<ZoomInOutWidget>.Place(widget, corner);

        return map;
    }
}
