using System;
using Stride.Core.Mathematics;
using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiLabelStyle = global::Mapsui.Styles.LabelStyle;
using MapsuiSymbolStyle = global::Mapsui.Styles.SymbolStyle;
using StyleCollection = global::Mapsui.Styles.StyleCollection;
using Font = global::Mapsui.Styles.Font;
using Pen = global::Mapsui.Styles.Pen;
using Offset = global::Mapsui.Styles.Offset;

namespace VL.Mapsui;

/// <summary>
/// Writes an attribute of each feature on the map. This is the difference between shapes on a map
/// and data you can read.
/// </summary>
/// <remarks>
/// **Attribute names an attribute, it is not the text.** Mapsui's `LabelColumn` looks the name up
/// on every feature, so one style labels a thousand features with a thousand different words — which
/// is the point, and the reason `Feature` carries attributes at all. Leave it empty and there is
/// nothing to write, so the style upstream passes through unchanged.
///
/// **Style is an input so the two arrive at a layer as one.** A layer takes a single style, and a
/// shape usually wants both a fill and a label; wiring `VectorStyle` into this makes the pair a
/// chain in the patch rather than two things a `FeatureLayer` would have to know how to merge.
/// Nothing upstream is required — labels alone are a legitimate map.
///
/// **That input is also how the label knows to get out of the marker's way.** Mapsui centres a
/// label on the feature it names — `Offset (0,0)`, both alignments `Center` — which is right for a
/// polygon and wrong for a point with a symbol on it, where the text and the marker then fight for
/// the same pixels. Every cartographic convention puts a point label *beside* its symbol: the
/// marker says where, the label says what, and they must not share ink.
///
/// So when a `SymbolStyle` is found upstream, this node reads its scale, works out how big the
/// marker is, and lifts the label clear of it. **No pin for it, because nothing needs to be asked**
/// — the node is already holding the symbol whose size is the answer. When there is no symbol
/// upstream the label is left centred, which is where a polygon's label belongs.
///
/// Stateful for its identity, exactly like `VectorStyle`: a layer treats a new style object as a
/// change, and Mapsui keys its rendered-geometry cache on the style object itself.
/// </remarks>
[ProcessNode(Name = "LabelStyle", Category = "Mapsui.Styles")]
public class LabelStyleNode
{
    IStyle? _built;
    IStyle? _upstream;
    string _attribute = string.Empty;
    Color4 _color;
    float _size = -1f;

    /// <summary>Styles built by this node. It should reach 1 and stay there.</summary>
    internal int StylesBuilt { get; private set; }

    /// <summary>A style to hand to a layer, labels included.</summary>
    /// <remarks>
    /// The halo is not a pin and is on by default: white behind dark text is what makes a label
    /// readable over aerial imagery or a busy basemap, and a label nobody can read is not a
    /// feature. Colour and size are pins because they are the two anyone actually changes.
    /// </remarks>
    public IStyle? Update(
        IStyle? style = null,
        string attribute = "",
        Color4? color = null,
        float size = 12f)
    {
        var name = (attribute ?? string.Empty).Trim();
        var ink = color ?? new Color4(0f, 0f, 0f, 1f);

        // Nothing named, nothing to write. Passing the upstream style through is the honest answer:
        // the patch keeps whatever it had rather than silently losing its fill.
        if (name.Length == 0)
        {
            _built = null;
            _upstream = null;
            _attribute = string.Empty;
            _size = -1f;
            return style;
        }

        if (_built is null
            || !ReferenceEquals(style, _upstream)
            || !string.Equals(name, _attribute, StringComparison.Ordinal)
            || ink != _color
            || size != _size)
        {
            var label = new MapsuiLabelStyle
            {
                LabelColumn = name,
                ForeColor = Colors.ToMapsui(ink),
                Font = new Font { Size = size },
                Halo = new Pen(Colors.ToMapsui(new Color4(1f, 1f, 1f, 0.9f)), 2),

                // Mapsui's LabelStyle ships with BackColor = OPAQUE WHITE, so every label paints a
                // solid box behind its text, centred on the feature. A halo and a box are two ways
                // of doing the same job and we already chose the halo; leaving both on meant every
                // label covered the thing it was labelling. Two hundred markers disappeared behind
                // two hundred white rectangles - measured 2026-08-16, adding a label REDUCED the
                // drawn pixels by 71%, which is the shape of a bug: a label can only add ink.
                BackColor = null,

                // CollisionDetection is NOT set, and its absence is deliberate. Mapsui has the
                // property and our renderer ignores it: eight labels three pixels apart draw 763
                // pixels with it on and 763 with it off - measured 2026-08-16, the same verdict
                // SymbolStyle.UnitType got. Setting it would read as "we declutter" and we do not.
                // Dense labels are handled by not drawing them, which is a zoom-range decision.
            };

            Place(label, style);

            _built = style is null ? label : Styles.Combine(style, label);
            _upstream = style;
            _attribute = name;
            _color = ink;
            _size = size;
            StylesBuilt++;
        }

        return _built;
    }

