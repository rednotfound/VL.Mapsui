using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using VectorStyle = global::Mapsui.Styles.VectorStyle;
using Brush = global::Mapsui.Styles.Brush;
using Pen = global::Mapsui.Styles.Pen;
using MapsuiColor = global::Mapsui.Styles.Color;
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
    MemoryLayer? _layer;
    Geometry[] _geometries = Array.Empty<Geometry>();
    Color4 _fill;
    Color4 _line;
    float _lineWidth = -1f;

    /// <summary>Layers built by this node. Should settle at 1 and stay there.</summary>
    internal int LayersBuilt { get; private set; }

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
        // A machine-independent default still cannot be a literal in the signature: Color4 is not
        // a compile-time constant. Same rule as the cache folder, one type along.
        var fill = fillColor ?? new Color4(1f, 0.2f, 0.1f, 0.35f);
        var line = lineColor ?? new Color4(1f, 0.2f, 0.1f, 0.9f);

        var incoming = geometries?.Where(g => g is not null).ToArray() ?? Array.Empty<Geometry>();

        if (incoming.Length == 0)
        {
            Release();
            layersBuilt = LayersBuilt;
            return null;
        }

        if (_layer is null
            || !incoming.SequenceEqual(_geometries)
            || fill != _fill || line != _line || lineWidth != _lineWidth)
        {
            Release();
            _layer = Build(incoming, fill, line, lineWidth);
            _geometries = incoming;
            _fill = fill;
            _line = line;
            _lineWidth = lineWidth;
            LayersBuilt++;
        }

        layersBuilt = LayersBuilt;
        return _layer;
    }

    static MemoryLayer Build(Geometry[] geometries, Color4 fill, Color4 line, float lineWidth)
        => new MemoryLayer("Geometry")
        {
            Features = geometries.Select(g => (global::Mapsui.IFeature)new GeometryFeature(ToMercator(g))).ToArray(),
            Style = new VectorStyle
            {
                Fill = new Brush(ToMapsui(fill)),
                Outline = new Pen(ToMapsui(line), lineWidth),
                Line = new Pen(ToMapsui(line), lineWidth),
            },
        };

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

    static MapsuiColor ToMapsui(Color4 c) => new MapsuiColor(
        (int)Math.Round(Math.Clamp(c.R, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.G, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.B, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.A, 0f, 1f) * 255f));

    void Release()
    {
        _layer?.Dispose();
        _layer = null;
        _geometries = Array.Empty<Geometry>();
        _lineWidth = -1f;
    }

    /// <summary>Releases the layer.</summary>
    public void Dispose() => Release();
}
