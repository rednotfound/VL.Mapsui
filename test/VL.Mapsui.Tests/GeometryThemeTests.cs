using System.Collections.Generic;
using SkiaSharp;
using Stride.Core.Mathematics;
using Xunit;
using Xunit.Abstractions;

using IStyle = global::Mapsui.Styles.IStyle;
using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using ILayer = global::Mapsui.Layers.ILayer;
using IFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using Viewport = global::Mapsui.Viewport;
using WKTReader = NetTopologySuite.IO.WKTReader;

namespace VL.Mapsui.Tests;

/// <summary>
/// One style per geometry type — the thing every mapping library does and this package did not.
/// </summary>
/// <remarks>
/// These are pixel tests rather than object tests, and deliberately so. `StyleByGeometry` wraps
/// `Mapsui.Styles.Thematics.ThemeStyle`, whose name appears **zero times** in
/// `Mapsui.Rendering.Skia.dll` — the same reading as `LabelColumn`, which works, and as
/// `UnitType`, which is inert. Only a render tells those apart, and by now that has cost enough
/// that the render is the test.
/// </remarks>
public class GeometryThemeTests
{
    readonly ITestOutputHelper _out;
    public GeometryThemeTests(ITestOutputHelper output) => _out = output;

    const string Box = "POLYGON ((-60 -60, 60 -60, 60 60, -60 60, -60 -60))";
    const string Dot = "POINT (0 0)";
    const string Line = "LINESTRING (-80 -80, 80 80)";

