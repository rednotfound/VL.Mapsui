using System;
using Stride.Core.Mathematics;
using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiVectorStyle = global::Mapsui.Styles.VectorStyle;
using StyleCollection = global::Mapsui.Styles.StyleCollection;
using Brush = global::Mapsui.Styles.Brush;
using Pen = global::Mapsui.Styles.Pen;
using MapsuiColor = global::Mapsui.Styles.Color;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A style is a value, and this node is still stateful - for a different reason than the layers are.
// It hands out the same object while its inputs are unchanged, because a style's IDENTITY matters
// downstream twice over:
// - a layer treats a new style as a change and rebuilds, so a fresh instance per frame would rebuild
//   the feature layer sixty times a second - the failure this package was rebuilt to undo, wearing a
//   colour picker;
// - Mapsui caches rendered geometry per style object: IFeature.RenderedGeometry is an
//   IDictionary<IStyle, object>. A new style per frame means a new key every frame, on every feature.
// So the rule "anything holding a resource is a process node" gains a second clause here: or
// anything whose identity is compared downstream. Nothing is held open; the object simply has to be
// the same object.
//
// The defaults are deliberately visible without configuration - a translucent red fill with a solid
// outline shows up on any basemap, which is what a first patch needs.

/// <summary>
/// Styles lines and polygons: fill colour, line colour and line width.
/// </summary>
[ProcessNode(Name = "VectorStyle", Category = "Mapsui.Styles")]
public class VectorStyleNode
{
    MapsuiVectorStyle? _style;
    Color4 _fill;
    Color4 _line;
    float _lineWidth = -1f;

    /// <summary>Styles built by this node. It should reach 1 and stay there.</summary>
    internal int StylesBuilt { get; private set; }

    /// <summary>
    /// Configures the style.
    /// </summary>
    /// <param name="fillColor">The polygon fill. Unconnected gives a translucent red.</param>
    /// <param name="lineColor">Lines and polygon outlines. Unconnected gives red.</param>
    /// <param name="lineWidth">Line width in pixels.</param>
    /// <returns>A style for a layer, or for StyleByGeometry's Line and Polygon pins.</returns>
    public IStyle Update(
        Color4? fillColor = null,
        Color4? lineColor = null,
        float lineWidth = 2f)
    {
        // A Color4 is not a compile-time constant either, so the default cannot sit in the
        // signature - the same rule as the cache folder, one type along.
        var fill = fillColor ?? new Color4(1f, 0.2f, 0.1f, 0.35f);
        var line = lineColor ?? new Color4(1f, 0.2f, 0.1f, 0.9f);

        if (_style is null || fill != _fill || line != _line || lineWidth != _lineWidth)
        {
            _style = new MapsuiVectorStyle
            {
                Fill = new Brush(Colors.ToMapsui(fill)),
                Outline = new Pen(Colors.ToMapsui(line), lineWidth),
                Line = new Pen(Colors.ToMapsui(line), lineWidth),
            };
            _fill = fill;
            _line = line;
            _lineWidth = lineWidth;
            StylesBuilt++;
        }

        return _style;
    }
}
