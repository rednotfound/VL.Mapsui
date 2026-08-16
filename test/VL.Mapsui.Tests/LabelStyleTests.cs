using System.Collections.Immutable;
using System.Linq;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Mapsui;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiLabelStyle = global::Mapsui.Styles.LabelStyle;
using StyleCollection = global::Mapsui.Styles.StyleCollection;

namespace VL.Mapsui.Tests;

/// <summary>
/// Labels, which is where a feature's attributes finally become visible.
/// </summary>
public class LabelStyleTests
{
    static readonly GeometryFactory Factory = new();

    [Fact]
    public void The_attribute_name_reaches_LabelColumn()
    {
        // LabelColumn is a NAME that Mapsui looks up on every feature, not the text itself. That is
        // the whole reason Feature carries attributes: one style, a thousand different words.
        var style = new LabelStyleNode().Update(attribute: "name");

        Assert.Equal("name", Assert.IsType<MapsuiLabelStyle>(style).LabelColumn);
    }

    [Fact]
    public void An_empty_attribute_passes_the_upstream_style_through_untouched()
    {
        // Nothing named, nothing to write. Swallowing the fill instead would make a patch look
        // broken for the length of time it takes to type an attribute name.
        var vector = new VectorStyleNode().Update();

        var style = new LabelStyleNode().Update(vector, attribute: "");

        Assert.Same(vector, style);
    }

    [Fact]
    public void A_style_upstream_arrives_at_the_layer_as_one_style()
    {
        // A layer takes a single style and a shape usually wants both a fill and a label, so the
        // pair is a chain in the patch rather than something FeatureLayer has to merge.
        var vector = new VectorStyleNode().Update();

        var style = new LabelStyleNode().Update(vector, attribute: "name");

        var collection = Assert.IsType<StyleCollection>(style);
        Assert.Contains(vector, collection.Styles);
        Assert.Contains(collection.Styles, s => s is MapsuiLabelStyle);
    }

    [Fact]
    public void Labels_alone_are_a_legitimate_map()
    {
        var style = new LabelStyleNode().Update(style: null, attribute: "name");

        Assert.IsType<MapsuiLabelStyle>(style);
    }

    [Fact]
    public void A_hundred_frames_build_one_style()
    {
        // The same identity rule as VectorStyle: a new style object every frame would rebuild every
        // layer holding it, and Mapsui keys its rendered-geometry cache on the object.
        var node = new LabelStyleNode();

        for (int frame = 0; frame < 100; frame++) node.Update(attribute: "name", size: 14f);

        Assert.Equal(1, node.StylesBuilt);
    }

    [Fact]
    public void Changing_a_pin_gives_a_new_style()
    {
        var node = new LabelStyleNode();

        var small = node.Update(attribute: "name", size: 10f);
        var large = node.Update(attribute: "name", size: 20f);

        Assert.NotSame(small, large);
        Assert.Equal(2, node.StylesBuilt);
    }

    [Fact]
    public void A_layer_wearing_a_label_style_still_builds_once()
    {
        // The end to end shape: attributes on a feature, a label style naming one of them, and a
        // layer that does not churn while the frames go by.
        var label = new LabelStyleNode();
        var vector = new VectorStyleNode();
        using var layer = new FeatureLayerNode();

        var feature = FeatureNodes.Feature(
            Factory.CreatePoint(new Coordinate(139.7671, 35.6812)),
            ImmutableDictionary.CreateRange(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, object>("name", "Tokyo"),
            }));

        for (int frame = 0; frame < 100; frame++)
            layer.Update(out _, out _, new[] { feature }, label.Update(vector.Update(), "name"));

        Assert.Equal(1, layer.LayersBuilt);
        Assert.Equal(1, label.StylesBuilt);
    }
}