    /// <summary>Pixels drawn, and the width of what sits on the centre row.</summary>
    static (int Count, int Width) Draw(IStyle style, string wkt)
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
        int count = 0, minX = 9999, maxX = -1;
        for (var x = 0; x < 400; x++)
        {
            if (bmp.GetPixel(x, 200) != SKColors.White) { if (x < minX) minX = x; if (x > maxX) maxX = x; }
            for (var y = 0; y < 400; y++) if (bmp.GetPixel(x, y) != SKColors.White) count++;
        }
        return (count, maxX < 0 ? 0 : maxX - minX + 1);
    }

    static IStyle Symbol(float scale = 0.6f) =>
        new SymbolStyleNode().Update(shape: SymbolShape.Ellipse, scale: scale,
            fillColor: new Color4(0f, 0f, 0f, 1f));

    static IStyle Fill() => new VectorStyleNode().Update();

    /// <summary>
    /// **Each geometry type draws exactly what its own style draws — no more, no less.**
    /// </summary>
    /// <remarks>
    /// Asserted against the style used alone rather than against a constant, so the test says
    /// "dispatch changed nothing" instead of "the number is 14884". It survives a SkiaSharp bump;
    /// a hardcoded count would not.
    /// </remarks>
    [Fact]
    public void Every_geometry_type_gets_the_style_it_was_given()
    {
        var symbol = Symbol();
        var fill = Fill();
        var theme = new StyleByGeometryNode().Update(point: symbol, line: fill, polygon: fill);

        foreach (var (wkt, alone, what) in new[]
        {
            (Dot,  symbol, "POINT"),
            (Line, fill,   "LINESTRING"),
            (Box,  fill,   "POLYGON"),
        })
        {
            var viaTheme = Draw(theme, wkt);
            var direct = Draw(alone, wkt);
            _out.WriteLine($"{what,-11} via theme {viaTheme.Count,6} px   direct {direct.Count,6} px");

            Assert.True(direct.Count > 0, $"{what} must draw something with its own style");
            Assert.Equal(direct.Count, viaTheme.Count);
        }
    }

    /// <summary>
    /// **A point gets ONE marker, and Scale means what it says again.**
    /// </summary>
    /// <remarks>
    /// This is the assertion the whole round is for. The previous design rescued polygons by
    /// stacking a `VectorStyle` under the `SymbolStyle`, and a `VectorStyle` draws its own
    /// 32-pixel circle on a point: two concentric markers, and `Scale 0.6` measuring 34 pixels
    /// across instead of 22 because the default underneath was the wider of the two. Dispatch
    /// draws each feature once.
    /// </remarks>
    [Fact]
    public void A_point_is_drawn_once_and_at_the_scale_that_was_asked_for()
    {
        var small = new StyleByGeometryNode().Update(point: Symbol(0.6f), polygon: Fill());
        var large = new StyleByGeometryNode().Update(point: Symbol(2f), polygon: Fill());

        var narrow = Draw(small, Dot);
        var wide = Draw(large, Dot);

        _out.WriteLine($"scale 0.6 -> {narrow.Width} px wide ({narrow.Count} px)");
        _out.WriteLine($"scale 2   -> {wide.Width} px wide ({wide.Count} px)");

        // 0.6 x 32 is 19 plus a 2 px outline. Stacked under a VectorStyle it measured 34 - the
        // plain default - which is the artifact this asserts is gone.
        Assert.InRange(narrow.Width, 18, 26);
        Assert.True(wide.Width > narrow.Width * 2, $"scale must still reach the screen: {narrow.Width} -> {wide.Width}");
    }

    /// <summary>An unwired pin draws nothing for that type, and does not throw.</summary>
    /// <remarks>
    /// Measured 2026-08-16: Mapsui accepts a null from `GetStyle` and simply draws nothing. Which
    /// is a silent disappearance, so `FeatureLayer`'s `Status` is what has to catch it — see
    /// <c>FeatureLayerStatusTests</c>.
    /// </remarks>
    [Fact]
    public void An_unwired_geometry_type_draws_nothing_rather_than_throwing()
    {
        var pointsOnly = new StyleByGeometryNode().Update(point: Symbol());

        Assert.Equal(0, Draw(pointsOnly, Box).Count);
        Assert.True(Draw(pointsOnly, Dot).Count > 0, "the type that IS wired still draws");
    }

    /// <summary>
    /// The label still lifts clear of the marker when the marker is behind a theme.
    /// </summary>
    /// <remarks>
    /// The regression this design could have caused. `LabelStyle` works out its offset by finding
    /// the `SymbolStyle` upstream, and a `ThemeStyle` is a function — opaque. `GeometryTheme`
    /// therefore carries its three styles as properties, and this is what says that mattered.
    /// </remarks>
    [Fact]
    public void A_label_still_clears_a_marker_that_is_inside_a_theme()
    {
        var theme = new StyleByGeometryNode().Update(point: Symbol(2f), polygon: Fill());
        var labelled = new LabelStyleNode().Update(theme, "Name")!;

        var collection = (global::Mapsui.Styles.StyleCollection)labelled;
        var label = (global::Mapsui.Styles.LabelStyle)collection.Styles[^1];

        _out.WriteLine($"offset ({label.Offset.X}, {label.Offset.Y}), valign {label.VerticalAlignment}");

        Assert.True(label.Offset.Y < 0, "the label must be lifted above the marker");
        Assert.Equal(global::Mapsui.Styles.LabelStyle.VerticalAlignmentEnum.Bottom, label.VerticalAlignment);
    }

    // ---------- identity ----------

    [Fact]
    public void A_hundred_frames_with_unchanged_inputs_build_one_theme()
    {
        var node = new StyleByGeometryNode();
        var symbol = Symbol();
        var fill = Fill();
        IStyle? last = null;

        for (var frame = 0; frame < 100; frame++) last = node.Update(symbol, fill, fill);

        Assert.Equal(1, node.StylesBuilt);
        Assert.Same(last, node.Update(symbol, fill, fill));
    }

    [Fact]
    public void Changing_any_of_the_three_builds_a_new_theme()
    {
        var node = new StyleByGeometryNode();
        var symbol = Symbol();
        var fill = Fill();

        node.Update(symbol, fill, fill);
        node.Update(Symbol(2f), fill, fill);   // point
        node.Update(Symbol(2f), null, fill);   // line
        node.Update(Symbol(2f), null, null);   // polygon

        Assert.Equal(4, node.StylesBuilt);
    }
}
