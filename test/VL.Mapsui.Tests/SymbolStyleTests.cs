using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using Stride.Core.Mathematics;
using Xunit;
using Xunit.Abstractions;

using MapsuiSymbolStyle = global::Mapsui.Styles.SymbolStyle;
using MapsuiSymbolType = global::Mapsui.Styles.SymbolType;
using IStyle = global::Mapsui.Styles.IStyle;
using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using ILayer = global::Mapsui.Layers.ILayer;
using IFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using Viewport = global::Mapsui.Viewport;
using MapsuiColor = global::Mapsui.Styles.Color;
using WKTReader = NetTopologySuite.IO.WKTReader;

namespace VL.Mapsui.Tests;

/// <summary>
/// Choosing what a point looks like.
/// </summary>
/// <remarks>
/// Two kinds of assertion here, and both are needed. The identity ones say the node does not churn —
/// a style handed out fresh every frame rebuilds every layer holding it. The **pixel** ones say the
/// node actually reaches the screen: a style object with the right fields set proves nothing if the
/// renderer ignores it.
/// </remarks>
public class SymbolStyleTests
{
    readonly ITestOutputHelper _out;
    public SymbolStyleTests(ITestOutputHelper output) => _out = output;

    // ---------- the object ----------

    [Fact]
    public void Shape_scale_and_colours_reach_the_style()
    {
        var node = new SymbolStyleNode();

        var style = (MapsuiSymbolStyle)node.Update(
            shape: SymbolShape.Triangle, scale: 2.5f,
            fillColor: new Color4(0f, 1f, 0f, 1f),
            outlineColor: new Color4(0f, 0f, 1f, 1f));

        Assert.Equal(MapsuiSymbolType.Triangle, style.SymbolType);
        Assert.Equal(2.5, style.SymbolScale);
        Assert.Equal(new MapsuiColor(0, 255, 0, 255), style.Fill!.Color);
        Assert.Equal(new MapsuiColor(0, 0, 255, 255), style.Outline!.Color);
    }

    /// <summary>
    /// Scale is a multiplier on 32 pixels, which is Mapsui's own default and worth pinning down —
    /// "scale" with no unit is a guess, and this is where the guess is settled.
    /// </summary>
    [Fact]
    public void Scale_one_means_thirty_two_pixels()
    {
        Assert.Equal(32d, MapsuiSymbolStyle.DefaultWidth);
        Assert.Equal(32d, MapsuiSymbolStyle.DefaultHeight);
    }

    [Fact]
    public void The_defaults_match_VectorStyles_so_swapping_one_for_the_other_changes_only_the_shape()
    {
        var symbol = (MapsuiSymbolStyle)new SymbolStyleNode().Update();
        var vector = (global::Mapsui.Styles.VectorStyle)new VectorStyleNode().Update();

        Assert.Equal(vector.Fill!.Color, symbol.Fill!.Color);
        Assert.Equal(vector.Outline!.Color, symbol.Outline!.Color);
    }

    // ---------- identity, the reason this is a process node ----------

    [Fact]
    public void A_hundred_frames_with_unchanged_inputs_build_one_style()
    {
        var node = new SymbolStyleNode();
        IStyle? last = null;

        for (var frame = 0; frame < 100; frame++) last = node.Update(shape: SymbolShape.Rectangle, scale: 1.5f);

        Assert.Equal(1, node.StylesBuilt);
        Assert.Same(last, node.Update(shape: SymbolShape.Rectangle, scale: 1.5f));
    }

    [Fact]
    public void Changing_any_input_builds_a_new_style()
    {
        var node = new SymbolStyleNode();

        node.Update(shape: SymbolShape.Ellipse, scale: 1f);
        node.Update(shape: SymbolShape.Rectangle, scale: 1f);                              // shape
        node.Update(shape: SymbolShape.Rectangle, scale: 2f);                              // scale
        node.Update(shape: SymbolShape.Rectangle, scale: 2f, fillColor: new Color4(1f, 1f, 1f, 1f));  // fill

        Assert.Equal(4, node.StylesBuilt);
    }

    // ---------- what actually lands on the canvas ----------