    /// <summary>How far the label clears the top of a marker, in pixels, once the marker is cleared.</summary>
    /// <remarks>
    /// Small on purpose. Further away reads as belonging to nothing in particular, and the halo
    /// already separates the text from whatever is behind it.
    /// </remarks>
    const double Gap = 4;

    /// <summary>
    /// Lifts the label clear of the marker upstream, if there is one.
    /// </summary>
    /// <remarks>
    /// **`VerticalAlignment.Bottom` pins the text's bottom edge to the offset point**, which is why
    /// the arithmetic can ignore the font size entirely — measured 2026-08-16: at `Bottom` a
    /// 10-point label draws to y 199 and a 30-point one also draws to y 199, growing upward instead.
    /// At `Center` both edges move and the clearance would have to include half the text.
    ///
    /// Negative Y is up, also measured rather than assumed: `Offset (0,-20)` moved the ink from
    /// y 194..205 to y 174..185.
    ///
    /// The marker's radius is `SymbolStyle.DefaultHeight / 2 × SymbolScale`, plus half the outline,
    /// since a pen straddles the edge it draws. Checked against the render: a scale-1 marker
    /// occupies y 183..216 about a centre of 199.5, so 16.5 — which is 16 plus half of our 2-pixel
    /// outline.
    /// </remarks>
    static void Place(MapsuiLabelStyle label, IStyle? upstream)
    {
        var symbol = FindSymbol(upstream);
        if (symbol is null) return;   // a polygon's label belongs at its centre. Leave it there.

        var radius = MapsuiSymbolStyle.DefaultHeight / 2 * symbol.SymbolScale
                   + (symbol.Outline?.Width ?? 0) / 2;

        label.VerticalAlignment = MapsuiLabelStyle.VerticalAlignmentEnum.Bottom;
        label.Offset = new Offset(0, -(radius + Gap), false);
    }

    /// <summary>
    /// The first <c>SymbolStyle</c> anywhere in the style handed to us, or null.
    /// </summary>
    /// <remarks>
    /// Recursive because a style arrives wrapped: this node combines into a `StyleCollection`, and
    /// `StyleByGeometry` puts the marker behind a `ThemeStyle`.
    ///
    /// **A `GeometryTheme` is opened by reading its `Point` property, not by calling it.** A
    /// `ThemeStyle` is a function of a feature and there is no feature here — placement is decided
    /// once, when the style is built, not per feature. That is why `StyleByGeometry` carries its
    /// three styles as properties as well as using them.
    /// </remarks>
    static MapsuiSymbolStyle? FindSymbol(IStyle? style)
    {
        switch (style)
        {
            case MapsuiSymbolStyle symbol:
                return symbol;
            case GeometryTheme theme:
                return FindSymbol(theme.Point);
            case StyleCollection collection:
                foreach (var inner in collection.Styles)
                {
                    var found = FindSymbol(inner);
                    if (found is not null) return found;
                }
                return null;
            default:
                return null;
        }
    }

}
