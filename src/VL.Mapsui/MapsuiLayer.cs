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

    // How long a layer change takes to settle, measured rather than guessed.
    //
    // **Second attempt.** The first timed how long any layer stayed Busy and printed "last burst
    // N ms". It produced three identical readings for three different basemaps, which is not a
    // measurement, and the flaw was not Busy's flicker - it was that the display **could not say
    // which event its number belonged to.** A switch whose burst went undetected left the previous
    // switch's figure on screen, looking exactly like a fresh one. A reading nobody can tell is
    // stale is worse than no reading: it gets written into NOTES.md.
    //
    // So this times from the event itself - the frame the map's layer SET changes, which is what a
    // basemap switch is - and counts completions, so a number that did not move is visibly a number
    // that did not move.
    readonly System.Diagnostics.Stopwatch _settling = new();
    global::Mapsui.Layers.ILayer[] _lastLayers = Array.Empty<global::Mapsui.Layers.ILayer>();
    bool _isSettling;
    double _quietSince = -1;
    double _lastSwitchMs;
    int _switches;

    // How long every layer must stay un-Busy before the picture counts as complete. Busy is
    // propagated from TileFetchDispatcher through PropertyChanged and can drop between batches, so
    // the first quiet frame is not the end. 300 ms is long enough to bridge that and short enough
    // to stay inside a switch; it is subtracted back out of the reported figure.
    const double QuietMs = 300;

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
    ///
    /// **Widgets are not an exception, and an earlier version of this made them one.** It handled a
    /// mouse press in here and offered it to the widgets, which quietly decided that a left press
    /// is what clicking a widget means - the same mistake as deciding that the left button pans,
    /// one layer down. vvvv's own answer is in VL.Skia's Explanation Mouse and Keyboard: *"The
    /// Mouse and Keyboard nodes need to be connected to the Renderer they want to interact with"*.
    /// The mouse is a value you wire, not something a layer swallows. So a press reaches a widget
    /// through the <c>Click</c> node, in the patch, where it is visible - and that node reports
    /// whether a widget took it, so the patch can decide not to pan as well.
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

        canvas.DrawRect(SKRect.Create(0f, 0f, 480f, 202f), back);

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

        // Whether one is attached, not where it is: a FileCache does not expose its folder, and
        // both the TileCache node and the layer node report that on a pin - which is the better
        // place for it anyway, being something a patch can read and act on.
        Line(attached
            ? "cache      on (see the layer node's Cache Status pin)"
            : "cache      off - every restart refetches the same view");

        // Widgets, and whether they can be clicked at all. Envelope is written by the renderer
        // while drawing, so "0 of 3 placed" means nothing has been laid out yet rather than that
        // the click arithmetic is wrong - two failures that look identical from the outside.
        var widgets = _map.Widgets.ToArray();
        var placed = widgets.Count(w => w.Envelope is not null);
        Line($"widgets    {widgets.Length}, {placed} placed by the renderer");

        // The cost of a basemap switch, as a number that says which switch it belongs to.
        var layers = _map.Layers.ToArray();
        if (!layers.SequenceEqual(_lastLayers))
        {
            _lastLayers = layers;
            _settling.Restart();
            _isSettling = true;
            _quietSince = -1;
        }

        if (_isSettling)
        {
            var now = _settling.Elapsed.TotalMilliseconds;
            if (layers.Any(l => l.Busy)) _quietSince = -1;
            else if (_quietSince < 0) _quietSince = now;
            else if (now - _quietSince >= QuietMs)
            {
                _lastSwitchMs = _quietSince;    // the quiet period itself is not part of the cost
                _switches++;
                _isSettling = false;
            }
        }

        // The counter is the point. If it does not move when you change the basemap, the figure
        // beside it belongs to an earlier change and means nothing about this one.
        Line(_isSettling
            ? $"switch #{_switches + 1}  settling, {_settling.Elapsed.TotalMilliseconds:0} ms so far"
            : $"switch #{_switches}  settled in {_lastSwitchMs:0} ms");

    }

    public void Dispose() => _map.Dispose();
}
