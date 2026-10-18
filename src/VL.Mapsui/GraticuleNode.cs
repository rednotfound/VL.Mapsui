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

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A map with no tile layer is a blank window, and blank has no direction. This is the layer that
// gives a tile-less map its bearings without any network: the coordinate system itself, drawn.
// Mapsui ships no graticule of its own.
//
// The lines are generated per VIEW, not per world. A fixed grid for the whole planet is millions of
// features at a fine spacing, because labels multiply where lines only add. Instead the layer
// computes just the lines crossing the view it is asked for - a handful of features at any zoom, at
// any spacing, rebuilt only when the view or the spacing changes. That is how every desktop GIS
// draws its grid.
//
// Styling is composed from the same pieces a patch would use - VectorStyleNode for the lines,
// crossing labels through StyleByGeometryNode -> LabelStyleNode. In WebMercator a meridian and a
// parallel are both straight, so every line is two vertices.

/// <summary>
/// Draws a latitude/longitude grid (a graticule) as a map layer.
/// </summary>
/// <remarks>
/// Works with no tile layer and no network, so a map without tiles still has its bearings.
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

    // Degrees Spacing 0 means automatic: each view picks a round number (1/2/5 ladder) that puts a
    // handful of lines on screen, the way a desktop GIS grid follows the zoom. A positive spacing is
    // honoured for as long as the view can afford it - a view that would hold more than a hundred of
    // its lines steps up the same ladder instead, because a reference grid that dense is a fill, not
    // a reference. A negative spacing gives no layer.

    /// <summary>
    /// Configures the graticule layer, ready to hand to a Map.
    /// </summary>
    /// <param name="linesBuilt">The lines in the most recently drawn view.</param>
    /// <param name="degreesSpacing">Degrees between lines. 0 picks a round spacing for the zoom; negative gives no layer.</param>
    /// <param name="showLabels">Labels the line crossings with their coordinates.</param>
    /// <param name="lineColor">Unconnected gives a translucent grey.</param>
    /// <param name="lineWidth">Line width in pixels.</param>
    /// <returns>The layer, for a Map's Layers.</returns>
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
