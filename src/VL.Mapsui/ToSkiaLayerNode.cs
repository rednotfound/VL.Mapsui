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
/// Draws a map into VL.Skia's scene graph.
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
    // This node does not handle the mouse. Interaction is the patch's business: read the mouse with
    // VL.Skia's own nodes and drive Mapsui.Navigate with it.

    /// <summary>
    /// Configures the layer that draws the map.
    /// </summary>
    /// <remarks>
    /// The Renderer's Space pin does not need setting.
    /// The mouse is not handled here: wire VL.Skia's mouse nodes to Mapsui.Navigate.
    /// </remarks>
    /// <param name="map">The map to draw.</param>
    /// <param name="diagnostics">Prints the map's viewport and layer state over it.</param>
    /// <returns>A layer for a Renderer.</returns>
    public ILayer? Update(Map? map, bool diagnostics = false)
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
