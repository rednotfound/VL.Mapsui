using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering.Skia;
using SkiaSharp;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// The graticule computes its lines from the view it is asked for — that is the whole design
/// since defect nine (NOTES.md 2026-09-24: a world-sized feature set at a fine spacing built
/// 24.5 million labels and froze the process). So the tests ask views: a world view, a city
/// view, the same view twice. No network anywhere — the node's whole point is a map with no
/// tiles.
/// </summary>
public class GraticuleTests
{
    // A hair wider than the world, the way a real world viewport is; the layer clamps.
    static readonly MRect WorldView = new(-20037509, -20037509, 20037509, 20037509);

    /// <summary>A view a few kilometres across, centred on Haneda — the box that found defect nine.</summary>
    static MRect CityView()
    {
        var (x, y) = global::Mapsui.Projections.SphericalMercator.FromLonLat(139.78, 35.55);
        return new MRect(x - 4000, y - 3000, x + 4000, y + 3000);
    }

    static ILayer Layer(GraticuleNode node, double spacing, bool labels = true)
    {
        var layer = node.Update(out _, degreesSpacing: spacing, showLabels: labels);
        Assert.NotNull(layer);
        return layer!;
    }

    [Fact]
    public void Ten_degrees_over_a_world_view_is_36_meridians_and_17_parallels()
    {
        // Meridians: -180..170 (180 duplicates -180 on a mercator map). Parallels: -80..80 —
        // ±90 cannot exist in WebMercator and ±85.05 is not a multiple of 10.
        using var node = new GraticuleNode();
        Layer(node, 10).GetFeatures(WorldView, 100000).ToArray();
        Assert.Equal(36 + 17, node.LinesBuilt);
    }

    [Fact]
    public void The_equator_and_the_antimeridian_are_present_exactly_once()
    {
        using var node = new GraticuleNode();
        Layer(node, 90).GetFeatures(WorldView, 100000).ToArray();
        // Meridians: -180, -90, 0, 90. Parallels: 0 only (±90 unshowable). No double antimeridian.
        Assert.Equal(4 + 1, node.LinesBuilt);
    }

    [Fact]
    public void The_view_decides_what_exists_so_a_fine_spacing_is_cheap_where_it_is_fine()
    {
        // Defect nine's own numbers: 0.05 degrees world-wide was 7,200 x ~3,400 lines and 24.5
        // million labels. Over the city view that spacing actually serves, it is a handful of
        // lines and a handful of labels.
        using var node = new GraticuleNode();
        var features = Layer(node, 0.05).GetFeatures(CityView(), 10).ToArray();

        Assert.InRange(node.LinesBuilt, 2, 20);
        Assert.InRange(node.LabelsBuilt, 1, 100);
        Assert.Equal(node.LinesBuilt + node.LabelsBuilt, features.Length);
        Assert.Equal(0.05, node.EffectiveSpacing);
    }

    [Fact]
    public void A_fine_spacing_over_a_world_view_coarsens_up_the_ladder_instead_of_exploding()
    {
        using var node = new GraticuleNode();
        Layer(node, 0.05).GetFeatures(WorldView, 100000).ToArray();

        Assert.True(node.EffectiveSpacing > 0.05, $"still {node.EffectiveSpacing}");
        Assert.InRange(node.LinesBuilt, 2, 100);
    }

    [Fact]
    public void Auto_spacing_follows_the_zoom()
    {
        using var node = new GraticuleNode();
        var layer = Layer(node, 0);

        layer.GetFeatures(WorldView, 100000).ToArray();
        var world = node.EffectiveSpacing;

        layer.GetFeatures(CityView(), 10).ToArray();
        var city = node.EffectiveSpacing;

        Assert.Equal(30, world);           // 360/6 = 60 wants the ladder's coarse end
        Assert.InRange(city, 0.005, 0.05); // a few kilometres of view wants hundredths
        Assert.True(city < world);
    }

    [Fact]
    public void Labels_build_where_the_view_can_read_them_and_step_aside_where_it_cannot()
    {
        using var node = new GraticuleNode();
        var layer = Layer(node, 30);
        layer.GetFeatures(WorldView, 100000).ToArray();
        Assert.Equal(12 * 5, node.LabelsBuilt);   // an atlas view, labelled: parallels -60..60

        Layer(node, 10);                          // 36 x 17 = 612 crossings: past the cap
        layer.GetFeatures(WorldView, 100000).ToArray();
        Assert.Equal(0, node.LabelsBuilt);
        Assert.Equal(36 + 17, node.LinesBuilt);   // the lines still draw
    }

    [Fact]
    public void Two_hundred_frames_are_one_layer_and_the_same_view_is_computed_once()
    {
        using var node = new GraticuleNode();
        var first = Layer(node, 10);
        for (int i = 0; i < 200; i++)
            Assert.Same(first, Layer(node, 10));
        Assert.Equal(1, node.LayersBuilt);

        var a = first.GetFeatures(WorldView, 100000);
        var b = first.GetFeatures(new MRect(WorldView.Min.X, WorldView.Min.Y, WorldView.Max.X, WorldView.Max.Y), 100000);
        Assert.Same(a, b);                        // the memo, not a rebuild

        Layer(node, 5);                           // a new spacing invalidates it
        var c = first.GetFeatures(WorldView, 100000);
        Assert.NotSame(a, c);
    }

    [Fact]
    public void A_negative_spacing_is_no_layer_and_zero_means_automatic()
    {
        using var node = new GraticuleNode();
        Assert.Null(node.Update(out var lines, degreesSpacing: -10));
        Assert.Equal(0, lines);
        Assert.NotNull(node.Update(out _, degreesSpacing: 0));
    }

    [Fact]
    public void The_graticule_actually_puts_pixels_on_a_world_view()
    {
        // Counting lines proves generation; only counting PIXELS proves rendering - the lesson of
        // this package's first defect (0 pixels with every readout healthy). This also proves the
        // real renderer reaches GetFeatures with the viewport's own box.
        var drawn = Pixels(spacing: 10, showLabels: false);
        Assert.True(drawn > 2000, $"only {drawn} pixels drawn");
    }

    [Fact]
    public void Labels_add_pixels_and_the_toggle_removes_them()
    {
        // The label text rides point features dispatched by StyleByGeometry; only pixels prove
        // the dispatch reached the renderer (a nested StyleCollection renders nothing - this
        // package's third defect, and the reason this is a pixel test).
        var on = Pixels(spacing: 30, showLabels: true);
        var off = Pixels(spacing: 30, showLabels: false);
        Assert.True(on > off + 500, $"labels on: {on}, off: {off}");
    }

    static int Pixels(double spacing, bool showLabels)
    {
        using var node = new GraticuleNode();
        var layer = node.Update(out _, degreesSpacing: spacing, showLabels: showLabels);

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
}
