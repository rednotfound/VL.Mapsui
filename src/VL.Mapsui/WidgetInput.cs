using System.Linq;
using Mapsui;

namespace VL.Mapsui;

/// <summary>
/// Handing a click to a widget, which is a thing only the host can do.
/// </summary>
/// <remarks>
/// **This is not the same as deciding what the mouse means.** <see cref="MapsuiLayer"/> deliberately
/// leaves panning and zooming to the patch, because an earlier version that handled drag and wheel
/// internally quietly decided that the left button pans, and left no way to drive the map from an
/// LFO or an OSC message. A widget is different: it is something the patch explicitly added to the
/// map, it draws itself at a position only the renderer knows, and there is nothing else in the
/// scene graph that could route to it. Mapsui's own hosts do exactly this in their MapControl.
///
/// Kept out of the layer so it can be tested without a canvas: everything here is a Map, a point,
/// and a bool.
/// </remarks>
static class WidgetInput
{
    /// <summary>
    /// Offer a press at <paramref name="position"/> (in the same pixels the map is drawn in) to the
    /// widgets on the map. True if one took it, which is the caller's cue to keep it to itself.
    /// </summary>
    /// <remarks>
    /// Envelope is set by the renderer while drawing, so it is null until the first frame has been
    /// drawn and a click before then simply lands on the map. Widgets are offered in reverse order
    /// so the most recently added - the one drawn last, and so on top - gets first refusal.
    /// </remarks>
    public static bool Route(Map? map, MPoint position)
    {
        if (map is null) return false;

        foreach (var widget in map.Widgets.Reverse())
        {
            if (!widget.Enabled) continue;
            if (widget.Envelope is null || !widget.Envelope.Contains(position)) continue;

            if (widget.HandleWidgetTouched(map.Navigator, position))
                return true;
        }

        return false;
    }
}
