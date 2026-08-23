using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using Stride.Core.Mathematics;
using Xunit;
using Xunit.Abstractions;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiVectorStyle = global::Mapsui.Styles.VectorStyle;
using MapsuiSymbolStyle = global::Mapsui.Styles.SymbolStyle;
using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using ILayer = global::Mapsui.Layers.ILayer;
using IFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using Viewport = global::Mapsui.Viewport;
using WKTReader = NetTopologySuite.IO.WKTReader;
using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;

namespace VL.Mapsui.Tests;

/// <summary>
/// Styling by a numeric attribute — the choropleth mechanism.
/// </summary>
/// <remarks>
/// Three kinds of assertion. Identity ones say the node does not churn and that the interpolated
/// styles are a bounded set (the render cache is keyed on style objects, so an unbounded stream of
/// fresh ones is a leak). Behaviour ones say what a feature gets for its value — including the
/// three ways a feature earns NOTHING, which raw Mapsui answers with a silent 0 or a mid-render
/// exception. And the pixel one says the whole thing reaches the screen, because a style object
/// with the right fields proves nothing if the renderer ignores it.
/// </remarks>
public class ValueThemeTests
{
    readonly ITestOutputHelper _out;
    public ValueThemeTests(ITestOutputHelper output) => _out = output;

    const string ABox = "POLYGON ((-60 -60, 60 -60, 60 60, -60 60, -60 -60))";

    static IStyle Red() => new VectorStyleNode().Update(
        fillColor: new Color4(1f, 0f, 0f, 1f), lineColor: new Color4(1f, 0f, 0f, 1f), lineWidth: 1f);

    static IStyle Blue() => new VectorStyleNode().Update(
        fillColor: new Color4(0f, 0f, 1f, 1f), lineColor: new Color4(0f, 0f, 1f, 1f), lineWidth: 5f);

    static ValueTheme Theme(string attribute = "pop", double min = 0, double max = 100,
        IStyle? minStyle = null, IStyle? maxStyle = null)
        => (ValueTheme)new StyleByValueNode().Update(attribute, min, max, minStyle ?? Red(), maxStyle ?? Blue());

    static IFeature Feat(object? pop)
    {
        var f = new GeometryFeature { Geometry = new WKTReader().Read(ABox) };
        if (pop is not null) f["pop"] = pop;
        return f;
    }

    static MapsuiVectorStyle StyleFor(ValueTheme theme, object? pop)
        => (MapsuiVectorStyle)theme.GetStyle(Feat(pop))!;

    // ---------- what a value earns ----------

    [Fact]
    public void The_ends_get_the_end_styles_and_the_middle_gets_a_blend()
    {
        var theme = Theme();

        var low = StyleFor(theme, 0.0);
        var mid = StyleFor(theme, 50.0);
        var high = StyleFor(theme, 100.0);

        _out.WriteLine($"fill R: low {low.Fill!.Color!.R}, mid {mid.Fill!.Color!.R}, high {high.Fill!.Color!.R}");
        _out.WriteLine($"line width: low {low.Line!.Width}, mid {mid.Line!.Width}, high {high.Line!.Width}");

        // The invariant is monotony, not exact numbers: red gives way to blue as the value rises.
        Assert.True(low.Fill!.Color!.R > mid.Fill!.Color!.R && mid.Fill.Color.R > high.Fill!.Color!.R,
            "red must fall as the value rises");
        Assert.True(low.Fill.Color.B < mid.Fill.Color.B && mid.Fill.Color.B < high.Fill!.Color!.B,
            "blue must rise as the value rises");
        Assert.True(low.Line!.Width < mid.Line!.Width && mid.Line.Width < high.Line!.Width,
            "line width must widen as the value rises");
    }

    [Fact]
    public void Values_outside_the_range_clamp_to_the_end_styles()
    {
        var theme = Theme(min: 0, max: 100);

        Assert.Equal(StyleFor(theme, 0.0).Fill!.Color, StyleFor(theme, -999.0).Fill!.Color);
        Assert.Equal(StyleFor(theme, 100.0).Fill!.Color, StyleFor(theme, 5000.0).Fill!.Color);
    }

