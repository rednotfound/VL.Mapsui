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
    readonly LabelStyleNode _label = new();
    readonly StyleByGeometryNode _theme = new();
    readonly FeatureLayerNode _layer = new();

    double _spacing = double.NaN;
    bool _showLabels;
    NtsFeature[] _features = Array.Empty<NtsFeature>();

    /// <summary>Layers built by this node. Should settle at 1 and stay there.</summary>
    internal int LayersBuilt => _layer.LayersBuilt;

    /// <summary>How many meridians and parallels the current spacing produced.</summary>
    internal int LinesBuilt { get; private set; }

    /// <summary>How many crossing labels the current spacing produced. 0 when the guard refused.</summary>
    internal int LabelsBuilt { get; private set; }

    // A fixed-degree graticule always spans the WHOLE world, so a fine spacing multiplies:
    // 0.05 degrees is 7,200 meridians x ~3,400 parallels - and a label per CROSSING is 24.5
    // MILLION point features, which froze vvvv outright on 2026-09-24 (defect nine, NOTES.md).
    // Lines only add; labels multiply. Hence two guards, both honest about what they do.
    const int MaxLines = 100_000;
    const int MaxLabelCrossings = 10_000;

    /// <summary>
    /// A graticule layer, ready to hand to a Map — with or without a tile layer beside it.
    /// </summary>
    /// <remarks>
    /// A spacing of 0 or less gives no layer rather than an infinite one, and so does a spacing
    /// so fine the world grid would exceed 100,000 lines - a graticule is a reference, not a
    /// point cloud. Labels appear only while the grid has at most 10,000 crossings (10 degrees
    /// is 612; at fine spacings the lines draw alone). The default grey is deliberately faint:
    /// a graticule is a reference, not a subject.
    /// </remarks>
    public ILayer? Update(
        out int linesBuilt,
        double degreesSpacing = 10,
        bool showLabels = true,
        Color4? lineColor = null,
        float lineWidth = 1f)
    {
        if (degreesSpacing <= 0 || 360.0 / degreesSpacing + 2 * LatLimit / degreesSpacing > MaxLines)
        {
            linesBuilt = 0;
            LabelsBuilt = 0;
            return null;
        }

        if (degreesSpacing != _spacing || showLabels != _showLabels)
        {
            _features = Build(degreesSpacing, showLabels, out var lines, out var labels);
            _spacing = degreesSpacing;
            _showLabels = showLabels;
            LinesBuilt = lines;
            LabelsBuilt = labels;
        }
        linesBuilt = LinesBuilt;

        var line = lineColor ?? new Color4(0.5f, 0.5f, 0.5f, 0.55f);
        var vector = _style.Update(fillColor: new Color4(0f, 0f, 0f, 0f), lineColor: line, lineWidth: lineWidth);

        // Labels ride on point features at the line crossings, dispatched by geometry type -
        // the same StyleByGeometry the mixed-features lesson produced (a nested StyleCollection
        // renders NOTHING; the dispatch is the fix, reused rather than re-learnt).
        var style = showLabels
            ? _theme.Update(point: _label.Update(attribute: "label", size: 11f), line: vector)
            : vector;

        return _layer.Update(out _, out _, _features, style, name: "Graticule");
    }

    static NtsFeature[] Build(double spacing, bool labels, out int lines, out int labelsBuilt)
    {
        var features = new List<NtsFeature>();
        var lons = new List<double>();
        var lats = new List<double>();

        // Meridians: every multiple of the spacing in [-180, 180). -180 and +180 are the same
        // line on a mercator map, so the upper edge is left out rather than drawn twice.
        for (var lon = -180.0; lon < 180.0 - 1e-9; lon += spacing)
            lons.Add(lon);

        // Parallels: every multiple of the spacing the projection can show, equator included.
        for (var lat = -Math.Floor(LatLimit / spacing) * spacing; lat <= LatLimit + 1e-9; lat += spacing)
            lats.Add(lat);

        foreach (var lon in lons)
            features.Add(Line(new Coordinate(lon, -LatLimit), new Coordinate(lon, LatLimit)));
        foreach (var lat in lats)
            features.Add(Line(new Coordinate(-180, lat), new Coordinate(180, lat)));
        lines = features.Count;

        // One label per crossing, saying exactly where the crossing is - 'lon, lat' in the
        // course's plain-numbers voice. Labels MULTIPLY where lines only add, so past the
        // crossing cap the lines draw alone (defect nine: 24.5 million labels at 0.05 degrees).
        labelsBuilt = 0;
        if (labels && (long)lons.Count * lats.Count <= MaxLabelCrossings)
            foreach (var lon in lons)
                foreach (var lat in lats)
                {
                    var table = new NetTopologySuite.Features.AttributesTable
                    {
                        { "label", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                                 "{0:0.###}, {1:0.###}", lon, lat) },
                    };
                    features.Add(new NtsFeature(
                        new NetTopologySuite.Geometries.Point(new Coordinate(lon, lat)), table));
                }
        labelsBuilt = features.Count - lines;

        return features.ToArray();
    }

    static NtsFeature Line(Coordinate a, Coordinate b) =>
        new(new LineString(new[] { a, b }), new NetTopologySuite.Features.AttributesTable());

    public void Dispose() => _layer.Dispose();
}
