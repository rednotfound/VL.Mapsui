using System;
using Mapsui;
using Mapsui.Extensions;   // Viewport.HasSize()
using VL.Core.Import;
using VL.Skia;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A process node because it owns a Mapsui MapRenderer, which caches what it has rasterised.

/// <summary>
/// Draws a map into VL.Skia's scene graph, and lets the mouse move it.
/// </summary>
[ProcessNode(Name = "ToSkiaLayer", Category = "Mapsui.Skia")]
public class ToSkiaLayerNode : IDisposable
{
    MapsuiLayer? _layer;
    Map? _map;

    // The Renderer's Space pin does not need setting. The layer resets the canvas matrix to pixels
    // itself, because an unrecognised Space value is silently replaced by the default and the
    // result is indistinguishable from a broken patch.
    //
    // The map answers the mouse by default since 2026-11-15 (MapInteraction has the decision).
    // Interactive is the pin that says no; wiring a Drag, ZoomByWheel or Click node takes that one
    // gesture over without touching the pin.

    /// <summary>
    /// Configures the layer that draws the map.
    /// </summary>
    /// <remarks>
    /// Drag with the left button to pan, turn the wheel to zoom.
    /// A Drag, ZoomByWheel or Click node wired to the map takes that gesture over.
    /// With UI drawn over the map, switch Interactive off while the mouse is on it: ImGui has IsAnyItemHovered.
    /// The Renderer's Space pin does not need setting.
    /// </remarks>
    /// <param name="map">The map to draw.</param>
    /// <param name="diagnostics">Prints the map's viewport and layer state over it.</param>
    /// <param name="interactive">Off makes the map ignore the mouse, to drive it entirely from the patch.</param>
    /// <returns>A layer for a Renderer.</returns>
    public ILayer? Update(Map? map, bool diagnostics = false, bool interactive = true)
    {
        if (map is null)
        {
            _layer = null;
            _map = null;
            return null;
        }

        // Only when the map itself is swapped. A new layer per frame would throw away the
        // renderer's raster cache along with it.
        if (_layer is null || !ReferenceEquals(map, _map))
        {
            _layer = new MapsuiLayer(map);
            _map = map;
        }

        _layer.Diagnostics = diagnostics;
        _layer.Interactive = interactive;
        return _layer;
    }

    // The map is not disposed here: it belongs to the Map node, and VL disposes that separately.

    /// <summary>Releases the layer.</summary>
    public void Dispose()
    {
        _layer = null;
        _map = null;
    }
}
