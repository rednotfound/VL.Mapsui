using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using ThemeStyle = global::Mapsui.Styles.Thematics.ThemeStyle;
using MapsuiFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace VL.Mapsui;

/// <summary>
/// One style per geometry type, chosen for each feature as it is drawn.
/// </summary>
/// <remarks>
/// **This is how a map with mixed data is styled, everywhere.** A layer read from a file holds
/// points and lines and polygons together, and each wants a different thing: a point wants a
/// marker, a line wants a stroke, a polygon wants a fill. Every mapping library says so in its own
/// vocabulary — OpenLayers takes a style function returning
/// <c>styles[feature.getGeometry().getType()]</c>, Mapbox GL splits <c>circle</c>, <c>line</c> and
/// <c>fill</c> into separate layer types, Leaflet has <c>pointToLayer</c> beside <c>style</c>. This
/// node is Mapsui's version: <c>Mapsui.Styles.Thematics.ThemeStyle</c>, which is handed each
/// feature and returns the style for it.
///
/// **The alternative is stacking, and stacking is what this node exists to undo.** A `SymbolStyle`
/// draws points and nothing else — a polygon under one covers **0 pixels**, measured. Putting a
/// `VectorStyle` underneath rescues the polygon and ruins the point, because a `VectorStyle` draws
/// its own 32-pixel circle there too: two concentric markers, and a `Scale` below 1 that cannot
/// shrink anything. Dispatching means each feature is drawn once, by the style meant for it —
/// measured 2026-08-16, every geometry type through this node puts down exactly the pixels its
/// style puts down alone.
///
/// **An unwired pin draws nothing for that geometry type, silently.** Mapsui does not object; the
/// features are simply absent. `FeatureLayer`'s `Status` is what says so, because it is the node
/// that can see the features and the style at once.
///
/// Stateful for its identity, like every style node here: a layer treats a new style object as a
/// change and rebuilds, so handing out a fresh one every frame would rebuild the map every frame.
/// </remarks>
[ProcessNode(Name = "StyleByGeometry", Category = "Mapsui.Styles")]
public class StyleByGeometryNode
{
    GeometryTheme? _theme;
    IStyle? _point;
    IStyle? _line;
    IStyle? _polygon;

    /// <summary>Themes built by this node. It should reach 1 and stay there.</summary>
    internal int StylesBuilt { get; private set; }

    /// <summary>A style to hand to a layer, or on to <c>LabelStyle</c>.</summary>
    /// <remarks>
    /// Wire `SymbolStyle` into `Point` and `VectorStyle` into `Polygon` and `Line` — a
    /// `VectorStyle`'s `Line Color` and `Line Width` are what a line is drawn with, and its
    /// `Fill Color` is what a polygon is filled with, so the same node serves both with different
    /// pins mattering.
    /// </remarks>
    public IStyle Update(IStyle? point = null, IStyle? line = null, IStyle? polygon = null)
    {
        if (_theme is null
            || !ReferenceEquals(point, _point)
            || !ReferenceEquals(line, _line)
            || !ReferenceEquals(polygon, _polygon))
        {
            _theme = new GeometryTheme(point, line, polygon);
            _point = point;
            _line = line;
            _polygon = polygon;
            StylesBuilt++;
        }

        return _theme;
    }
}

/// <summary>
/// A <c>ThemeStyle</c> that also says what it holds.
/// </summary>
/// <remarks>
/// **The three styles are properties as well as a decision, because two other nodes have to read
/// them.** A `ThemeStyle` is a function, and a function is opaque: `LabelStyle` walks the style
/// upstream of it to find the marker it must lift the label clear of, and `FeatureLayer` walks it
/// to warn when the features it holds cannot be drawn. Neither can call the function — they have no
/// feature to call it with, and at that moment there may not be one. So the answer is carried
/// alongside rather than hidden inside.
///
/// <c>ThemeStyle.GetStyle</c> cannot be overridden — reflection reports it virtual, but that is
/// only how an interface implementation looks, and the compiler says <c>CS0506</c>. So the dispatch
/// goes to the base constructor as a function over the three <b>parameters</b>: C# forbids
/// <c>this</c> in a base-constructor argument, and captured locals are the way round it.
/// </remarks>
public sealed class GeometryTheme : ThemeStyle
{
    internal GeometryTheme(IStyle? point, IStyle? line, IStyle? polygon)
        : base(feature => Dispatch(feature, point, line, polygon)!)
    {
        Point = point;
        Line = line;
        Polygon = polygon;
    }

    /// <summary>What a Point or MultiPoint is drawn with.</summary>
    public IStyle? Point { get; }

    /// <summary>What a LineString, MultiLineString or LinearRing is drawn with.</summary>
    public IStyle? Line { get; }

    /// <summary>What a Polygon or MultiPolygon is drawn with.</summary>
    public IStyle? Polygon { get; }

    /// <summary>
    /// The style for one feature, by the kind of geometry it carries.
    /// </summary>
    /// <remarks>
    /// A <c>GeometryCollection</c> can hold anything, so it takes whichever pin is wired, preferring
    /// the one that fills: Polygon, then Line, then Point. That is a guess, but a stated one — the
    /// alternative is drawing nothing for a geometry type the patch clearly meant to style.
    ///
    /// Returning <c>null</c> is allowed and draws nothing: measured 2026-08-16, Mapsui does not
    /// throw for it.
    /// </remarks>
    static IStyle? Dispatch(MapsuiFeature feature, IStyle? point, IStyle? line, IStyle? polygon) =>
        Kind(feature) switch
        {
            GeometryKind.Point => point,
            GeometryKind.Line => line,
            GeometryKind.Polygon => polygon,
            _ => polygon ?? line ?? point,
        };

    internal enum GeometryKind { Point, Line, Polygon, Other }

    static GeometryKind Kind(MapsuiFeature feature) =>
        feature is GeometryFeature { Geometry: { } geometry } ? Kind(geometry) : GeometryKind.Other;

    internal static GeometryKind Kind(NtsGeometry geometry) => geometry switch
    {
        NetTopologySuite.Geometries.Point or NetTopologySuite.Geometries.MultiPoint => GeometryKind.Point,
        // A LinearRing IS a LineString in NTS, so it is caught by the pattern below; naming it
        // would be dead code that reads like a decision.
        NetTopologySuite.Geometries.LineString or NetTopologySuite.Geometries.MultiLineString => GeometryKind.Line,
        NetTopologySuite.Geometries.Polygon or NetTopologySuite.Geometries.MultiPolygon => GeometryKind.Polygon,
        _ => GeometryKind.Other,
    };

    /// <summary>Whether this theme has a style for the given geometry — what <c>Status</c> asks.</summary>
    internal bool CanDraw(NtsGeometry geometry) => Kind(geometry) switch
    {
        GeometryKind.Point => Point is not null,
        GeometryKind.Line => Line is not null,
        GeometryKind.Polygon => Polygon is not null,
        _ => (Polygon ?? Line ?? Point) is not null,
    };

    /// <summary>The word a status line uses for what is missing.</summary>
    internal static string Name(NtsGeometry geometry) => Kind(geometry) switch
    {
        GeometryKind.Point => "Point",
        GeometryKind.Line => "Line",
        GeometryKind.Polygon => "Polygon",
        _ => "Polygon",
    };
}
