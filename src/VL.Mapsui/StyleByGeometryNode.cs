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
// This is how a map with mixed data is styled, everywhere. A layer read from a file holds points and
// lines and polygons together, and each wants a different thing: a point wants a marker, a line
// wants a stroke, a polygon wants a fill. Every mapping library says so in its own vocabulary -
// OpenLayers takes a style function returning styles[feature.getGeometry().getType()], Mapbox GL
// splits circle, line and fill into separate layer types, Leaflet has pointToLayer beside style.
// This node is Mapsui's version: Mapsui.Styles.Thematics.ThemeStyle, which is handed each feature
// and returns the style for it.
//
// The alternative is stacking, and stacking is what this node exists to undo. A SymbolStyle draws
// points and nothing else - a polygon under one covers 0 pixels, measured. Putting a VectorStyle
// underneath rescues the polygon and ruins the point, because a VectorStyle draws its own 32-pixel
// circle there too: two concentric markers, and a Scale below 1 that cannot shrink anything.
// Dispatching means each feature is drawn once, by the style meant for it - measured, every
// geometry type through this node puts down exactly the pixels its style puts down alone.
//
// An unwired pin draws nothing for that geometry type, silently. Mapsui does not object; the
// features are simply absent. FeatureLayer's Status is what says so, because it is the node that
// can see the features and the style at once.
//
// Stateful for its identity, like every style node here: a layer treats a new style object as a
// change and rebuilds, so handing out a fresh one every frame would rebuild the map every frame.

/// <summary>
/// Picks a style for each feature by its geometry type: point, line or polygon.
/// </summary>
/// <remarks>
/// Use it when a layer holds more than one kind of geometry.
/// An unwired pin draws nothing for that kind, and FeatureLayer's Status says so.
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

    // Wire SymbolStyle into Point and VectorStyle into Polygon and Line - a VectorStyle's Line Color
    // and Line Width are what a line is drawn with, and its Fill Color is what a polygon is filled
    // with, so the same node serves both with different pins mattering.

    /// <summary>
    /// Configures the style from one style per geometry type.
    /// </summary>
    /// <param name="point">The style for points, usually a SymbolStyle.</param>
    /// <param name="line">The style for lines, usually a VectorStyle. Its line colour and width are used.</param>
    /// <param name="polygon">The style for polygons, usually a VectorStyle.</param>
    /// <returns>A style for a layer, or for LabelStyle.</returns>
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
