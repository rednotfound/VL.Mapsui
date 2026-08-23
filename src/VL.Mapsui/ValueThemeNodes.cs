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

/// <summary>
/// One style per feature, interpolated across a numeric attribute. This is what a choropleth is.
/// </summary>
/// <remarks>
/// **The value-driven sibling of `StyleByGeometry`**: that node picks a style by what a feature IS,
/// this one by what a feature SAYS. Population, elevation, magnitude — any numeric attribute maps
/// its `[Min, Max]` range onto the range between two styles, and every feature is drawn with the
/// blend its value earns. It wraps `Mapsui.Styles.Thematics.GradientTheme`, the same mechanism
/// every desktop GIS calls graduated symbols or a colour ramp.
///
/// **Five pins, one decision**: a range of values onto a range of styles. Each pin is half of one
/// of the two ranges, which is why none can be dropped — and it is exactly the parameter list of
/// Mapsui's own constructor.
///
/// **What interpolates** (Mapsui's table, measured here): between two `VectorStyle`s the fill
/// colour, line colour and line width scale linearly; between two `SymbolStyle`s the scale does.
/// Values outside `[Min, Max]` clamp to the end styles rather than extrapolate. Wire the SAME KIND
/// of style into both pins — Mapsui refuses a mix, so this node draws nothing for it and
/// `FeatureLayer`'s `Status` says so.
///
/// **A feature whose attribute is missing or not numeric is NOT DRAWN, and `Status` counts them.**
/// That is this node's one deliberate departure from raw Mapsui, which reads the attribute with
/// `Convert.ToDouble` mid-render: a missing attribute silently becomes 0 — a wrong answer wearing
/// the Min style — and a non-numeric one throws inside the render loop. Both violate the rule this
/// family keeps: draw nothing, and say so where the features and the style meet. (Numeric strings
/// still count as numeric, read invariant-culture, which is what a GeoJSON file means by them.)
///
/// **Interpolated styles are quantized to 64 steps, and that is a resource decision, not a visual
/// one.** Mapsui's `GetStyle` builds a brand-new style object per feature per frame
/// (`Activator.CreateInstance`), and a style's identity is what the renderer keys its caches on —
/// an unbounded stream of fresh styles is rule 12's "who frees them?" with no answer. 64 cached
/// steps bound the identity set, let the render cache actually hit, and sit below anything an eye
/// can resolve (at most 4 of 255 per colour channel between neighbouring steps).
///
/// Not exposed yet: Mapsui's `ColorBlend` ramps (rainbow and friends) — two styles interpolate two
/// colours, which is a working choropleth; multi-stop ramps are the obvious next pin. Known
/// upstream wart, measured 2026-08-23: `GradientTheme` switches `Enabled` (and a `SymbolStyle`'s
/// `BitmapId`/`SymbolOffset`) at the midpoint THE WRONG WAY ROUND — nearer Max takes the MIN side.
/// Styles built by this package's own nodes are always enabled and bitmap-free, so nothing here
/// trips it; hand-built ones might.
///
/// Stateful for its identity, like every style node here: a layer treats a new style object as a
/// change and rebuilds, so handing out a fresh one every frame would rebuild the map every frame.
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

    /// <summary>A style to hand to a layer, or into <c>StyleByGeometry</c>'s pins.</summary>
    /// <remarks>
    /// `Min`/`Max` are the attribute values the two styles belong to, not a filter: values beyond
    /// them are drawn with the end styles. An empty range (`Max` not above `Min`) draws every
    /// readable feature with `Min Style`, which is what a range of one value means.
    /// </remarks>
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

/// <summary>
/// A theme interpolating between two styles across a numeric attribute, that also says what it holds.
/// </summary>
/// <remarks>
/// The same shape as <see cref="GeometryTheme"/> and for the same reason: a theme is a function and
/// a function is opaque, but `FeatureLayer` has to warn about features this style cannot draw, and
/// it cannot call the function to find out — so the inputs are carried alongside as properties.
///
/// The dispatch guards run in the factory, once, rather than per feature: an empty attribute name,
/// an unwired pin or mismatched style kinds each produce a theme that draws nothing at all, and the
/// per-feature closure only ever handles the case that varies per feature — whether ITS attribute
/// reads as a number.
/// </remarks>
public sealed class ValueTheme : ThemeStyle
{
    /// <summary>How many distinct interpolated styles exist between the two ends. See the node's remarks.</summary>
    internal const int Steps = 64;

