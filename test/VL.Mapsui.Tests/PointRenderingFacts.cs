using System.Collections.Generic;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using ILayer = global::Mapsui.Layers.ILayer;
using IFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using VectorStyle = global::Mapsui.Styles.VectorStyle;
using SymbolStyle = global::Mapsui.Styles.SymbolStyle;
using IWidget = global::Mapsui.Widgets.IWidget;
using Color = global::Mapsui.Styles.Color;
using Viewport = global::Mapsui.Viewport;
using WKTReader = NetTopologySuite.IO.WKTReader;

namespace VL.Mapsui.Tests;

/// <summary>
/// Whether a bare POINT is actually drawn, and by which style — the claim in
/// docs/MAPSUI-SURFACE.md, measured instead of assumed.
/// </summary>
/// <remarks>
/// **What this proves and what it does not.** Rendering onto an SKSurface says nothing about VL.Skia's
/// coordinate space — that false proof has been made on this stack once already. It is honest for
/// exactly one question: given a viewport and a layer, does Mapsui's renderer put any pixels down at
/// all? Which is the question the doc was answering from memory.
/// </remarks>
public class PointRenderingFacts
{
    readonly ITestOutputHelper _out;
    public PointRenderingFacts(ITestOutputHelper output) => _out = output;

    static int PixelsDrawn(ILayer layer)
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        new MapRenderer().Render(
            canvas,
            new Viewport(0, 0, 1, 0, 200, 200),
            new List<ILayer> { layer },
            new List<IWidget>(),
            Color.White);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        var drawn = 0;
        for (var x = 0; x < bitmap.Width; x++)
        for (var y = 0; y < bitmap.Height; y++)
            if (bitmap.GetPixel(x, y) != SKColors.White) drawn++;

        return drawn;
    }

    static MemoryLayer PointWith(global::Mapsui.Styles.IStyle style)
    {
        var feature = new GeometryFeature { Geometry = new WKTReader().Read("POINT (0 0)") };
        return new MemoryLayer { Features = new[] { (IFeature)feature }, Style = style };
    }

    [Fact]
    public void A_point_is_drawn_with_a_SymbolStyle()
    {
        var drawn = PixelsDrawn(PointWith(new SymbolStyle()));
        _out.WriteLine($"SymbolStyle -> {drawn} pixels");
        Assert.True(drawn > 0, "a SymbolStyle should mark a point");
    }

    /// <summary>
    /// A point is marked with a VectorStyle too — which is the opposite of what
    /// docs/MAPSUI-SURFACE.md claimed until this test was written.
    /// </summary>
    /// <remarks>
    /// Both styles put down **the same 180 pixels**, which says what is really happening: Mapsui's
    /// point renderer wants a `SymbolStyle` and falls back to a default one rather than drawing
    /// nothing. So `SymbolStyle` is still worth wrapping — it is how you choose the marker — but not
    /// for the reason that was written down. A point is visible today.
    ///
    /// The claim it replaces was written from memory about how Mapsui's renderers divide the work.
    /// It was plausible, it was in a document that exists to be trusted, and it was wrong.
    /// </remarks>
    [Fact]
    public void A_point_is_marked_with_a_VectorStyle_as_well()
    {
        var withVector = PixelsDrawn(PointWith(new VectorStyle()));
        var withSymbol = PixelsDrawn(PointWith(new SymbolStyle()));

        _out.WriteLine($"VectorStyle -> {withVector} pixels, SymbolStyle -> {withSymbol} pixels");

        Assert.True(withVector > 0, "a point with a VectorStyle is drawn, not invisible");
        Assert.Equal(withSymbol, withVector);
    }

    [Fact]
    public void A_polygon_is_drawn_with_a_VectorStyle()
    {
        var feature = new GeometryFeature
        {
            Geometry = new WKTReader().Read("POLYGON ((-50 -50, 50 -50, 50 50, -50 50, -50 -50))"),
        };
        var layer = new MemoryLayer { Features = new[] { (IFeature)feature }, Style = new VectorStyle() };

        var drawn = PixelsDrawn(layer);
        _out.WriteLine($"VectorStyle on a polygon -> {drawn} pixels");
        Assert.True(drawn > 0, "the control: a VectorStyle definitely draws a polygon");
    }
}
