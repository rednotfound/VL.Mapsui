using System;
using Mapsui;
using Mapsui.Extensions;   // Viewport.HasSize()
using VL.Core.Import;
using VL.Skia;

namespace VL.Mapsui;

/// <summary>
/// Drawing a Mapsui map into VL.Skia's scene graph.
/// </summary>
/// <remarks>
/// A process node because it owns a Mapsui MapRenderer, which caches what it has rasterised.
/// </remarks>
[ProcessNode(Name = "ToSkiaLayer", Category = "Mapsui.Skia")]
public class ToSkiaLayerNode : IDisposable
{
    MapsuiLayer? _layer;
    Map? _map;

    /// <summary>
    /// The layer to hand a Renderer.
    /// </summary>
    /// <remarks>
    /// The Renderer's Space pin does not need setting. The layer resets the canvas matrix to
    /// pixels itself, because an unrecognised Space value is silently replaced by the default
    /// and the result is indistinguishable from a broken patch.
    ///
    /// This node does not handle the mouse. Interaction is the patch's business: read the mouse
    /// with VL.Skia's own nodes and drive Mapsui.Navigate with it.
    /// </remarks>
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

    /// <summary>
    /// Drops the layer. The map is not disposed here: it belongs to the Map node, and VL
    /// disposes that separately.
    /// </summary>
    public void Dispose()
    {
        _layer = null;
        _map = null;
    }
}
