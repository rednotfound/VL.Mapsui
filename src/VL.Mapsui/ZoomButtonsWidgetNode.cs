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

/// <summary>
/// Plus and minus buttons that zoom the map. Wire a <c>Click</c> node for them to respond.
/// </summary>
/// <remarks>
/// The buttons draw as soon as they are on the map, but a press has to be wired to them like
/// anything else in this package: <c>Click [Mapsui.Widgets]</c> takes a position and a pressed
/// gate, exactly as <c>Drag</c> does. Nothing here decides that the left button means clicking a
/// widget.
/// </remarks>
[ProcessNode(Name = "ZoomButtons", Category = "Mapsui.Widgets")]
public class ZoomButtonsWidgetNode
{
    readonly WidgetSlot<ZoomInOutWidget> _slot = new();

    /// <summary>Widgets this node has added. It should reach 1 and stay there.</summary>
    internal int WidgetsAdded => _slot.Added;

    /// <summary>The same map, so this sits in the chain between Map and ToSkiaLayer.</summary>
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
