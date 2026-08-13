using System;
using Mapsui;
using Mapsui.Rendering.Skia;
using SkiaSharp;
using VL.Lib.IO.Notifications;
using VL.Skia;

namespace VL.Mapsui;

/// <summary>
/// Renders a Mapsui map into VL.Skia's scene graph.
/// </summary>
/// <remarks>
/// Mapsui owns everything about how the map looks - tiles, styles, labels, widgets. This class
/// only decides where on the canvas that happens, which is the one thing Mapsui cannot know.
/// See <see cref="PixelSpace"/> for why the matrix is reset rather than the Renderer configured.
/// </remarks>
sealed class MapsuiLayer : ILayer, IDisposable
{
    readonly Map _map;
    readonly MapRenderer _renderer = new();

    public MapsuiLayer(Map map) => _map = map ?? throw new ArgumentNullException(nameof(map));

    /// <summary>The map being drawn, so a patch can navigate it or add layers.</summary>
    public Map Map => _map;

    // A map fills whatever it is given, so it has no natural extent to report. null says that;
    // a concrete rectangle would make downstream layout try to fit it.
    public Stride.Core.Mathematics.RectangleF? Bounds => null;

    // Interaction comes after the map renders at all. Returning false leaves notifications for
    // whatever else is in the scene graph rather than swallowing them.
    public bool Notify(INotification notification, CallerInfo caller) => false;

    public void Render(CallerInfo caller)
    {
        PixelSpace.Draw(caller, (canvas, bounds) =>
        {
            var width = bounds.Width;
            var height = bounds.Height;
            if (width <= 0f || height <= 0f) return;

            // Mapsui derives its viewport from the size it is told about, so this is also what
            // decides which tiles get fetched. Setting it every frame is cheap and is how
            // Mapsui's own hosts handle a resizable window.
            _map.Navigator.SetSize(width, height);

            _renderer.Render(
                canvas,
                _map.Navigator.Viewport,
                _map.Layers,
                _map.Widgets,
                _map.BackColor);
        });
    }

    public void Dispose() => _map.Dispose();
}
