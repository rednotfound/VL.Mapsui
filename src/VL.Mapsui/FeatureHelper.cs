using System.Collections.Immutable;

using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace VL.Mapsui;

/// <summary>
/// Builds the neutral NetTopologySuite feature this package's layers consume.
/// </summary>
/// <remarks>
/// **Not a node.** The <c>Feature</c> and <c>Split</c> nodes lived here (as <c>FeatureNodes</c>)
/// until 2026-08-22 and moved to VL.NetTopologySuite's <c>NTS.Feature</c> category, because a
/// feature is a data-model object that has to be constructible without a map engine installed —
/// VL.GeoJSON writes them, a patch can make one by hand, and this package only draws and picks
/// them. The field-wide evidence for that layering is in vl-nettopologysuite's
/// <c>docs/ARCHITECTURE.md</c>, "Where a feature lives"; the local summary is in
/// <c>docs/MAPSUI-SURFACE.md</c>.
///
/// What stays behind is this internal helper, because <c>GeometryLayer</c> and <c>ToFeatures</c>
/// build features as plumbing. Internal plumbing is not a node surface, and duplicating six lines
/// is cheaper than an assembly reference between two packages that deliberately compose through
/// NTS types alone.
/// </remarks>
internal static class FeatureHelper
{
    internal static NtsFeature Feature(
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
