using System;
using System.Runtime.CompilerServices;
using Mapsui;
using VL.Lib.IO;
using VL.Lib.IO.Notifications;

namespace VL.Mapsui;

// The mouse a map answers to when nobody wired one: left drag pans, the wheel zooms, a left press
// on a widget clicks it.
//
// DECIDED 2026-11-15, reversing 2026-08: until then the layer ignored every notification and a
// patch had to wire Console, MouseState, Drag and ZoomByWheel before a map would move - 14 nodes
// against 6 for a map that stands still. The maintainer's point: nobody wants to build the floor.
// MapLibre, OpenLayers, Leaflet and deck.gl all answer the mouse out of the box and let you switch
// handlers off; vvvv's own SceneWindow has Enable Default Camera, and Skia's Camera reads the
// mouse itself. docs/RULES.md had recorded the tension as undecided, noting that the incident
// behind "never bundle what the mouse means" was a node that REBUILT the map on movement, not one
// that interpreted the mouse. That rule stands; this goes through the same Navigator calls the
// Navigate nodes use and rebuilds nothing.
//
// TWO WAYS OUT, because a default that cannot be overridden is the old mistake:
//
//   1. ToSkiaLayer's Interactive pin, off: the map ignores the mouse altogether - for a map driven
//      by an LFO, OSC, or a patch's own idea of what a drag is.
//   2. Wire the node and the default steps aside. A Drag, DragBetween, ZoomByWheel or Click node
//      that is running on this map takes that one gesture over, so a patch written against
//      0.0.2-alpha, where these nodes were the only way, does not pan twice per drag. Each node
//      stamps the map every frame; a stamp older than StepAsideMs is a node that has gone.
//
// Nothing here marks a notification handled. A Console in the same Group still hears everything,
// so Pick and ScreenToWorld keep working off the same mouse.

sealed class MapInteraction
{
    internal enum Gesture { Drag, Wheel, Click }

    sealed class Stamps
    {
        public long Drag = long.MinValue, Wheel = long.MinValue, Click = long.MinValue;
    }

    // Weak, so a stamp never keeps a map alive.
    static readonly ConditionalWeakTable<Map, Stamps> PatchDriven = new();

    // A patch runs at tens of frames a second; a quarter of a second without a stamp means the
    // node is no longer being evaluated.
    internal const long StepAsideMs = 250;

    // Replaceable so a test can let a stamp go stale without sleeping.
    internal Func<long> Clock = static () => Environment.TickCount64;

    bool _dragging;
    float _lastX;
    float _lastY;

    /// <summary>Called by a Navigate or Widgets node every frame it runs: this gesture is the patch's now.</summary>
    internal static void DrivenByPatch(Map? map, Gesture gesture)
    {
        if (map is null) return;
        var stamps = PatchDriven.GetOrCreateValue(map);
        var now = Environment.TickCount64;
        switch (gesture)
        {
            case Gesture.Drag: stamps.Drag = now; break;
            case Gesture.Wheel: stamps.Wheel = now; break;
            default: stamps.Click = now; break;
        }
    }

    bool PatchHas(Map map, Gesture gesture)
    {
        if (!PatchDriven.TryGetValue(map, out var stamps)) return false;
        var stamp = gesture switch
        {
            Gesture.Drag => stamps.Drag,
            Gesture.Wheel => stamps.Wheel,
            _ => stamps.Click,
        };
        return stamp != long.MinValue && Clock() - stamp <= StepAsideMs;
    }

    /// <summary>Forget a drag in progress, for when interaction is switched off mid-gesture.</summary>
    internal void Cancel() => _dragging = false;

    /// <summary>One notification from the Renderer. Width and height are the map's size in its own pixels.</summary>
    internal void Handle(Map map, INotification notification, float width, float height)
    {
        if (notification is not MouseNotification mouse) return;

        // The Renderer reports positions in client-area pixels; the map was told its size by the
        // canvas. They are the same number on every setup seen so far, and scaling costs nothing
        // where they are not.
        var scaleX = width > 0f && mouse.ClientArea.X > 0f ? width / mouse.ClientArea.X : 1f;
        var scaleY = height > 0f && mouse.ClientArea.Y > 0f ? height / mouse.ClientArea.Y : 1f;
        var x = mouse.Position.X * scaleX;
        var y = mouse.Position.Y * scaleY;

        switch (mouse)
        {
            // Ending a gesture is never skipped: a release that something else claimed must not
            // leave the map glued to the pointer.
            case MouseUpNotification up when (up.Buttons & MouseButtons.Left) != 0:
            case MouseLostNotification:
                if (_dragging) NavigateNodes.Refresh(map, continuous: false);
                _dragging = false;
                break;

            case MouseDownNotification down when (down.Buttons & MouseButtons.Left) != 0 && !notification.Handled:
                // A widget gets the press first, and a press it took is not the start of a drag.
                if (!PatchHas(map, Gesture.Click) && WidgetInput.Route(map, new MPoint(x, y))) break;
                if (PatchHas(map, Gesture.Drag)) break;
                _dragging = true;
                _lastX = x;
                _lastY = y;
                break;

            case MouseMoveNotification when _dragging:
                if (PatchHas(map, Gesture.Drag)) { _dragging = false; break; }
                NavigateNodes.Pan(map, x, y, _lastX, _lastY);
                _lastX = x;
                _lastY = y;
                break;

            // One step per notification, whatever the magnitude: Windows sends 120 a notch, a
            // precision touchpad sends less and more often, and vvvv's own patches take the sign.
            case MouseWheelNotification wheel when !notification.Handled && !PatchHas(map, Gesture.Wheel):
                NavigateNodes.ZoomAt(map, x, y, Math.Sign(wheel.WheelDelta));
                break;
        }
    }
}
