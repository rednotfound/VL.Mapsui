using System.Linq;
using Mapsui;

namespace VL.Mapsui;

/// <summary>
/// Handing a click to a widget, which is a thing only the host can do.
/// </summary>
/// <remarks>
/// The arithmetic only. **What counts as a click is the patch's business** and arrives through
/// <c>Click [Mapsui.Widgets]</c>; this is what that node calls once it has been told a press
/// happened. Everything here is a Map, a point and a bool, so it can be tested without a canvas -
/// which is also why the first version of this lived in the Skia layer and was wrong: putting it
/// there meant deciding for every patch that a left press is what clicking a widget means.
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
