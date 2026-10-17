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

/// <summary>
/// The style a layer uses when nothing is connected.
/// </summary>
/// <remarks>
/// One shared instance, for the same reason <c>VectorStyle</c> caches its own: a layer compares
/// styles by identity, and Mapsui keys its rendered-geometry cache on the style object.
/// </remarks>
static class Styles
{
    public static IStyle Default { get; } = new MapsuiVectorStyle
    {
        Fill = new Brush(Colors.ToMapsui(new Color4(1f, 0.2f, 0.1f, 0.35f))),
        Outline = new Pen(Colors.ToMapsui(new Color4(1f, 0.2f, 0.1f, 0.9f)), 2f),
        Line = new Pen(Colors.ToMapsui(new Color4(1f, 0.2f, 0.1f, 0.9f)), 2f),
    };

    /// <summary>
    /// Two styles as one, because a layer takes a single style — <b>flattened, never nested</b>.
    /// </summary>
    /// <remarks>
    /// **A nested `StyleCollection` draws nothing, and says nothing about it.** Measured
    ///: `{ { VectorStyle, SymbolStyle }, LabelStyle }` over a polygon put down 156
    /// pixels — the label text and not one pixel of the shape — where the flat
    /// `{ VectorStyle, SymbolStyle }` drew 14884. Mapsui's renderer walks a collection's members
    /// and does not recurse into a member that is itself a collection.
    ///
    /// That did not matter while a chain was only ever two nodes long, which is why it went
    /// unnoticed. It matters the moment styles chain three deep, which is what
    /// `VectorStyle → SymbolStyle → LabelStyle` is. So splicing is the rule, and it lives here
    /// rather than in either node, because both build the same shape.
    /// </remarks>
    public static IStyle Combine(IStyle first, IStyle second)
    {
        var flat = new StyleCollection();
        Splice(flat, first);
        Splice(flat, second);
        return flat;
    }

    static void Splice(StyleCollection into, IStyle style)
    {
        if (style is StyleCollection nested)
            foreach (var inner in nested.Styles) Splice(into, inner);
        else
            into.Styles.Add(style);
    }
}