    [Fact]
    public void An_empty_range_draws_everything_with_the_min_style()
    {
        // Mapsui's own Fraction() divides zero by zero here and the NaN becomes a transparent
        // colour - invisible features with every pin wired. A range of one value means one style.
        var red = Red();
        var theme = (ValueTheme)new StyleByValueNode().Update("pop", 42, 42, red, Blue());

        Assert.Same(red, theme.GetStyle(Feat(42.0)));
        Assert.Same(red, theme.GetStyle(Feat(7.0)));
        Assert.Null(theme.GetStyle(Feat(null)));
    }

    // ---------- the three ways a feature earns nothing ----------

    [Fact]
    public void A_missing_attribute_is_not_drawn_rather_than_styled_as_zero()
    {
        // Raw Mapsui reads Convert.ToDouble(null) = 0 and quietly hands out the Min style - a
        // wrong answer that looks like data. Absent means absent.
        Assert.Null(Theme().GetStyle(Feat(null)));
    }

    [Fact]
    public void A_non_numeric_attribute_is_not_drawn_and_nothing_throws()
    {
        // Raw Mapsui throws mid-render for this one.
        Assert.Null(Theme().GetStyle(Feat("not a number")));
    }

    [Fact]
    public void Mismatched_style_kinds_draw_nothing_instead_of_throwing()
    {
        // Raw Mapsui: ArgumentException per feature, in the render loop.
        var theme = (ValueTheme)new StyleByValueNode().Update("pop", 0, 100, Red(), new MapsuiSymbolStyle());
        Assert.Null(theme.GetStyle(Feat(50.0)));
    }

    [Fact]
    public void Numeric_strings_count_as_numeric()
    {
        // GeoJSON files carry numbers as strings often enough that refusing them would read as
        // data loss. Invariant culture: "3.5" is three and a half on every machine.
        var theme = Theme();
        Assert.NotNull(theme.GetStyle(Feat("50")));
        Assert.Equal(StyleFor(theme, 50.0).Fill!.Color, StyleFor(theme, "50").Fill!.Color);
    }

    // ---------- identity, which is what the render cache keys on ----------

    [Fact]
    public void The_same_value_gets_the_same_style_object_and_the_set_is_bounded()
    {
        var theme = Theme();

        Assert.Same(theme.GetStyle(Feat(37.0)), theme.GetStyle(Feat(37.0)));

        // Sweep far more values than there are steps: the distinct style objects must stay at or
        // below the step count, or every frame of a live map mints fresh identities and the
        // renderer's per-style caches grow without bound (rule 12).
        var distinct = Enumerable.Range(0, 1000)
            .Select(i => theme.GetStyle(Feat(i / 10.0)))
            .Distinct()
            .Count();

        _out.WriteLine($"{distinct} distinct styles over 1000 values (cap {ValueTheme.Steps})");
        Assert.InRange(distinct, 2, ValueTheme.Steps);
    }

    [Fact]
    public void The_node_keeps_its_theme_while_pins_are_unchanged()
    {
        var node = new StyleByValueNode();
        var red = Red();
        var blue = Blue();

        var first = node.Update("pop", 0, 100, red, blue);
        var second = node.Update("pop", 0, 100, red, blue);

        Assert.Same(first, second);
        Assert.Equal(1, node.StylesBuilt);

        node.Update("pop", 0, 200, red, blue);
        Assert.Equal(2, node.StylesBuilt);
    }

    // ---------- status, where the features and the style meet ----------

    static NtsFeature Nts(string wkt, AttributesTable? attributes = null)
        => new(new WKTReader().Read(wkt), attributes ?? new AttributesTable());

    [Fact]
    public void Status_names_an_unwired_pin()
    {
        using var layer = new FeatureLayerNode();
        var theme = (ValueTheme)new StyleByValueNode().Update("pop", 0, 100, minStyle: Red(), maxStyle: null);

        layer.Update(out _, out var status, new[] { Nts(ABox) }, theme);

        _out.WriteLine(status);
        Assert.Contains("Max Style", status);
        Assert.Contains("NOTHING", status);
    }

