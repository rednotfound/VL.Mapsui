using System;
using System.Collections.Immutable;
using System.Linq;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Mapsui;

using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.Mapsui.Tests;

/// <summary>
/// Geometry, style and layer as three things instead of one, and the seams between them.
/// </summary>
/// <remarks>
/// The split exists so a patch can compose them; these assert the two properties that make the
/// composition safe rather than merely tidy — **a style must keep its identity**, or the layer it
/// feeds rebuilds every frame, and **the number of features must not change how often anything is
/// built**.
///
/// No network: a MemoryLayer holds features and nothing else.
/// </remarks>
public class FeatureLayerTests
{
    static readonly GeometryFactory Factory = new();

    static NtsFeature At(double lon, double lat, params (string key, object value)[] attributes)
        => FeatureNodes.Feature(
            Factory.CreatePoint(new Coordinate(lon, lat)),
            attributes.Length == 0
                ? null
                : ImmutableDictionary.CreateRange(attributes.Select(a => new System.Collections.Generic.KeyValuePair<string, object>(a.key, a.value))));

    // ── The feature, which is NTS's and not ours ──────────────────────────────

    [Fact]
    public void A_feature_is_the_neutral_NTS_type_carrying_its_attributes()
    {
        // Deliberately not a VLFeature and not a Mapsui feature: something that never heard of
        // Mapsui can make one, and something that has can draw it.
        var feature = At(139.7671, 35.6812, ("name", "Tokyo"), ("population", 13960000));

        Assert.IsType<NtsFeature>(feature);
        Assert.Equal("Tokyo", feature.Attributes["name"]);
        Assert.Equal(13960000, feature.Attributes["population"]);
    }

    [Fact]
    public void A_feature_without_attributes_is_still_a_feature()
    {
        // Attributes are what a click can report; a shape needs none of them to be drawn.
        var feature = At(0, 0);

        Assert.NotNull(feature.Attributes);
        Assert.Empty(feature.Attributes.GetNames());
    }

    [Fact]
    public void Attributes_survive_the_crossing_into_Mapsui()
    {
        // Mapsui keeps attributes behind an indexer and lists them on Fields; NTS keeps them in an
        // AttributesTable. The adapter copies them one name at a time, and this is the assertion
        // that it actually does - the whole point of the neutral type is that nothing is lost here.
        var converted = FeatureLayerNode.ToMapsui(At(139.7671, 35.6812, ("name", "Tokyo")));

        Assert.Contains("name", converted.Fields);
        Assert.Equal("Tokyo", converted["name"]);
    }

    [Fact]
    public void The_crossing_projects_into_mercator()
    {
        var converted = FeatureLayerNode.ToMapsui(At(139.7671, 35.6812));

        // Same numbers as the geometry layer's own projection test, arrived at the other way.
        Assert.InRange(converted.Extent!.MinX, 15558800, 15558805);
        Assert.InRange(converted.Extent!.MinY, 4256841, 4256846);
    }

    // ── Style identity, which is what makes the split safe ───────────────────

    [Fact]
    public void A_hundred_frames_build_one_style()
    {
        // A style is a value, and this node is stateful anyway: a fresh instance every frame would
        // rebuild every layer holding it, and Mapsui keys its rendered-geometry cache on the style
        // object itself.
        var node = new VectorStyleNode();

        for (int frame = 0; frame < 100; frame++) node.Update();

        Assert.Equal(1, node.StylesBuilt);
    }

    [Fact]
    public void The_same_style_object_comes_back_while_the_pins_are_unchanged()
    {
        var node = new VectorStyleNode();

        var first = node.Update(lineWidth: 3f);
        var second = node.Update(lineWidth: 3f);

        Assert.Same(first, second);
    }

    [Fact]
    public void Changing_a_style_pin_gives_a_new_style()
    {
        var node = new VectorStyleNode();

        var thin = node.Update(lineWidth: 1f);
        var thick = node.Update(lineWidth: 8f);

        Assert.NotSame(thin, thick);
        Assert.Equal(2, node.StylesBuilt);
    }

    [Fact]
    public void A_style_that_never_changes_never_rebuilds_the_layer()
    {
        // The composition property: two nodes, a hundred frames, one layer. If VectorStyle handed
        // out a new object each frame this would be 100 - which is what the old single Geometry
        // node could not get wrong because it never exposed the seam.
        var style = new VectorStyleNode();
        using var layer = new FeatureLayerNode();
        var features = new[] { At(0, 0), At(1, 1) };

        for (int frame = 0; frame < 100; frame++)
            layer.Update(out _, features, style.Update());

        Assert.Equal(1, layer.LayersBuilt);
    }

    // ── The layer ─────────────────────────────────────────────────────────────

    [Fact]
    public void No_features_gives_no_layer()
    {
        // So a Map can be wired up before there is anything to draw.
        using var node = new FeatureLayerNode();

        Assert.Null(node.Update(out _, Array.Empty<NtsFeature>()));
        Assert.Null(node.Update(out _, null));
    }

    [Fact]
    public void An_unconnected_style_still_draws()
    {
        // Good defaults are public API: a feature layer with nothing but features must be visible.
        using var node = new FeatureLayerNode();

        var layer = node.Update(out _, new[] { At(0, 0) });

        Assert.NotNull(layer);
        Assert.NotNull(((global::Mapsui.Layers.MemoryLayer)layer!).Style);
    }

    [Fact]
    public void Changing_the_features_rebuilds_once()
    {
        using var node = new FeatureLayerNode();
        var first = new[] { At(0, 0) };
        var second = new[] { At(0, 0), At(1, 1) };

        for (int frame = 0; frame < 10; frame++) node.Update(out _, first);
        for (int frame = 0; frame < 10; frame++) node.Update(out _, second);

        Assert.Equal(2, node.LayersBuilt);
    }

    // ── Size, which the architecture document asks about ─────────────────────

    [Theory]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(10_000)]
    public void The_number_of_features_does_not_change_how_often_anything_is_built(int count)
    {
        // Not a speed test. The question is whether the lifecycle is right at every size: a shape
        // that rebuilds per feature, or per frame, only shows up when the count is large enough to
        // hurt - and by then it looks like "the map is slow" rather than like a bug.
        var features = Enumerable.Range(0, count)
            .Select(i => At(i % 180 * 0.01, i % 90 * 0.01))
            .ToArray();
        var style = new VectorStyleNode();
        using var layer = new FeatureLayerNode();

        for (int frame = 0; frame < 60; frame++)
            layer.Update(out _, features, style.Update());

        Assert.Equal(1, layer.LayersBuilt);
        Assert.Equal(1, style.StylesBuilt);
    }

    // ── The shortcut is the three, composed ──────────────────────────────────

    [Fact]
    public void The_Geometry_node_still_works_and_is_now_the_three_composed()
    {
        // Working functionality does not get deleted for a cleaner theory. It is the same node
        // from the outside; inside it is a VectorStyle and a FeatureLayer, so if the pieces
        // compose here they compose in a patch.
        using var node = new GeometryLayerNode();
        var geometry = new Geometry[] { Factory.CreatePoint(new Coordinate(139.7671, 35.6812)) };

        for (int frame = 0; frame < 100; frame++)
            node.Update(out var built, geometry, lineWidth: 4f);

        var layer = node.Update(out var layersBuilt, geometry, lineWidth: 4f);

        Assert.NotNull(layer);
        Assert.Equal(1, layersBuilt);
    }
}
