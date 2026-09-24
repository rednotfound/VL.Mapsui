using System;
using System.Collections.Generic;
using System.Globalization;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using BaseLayer = global::Mapsui.Layers.BaseLayer;
using MRect = global::Mapsui.MRect;
using MapsuiFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using IStyle = global::Mapsui.Styles.IStyle;
using SphericalMercator = global::Mapsui.Projections.SphericalMercator;

namespace VL.Mapsui;

/// <summary>
/// The lat/lon grid every desktop GIS calls a graticule — meridians and parallels, as a layer.
/// Uses Mapsui and NetTopologySuite.
/// </summary>
/// <remarks>
/// **A map with no tile layer is a blank window, and blank has no direction.** This is the layer
/// that gives a tile-less map its bearings without any network: the coordinate system itself,
/// drawn. Mapsui ships no graticule of its own.
///
/// **The lines are generated per VIEW, not per world.** A fixed grid for the whole planet is
/// millions of features at a fine spacing, because labels multiply where lines only add. Instead
/// the layer computes just the lines crossing the view it is asked for — a handful of features
/// at any zoom, at any spacing, rebuilt only when the view or the spacing changes. That is how
/// every desktop GIS draws its grid.
///
/// Styling is composed from the same pieces a patch would use — <see cref="VectorStyleNode"/>
/// for the lines, crossing labels through <see cref="StyleByGeometryNode"/> →
/// <see cref="LabelStyleNode"/>. In WebMercator a meridian and a parallel are both straight, so
/// every line is two vertices.
/// </remarks>
[ProcessNode(Name = "Graticule", Category = "Mapsui.Layers")]
public class GraticuleNode : IDisposable
{
    readonly VectorStyleNode _style = new();
    readonly LabelStyleNode _label = new();
    readonly StyleByGeometryNode _theme = new();
    readonly GraticuleLayer _layer = new();
    IStyle? _applied;

    /// <summary>Layers built by this node. One, ever — the layer computes per view instead of rebuilding.</summary>
    internal int LayersBuilt => 1;

    /// <summary>Meridians and parallels in the most recently rendered view.</summary>
    internal int LinesBuilt => _layer.LinesInView;

    /// <summary>Crossing labels in the most recently rendered view. 0 while the view has too many crossings.</summary>
    internal int LabelsBuilt => _layer.LabelsInView;

    /// <summary>The spacing the most recent view actually drew — what auto picked, or the requested value, coarsened if the view could not afford it.</summary>
    internal double EffectiveSpacing => _layer.EffectiveSpacing;

    /// <summary>
    /// A graticule layer, ready to hand to a Map — with or without a tile layer beside it.
    /// </summary>
    /// <remarks>
    /// **Degrees Spacing 0 means automatic**: each view picks a round number (1/2/5 ladder) that
    /// puts a handful of lines on screen, the way a desktop GIS grid follows the zoom. A positive
    /// spacing is honoured for as long as the view can afford it — a view that would hold more
    /// than a hundred of its lines steps up the same ladder instead, because a reference grid
    /// that dense is a fill, not a reference. A negative spacing gives no layer.
    ///
    /// `Lines Built` reports the lines in the most recently drawn view.
    /// </remarks>
    public ILayer? Update(
        out int linesBuilt,
        double degreesSpacing = 0,
        bool showLabels = true,
        Color4? lineColor = null,
        float lineWidth = 1f)
    {
        if (degreesSpacing < 0)
        {
            linesBuilt = 0;
            return null;
        }

        var line = lineColor ?? new Color4(0.5f, 0.5f, 0.5f, 0.55f);
        var vector = _style.Update(fillColor: new Color4(0f, 0f, 0f, 0f), lineColor: line, lineWidth: lineWidth);

        // Labels ride on point features at the line crossings, dispatched by geometry type -
        // the same StyleByGeometry the mixed-features lesson produced (a nested StyleCollection
        // renders NOTHING; the dispatch is the fix, reused rather than re-learnt).
        var style = showLabels
            ? _theme.Update(point: _label.Update(attribute: "label", size: 11f), line: vector)
            : vector;
        if (!ReferenceEquals(style, _applied))
        {
            _layer.Style = style;
            _applied = style;
        }

        _layer.Configure(degreesSpacing, showLabels);
        linesBuilt = _layer.LinesInView;
        return _layer;
    }

    /// <summary>Releases the layer.</summary>
    public void Dispose() => _layer.Dispose();
}

/// <summary>
/// The layer that computes its lines from the view it is asked for, instead of holding a
/// world-sized feature set. This is what makes any spacing safe at any zoom.
/// </summary>
sealed class GraticuleLayer : BaseLayer
{
    // WebMercator's own latitude limit; a parallel outside it cannot be drawn on this map.
    const double LatLimit = 85.0511287798066;

    static readonly MRect World = new(
        -20037508.342789244, -20037508.342789244, 20037508.342789244, 20037508.342789244);

    // The round numbers a grid may use, the 1/2/5 ladder every axis-labelling algorithm walks.
    // 30 is the coarse end: an atlas world view, 12 meridians by 6 parallels.
    static readonly double[] Ladder =
        { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 30 };

    // More lines than this in one view is a fill, not a reference - the spacing steps up the
    // ladder instead. Labels are heavier than lines (text layout per feature), so their own cap
    // is lower: past it the lines draw alone.
    const int MaxLinesInView = 100;
    const int MaxLabelsInView = 400;

    double _requested;
    bool _labels = true;

