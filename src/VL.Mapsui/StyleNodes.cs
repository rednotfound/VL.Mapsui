using System;
using Stride.Core.Mathematics;
using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiVectorStyle = global::Mapsui.Styles.VectorStyle;
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
}

/// <summary>
/// Colour conversion, in one place because two nodes need it.
/// </summary>
static class Colors
{
    public static MapsuiColor ToMapsui(Color4 c) => new(
        (int)Math.Round(Math.Clamp(c.R, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.G, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.B, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.A, 0f, 1f) * 255f));
}

/// <summary>
/// How geometry is drawn: fill, outline, line width. Hand it to a <c>FeatureLayer</c>.
/// </summary>
/// <remarks>
/// **A style is a value, and this node is still stateful — for a different reason than the layers
/// are.** It hands out the same object while its inputs are unchanged, because a style's *identity*
/// matters downstream twice over:
///
/// - a layer treats a new style as a change and rebuilds, so a fresh instance per frame would
///   rebuild the feature layer sixty times a second — the failure this package was rebuilt to undo,
///   wearing a colour picker
/// - Mapsui caches rendered geometry **per style object**: `IFeature.RenderedGeometry` is an
///   `IDictionary&lt;IStyle, object&gt;`. A new style每 frame means a new key every frame, on every
///   feature
///
/// So the rule "anything holding a resource is a process node" gains a second clause here: **or
/// anything whose identity is compared downstream**. Nothing is held open; the object simply has to
/// be the same object.
///
/// The defaults are deliberately visible without configuration — a translucent red fill with a
/// solid outline shows up on any basemap, which is what a first patch needs.
/// </remarks>
[ProcessNode(Name = "VectorStyle", Category = "Mapsui.Styles")]
public class VectorStyleNode
{
    MapsuiVectorStyle? _style;
    Color4 _fill;
    Color4 _line;
    float _lineWidth = -1f;

    /// <summary>Styles built by this node. It should reach 1 and stay there.</summary>
    internal int StylesBuilt { get; private set; }

    /// <summary>A style to hand to a layer.</summary>
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
