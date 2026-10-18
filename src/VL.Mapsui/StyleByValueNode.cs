using System;
using System.Globalization;
using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using ThemeStyle = global::Mapsui.Styles.Thematics.ThemeStyle;
using GradientTheme = global::Mapsui.Styles.Thematics.GradientTheme;
using MapsuiFeature = global::Mapsui.IFeature;
using PointFeature = global::Mapsui.Layers.PointFeature;
using IAttributesTable = NetTopologySuite.Features.IAttributesTable;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// The value-driven sibling of StyleByGeometry: that node picks a style by what a feature IS, this
// one by what a feature SAYS. Population, elevation, magnitude - any numeric attribute maps its
// [Min, Max] range onto the range between two styles, and every feature is drawn with the blend its
// value earns. It wraps Mapsui.Styles.Thematics.GradientTheme, the same mechanism every desktop GIS
// calls graduated symbols or a colour ramp.
//
// Five pins, one decision: a range of values onto a range of styles. Each pin is half of one of the
// two ranges, which is why none can be dropped - and it is exactly the parameter list of Mapsui's
// own constructor.
//
// What interpolates (Mapsui's table, measured here): between two VectorStyles the fill colour, line
// colour and line width scale linearly; between two SymbolStyles the scale does. Values outside
// [Min, Max] clamp to the end styles rather than extrapolate. Wire the SAME KIND of style into both
// pins - Mapsui refuses a mix, so this node draws nothing for it and FeatureLayer's Status says so.
//
// A feature whose attribute is missing or not numeric is NOT DRAWN, and Status counts them. That is
// this node's one deliberate departure from raw Mapsui, which reads the attribute with
// Convert.ToDouble mid-render: a missing attribute silently becomes 0 - a wrong answer wearing the
// Min style - and a non-numeric one throws inside the render loop. Both violate the rule this family
// keeps: draw nothing, and say so where the features and the style meet. (Numeric strings still
// count as numeric, read invariant-culture, which is what a GeoJSON file means by them.)
//
// Interpolated styles are quantized to 64 steps, and that is a resource decision, not a visual one.
// Mapsui's GetStyle builds a brand-new style object per feature per frame
// (Activator.CreateInstance), and a style's identity is what the renderer keys its caches on - an
// unbounded stream of fresh styles is rule 12's "who frees them?" with no answer. 64 cached steps
// bound the identity set, let the render cache actually hit, and sit below anything an eye can
// resolve (at most 4 of 255 per colour channel between neighbouring steps).
//
// Not exposed yet: Mapsui's ColorBlend ramps (rainbow and friends) - two styles interpolate two
// colours, which is a working choropleth; multi-stop ramps are the obvious next pin. Known upstream
// wart, measured: GradientTheme switches Enabled (and a SymbolStyle's BitmapId/SymbolOffset) at the
// midpoint THE WRONG WAY ROUND - nearer Max takes the MIN side. Styles built by this package's own
// nodes are always enabled and bitmap-free, so nothing here trips it; hand-built ones might.
//
// Stateful for its identity, like every style node here: a layer treats a new style object as a
// change and rebuilds, so handing out a fresh one every frame would rebuild the map every frame.

/// <summary>
/// Blends between two styles by a numeric attribute of each feature, as in a choropleth.
/// </summary>
/// <remarks>
/// Wire the same kind of style into both pins, for example two VectorStyles.
/// A feature whose attribute is missing or not a number is not drawn, and FeatureLayer's Status counts it.
/// </remarks>
[ProcessNode(Name = "StyleByValue", Category = "Mapsui.Styles")]
public class StyleByValueNode
{
    ValueTheme? _theme;
    string _attribute = string.Empty;
    double _min;
    double _max;
    IStyle? _minStyle;
    IStyle? _maxStyle;

    /// <summary>Themes built by this node. It should reach 1 and stay there.</summary>
    internal int StylesBuilt { get; private set; }

    // Min/Max are the attribute values the two styles belong to, not a filter: values beyond them
    // are drawn with the end styles. An empty range (Max not above Min) draws every readable
    // feature with Min Style, which is what a range of one value means.

    /// <summary>
    /// Configures the style from an attribute name and two styles.
    /// </summary>
    /// <param name="attribute">The name of the numeric attribute to read.</param>
    /// <param name="min">The attribute value drawn with Min Style. Lower values are drawn the same.</param>
    /// <param name="max">The attribute value drawn with Max Style. Higher values are drawn the same.</param>
    /// <param name="minStyle">The style at Min.</param>
    /// <param name="maxStyle">The style at Max.</param>
    /// <returns>A style for a layer, or for one of StyleByGeometry's pins.</returns>
    public IStyle Update(string attribute = "", double min = 0, double max = 1, IStyle? minStyle = null, IStyle? maxStyle = null)
    {
        if (_theme is null
            || attribute != _attribute
            || min != _min
            || max != _max
            || !ReferenceEquals(minStyle, _minStyle)
            || !ReferenceEquals(maxStyle, _maxStyle))
        {
            _theme = new ValueTheme(attribute, min, max, minStyle, maxStyle);
            _attribute = attribute;
            _min = min;
            _max = max;
            _minStyle = minStyle;
            _maxStyle = maxStyle;
            StylesBuilt++;
        }

        return _theme;
    }
}