    internal ValueTheme(string attribute, double min, double max, IStyle? minStyle, IStyle? maxStyle)
        : base(MakeDispatch(attribute, min, max, minStyle, maxStyle))
    {
        Attribute = attribute;
        Min = min;
        Max = max;
        MinStyle = minStyle;
        MaxStyle = maxStyle;
    }

    /// <summary>The attribute whose value picks the style.</summary>
    public string Attribute { get; }

    /// <summary>The value drawn with <see cref="MinStyle"/>; smaller values clamp to it.</summary>
    public double Min { get; }

    /// <summary>The value drawn with <see cref="MaxStyle"/>; larger values clamp to it.</summary>
    public double Max { get; }

    /// <summary>What the low end of the range is drawn with.</summary>
    public IStyle? MinStyle { get; }

    /// <summary>What the high end of the range is drawn with.</summary>
    public IStyle? MaxStyle { get; }

    static Func<MapsuiFeature, IStyle> MakeDispatch(string attribute, double min, double max, IStyle? minStyle, IStyle? maxStyle)
    {
        // Theme-level refusals, decided once. Draw nothing; FeatureLayer's Status names the reason.
        if (string.IsNullOrWhiteSpace(attribute)
            || minStyle is null
            || maxStyle is null
            || minStyle.GetType() != maxStyle.GetType())
            return _ => null!;

        // A range of one value has one style. Mapsui's own Fraction() would divide zero by zero
        // here and the NaN comes out as a transparent colour - invisible features with every pin
        // wired, which is the silent failure this package exists to refuse.
        if (!(max > min))
            return feature => (TryRead(feature, attribute, out _) ? minStyle : null)!;

        var inner = new GradientTheme(attribute, min, max, minStyle, maxStyle);
        var cache = new IStyle?[Steps];

        return feature =>
        {
            if (!TryRead(feature, attribute, out var value)) return null!;

            var fraction = (value - min) / (max - min);
            if (fraction < 0) fraction = 0;
            else if (fraction > 1) fraction = 1;
            var step = (int)Math.Round(fraction * (Steps - 1));

            // The probe is a fresh throwaway feature carrying an already-parsed double, so the
            // inner theme's Convert.ToDouble can never throw mid-render whatever the real
            // feature's attribute held. Built at most once per step; a benign race rebuilds one.
            if (cache[step] is { } cached) return cached;
            var probe = new PointFeature(0, 0);
            probe[attribute] = min + step * (max - min) / (Steps - 1);
            return (cache[step] = inner.GetStyle(probe))!;
        };
    }

    /// <summary>The feature-side read: present, convertible, finite.</summary>
    static bool TryRead(MapsuiFeature feature, string attribute, out double value)
    {
        object? raw;
        try { raw = feature[attribute]; }
        catch { value = 0; return false; }   // an indexer that throws for a missing key means absent
        return TryRead(raw, out value);
    }

    /// <summary>
    /// Whether a raw attribute value counts as numeric here: numbers and numeric strings
    /// (invariant culture) do, everything else is "not drawn" rather than 0 or an exception.
    /// </summary>
    internal static bool TryRead(object? raw, out double value)
    {
        value = 0;
        if (raw is null) return false;
        try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
        catch { return false; }
        return double.IsFinite(value);
    }

    /// <summary>Whether this theme can draw a feature with the given attributes — what <c>Status</c> asks.</summary>
    internal bool CanStyle(IAttributesTable? attributes)
        => attributes is not null
        && attributes.Exists(Attribute)
        && TryRead(attributes[Attribute], out _);

    /// <summary>A theme-level reason nothing at all will be drawn, or null when the theme is usable.</summary>
    internal string? Refusal()
    {
        if (string.IsNullOrWhiteSpace(Attribute)) return "its Attribute pin is empty";
        if (MinStyle is null && MaxStyle is null) return "its Min Style and Max Style pins are unwired";
        if (MinStyle is null) return "its Min Style pin is unwired";
        if (MaxStyle is null) return "its Max Style pin is unwired";
        if (MinStyle.GetType() != MaxStyle.GetType())
            return $"Min Style is a {MinStyle.GetType().Name} and Max Style is a {MaxStyle.GetType().Name}, and they must be the same kind";
        return null;
    }
}
