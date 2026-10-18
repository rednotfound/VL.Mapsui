using Mapsui;
using VL.Core.Import;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A process node for one reason: dragging needs the position from the previous frame, and keeping
// that is bookkeeping rather than a decision. Making every patch wire a FrameDelay for it would be
// noise.
//
// What is deliberately not decided here: what counts as dragging. The left button is the obvious
// answer and it is not the only one - a patch might want the right button, a modifier, a touch
// gesture, a foot pedal. Gate the Dragging pin with whatever it likes; VL.Skia's MouseState has
// Left Pressed, Middle Pressed and Right Pressed sitting there.
//
// The first frame of a gesture only records where it started, so a press does not jump the map by
// however far the pointer happened to be from wherever it was last time.

/// <summary>
/// Pans the map while Dragging is on, following a position in view pixels.
/// </summary>
/// <remarks>
/// Wire it to a mouse, a touch point, or anything else that moves and can be gated.
/// Gate Dragging with whatever should count as a drag, for example MouseState's Left Pressed.
/// </remarks>
[ProcessNode(Name = "Drag", Category = "Mapsui.Navigate")]
public class DragNode
{
    bool _wasDragging;
    float _lastX;
    float _lastY;

    // Returns the map so this can sit in a chain with the other navigation nodes.

    /// <summary>
    /// Pans the map by how far the position moved since the last frame.
    /// </summary>
    /// <param name="map">The map to move.</param>
    /// <param name="x">The position, pixels from the left of the view.</param>
    /// <param name="y">The position, pixels from the top of the view.</param>
    /// <param name="dragging">Pans only while this is on.</param>
    /// <returns>The same map.</returns>
    public Map Update(Map map, float x = 0f, float y = 0f, bool dragging = false)
    {
        if (map is null) return map!;

        if (!dragging)
        {
            // Ending a gesture is worth one discrete refresh: during the drag Mapsui was asked
            // to fetch conservatively, so this is the moment to ask for everything on screen.
            if (_wasDragging) NavigateNodes.Refresh(map, continuous: false);
            _wasDragging = false;
            return map;
        }

        if (!_wasDragging)
        {
            _wasDragging = true;
            _lastX = x;
            _lastY = y;
            return map;
        }

        NavigateNodes.DragBetween(map, x, y, _lastX, _lastY);
        _lastX = x;
        _lastY = y;
        return map;
    }
}
