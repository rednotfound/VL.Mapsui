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
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using IAttributesTable = NetTopologySuite.Features.IAttributesTable;
using MapsuiSymbolStyle = global::Mapsui.Styles.SymbolStyle;
using MapsuiVectorStyle = global::Mapsui.Styles.VectorStyle;
using StyleCollection = global::Mapsui.Styles.StyleCollection;

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
    /// How often the features on the layer were replaced. Separate from LayersBuilt because they
    /// answer different questions: whether the layer's identity churned, and whether its contents
    /// did.
    /// </summary>
    internal int FeatureSetsBuilt { get; private set; }

    /// <summary>
    /// A layer drawing the given features, ready to hand to a Map alongside a tile layer.
    /// </summary>
    /// <remarks>
    /// No features gives no layer rather than an empty one, so a Map can be wired up before there
    /// is anything to draw.
    ///
    /// **Watch Layers Built.** It should reach 1 and stay, and it now can: **features are compared
    /// by value, not by reference.** That is not a refinement, it is the difference between a map
    /// and a flickering map. `Feature` is a static node, so VL evaluates it every frame and it
    /// returns a new object every frame; comparing those by reference meant the layer was rebuilt
    /// sixty times a second, the Map saw a new layer each time and rebuilt in turn, and the whole
    /// thing flickered. Seen on screen before any test caught it.
    /// </remarks>
    public ILayer? Update(
        out int layersBuilt,
        out string status,
        IEnumerable<NtsFeature>? features = null,
        IStyle? style = null,
        bool enabled = true,
        string name = "Features")
    {
        var incoming = features?.Where(f => f?.Geometry is not null).ToArray() ?? Array.Empty<NtsFeature>();
        var wanted = style ?? Styles.Default;

        status = Describe(incoming, wanted);

        if (incoming.Length == 0)
        {
            Release();
            layersBuilt = LayersBuilt;
            return null;
        }

        // The layer object is built once and kept. Everything else is set on it, because its
        // identity is what a Map compares - handing out a new layer is what makes a map rebuild.
        if (_layer is null)
        {
            _layer = new MemoryLayer(name)
            {
                // Mapsui hit-tests only layers that opted in, and the default is FALSE - measured
                // 2026-08-14: with it off, Pick over the dead centre of a square returns no
                // feature and no error anywhere. A data layer exists to be asked about, so it is
                // switched on here rather than left as a pin nobody would know to look for.
                IsMapInfoLayer = true,
            };
            _name = name;
            LayersBuilt++;
        }
        else if (name != _name)
        {
            _layer.Name = name;
            _name = name;
        }

        if (!SameFeatures(incoming, _features))
        {
            _layer.Features = incoming.Select(ToMapsui).ToArray();
            _features = incoming;
            _layer.DataHasChanged();
            FeatureSetsBuilt++;
        }

        if (!ReferenceEquals(wanted, _style))
        {
            _layer.Style = wanted;
            _style = wanted;
        }

        // Enabled is Mapsui's own visibility flag, set rather than rebuilt: switching a layer off
        // and on again keeps its features, its style and everything the renderer has cached about
        // them. It is also why this is a pin at all - a patch should be able to hide a layer
        // without dismantling it.
        _layer.Enabled = enabled;

        layersBuilt = LayersBuilt;
        return _layer;
    }

    /// <summary>
    /// Whether two feature sets say the same thing, rather than whether they are the same objects.
    /// </summary>
    /// <remarks>
    /// Reference equality is the wrong question here and asking it cost a flickering map: in a
    /// patch every one of these is a fresh object every frame. <c>EqualsExact</c> is NTS's own
    /// structural comparison — same type, same coordinates, same order — and walking the
    /// coordinates is far cheaper than reprojecting them and rebuilding Mapsui's features, which is
    /// what the alternative does.
    /// </remarks>
    static bool SameFeatures(NtsFeature[] a, NtsFeature[] b)
    {
        if (a.Length != b.Length) return false;

        for (var i = 0; i < a.Length; i++)
        {
            if (ReferenceEquals(a[i], b[i])) continue;
            if (a[i].Geometry is not { } left || b[i].Geometry is not { } right) return false;
            if (!left.EqualsExact(right)) return false;
            if (!SameAttributes(a[i].Attributes, b[i].Attributes)) return false;
        }

        return true;
    }

    static bool SameAttributes(IAttributesTable? a, IAttributesTable? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        var names = a.GetNames();
        if (names.Length != b.GetNames().Length) return false;

        foreach (var name in names)
        {
            if (!b.Exists(name)) return false;
            if (!Equals(a[name], b[name])) return false;
        }

        return true;
    }

    /// <summary>
    /// One neutral feature as one Mapsui feature: geometry projected, attributes copied across.
    /// </summary>
    /// <remarks>
    /// Mapsui keeps attributes behind an indexer and lists them on <c>Fields</c>; NTS keeps them in
    /// an <c>AttributesTable</c>. Neither is the other's, so they are copied one name at a time —
    /// read off the assemblies rather than from an example, since Mapsui ships no XML docs.
    /// </remarks>
    /// <summary>
    /// What the layer holds, and — the reason this pin exists — <b>what of it will not be drawn</b>.
    /// </summary>
    /// <remarks>
    /// This node is the only place that sees the features and the style at the same time, which
    /// makes it the only place that can notice a combination guaranteeing an empty screen. Mapsui's
    /// renderer dispatches on the style's runtime type: a `SymbolStyle` draws points and refuses
    /// polygons and lines outright — 0 pixels, measured 2026-08-16, no exception and nothing in any
    /// log. A whole GeoJSON file's worth of shapes can go missing this way while every other
    /// readout in the patch says the data arrived, which is exactly what happened the first time
    /// these three packages were joined.
    ///
    /// A `SymbolStyle` with a `VectorStyle` beside it in the same collection is fine, because the
    /// polygon renderer finds the second one. So the check is not "is there a SymbolStyle" but
    /// "is there a SymbolStyle and nothing else that can draw a shape".
    /// </remarks>
    static string Describe(NtsFeature[] features, IStyle style)
    {
        if (features.Length == 0) return "nothing connected";

        var shapes = features.Count(f => f.Geometry is not null && !IsPointLike(f.Geometry));
        var plural = features.Length == 1 ? "feature" : "features";

        // A bare SymbolStyle over anything but points: the shapes are simply absent.
        if (shapes > 0 && DrawsPointsOnly(style))
            return $"{features.Length} {plural}, but {shapes} of them are not points and a "
                 + "SymbolStyle draws NOTHING for those. Use StyleByGeometry.";

        // A StyleByGeometry with a pin nobody wired does the same thing one level up, and Mapsui
        // does not object to it either - measured 2026-08-16, a null from GetStyle draws 0 pixels
        // and throws nothing. Naming the empty pin is the difference between a minute and an hour.
        if (FindTheme(style) is { } theme)
        {
            var missing = features
                .Where(f => f.Geometry is not null && !theme.CanDraw(f.Geometry))
                .GroupBy(f => GeometryTheme.Name(f.Geometry))
                .Select(g => $"{g.Count()} {(g.Count() == 1 ? "feature needs" : "features need")} StyleByGeometry's {g.Key} pin")
                .ToArray();

            if (missing.Length > 0)
                return $"{features.Length} {plural}, but nothing is wired for some: {string.Join("; ", missing)}.";
        }

        return $"{features.Length} {plural}";
    }

    /// <summary>The geometry theme in a style, however deeply a LabelStyle has wrapped it.</summary>
    static GeometryTheme? FindTheme(IStyle style)
    {
        switch (style)
        {
            case GeometryTheme theme:
                return theme;
            case StyleCollection collection:
                foreach (var inner in collection.Styles)
                    if (FindTheme(inner) is { } found) return found;
                return null;
            default:
                return null;
        }
    }

    static bool IsPointLike(NtsGeometry geometry)
        => geometry is NetTopologySuite.Geometries.Point or NetTopologySuite.Geometries.MultiPoint;

    /// <summary>A SymbolStyle somewhere, and nothing alongside it that can draw a shape.</summary>
    static bool DrawsPointsOnly(IStyle style)
    {
        var symbol = false;
        var other = false;
        Walk(style);
        return symbol && !other;

        void Walk(IStyle s)
        {
            switch (s)
            {
                case MapsuiSymbolStyle:
                    symbol = true;
                    break;
                case MapsuiVectorStyle:          // a plain one - SymbolStyle is caught above
                    other = true;
                    break;
                case StyleCollection collection:
                    foreach (var inner in collection.Styles) Walk(inner);
                    break;
            }
        }
    }

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
