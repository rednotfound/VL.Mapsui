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
/// Offers a press to the widgets on the map. Handled says whether one took it.
/// </summary>
/// <remarks>
/// **This node exists so that a click is wired rather than assumed.** A first version handled the
/// press inside the Skia layer, which meant deciding for every patch that a left press is what
/// clicking a widget means — the same thing this package refuses to do for panning and zooming.
/// vvvv's own answer is in VL.Skia's *Explanation Mouse and Keyboard*: the Mouse node is connected
/// to the Renderer it interacts with, and its position is wired onward. So this takes a position
/// and a gate, exactly like <c>Drag</c>, and a patch is free to use the right button, a modifier or
/// a touch point instead.
///
/// **Handled is the pin that earns the node.** With the press swallowed inside the layer, a patch
/// could not know that a click had hit a button — so pressing zoom would also start a pan, and
/// nothing on screen would say why. Gate Dragging with it: `Left Pressed AND NOT Handled`.
///
/// Only the rising edge routes. A press held down would otherwise zoom on every frame, which is a
/// fresh round of tile requests sixty times a second — the same reason ZoomIn and ZoomOut watch
/// their trigger's edge rather than its level.
/// </remarks>
[ProcessNode(Name = "Click", Category = "Mapsui.Widgets")]
public class WidgetClickNode
{
    bool _wasPressed;

    /// <summary>The same map, so this sits in the chain with the widget nodes.</summary>
    public Map? Update(
        Map? map,
        out bool handled,
        float x = 0f,
        float y = 0f,
        bool pressed = false)
    {
        handled = false;
        if (map is null) return null;

        if (pressed && !_wasPressed)
            handled = WidgetInput.Route(map, new global::Mapsui.MPoint(x, y));

        _wasPressed = pressed;
        return map;
    }
}
