using Stride.Core.Mathematics;
using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiSymbolStyle = global::Mapsui.Styles.SymbolStyle;
using MapsuiSymbolType = global::Mapsui.Styles.SymbolType;
using Brush = global::Mapsui.Styles.Brush;
using Pen = global::Mapsui.Styles.Pen;

namespace VL.Mapsui;

/// <summary>
/// The shape a point is marked with.
/// </summary>
/// <remarks>
/// Ours rather than Mapsui's, the same way <c>WidgetCorner</c> is — an enum in a node signature is
/// part of the public API, and one we own can drop what we do not support.
///
/// **Mapsui's fourth value, `Image`, is deliberately not here.** It draws a bitmap, which needs a
/// `BitmapId` from Mapsui's `BitmapRegistry` — loading, ownership and disposal of an image, none of
/// which is a style decision. A marker made from a file is its own node when it exists.
/// </remarks>
public enum SymbolShape
{
    /// <summary>A circle at equal scale. Mapsui's default.</summary>
    Ellipse,

    /// <summary>A square at equal scale.</summary>
    Rectangle,

    /// <summary>Pointing up.</summary>
    Triangle,
}

/// <summary>
/// What a point looks like on the map: its shape, its size and its colours.
/// </summary>
/// <remarks>
/// **This is the style for POINTS, and only for points — measured, after getting it wrong.** The
/// claim here used to be that `SymbolStyle` takes `VectorStyle`'s place in a chain, since Mapsui's
/// `SymbolStyle` *derives from* `VectorStyle`. Inheritance said yes and the renderer says no: a
/// polygon under a `SymbolStyle` draws **0 pixels** where the same polygon under a `VectorStyle`
/// draws 14884, and where even *no style at all* manages 956. Mapsui's Skia renderer dispatches on
/// the style's runtime type rather than on what it inherits, and a raw `Mapsui.Styles.SymbolStyle`
/// behaves the same way, so this is the library's shape and not something we set (2026-08-16).
///
/// **A map holding more than points therefore needs `StyleByGeometry`**, which hands each feature
/// the style for its own kind of geometry: this into `Point`, a `VectorStyle` into `Polygon` and
/// `Line`. That is how OpenLayers, Mapbox GL, Leaflet and QGIS all do it, and Mapsui's
/// `ThemeStyle` is the mechanism underneath.
///
/// **Stacking a `VectorStyle` under this one is not the answer, though it looks like one.** It was
/// tried for an afternoon: the polygon comes back, and every point gets two concentric circles,
/// because a `VectorStyle` draws its own 32-pixel marker there as well. `Scale` below 1 then
/// shrinks nothing — 0.6 measured 22 pixels across alone and 34 with a `VectorStyle` behind it,
/// which is simply the default. Dispatch, do not stack.
///
/// **Without it a point is still drawn — as a ring.** Mapsui's point renderer falls back to a
/// default symbol, so nothing is invisible, but measured 2026-08-16 that fallback covers **180
/// pixels** where a filled marker covers **952**: it is a 2-pixel outline around a 32-pixel circle,
/// and against a busy basemap that is close to nothing. Five times the ink is what this node buys
/// before anyone changes a colour.
///
/// **Scale 1 is 32 pixels, and a symbol is always sized in pixels.** That is
/// `SymbolStyle.DefaultWidth`, read off Mapsui, and it is what `Scale` multiplies. The marker keeps
/// its size on screen while the ground under it zooms, which is right for a marker — its size says
/// "there is a thing here", not how big the thing is.
///
/// **There is no pin for sizing it on the ground, because Mapsui 4.1.9 cannot do it.**
/// `Mapsui.Styles.UnitType` offers `Pixel` and `WorldUnit` and `SymbolStyle` has a `UnitType`
/// property, but the Skia renderer never reads it: measured 2026-08-16, a rectangle at scale 1 draws
/// 1156 pixels under both settings at both zoom levels, and the string `UnitType` appears **zero**
/// times in `Mapsui.Rendering.Skia.dll`. A pin that does nothing is worse than no pin.
///
/// **When the size is the information, make it geometry.** "The 500-metre catchment", "the GPS
/// accuracy", "the area affected" — buffer the point into a polygon (`VL.NetTopologySuite` has
/// `Buffer`) and style it with `VectorStyle`. It then scales with the map because it *is* on the
/// map, and it is also pickable, intersectable and measurable, which a symbol never is. Note that
/// spherical mercator stretches with latitude — a "unit" is a metre only at the equator, and about
/// 0.82 of one at Kyoto — so buffer in a metric projection if the number has to mean metres.
///
/// A process node for the reason `VectorStyle` is: **its identity is compared downstream twice.** A
/// layer treats a new style object as a change and rebuilds, and Mapsui keys its rendered-geometry
/// cache on the style object (`IFeature.RenderedGeometry` is an `IDictionary&lt;IStyle, object&gt;`).
/// Handing out a fresh style every frame would rebuild every layer holding it, on every feature.
/// Nothing is held open; the object simply has to be the same object.
/// </remarks>
[ProcessNode(Name = "SymbolStyle", Category = "Mapsui.Styles")]
public class SymbolStyleNode
{
    MapsuiSymbolStyle? _style;
    SymbolShape _shape;
    float _scale = -1f;
    Color4 _fill;
    Color4 _outline;

    /// <summary>Styles built by this node. It should reach 1 and stay there.</summary>
    internal int StylesBuilt { get; private set; }

    /// <summary>A style to hand to a layer, or to a <c>LabelStyle</c> to carry along with it.</summary>
    /// <remarks>
    /// Scale multiplies 32 pixels, so 1 is a 32-pixel marker and 0.5 a 16-pixel one.
    ///
    /// The colour defaults are the same translucent red as `VectorStyle`'s, so a patch that swaps
    /// one node for the other sees the shape change and not the palette. They cannot sit in the
    /// signature — a `Color4` is not a compile-time constant — so `null` means "the default" and the
    /// body supplies it, the same rule as the cache folder one type along.
    /// </remarks>
    public IStyle Update(
        SymbolShape shape = SymbolShape.Ellipse,
        float scale = 1f,
        Color4? fillColor = null,
        Color4? outlineColor = null)
    {
        var fill = fillColor ?? new Color4(1f, 0.2f, 0.1f, 0.35f);
        var outline = outlineColor ?? new Color4(1f, 0.2f, 0.1f, 0.9f);

        if (_style is null || shape != _shape || scale != _scale
            || fill != _fill || outline != _outline)
        {
            _style = new MapsuiSymbolStyle
            {
                SymbolType = ToMapsui(shape),
                SymbolScale = scale,
                Fill = new Brush(Colors.ToMapsui(fill)),
                Outline = new Pen(Colors.ToMapsui(outline), 2f),
            };
            _shape = shape;
            _scale = scale;
            _fill = fill;
            _outline = outline;
            StylesBuilt++;
        }

        return _style;
    }

    /// <summary>
    /// Ours to Mapsui's. Written out rather than cast, so adding a value to either enum is a compile
    /// error here instead of a wrong shape on the map.
    /// </summary>
    static MapsuiSymbolType ToMapsui(SymbolShape shape) => shape switch
    {
        SymbolShape.Rectangle => MapsuiSymbolType.Rectangle,
        SymbolShape.Triangle => MapsuiSymbolType.Triangle,
        _ => MapsuiSymbolType.Ellipse,
    };
}
