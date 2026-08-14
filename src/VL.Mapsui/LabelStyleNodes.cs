using System;
using Stride.Core.Mathematics;
using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiLabelStyle = global::Mapsui.Styles.LabelStyle;
using StyleCollection = global::Mapsui.Styles.StyleCollection;
using Font = global::Mapsui.Styles.Font;
using Pen = global::Mapsui.Styles.Pen;

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
                CollisionDetection = true,
            };

            _built = style is null ? label : Combined(style, label);
            _upstream = style;
            _attribute = name;
            _color = ink;
            _size = size;
            StylesBuilt++;
        }

        return _built;
    }

    /// <summary>Both styles as one, because a layer takes a single style.</summary>
    static IStyle Combined(IStyle first, IStyle second)
    {
        var collection = new StyleCollection();
        collection.Styles.Add(first);
        collection.Styles.Add(second);
        return collection;
    }
}