    MRect? _cachedBox;
    double _cachedRequested = double.NaN;
    bool _cachedLabels;
    MapsuiFeature[] _cache = Array.Empty<MapsuiFeature>();

    public GraticuleLayer() : base("Graticule") { }

    internal int LinesInView { get; private set; }
    internal int LabelsInView { get; private set; }
    internal double EffectiveSpacing { get; private set; }

    internal void Configure(double requestedSpacing, bool labels)
    {
        if (requestedSpacing == _requested && labels == _labels) return;
        _requested = requestedSpacing;
        _labels = labels;
        _cachedBox = null;
        DataHasChanged();
    }

    public override MRect? Extent => World;

    public override IEnumerable<MapsuiFeature> GetFeatures(MRect box, double resolution)
    {
        if (box is null) return Array.Empty<MapsuiFeature>();
        if (_cachedBox is not null && SameBox(_cachedBox, box)
            && _cachedRequested == _requested && _cachedLabels == _labels)
            return _cache;

        // The view, in degrees, clamped to what the projection can show at all.
        var (lonMin, latMin) = SphericalMercator.ToLonLat(
            Math.Max(box.Min.X, World.Min.X), Math.Max(box.Min.Y, World.Min.Y));
        var (lonMax, latMax) = SphericalMercator.ToLonLat(
            Math.Min(box.Max.X, World.Max.X), Math.Min(box.Max.Y, World.Max.Y));
        lonMin = Math.Max(lonMin, -180); lonMax = Math.Min(lonMax, 180);
        latMin = Math.Max(latMin, -LatLimit); latMax = Math.Min(latMax, LatLimit);

        // Auto starts from "about six lines across"; any spacing coarsens up the ladder while
        // the view cannot afford it. Both end on round numbers a reader can say out loud.
        var spacing = _requested > 0 ? _requested : FirstAtLeast((lonMax - lonMin) / 6);
        while (CountLines(lonMin, lonMax, latMin, latMax, spacing) > MaxLinesInView
               && NextUp(spacing) is { } coarser)
            spacing = coarser;

        var features = new List<MapsuiFeature>();
        // -180 is in, +180 is out: on a mercator map they are the same line, drawn once.
        var lons = Multiples(lonMin, lonMax, spacing, lo: -180, hi: 180 - 1e-9);
        var lats = Multiples(latMin, latMax, spacing, lo: -LatLimit, hi: LatLimit);

        // Each line spans one spacing step beyond the view, so panning meets its continuation.
        var latLo = Math.Max(latMin - spacing, -LatLimit);
        var latHi = Math.Min(latMax + spacing, LatLimit);
        var lonLo = Math.Max(lonMin - spacing, -180);
        var lonHi = Math.Min(lonMax + spacing, 180);

        foreach (var lon in lons)
            features.Add(Line(lon, latLo, lon, latHi));
        foreach (var lat in lats)
            features.Add(Line(lonLo, lat, lonHi, lat));
        LinesInView = features.Count;

        // One label per crossing IN VIEW, saying exactly where the crossing is - 'lon, lat' in
        // plain numbers. Labels multiply where lines add (defect nine was 24.5 million of them),
        // so past the cap the lines draw alone.
        LabelsInView = 0;
        if (_labels && (long)lons.Count * lats.Count <= MaxLabelsInView)
        {
            foreach (var lon in lons)
                foreach (var lat in lats)
                {
                    var (x, y) = SphericalMercator.FromLonLat(lon, lat);
                    var label = new GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
                    label["label"] = string.Format(CultureInfo.InvariantCulture,
                        "{0:0.#####}, {1:0.#####}", lon, lat);
                    features.Add(label);
                }
            LabelsInView = lons.Count * lats.Count;
        }

        EffectiveSpacing = spacing;
        _cache = features.ToArray();
        _cachedBox = box;
        _cachedRequested = _requested;
        _cachedLabels = _labels;
        return _cache;
    }

    static bool SameBox(MRect a, MRect b) =>
        a.Min.X == b.Min.X && a.Min.Y == b.Min.Y && a.Max.X == b.Max.X && a.Max.Y == b.Max.Y;

    /// <summary>The smallest round spacing at least this wide; the ladder's ends for everything beyond it.</summary>
    static double FirstAtLeast(double raw)
    {
        foreach (var step in Ladder)
            if (step >= raw) return step;
        return Ladder[^1];
    }

    static double? NextUp(double spacing)
    {
        foreach (var step in Ladder)
            if (step > spacing) return step;
        return null;  // 30 degrees is 18 lines on a world view - always affordable
    }

    static int CountLines(double lonMin, double lonMax, double latMin, double latMax, double spacing) =>
        CountMultiples(lonMin, lonMax, spacing) + CountMultiples(latMin, latMax, spacing);

    static int CountMultiples(double from, double to, double spacing) =>
        Math.Max(0, (int)(Math.Floor(to / spacing) - Math.Ceiling(from / spacing)) + 1);

    static List<double> Multiples(double from, double to, double spacing, double lo, double hi)
    {
        var values = new List<double>();
        for (var k = (long)Math.Ceiling(from / spacing); k * spacing <= to; k++)
        {
            var v = k * spacing;
            if (v >= lo && v <= hi) values.Add(v);
        }
        return values;
    }

    static GeometryFeature Line(double lonA, double latA, double lonB, double latB)
    {
        var (xa, ya) = SphericalMercator.FromLonLat(lonA, latA);
        var (xb, yb) = SphericalMercator.FromLonLat(lonB, latB);
        return new GeometryFeature(new LineString(new[] { new Coordinate(xa, ya), new Coordinate(xb, yb) }));
    }
}