    /// <summary>
    /// The feature carries a `Name`, so a `LabelStyle` wired to it actually draws.
    /// </summary>
    /// <remarks>
    /// It did not, once. The first version of the composition test used a feature with no
    /// attributes, so the label had nothing to write, nothing was painted over the marker, and the
    /// test passed while two hundred markers were invisible on screen. **A double that cannot
    /// exercise the failure is not a test of it.**
    /// </remarks>
    static int PixelsDrawn(IStyle style, double resolution = 1)
    {
        var point = new GeometryFeature { Geometry = new WKTReader().Read("POINT (0 0)") };
        point["Name"] = "place";

        var layer = new MemoryLayer { Features = new[] { (IFeature)point }, Style = style };

        using var surface = SKSurface.Create(new SKImageInfo(400, 400));
        surface.Canvas.Clear(SKColors.White);

        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, resolution, 0, 400, 400),
            new List<ILayer> { layer }, new List<global::Mapsui.Widgets.IWidget>(),
            global::Mapsui.Styles.Color.White);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        var drawn = 0;
        for (var x = 0; x < bitmap.Width; x++)
        for (var y = 0; y < bitmap.Height; y++)
            if (bitmap.GetPixel(x, y) != SKColors.White) drawn++;

        return drawn;
    }

    /// <summary>
    /// Doubling the scale covers about four times the area — which is what proves the node reaches
    /// the renderer at all.
    /// </summary>
    /// <remarks>
    /// Asserted as a ratio, not as a pixel count: antialiasing and the outline stroke put the exact
    /// number a few percent either side, and a hard number would fail on someone else's SkiaSharp.
    /// </remarks>
    [Fact]
    public void Doubling_the_scale_quadruples_the_marker()
    {
        var node = new SymbolStyleNode();

        var one = PixelsDrawn(node.Update(shape: SymbolShape.Ellipse, scale: 1f, fillColor: new Color4(0f, 0f, 0f, 1f)));
        var two = PixelsDrawn(node.Update(shape: SymbolShape.Ellipse, scale: 2f, fillColor: new Color4(0f, 0f, 0f, 1f)));

        _out.WriteLine($"scale 1 -> {one} px, scale 2 -> {two} px, ratio {(double)two / one:0.00}");

        Assert.True(two > one * 3.0, $"expected about 4x, got {(double)two / one:0.00}x");
        Assert.True(two < one * 5.0, $"expected about 4x, got {(double)two / one:0.00}x");
    }

    /// <summary>
    /// Mapsui's fallback marker is a RING, and this node is what fills it in.
    /// </summary>
    /// <remarks>
    /// Settled by measurement rather than by argument. A 32-pixel disc is ~804 px²; the fallback
    /// covers **180**, which is what a 2-pixel stroke around a 32-pixel circle comes to
    /// (circumference ≈ 100). `SymbolStyle` with a fill covers **952** — the disc plus that ring.
    ///
    /// So "a point is drawn without a style" was true and misleading: it is drawn as an outline, and
    /// against a busy basemap that is close to invisible. Five times the ink is the difference this
    /// node makes, before anyone changes a colour.
    /// </remarks>
    [Fact]
    public void The_fallback_marker_is_a_ring_and_a_symbol_fills_it_in()
    {
        var fallback = PixelsDrawn(new global::Mapsui.Styles.VectorStyle());
        var filled = PixelsDrawn(new SymbolStyleNode().Update(shape: SymbolShape.Ellipse, scale: 1f, fillColor: new Color4(0f, 0f, 0f, 1f)));

        _out.WriteLine($"plain VectorStyle on a point : {fallback} px");
        _out.WriteLine($"SymbolStyle with a fill      : {filled} px");
        _out.WriteLine($"a filled 32 px disc would be : {(int)(System.Math.PI * 16 * 16)} px");

        Assert.True(fallback > 0, "the fallback does draw something - it is a ring, not nothing");
        Assert.True(filled > fallback * 4, $"a filled marker should dwarf the ring: {filled} vs {fallback}");

        // The filled marker is a disc plus its outline, so it must exceed the bare disc.
        Assert.True(filled > System.Math.PI * 16 * 16, $"{filled} px is less than a 32 px disc");
    }

    /// <summary>
    /// **A label may only ADD ink.** If adding one reduces what is drawn, it is covering something.
    /// </summary>
    /// <remarks>
    /// This is the assertion that catches the bug of 2026-08-16, and the shape of it is worth
    /// keeping: nobody would have written "the marker must still be 952 pixels", but everybody can
    /// agree that drawing a word on top of a circle cannot leave *less* on the canvas.
    ///
    /// What it caught: Mapsui's `LabelStyle` ships with `BackColor` = **opaque white**, so every
    /// label painted a solid box over the feature it named. Two hundred markers vanished behind two
    /// hundred white rectangles, and the drawn pixels fell by 71%.
    ///
    /// The composition, not the piece. Every other pixel test here puts one style on the layer; a
    /// patch wires `SymbolStyle → LabelStyle → FeatureLayer` and `LabelStyle` wraps both in a
    /// `StyleCollection`. Testing the piece and shipping the composition is how ten green tests
    /// coexisted with an empty screen.
    /// </remarks>
    [Fact]
    public void Adding_a_label_never_reduces_what_is_drawn()
    {
        var symbol = new SymbolStyleNode().Update(shape: SymbolShape.Ellipse, scale: 0.5f, fillColor: new Color4(0f, 0f, 0f, 1f));
        var alone = PixelsDrawn(symbol);

        var labelled = new LabelStyleNode().Update(symbol, "Name");
        var combined = PixelsDrawn(labelled);

        _out.WriteLine($"symbol alone {alone} px, with a label {combined} px");

        Assert.True(combined > 0, "a symbol inside a StyleCollection must still be drawn");
        Assert.True(combined >= alone,
            $"a label covered the marker: {combined} px with it, {alone} px without. " +
            "Check LabelStyle's BackColor and Halo.");
    }

    // ---------- pixels or ground: why there is no Unit pin ----------

    /// <summary>
    /// **A symbol is sized in pixels and there is no way to size it on the ground** — measured, not
    /// assumed, and this test is what would tell us if that ever changed.
    /// </summary>
    /// <remarks>
    /// Mapsui *looks* like it can: `Mapsui.Styles.UnitType` has `Pixel` and `WorldUnit`, and
    /// `SymbolStyle.UnitType` selects between them. It is inert. The Skia renderer never reads the
    /// property — the string `UnitType` appears **zero times** in `Mapsui.Rendering.Skia.dll` — and
    /// this renders all four combinations to prove it from the pixels rather than from the strings.
    ///
    /// So the `Unit` pin was written, measured, and taken back out. **A pin that does nothing is
    /// worse than no pin**: it is a promise the patch cannot keep, and it fails silently, which is
    /// this repository's whole recurring theme.
    ///
    /// The answer for "I need a circle 500 metres across" is to **buffer the point into a polygon**
    /// and style it as one. It scales with the map because it is on the map.
    ///
    /// **When this test goes red, add the pin.** A newer Mapsui implementing `UnitType` is the only
    /// thing that can break it, and that is exactly the news we would want.
    /// </remarks>
    [Fact]
    public void Mapsuis_world_unit_switch_is_inert_which_is_why_there_is_no_Unit_pin()
    {
        var black = new Color4(0f, 0f, 0f, 1f);

        MapsuiSymbolStyle Marker(global::Mapsui.Styles.UnitType unit) => new()
        {
            SymbolType = MapsuiSymbolType.Rectangle,
            SymbolScale = 1f,
            UnitType = unit,
            Fill = new global::Mapsui.Styles.Brush(Colors.ToMapsui(black)),
        };

        var pixelOut = PixelsDrawn(Marker(global::Mapsui.Styles.UnitType.Pixel), resolution: 1);
        var pixelIn = PixelsDrawn(Marker(global::Mapsui.Styles.UnitType.Pixel), resolution: 0.5);
        var worldOut = PixelsDrawn(Marker(global::Mapsui.Styles.UnitType.WorldUnit), resolution: 1);
        var worldIn = PixelsDrawn(Marker(global::Mapsui.Styles.UnitType.WorldUnit), resolution: 0.5);

        _out.WriteLine($"Pixel    : {pixelOut} px at resolution 1, {pixelIn} px zoomed in 2x");
        _out.WriteLine($"WorldUnit: {worldOut} px at resolution 1, {worldIn} px zoomed in 2x");

        // Zooming in is `resolution` getting smaller - it is map units per pixel. A world-unit
        // symbol would have to quadruple in area here. All four numbers are the same instead.
        Assert.Equal(pixelOut, pixelIn);
        Assert.Equal(pixelOut, worldOut);
        Assert.Equal(pixelOut, worldIn);
    }

    // ---------- the label gets out of the marker's way ----------

    /// <summary>
    /// Non-white pixels inside a box centred on the feature — the marker's own square of canvas.
    /// </summary>
    static int PixelsInsideTheMarker(IStyle style, float scale)
    {
        using var surface = SKSurface.Create(new SKImageInfo(400, 400));
        surface.Canvas.Clear(SKColors.White);

        var point = new GeometryFeature { Geometry = new WKTReader().Read("POINT (0 0)") };
        point["Name"] = "Kyoto";

        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, 1, 0, 400, 400),
            new List<ILayer> { new MemoryLayer { Features = new[] { (IFeature)point }, Style = style } },
            new List<global::Mapsui.Widgets.IWidget>(), global::Mapsui.Styles.Color.White);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        // Scale 1 is 32 px across, so the marker spans centre +- (16 * scale + outline).
        var half = (int)System.Math.Ceiling(MapsuiSymbolStyle.DefaultHeight / 2 * scale) + 2;
        var drawn = 0;
        for (var x = 200 - half; x <= 200 + half; x++)
        for (var y = 200 - half; y <= 200 + half; y++)
            if (bitmap.GetPixel(x, y) != SKColors.White) drawn++;

        return drawn;
    }

    /// <summary>
    /// **Nothing inside the marker changes when a label is added.** This is the assertion the
    /// monotone-ink one is too weak to make.
    /// </summary>
    /// <remarks>
    /// Total ink can rise while the marker is still half buried — a label that overlaps the top of
    /// a disc adds text pixels outside it and removes marker pixels inside it, and the sum can go
    /// either way. Counting only *within the marker's own square* removes the ambiguity: if a
    /// single pixel there differs, something was drawn over the symbol.
    ///
    /// What makes it pass is `Place`: Mapsui centres a label on its feature, and this node lifts it
    /// by the marker's radius plus a gap, using `VerticalAlignment.Bottom` so the clearance does not
    /// depend on the font size.
    /// </remarks>
    [Fact]
    public void A_label_does_not_touch_a_single_pixel_of_its_marker()
    {
        foreach (var scale in new[] { 0.5f, 1f, 2f })
        {
            var symbol = new SymbolStyleNode().Update(shape: SymbolShape.Ellipse, scale: scale, fillColor: new Color4(0f, 0f, 0f, 1f));
            var bare = PixelsInsideTheMarker(symbol, scale);

            var labelled = new LabelStyleNode().Update(symbol, "Name")!;
            var withLabel = PixelsInsideTheMarker(labelled, scale);

            _out.WriteLine($"scale {scale}: {bare} px inside the marker alone, {withLabel} px with a label");

            Assert.True(bare > 0, "the marker itself must draw something");
            Assert.Equal(bare, withLabel);
        }
    }

    /// <summary>
    /// The clearance follows the marker rather than being a constant that happens to fit one size.
    /// </summary>
    [Fact]
    public void A_bigger_marker_pushes_its_label_further_away()
    {
        static double OffsetOf(float scale)
        {
            var symbol = new SymbolStyleNode().Update(shape: SymbolShape.Ellipse, scale: scale);
            var collection = (global::Mapsui.Styles.StyleCollection)new LabelStyleNode().Update(symbol, "Name")!;
            var label = (global::Mapsui.Styles.LabelStyle)collection.Styles.Last();
            return label.Offset.Y;
        }

        var small = OffsetOf(0.5f);
        var large = OffsetOf(2f);

        _out.WriteLine($"scale 0.5 -> offset Y {small}, scale 2 -> offset Y {large}");

        // Negative Y is up - measured 2026-08-16, not assumed.
        Assert.True(small < 0, "the label must move up, away from the marker");
        Assert.True(large < small, $"a 4x marker needs more room: {large} vs {small}");
    }

    /// <summary>
    /// **A polygon's label stays at its centre**, which is where a polygon label belongs.
    /// </summary>
    /// <remarks>
    /// The rule keys on what is actually upstream, not on a guess about the geometry: there is a
    /// marker to clear only when there is a `SymbolStyle` to read. A `VectorStyle` fill, or nothing
    /// at all, leaves Mapsui's centred default alone.
    /// </remarks>
    [Fact]
    public void Without_a_symbol_upstream_the_label_stays_centred()
    {
        var overPolygonFill = (global::Mapsui.Styles.StyleCollection)
            new LabelStyleNode().Update(new VectorStyleNode().Update(), "Name")!;
        var fillCase = (global::Mapsui.Styles.LabelStyle)overPolygonFill.Styles.Last();

        var alone = (global::Mapsui.Styles.LabelStyle)new LabelStyleNode().Update(null, "Name")!;

        foreach (var (label, what) in new[] { (fillCase, "over a VectorStyle"), (alone, "with nothing upstream") })
        {
            _out.WriteLine($"{what}: offset ({label.Offset.X}, {label.Offset.Y}), valign {label.VerticalAlignment}");
            Assert.Equal(0, label.Offset.Y);
            Assert.Equal(global::Mapsui.Styles.LabelStyle.VerticalAlignmentEnum.Center, label.VerticalAlignment);
        }
    }

    // ---------- points only, and what that costs a map holding both ----------

    /// <summary>Non-white pixels for one feature of any WKT, at a scale that fills the canvas.</summary>
    static int PixelsFor(IStyle style, string wkt)
    {
        var f = new GeometryFeature { Geometry = new WKTReader().Read(wkt) };
        f["Name"] = "Kanto";

        using var surface = SKSurface.Create(new SKImageInfo(400, 400));
        surface.Canvas.Clear(SKColors.White);

        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, 1, 0, 400, 400),
            new List<ILayer> { new MemoryLayer { Features = new[] { (IFeature)f }, Style = style } },
            new List<global::Mapsui.Widgets.IWidget>(), global::Mapsui.Styles.Color.White);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var n = 0;
        for (var x = 0; x < bmp.Width; x++)
        for (var y = 0; y < bmp.Height; y++)
            if (bmp.GetPixel(x, y) != SKColors.White) n++;
        return n;
    }

    const string ABox = "POLYGON ((-60 -60, 60 -60, 60 60, -60 60, -60 -60))";

    /// <summary>
    /// **A SymbolStyle draws points and NOTHING else** — not less of a polygon, none of it.
    /// </summary>
    /// <remarks>
    /// Inheritance says otherwise and inheritance is wrong here: Mapsui's `SymbolStyle` derives
    /// from `VectorStyle`, so "it takes VectorStyle's place in the chain" was written into this
    /// node's own documentation and believed for a day. The renderer dispatches on the runtime
    /// type. A raw `Mapsui.Styles.SymbolStyle` behaves identically, so this is the library's shape
    /// rather than something the node sets.
    ///
    /// It cost a real screen: the first patch joining VL.GeoJSON to VL.Mapsui drew five cities and
    /// silently dropped the one polygon in the file (2026-08-16).
    /// </remarks>
    [Fact]
    public void A_symbol_style_draws_no_polygon_at_all()
    {
        var withVector = PixelsFor(new VectorStyleNode().Update(), ABox);
        var withSymbol = PixelsFor(new SymbolStyleNode().Update(), ABox);
        var withNothing = PixelsFor(new global::Mapsui.Styles.VectorStyle(), ABox);

        _out.WriteLine($"POLYGON: VectorStyle {withVector} px, SymbolStyle {withSymbol} px, bare default {withNothing} px");

        Assert.True(withVector > 10000, "a VectorStyle must fill the polygon");
        Assert.Equal(0, withSymbol);
        Assert.True(withNothing > 0, "even no style at all outdraws a SymbolStyle here");
    }

    /// <summary>
    /// <c>StyleByGeometry</c> is what fixes it — see <c>GeometryThemeTests</c> for the whole story.
    /// </summary>
    /// <remarks>
    /// Kept here beside the failure it answers, because the two belong together: the test above
    /// says a `SymbolStyle` erases a polygon, and this says what to do instead. The route that was
    /// tried and rejected in between — a `VectorStyle` stacked underneath — is recorded in
    /// `NOTES.md`, 2026-08-16, along with the two concentric circles it drew on every point.
    /// </remarks>
    [Fact]
    public void Dispatching_by_geometry_type_brings_the_polygon_back()
    {
        var theme = new StyleByGeometryNode().Update(
            point: new SymbolStyleNode().Update(), polygon: new VectorStyleNode().Update());

        var polygon = PixelsFor(theme, ABox);
        var point = PixelsFor(theme, "POINT (0 0)");

        _out.WriteLine($"StyleByGeometry: polygon {polygon} px, point {point} px");

        Assert.True(polygon > 10000, $"the polygon must draw through the theme: {polygon} px");
        Assert.True(point > 0, "and the point must still have its marker");
    }

    /// <summary>
    /// **A nested StyleCollection draws nothing, so the combination has to be flat.**
    /// </summary>
    /// <remarks>
    /// Mapsui's renderer walks a collection's members and does not recurse into a member that is
    /// itself a collection: measured 2026-08-16, a nested `{ { Vector, Symbol }, Label }` put down
    /// 156 pixels over a polygon — the label text and none of the shape — where the flat form drew
    /// 14884. `Styles.Combine` splices instead of wrapping, and this is what says so.
    ///
    /// The chain that found it has since been replaced by `StyleByGeometry`, so nothing in the
    /// package nests today. **That is exactly why the test stays.** It could not have shown up
    /// while chains were two nodes long, and it will not show up again until someone adds a fourth
    /// style node — at which point this fails instead of the screen going quietly blank.
    /// </remarks>
    [Fact]
    public void Combining_stays_flat_however_deep_the_chain()
    {
        var chain = new LabelStyleNode().Update(
            Styles.Combine(new VectorStyleNode().Update(), new SymbolStyleNode().Update()), "Name")!;

        var collection = Assert.IsType<global::Mapsui.Styles.StyleCollection>(chain);
        Assert.All(collection.Styles, s =>
            Assert.False(s is global::Mapsui.Styles.StyleCollection, "a nested collection draws nothing"));

        var polygon = PixelsFor(chain, ABox);
        _out.WriteLine($"Vector -> Symbol -> Label over a polygon: {collection.Styles.Count} flat styles, {polygon} px");

        Assert.Equal(3, collection.Styles.Count);
        Assert.True(polygon > 10000, $"the flattened chain must still fill the polygon: {polygon} px");
    }

    [Fact]
    public void Each_shape_draws_something_and_they_differ()
    {
        var node = new SymbolStyleNode();
        var black = new Color4(0f, 0f, 0f, 1f);

        var ellipse = PixelsDrawn(node.Update(shape: SymbolShape.Ellipse, scale: 1f, fillColor: black));
        var rectangle = PixelsDrawn(node.Update(shape: SymbolShape.Rectangle, scale: 1f, fillColor: black));
        var triangle = PixelsDrawn(node.Update(shape: SymbolShape.Triangle, scale: 1f, fillColor: black));

        _out.WriteLine($"ellipse {ellipse} px, rectangle {rectangle} px, triangle {triangle} px");

        Assert.True(ellipse > 0 && rectangle > 0 && triangle > 0);

        // A square of side s covers more than the circle inscribed in it, which covers more than a
        // triangle in the same box. If two of these were equal the shape pin would not be reaching
        // the renderer.
        Assert.True(rectangle > ellipse, $"a square should cover more than its circle: {rectangle} vs {ellipse}");
        Assert.True(ellipse > triangle, $"a circle should cover more than its triangle: {ellipse} vs {triangle}");
    }
}
