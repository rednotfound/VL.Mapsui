using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using ThemeStyle = global::Mapsui.Styles.Thematics.ThemeStyle;
using MapsuiFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A ThemeStyle that also says what it holds. The three styles are properties as well as a decision,
// because two other nodes have to read them. A ThemeStyle is a function, and a function is opaque:
// LabelStyle walks the style upstream of it to find the marker it must lift the label clear of, and
// FeatureLayer walks it to warn when the features it holds cannot be drawn. Neither can call the
// function - they have no feature to call it with, and at that moment there may not be one. So the
// answer is carried alongside rather than hidden inside.
//
// ThemeStyle.GetStyle cannot be overridden - reflection reports it virtual, but that is only how an
// interface implementation looks, and the compiler says CS0506. So the dispatch goes to the base
// constructor as a function over the three PARAMETERS: C# forbids "this" in a base-constructor
// argument, and captured locals are the way round it.

/// <summary>
/// The style StyleByGeometry builds: one style each for points, lines and polygons.
/// </summary>
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
    /// Returning <c>null</c> is allowed and draws nothing: measured, Mapsui does not
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
