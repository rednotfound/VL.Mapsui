using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using NtsFeature = NetTopologySuite.Features.Feature;
using SphericalMercator = global::Mapsui.Projections.SphericalMercator;

namespace VL.Mapsui;

/// <summary>
/// Draws geometry on the map — the layer that puts your own data over the basemap.
/// Uses Mapsui and NetTopologySuite.
/// </summary>
/// <remarks>
/// **This is where the two packages meet, and they meet through NetTopologySuite rather than
/// through each other.** VL.GIS computes geometry — reproject, buffer, intersect, read GeoJSON —
/// and hands out plain NTS types; this turns those into something Mapsui draws. Neither package
/// references the other, and neither needs to: NTS is the vocabulary they already share, which is
/// also why the same node works for geometry from any other source.
///
/// **Coordinates are WGS84 longitude and latitude, x first.** That is what VL.GIS produces and
/// what every GeoJSON file carries; Mapsui works in spherical mercator, so this projects on the
/// way in. Doing it here rather than making the patch do it is the point of the node: which
/// projection a map engine happens to draw in is not a decision anyone wants to take.
///
/// A process node rather than a static method, for the usual reason — see the OpenStreetMap
/// layer. Rebuilding a layer every frame is how a map ends up refetching and re-styling sixty
/// times a second.
/// </remarks>
[ProcessNode(Name = "Geometry", Category = "Mapsui.Layers")]
public class GeometryLayerNode : IDisposable
{
    // The shortcut is composed of the three nodes it stands in for rather than being a fourth
    // implementation of the same thing. If they compose here they compose in a patch.
    readonly VectorStyleNode _style = new();
    readonly FeatureLayerNode _layer = new();

    Geometry[] _geometries = Array.Empty<Geometry>();
    NtsFeature[] _features = Array.Empty<NtsFeature>();

    /// <summary>Layers built by this node. Should settle at 1 and stay there.</summary>
    internal int LayersBuilt => _layer.LayersBuilt;

    /// <summary>
    /// A layer drawing the given geometry, ready to hand to a Map alongside a tile layer.
    /// </summary>
    /// <remarks>
    /// Feed it geometry from VL.GIS — <c>GIS.Geometry</c>, <c>GIS.Serialization</c>,
    /// <c>GIS.Projection</c> — in WGS84 lon/lat. An empty or null input gives no layer rather
    /// than an empty one, so a Map can be wired up before there is anything to draw.
    ///
    /// **Watch Layers Built.** A number that climbs frame after frame means one of the inputs is
    /// changing every frame; the geometry set is compared by identity, so building new geometry
    /// objects each frame rebuilds the layer even when the shapes are the same.
    /// </remarks>
    public ILayer? Update(
        out int layersBuilt,
        IEnumerable<Geometry>? geometries = null,
        Color4? fillColor = null,
        Color4? lineColor = null,
        float lineWidth = 2f)
    {
        var incoming = geometries?.Where(g => g is not null).ToArray() ?? Array.Empty<Geometry>();

        // Features are rebuilt only when the geometry set changes. Building them every frame would
        // hand the layer a new set every frame and rebuild it, which is the whole failure this
        // package guards against - and it would be hidden inside a convenience node, which is
        // worse than having it in the open.
        if (!incoming.SequenceEqual(_geometries))
        {
            _features = incoming.Select(g => FeatureNodes.Feature(g)).ToArray();
            _geometries = incoming;
        }

        return _layer.Update(out layersBuilt, _features, _style.Update(fillColor, lineColor, lineWidth), "Geometry");
    }

    /// <summary>
    /// The same geometry in spherical mercator, which is what the map draws in.
    /// </summary>
    /// <remarks>
    /// On a copy, through NTS's own coordinate filter. Projecting in place would mutate geometry
    /// the patch still holds — the caller would find its own WGS84 shapes silently turned into
    /// metres, and every later reprojection would compound it.
    /// </remarks>
    internal static Geometry ToMercator(Geometry geometry)
    {
        var projected = geometry.Copy();
        projected.Apply(new LonLatToMercator());
        projected.GeometryChanged();
        return projected;
    }

    sealed class LonLatToMercator : ICoordinateSequenceFilter
    {
        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            var (x, y) = SphericalMercator.FromLonLat(seq.GetX(i), seq.GetY(i));
            seq.SetX(i, x);
            seq.SetY(i, y);
        }
    }

    /// <summary>Releases the layer it composed.</summary>
    public void Dispose() => _layer.Dispose();
}
