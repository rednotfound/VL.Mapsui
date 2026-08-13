using Mapsui;
using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// Pans the map while Dragging is on, following a position in view pixels.
/// Wire it to a mouse, a touch point, or anything else that moves and can be gated.
/// </summary>
/// <remarks>
/// A process node for one reason: dragging needs the position from the previous frame, and
/// keeping that is bookkeeping rather than a decision. Making every patch wire a FrameDelay for
/// it would be noise.
///
/// **What is deliberately not decided here: what counts as dragging.** The left button is the
/// obvious answer and it is not the only one - a patch might want the right button, a modifier,
/// a touch gesture, a foot pedal. Gate the Dragging pin with whatever it likes; VL.Skia's
/// MouseState has Left Pressed, Middle Pressed and Right Pressed sitting there.
///
/// The first frame of a gesture only records where it started, so a press does not jump the map
/// by however far the pointer happened to be from wherever it was last time.
/// </remarks>
[ProcessNode(Name = "Drag", Category = "Mapsui.Navigate")]
public class DragNode
{
    bool _wasDragging;
    float _lastX;
    float _lastY;

    /// <summary>The map, so this can sit in a chain with the other navigation nodes.</summary>
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
