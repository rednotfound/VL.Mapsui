using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.Mapsui;

/// <summary>
/// The lat/lon grid every desktop GIS calls a graticule — meridians and parallels at a fixed
/// spacing, as a layer. Uses Mapsui and NetTopologySuite.
/// </summary>
/// <remarks>
/// **A map with no tile layer is a blank window, and blank has no direction.** This is the layer
/// that gives a tile-less map its bearings without any network: the coordinate system itself,
/// drawn. Requested by vl-overworld's Tutorial 01 (2026-09-23), whose right window IS a bare
/// coordinate system; Mapsui ships no graticule of its own.
///
/// Composed, not implemented a fourth time — the same pattern as <see cref="GeometryLayerNode"/>:
/// this node only *generates* WGS84 line geometry (meridians ±180°, parallels within the
/// WebMercator latitude limit of ±85.05°) and hands it to <see cref="FeatureLayerNode"/> with a
/// <see cref="VectorStyleNode"/>. Projection, change detection and layer lifetime are theirs.
/// In WebMercator a meridian and a parallel are both straight, so every line is two vertices.
///
/// **Watch Layers Built** exactly as with every layer node here: it should settle at 1. The line
/// set is rebuilt only when the spacing changes.
/// </remarks>
[ProcessNode(Name = "Graticule", Category = "Mapsui.Layers")]
public class GraticuleNode : IDisposable
{
    // WebMercator's own latitude limit; a parallel outside it cannot be drawn on this map.
    const double LatLimit = 85.0511287798066;

    readonly VectorStyleNode _style = new();
    readonly FeatureLayerNode _layer = new();

    double _spacing = double.NaN;
    NtsFeature[] _features = Array.Empty<NtsFeature>();

    /// <summary>Layers built by this node. Should settle at 1 and stay there.</summary>
    internal int LayersBuilt => _layer.LayersBuilt;

    /// <summary>How many meridians and parallels the current spacing produced.</summary>
    internal int LinesBuilt { get; private set; }

    /// <summary>
    /// A graticule layer, ready to hand to a Map — with or without a tile layer beside it.
    /// </summary>
    /// <remarks>
    /// A spacing of 0 or less gives no layer rather than an infinite one. The default grey is
    /// deliberately faint: a graticule is a reference, not a subject.
    /// </remarks>
    public ILayer? Update(
        out int linesBuilt,
        double degreesSpacing = 10,
        Color4? lineColor = null,
        float lineWidth = 1f)
    {
        if (degreesSpacing <= 0)
        {
            linesBuilt = 0;
            return null;
        }

        if (degreesSpacing != _spacing)
        {
            _features = Build(degreesSpacing);
            _spacing = degreesSpacing;
            LinesBuilt = _features.Length;
        }
        linesBuilt = LinesBuilt;

        var line = lineColor ?? new Color4(0.5f, 0.5f, 0.5f, 0.55f);
        return _layer.Update(out _, out _, _features,
            _style.Update(fillColor: new Color4(0f, 0f, 0f, 0f), lineColor: line, lineWidth: lineWidth),
            name: "Graticule");
    }

    static NtsFeature[] Build(double spacing)
    {
        var features = new List<NtsFeature>();

        // Meridians: every multiple of the spacing in [-180, 180). -180 and +180 are the same
        // line on a mercator map, so the upper edge is left out rather than drawn twice.
        for (var lon = -180.0; lon < 180.0 - 1e-9; lon += spacing)
            features.Add(Line(new Coordinate(lon, -LatLimit), new Coordinate(lon, LatLimit)));

        // Parallels: every multiple of the spacing the projection can show, equator included.
        for (var lat = -Math.Floor(LatLimit / spacing) * spacing; lat <= LatLimit + 1e-9; lat += spacing)
            features.Add(Line(new Coordinate(-180, lat), new Coordinate(180, lat)));

        return features.ToArray();
    }

    static NtsFeature Line(Coordinate a, Coordinate b) =>
        new(new LineString(new[] { a, b }), new NetTopologySuite.Features.AttributesTable());

    public void Dispose() => _layer.Dispose();
}
