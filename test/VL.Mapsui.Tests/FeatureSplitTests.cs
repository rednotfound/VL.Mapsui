using System.Collections.Immutable;
using Xunit;

using WKTReader = NetTopologySuite.IO.WKTReader;

namespace VL.Mapsui.Tests;

/// <summary>
/// Taking a feature apart — the half of the boundary that lets a picked feature be read.
/// </summary>
public class FeatureSplitTests
{
    static ImmutableDictionary<string, object> Attributes(params (string key, object value)[] pairs)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var (key, value) in pairs) builder[key] = value;
        return builder.ToImmutable();
    }

    [Fact]
    public void Split_undoes_Feature()
    {
        var geometry = new WKTReader().Read("POINT (135.7581 34.9859)");
        var feature = FeatureNodes.Feature(geometry, Attributes(("name", "Kyoto Station"), ("type", "station")));

        FeatureNodes.Split(feature, out var back, out var attributes);

        Assert.Equal(geometry, back);
        Assert.Equal("Kyoto Station", attributes["name"]);
        Assert.Equal("station", attributes["type"]);
        Assert.Equal(2, attributes.Count);
    }

    [Fact]
    public void A_feature_with_no_attributes_splits_into_an_empty_dictionary()
    {
        var feature = FeatureNodes.Feature(new WKTReader().Read("POINT (0 0)"));

        FeatureNodes.Split(feature, out _, out var attributes);

        Assert.NotNull(attributes);
        Assert.Empty(attributes);
    }

    [Fact]
    public void Nothing_splits_into_nothing_rather_than_throwing()
    {
        FeatureNodes.Split(null, out var geometry, out var attributes);

        Assert.Null(geometry);
        Assert.Empty(attributes);
    }

    /// <summary>
    /// The round trip a patch actually makes: build a feature, draw it, pick it, read it.
    /// </summary>
    /// <remarks>
    /// This is the one that would have caught `Pick` returning something unreadable, because it
    /// asserts on the attribute rather than on the feature object.
    /// </remarks>
    [Fact]
    public void An_attribute_survives_the_whole_round_trip()
    {
        var feature = FeatureNodes.Feature(
            new WKTReader().Read("POLYGON ((-0.5 -0.5, 0.5 -0.5, 0.5 0.5, -0.5 0.5, -0.5 -0.5))"),
            Attributes(("name", "probe")));

        using var layerNode = new FeatureLayerNode();
        var layer = layerNode.Update(out _, new[] { feature })!;

        var map = new MapNode().Update(new[] { layer });
        map.Navigator.SetSize(400, 400);
        map.Navigator.CenterOn(0, 0);
        map.Navigator.ZoomTo(400);

        var picked = new PickNode().Update(map, out var hit, out _, 200, 200);
        FeatureNodes.Split(picked, out var geometry, out var attributes);

        Assert.True(hit);
        Assert.NotNull(geometry);
        Assert.Equal("probe", attributes["name"]);
    }
}
