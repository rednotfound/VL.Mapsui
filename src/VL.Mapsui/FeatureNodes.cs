using System.Collections.Immutable;
using VL.Core.Import;

using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace VL.Mapsui;

/// <summary>
/// Geometry with attributes: what a map actually shows and what a click can report.
/// </summary>
/// <remarks>
/// **The type is NetTopologySuite's <c>Feature</c>, not a Mapsui one and not one of ours.** It is
/// the mature neutral model that already exists — geometry plus an attributes table, nothing about
/// styles, layers or renderers — so a feature can be produced by something that has never heard of
/// Mapsui and consumed by something that has. Inventing a `VLFeature` here would have made this
/// package the definition of the whole domain, which is the thing to avoid.
///
/// The conversion into Mapsui's own feature happens where it is consumed, in
/// <see cref="FeatureLayerNode"/>. That is the adapter boundary, and it points the right way: this
/// package knows about NTS, and NTS knows nothing about this package.
///
/// A static operation, because a feature holds no resource and its identity is not compared
/// downstream the way a style's is — a layer compares the *set* of features.
/// </remarks>
/// <remarks>
/// **SkipCategory, not [Name(...)].** A named static class becomes a category level of its own, so
/// naming this one would put the node in a category beside where it belongs and the node would
/// then silently fail to resolve, taking every link to it along. Same reason as
/// <see cref="MapInfoNodes"/>.
/// </remarks>
[SkipCategory]
public static class FeatureNodes
{
    /// <summary>
    /// One feature: geometry in WGS84 longitude and latitude, plus whatever attributes describe it.
    /// </summary>
    /// <remarks>
    /// Attributes are a plain Dictionary of string to object — VL's own Dictionary, which is an
    /// ImmutableDictionary underneath. Leaving it unconnected gives a feature with no attributes,
    /// which is all a shape needs to be drawn; attributes are what makes a click able to say
    /// something about what it hit.
    /// </remarks>
    public static NtsFeature Feature(
        NtsGeometry? geometry = null,
        ImmutableDictionary<string, object>? attributes = null)
    {
        var table = new AttributesTable();
        if (attributes is not null)
            foreach (var pair in attributes)
                table.Add(pair.Key, pair.Value);

        return new NtsFeature(geometry!, table);
    }
}
