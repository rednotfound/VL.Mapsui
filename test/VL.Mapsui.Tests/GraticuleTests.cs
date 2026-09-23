using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering.Skia;
using SkiaSharp;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// The graticule is generated geometry, so the tests pin the generation rules down: how many
/// lines a spacing produces, which lines the projection cannot show, and that nothing rebuilds
/// per frame. No network anywhere — the node's whole point is a map with no tiles.
/// </summary>
public class GraticuleTests
{
    [Fact]
    public void Ten_degrees_gives_36_meridians_and_17_parallels()
    {
        // Meridians: -180..170 (180 duplicates -180 on a mercator map). Parallels: -80..80 —
        // ±90 cannot exist in WebMercator and ±85.05 is not a multiple of 10.
        using var node = new GraticuleNode();
        var layer = node.Update(out var lines, degreesSpacing: 10);

        Assert.NotNull(layer);
        Assert.Equal(36 + 17, lines);
    }

    [Fact]
    public void The_equator_and_the_antimeridian_are_present_exactly_once()
    {
        using var node = new GraticuleNode();
        node.Update(out var lines, degreesSpacing: 90);

        // Meridians: -180, -90, 0, 90. Parallels: 0 only (±90 unshowable). No double antimeridian.
        Assert.Equal(4 + 1, lines);
    }

    [Fact]
    public void Two_hundred_frames_build_one_layer_and_one_line_set()
    {
        using var node = new GraticuleNode();
        for (int i = 0; i < 200; i++)
            node.Update(out _, degreesSpacing: 10);

        Assert.Equal(1, node.LayersBuilt);
    }

    [Fact]
    public void Changing_the_spacing_rebuilds_the_lines_but_not_the_world()
    {
        using var node = new GraticuleNode();
        node.Update(out var coarse, degreesSpacing: 30);
        node.Update(out var fine, degreesSpacing: 5);

        Assert.True(fine > coarse);
    }

    [Fact]
    public void The_graticule_actually_puts_pixels_on_a_world_view()
    {
        // Counting lines proves generation; only counting PIXELS proves rendering - the lesson of
        // this package's first defect (0 pixels with every readout healthy). World view: the whole
        // mercator square in a 400px canvas.
        using var node = new GraticuleNode();
        var layer = node.Update(out _, degreesSpacing: 10);

        using var surface = SKSurface.Create(new SKImageInfo(400, 400));
        surface.Canvas.Clear(SKColors.White);
        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, 20037508.34 * 2 / 400, 0, 400, 400),
            new List<ILayer> { layer! }, new List<global::Mapsui.Widgets.IWidget>(),
            global::Mapsui.Styles.Color.White);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var drawn = 0;
        for (var x = 0; x < bitmap.Width; x++)
        for (var y = 0; y < bitmap.Height; y++)
            if (bitmap.GetPixel(x, y) != SKColors.White) drawn++;

        // 36 meridians + 17 parallels across 400px should paint thousands of pixels, not zero.
        Assert.True(drawn > 2000, $"only {drawn} pixels drawn");
    }

    [Fact]
    public void Labels_add_pixels_and_the_toggle_removes_them()
    {
        // The label text rides point features dispatched by StyleByGeometry; only pixels prove
        // the dispatch reached the renderer (a nested StyleCollection renders nothing - this
        // package's third defect, and the reason this is a pixel test).
        var on = Pixels(showLabels: true);
        var off = Pixels(showLabels: false);
        Assert.True(on > off + 500, $"labels on: {on}, off: {off}");
    }

    static int Pixels(bool showLabels)
    {
        using var node = new GraticuleNode();
        var layer = node.Update(out _, degreesSpacing: 30, showLabels: showLabels);

        using var surface = SKSurface.Create(new SKImageInfo(400, 400));
        surface.Canvas.Clear(SKColors.White);
        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, 20037508.34 * 2 / 400, 0, 400, 400),
            new List<ILayer> { layer! }, new List<global::Mapsui.Widgets.IWidget>(),
            global::Mapsui.Styles.Color.White);
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var drawn = 0;
        for (var x = 0; x < bitmap.Width; x++)
        for (var y = 0; y < bitmap.Height; y++)
            if (bitmap.GetPixel(x, y) != SKColors.White) drawn++;
        return drawn;
    }

    [Fact]
    public void A_nonpositive_spacing_is_no_layer_not_an_infinite_one()
    {
        using var node = new GraticuleNode();
        Assert.Null(node.Update(out var lines, degreesSpacing: 0));
        Assert.Equal(0, lines);
        Assert.Null(node.Update(out _, degreesSpacing: -10));
    }
}
