using System;
using System.Linq;
using Mapsui;
using Mapsui.Extensions;   // Viewport.HasSize()
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

    // Last size handed to the navigator. Calling SetSize raises ViewportChanged, which Mapsui
    // answers with a data refresh, so calling it every frame keeps the tile layer permanently
    // re-fetching. Only tell it about sizes that actually changed.
    float _width = -1f;
    float _height = -1f;

    public MapsuiLayer(Map map) => _map = map ?? throw new ArgumentNullException(nameof(map));

    /// <summary>Print Mapsui's viewport and layer state over the map.</summary>
    public bool Diagnostics { get; set; }

    /// <summary>The map being drawn, so a patch can navigate it or add layers.</summary>
    public Map Map => _map;

    // A map fills whatever it is given, so it has no natural extent to report. null says that;
    // a concrete rectangle would make downstream layout try to fit it.
    public Stride.Core.Mathematics.RectangleF? Bounds => null;

    /// <summary>
    /// Notifications pass straight through.
    /// </summary>
    /// <remarks>
    /// **Interaction is the patch's business, not this layer's.** An earlier version handled
    /// drag and wheel in here, which quietly decided that the left button pans and the wheel
    /// zooms, and left no way to drive the map from an LFO, an OSC message or a keyboard.
    /// VL.Skia already has Mouse, MouseState and Notifications nodes; a patch reads those and
    /// wires them to Mapsui.Navigate. That composition is the reason to reach for a patching
    /// environment in the first place.
    ///
    /// Returning false leaves every notification for whatever else is in the scene graph.
    /// </remarks>
    public bool Notify(INotification notification, CallerInfo caller) => false;

    public void Render(CallerInfo caller)
    {
        PixelSpace.Draw(caller, (canvas, bounds) =>
        {
            var width = bounds.Width;
            var height = bounds.Height;
            if (width <= 0f || height <= 0f) return;

            // Mapsui derives its viewport from the size it is told about, so this is also what
            // decides which tiles get fetched - which is exactly why it must not be set on
            // every frame. Each call raises ViewportChanged and Mapsui answers with a refresh.
            if (width != _width || height != _height)
            {
                _map.Navigator.SetSize(width, height);
                _width = width;
                _height = height;
            }

            // Mapsui expects its host to do this, and there is no host here. Home carries the
            // initial centre and zoom, and cannot run before the viewport has a size because a
            // zoom level is meaningless without one. Skip it and the map renders at the default
            // resolution, which shows nothing at all - no error, just an empty window.
            if (!_map.HomeIsCalledOnce && _map.Navigator.Viewport.HasSize())
            {
                _map.Home?.Invoke(_map.Navigator);
                _map.HomeIsCalledOnce = true;
                _map.OnViewportSizeInitialized();
                _map.Refresh();
            }

            // Drives fly-to and easing. Also the frame tick Mapsui's own hosts give it.
            _map.UpdateAnimations();

            _renderer.Render(
                canvas,
                _map.Navigator.Viewport,
                _map.Layers,
                _map.Widgets,
                _map.BackColor);

            if (Diagnostics) DrawDiagnostics(canvas, width, height);
        });
    }

    /// <summary>
    /// Print Mapsui's own state over the map.
    /// </summary>
    /// <remarks>
    /// An empty map window says nothing about why it is empty. These few numbers separate the
    /// likely causes at a glance: a Resolution of 0 or NaN means Home never ran, an empty layer
    /// list means the map was never populated, and a plausible viewport with a still-blank
    /// window points at tile fetching rather than at anything here.
    /// </remarks>
    void DrawDiagnostics(SKCanvas canvas, float width, float height)
    {
        var v = _map.Navigator.Viewport;

        using var back = new SKPaint { Color = new SKColor(0, 0, 0, 170) };
        using var text = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 13f,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
        };

        canvas.DrawRect(SKRect.Create(0f, 0f, 480f, 152f), back);

        var y = 20f;
        void Line(string s) { canvas.DrawText(s, 8f, y, text); y += 16f; }


        Line($"viewport   {v.Width:0} x {v.Height:0}   home called: {_map.HomeIsCalledOnce}");
        Line($"center     {v.CenterX:0.##}, {v.CenterY:0.##}   (spherical mercator metres)");
        Line($"resolution {v.Resolution:0.####}   rotation {v.Rotation:0.##}");
        Line($"layers     {_map.Layers.Count}");
        foreach (var l in _map.Layers)
            Line($"  - {l.Name}  enabled={l.Enabled}  busy={l.Busy}");

        // Whether a cache is really attached, not whether one was asked for. And the size on
        // disk, so it never becomes something growing quietly on someone's machine.
        // global:: throughout: our own namespace is VL.Mapsui, so a bare "Mapsui.Tiling" binds
        // to VL.Mapsui.Tiling and does not exist.
        var attached = _map.Layers
            .OfType<global::Mapsui.Tiling.Layers.TileLayer>()
            .Any(l => l.TileSource is BruTile.Web.HttpTileSource { PersistentCache: BruTile.Cache.FileCache });

        if (attached)
        {
            var (tiles, bytes) = TileCache.Stats();
            Line($"cache      {tiles} tiles, {bytes / 1024.0 / 1024.0:0.0} MB on disk");
        }
        else
        {
            Line("cache      off - every restart refetches the same view");
        }

    }

    public void Dispose() => _map.Dispose();
}