    [Fact]
    public void Status_counts_features_whose_attribute_is_missing_or_not_numeric()
    {
        using var layer = new FeatureLayerNode();
        var theme = Theme();

        var readable = Nts(ABox, new AttributesTable { { "pop", 12.0 } });
        var missing = Nts(ABox);
        var text = Nts(ABox, new AttributesTable { { "pop", "twelve" } });

        layer.Update(out _, out var status, new[] { readable, missing, text }, theme);

        _out.WriteLine(status);
        Assert.Contains("2 of them", status);
        Assert.Contains("pop", status);
    }

    [Fact]
    public void Status_sees_a_value_theme_hiding_inside_style_by_geometry()
    {
        using var layer = new FeatureLayerNode();
        var inner = (ValueTheme)new StyleByValueNode().Update("", 0, 100, Red(), Blue());
        var outer = new StyleByGeometryNode().Update(polygon: inner);

        layer.Update(out _, out var status, new[] { Nts(ABox) }, outer);

        _out.WriteLine(status);
        Assert.Contains("Attribute pin is empty", status);
    }

    // ---------- pixels, because a style object proves nothing about the screen ----------

    static SKColor CentrePixel(IStyle style, double pop)
    {
        var f = new GeometryFeature { Geometry = new WKTReader().Read(ABox) };
        f["pop"] = pop;

        using var surface = SKSurface.Create(new SKImageInfo(400, 400));
        surface.Canvas.Clear(SKColors.White);

        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, 1, 0, 400, 400),
            new List<ILayer> { new MemoryLayer { Features = new[] { (IFeature)f }, Style = style } },
            new List<global::Mapsui.Widgets.IWidget>(), global::Mapsui.Styles.Color.White);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        return bmp.GetPixel(200, 200);
    }

    [Fact]
    public void The_rendered_colour_follows_the_value()
    {
        var theme = Theme(min: 0, max: 100);

        var low = CentrePixel(theme, 0);
        var mid = CentrePixel(theme, 50);
        var high = CentrePixel(theme, 100);

        _out.WriteLine($"low {low}, mid {mid}, high {high}");

        Assert.True(low.Red > 200 && low.Blue < 60, $"the low end must render red, got {low}");
        Assert.True(high.Blue > 200 && high.Red < 60, $"the high end must render blue, got {high}");
        Assert.True(low.Red > mid.Red && mid.Red > high.Red, "red must fall with the value on screen too");
        Assert.True(low.Blue < mid.Blue && mid.Blue < high.Blue, "blue must rise with the value on screen too");
    }

    [Fact]
    public void A_feature_that_earns_nothing_puts_down_no_ink()
    {
        var theme = Theme();

        var f = new GeometryFeature { Geometry = new WKTReader().Read(ABox) };   // no attribute at all

        using var surface = SKSurface.Create(new SKImageInfo(400, 400));
        surface.Canvas.Clear(SKColors.White);
        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, 1, 0, 400, 400),
            new List<ILayer> { new MemoryLayer { Features = new[] { (IFeature)f }, Style = theme } },
            new List<global::Mapsui.Widgets.IWidget>(), global::Mapsui.Styles.Color.White);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var inked = 0;
        for (var x = 0; x < bmp.Width; x++)
        for (var y = 0; y < bmp.Height; y++)
            if (bmp.GetPixel(x, y) != SKColors.White) inked++;

        Assert.Equal(0, inked);
    }

    // ---------- symbols, the graduated-size half ----------

    [Fact]
    public void Between_two_symbol_styles_the_scale_interpolates()
    {
        var small = new MapsuiSymbolStyle { SymbolScale = 1 };
        var large = new MapsuiSymbolStyle { SymbolScale = 3 };
        var theme = (ValueTheme)new StyleByValueNode().Update("pop", 0, 100, small, large);

        var lowScale = ((MapsuiSymbolStyle)theme.GetStyle(Feat(0.0))!).SymbolScale;
        var midScale = ((MapsuiSymbolStyle)theme.GetStyle(Feat(50.0))!).SymbolScale;
        var highScale = ((MapsuiSymbolStyle)theme.GetStyle(Feat(100.0))!).SymbolScale;

        _out.WriteLine($"scales: {lowScale}, {midScale}, {highScale}");
        Assert.True(lowScale < midScale && midScale < highScale, "the symbol must grow with the value");
    }
}
