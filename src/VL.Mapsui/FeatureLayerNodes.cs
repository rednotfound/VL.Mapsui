using System;
using System.Collections.Generic;
using System.Linq;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using MapsuiFeature = global::Mapsui.IFeature;
using IStyle = global::Mapsui.Styles.IStyle;
using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.Mapsui;

/// <summary>
/// Draws features on the map. This is the adapter between NetTopologySuite and Mapsui.
/// </summary>
/// <remarks>
/// **The conversion lives here, on the consuming side, and only here.** A feature arrives as
/// NetTopologySuite's neutral <c>Feature</c> — geometry plus attributes, knowing nothing about
/// rendering — and leaves as something Mapsui draws. Putting the adapter in this package rather
/// than teaching NTS about Mapsui is what keeps the dependency pointing one way: whatever produced
/// the geometry never has to know which engine ends up drawing it.
///
/// **Coordinates are WGS84 longitude and latitude, x first**, which is what every GeoJSON file
/// carries and what VL.GIS produces. Mapsui works in spherical mercator, so this projects on the
/// way in — on a copy, so the geometry the patch still holds is not silently turned into metres.
///
/// A process node, because a layer is a resource: rebuilding one every frame re-styles and
/// re-renders everything on it.
/// </remarks>
[ProcessNode(Name = "FeatureLayer", Category = "Mapsui.Layers")]
public class FeatureLayerNode : IDisposable
{
    MemoryLayer? _layer;
    NtsFeature[] _features = Array.Empty<NtsFeature>();
    IStyle? _style;
    string _name = string.Empty;

    /// <summary>Layers built by this node. It should settle at 1 and stay there.</summary>
    internal int LayersBuilt { get; private set; }

    /// <summary>
    /// A layer drawing the given features, ready to hand to a Map alongside a tile layer.
    /// </summary>
    /// <remarks>
    /// No features gives no layer rather than an empty one, so a Map can be wired up before there
    /// is anything to draw.
    ///
    /// **Watch Layers Built.** It should reach 1 and stay. A number that climbs frame after frame
    /// means one of the inputs is a new object every frame — features are compared as a set by
    /// identity, and the style by identity too, which is why <c>VectorStyle</c> hands out the same
    /// object while its pins are unchanged.
    /// </remarks>
    public ILayer? Update(
        out int layersBuilt,
        IEnumerable<NtsFeature>? features = null,
        IStyle? style = null,
        string name = "Features")
    {
        var incoming = features?.Where(f => f?.Geometry is not null).ToArray() ?? Array.Empty<NtsFeature>();
        var wanted = style ?? Styles.Default;

        if (incoming.Length == 0)
        {
            Release();
            layersBuilt = LayersBuilt;
            return null;
        }

        if (_layer is null
            || !incoming.SequenceEqual(_features)
            || !ReferenceEquals(wanted, _style)
            || name != _name)
        {
            Release();
            _layer = Build(incoming, wanted, name);
            _features = incoming;
            _style = wanted;
            _name = name;
            LayersBuilt++;
        }

        layersBuilt = LayersBuilt;
        return _layer;
    }

    static MemoryLayer Build(NtsFeature[] features, IStyle style, string name)
        => new(name)
        {
            Features = features.Select(ToMapsui).ToArray(),
            Style = style,
        };

    /// <summary>
    /// One neutral feature as one Mapsui feature: geometry projected, attributes copied across.
    /// </summary>
    /// <remarks>
    /// Mapsui keeps attributes behind an indexer and lists them on <c>Fields</c>; NTS keeps them in
    /// an <c>AttributesTable</c>. Neither is the other's, so they are copied one name at a time —
    /// read off the assemblies rather than from an example, since Mapsui ships no XML docs.
    /// </remarks>
    internal static MapsuiFeature ToMapsui(NtsFeature feature)
    {
        var converted = new GeometryFeature(GeometryLayerNode.ToMercator(feature.Geometry));

        if (feature.Attributes is not null)
            foreach (var name in feature.Attributes.GetNames())
                converted[name] = feature.Attributes[name];

        return converted;
    }

    void Release()
    {
        _layer?.Dispose();
        _layer = null;
        _features = Array.Empty<NtsFeature>();
        _style = null;
        _name = string.Empty;
    }

    /// <summary>Releases the layer.</summary>
    public void Dispose() => Release();
}
