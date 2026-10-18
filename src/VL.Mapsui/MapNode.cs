using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using VL.Core.Import;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A process node because the map owns its layers, and those own connections and caches.
//
// The Initial pins are named that way because they mean it. They are applied once, when the
// viewport first has a size - a zoom level means nothing before then - and ignored on every frame
// after. Reading them continuously would undo any navigation on the very next frame, so dragging a
// map would be impossible.
//
// To move the map, use the nodes in Mapsui.Navigate. That is also where a patch wires the mouse,
// an LFO, or anything else it wants to drive the view with.

/// <summary>
/// A map: a stack of layers, a viewport and a navigator.
/// </summary>
/// <remarks>
/// Move it with the nodes in Mapsui.Navigate and draw it with ToSkiaLayer.
/// The Initial pins are applied once, when the map first has a size, so they never undo navigation.
/// </remarks>
[ProcessNode(Name = "Map", Category = "Mapsui")]
public class MapNode : IDisposable
{
    readonly Map _map = new();
    ILayer[] _current = Array.Empty<ILayer>();

    /// <summary>Maps built by this node. Always one: the map outlives every layer change.</summary>
    internal int MapsBuilt { get; private set; }

    /// <summary>
    /// Configures the map with its layers.
    /// </summary>
    /// <param name="layers">The layers, bottom first.</param>
    /// <param name="initialCenterLongitude">Longitude of the centre when the map first appears.</param>
    /// <param name="initialCenterLatitude">Latitude of the centre when the map first appears.</param>
    /// <param name="initialZoomLevel">Zoom level when the map first appears.</param>
    /// <returns>The map, ready for the navigation and drawing nodes.</returns>
    public Map Update(
        IEnumerable<ILayer>? layers = null,
        double initialCenterLongitude = 139.7,
        double initialCenterLatitude = 35.68,
        int initialZoomLevel = 12)
    {
        if (MapsBuilt == 0)
        {
            MapsBuilt = 1;

            // Home runs once the viewport has a size, which the renderer sets. Calling the
            // navigator here would be a no-op, and skipping Home altogether leaves the map at a
            // default resolution that shows nothing at all - no error, just an empty window.
            var center = SphericalMercator.FromLonLat(initialCenterLongitude, initialCenterLatitude);
            _map.Home = navigator =>
            {
                // Without this, CenterOn is silently clamped to the extent of the layers - the
                // default pan bounds. A tile map's schema extent is the whole world, so nobody
                // saw it there; a feature-only map is clamped to its own features, and a map
                // whose only layer was a single point near (0,0) swallowed its Initial Center
                // whole (found by vl-overworld Tutorial 01, 2026-09-23: zoom applied, centre
                // ignored, no error). The override IS the tile-world extent, so every map pans
                // the same whether or not tiles are present.
                navigator.OverridePanBounds = new MRect(
                    -20037508.342789244, -20037508.342789244,
                     20037508.342789244,  20037508.342789244);
                navigator.CenterOn(center.x, center.y);

                // ZoomToLevel looks the level up in the navigator's resolutions list, and that
                // list comes from a tile layer's schema. A map WITHOUT a tile layer has an empty
                // list, and Mapsui answers with a log line and no zoom at all - the viewport
                // stays at its default resolution of 1 metre per pixel, a few hundred metres
                // wide, which on a feature map looks like one polygon's fill filling the window.
                // Found by the first offline help patch, 2026-08-23: a full-screen solid colour
                // with every readout healthy. The fallback is the number the list would have
                // held: the standard WebMercator resolution for that level, 256-pixel tiles.
                // The same fallback ZoomToLevel uses, from one place (ZoomLadder): the two drifted
                // apart once, and ZoomToLevel stayed broken on tile-less maps for a month.
                ZoomLadder.ZoomToLevel(navigator, initialZoomLevel);
            };
        }

        // Only touch the collection when the set of layers actually changed. Clearing and
        // re-adding every frame would discard each layer's fetched tiles, which is the same
        // per-frame-rebuild mistake one level up.
        var incoming = (layers ?? Enumerable.Empty<ILayer>()).Where(l => l is not null).ToArray();
        if (!incoming.SequenceEqual(_current))
        {
            _map.Layers.Clear();
            foreach (var l in incoming) _map.Layers.Add(l);
            _current = incoming;

            // A new layer holds nothing until someone asks it to fetch, and Mapsui expects its
            // host to do the asking. Without this the map stays empty after Enabled is switched
            // on: Home already ran, on a map that had no layers at the time, and nothing else
            // ever triggers a fetch. The same omission is what left panning on stale tiles until
            // the navigation nodes started calling Refresh.
            _map.Refresh();
        }

        // The frame tick Mapsui's own hosts give it. **Both calls are needed and they do
        // different things.** Map.UpdateAnimations only walks the layers - read it in Mapsui
        // 4.1.9 and it is a foreach over Layers and nothing else. The viewport's own animation,
        // which is what MouseWheelZoom and FlyTo start, lives on the Navigator.
        //
        // Calling only the first is why wheel zoom did nothing for a round while the bang nodes
        // worked: Navigator.ZoomIn lands immediately, whereas MouseWheelZoom eases over
        // MouseWheelAnimation.Duration and an animation nobody advances never arrives.
        _map.UpdateAnimations();
        _map.Navigator.UpdateAnimations();

        return _map;
    }

    // Not the layers: those belong to the nodes that made them, and VL disposes those separately.

    /// <summary>Releases the map.</summary>
    public void Dispose()
    {
        _map.Layers.Clear();
        _map.Dispose();
    }
}
