using System.Collections.Generic;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

using ILayer = global::Mapsui.Layers.ILayer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using IFeature = global::Mapsui.IFeature;
using GeometryFeature = global::Mapsui.Nts.GeometryFeature;
using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using Viewport = global::Mapsui.Viewport;
using WKTReader = NetTopologySuite.IO.WKTReader;
using GlobalSphericalMercator = BruTile.Predefined.GlobalSphericalMercator;

namespace VL.Mapsui.Tests;

/// <summary>
/// A layer drawn only between two zoom levels.
/// </summary>
/// <remarks>
/// Pixel tests, because `MinVisible` and `MaxVisible` read exactly like dead properties: the string
/// `get_MinVisible` occurs **zero times** in `Mapsui.Rendering.Skia.dll`, which is also true of
/// `LabelColumn` (works) and of `UnitType` (does nothing at all). Three properties have now been
/// wrong about themselves in this library, so the render is the test.
/// </remarks>
public class VisibleRangeTests
{
    readonly ITestOutputHelper _out;
    public VisibleRangeTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// A square a thousand kilometres across, so it fills the viewport at every zoom tested.
    /// </summary>
    /// <remarks>
    /// The first version was 300 map units wide and drew **four pixels** at zoom 4 — smaller than
    /// one pixel of ground at that resolution. The assertions passed, but on a number that
    /// antialiasing could have taken to zero at any time. A visibility test has to fail because the
    /// layer is hidden, never because the shape got too small to see.
    /// </remarks>
    static MemoryLayer BigSquare()
    {
        var f = new GeometryFeature
        {
            Geometry = new WKTReader().Read(
                "POLYGON ((-1000000 -1000000, 1000000 -1000000, 1000000 1000000, -1000000 1000000, -1000000 -1000000))"),
        };
        return new MemoryLayer
        {
            Features = new[] { (IFeature)f },
            Style = new VectorStyleNode().Update(),
        };
    }

    /// <summary>Non-white pixels with the viewport at the resolution of a zoom level.</summary>
    static int DrawnAtZoom(ILayer layer, int zoomLevel)
    {
        var resolution = new GlobalSphericalMercator().Resolutions[0].UnitsPerPixel
                       / System.Math.Pow(2, zoomLevel);

        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        surface.Canvas.Clear(SKColors.White);
        new MapRenderer().Render(
            surface.Canvas, new Viewport(0, 0, resolution, 0, 200, 200),
            new List<ILayer> { layer }, new List<global::Mapsui.Widgets.IWidget>(),
            global::Mapsui.Styles.Color.White);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var n = 0;
        for (var x = 0; x < 200; x++)
        for (var y = 0; y < 200; y++)
            if (bmp.GetPixel(x, y) != SKColors.White) n++;
        return n;
    }

    /// <summary>
    /// **Both named levels are inside the range, and the ones next to them are outside.**
    /// </summary>
    /// <remarks>
    /// The reason each end is widened by half a level. Setting `MaxVisible` to exactly the
    /// resolution of `From Zoom` would put that level on the boundary, and whether Mapsui's
    /// comparison is inclusive is not a thing a patch author should have to find out by zooming.
    /// </remarks>
    [Fact]
    public void The_layer_draws_at_both_ends_of_the_range_and_not_outside_it()
    {
        var layer = BigSquare();
        VisibleRangeNodes.VisibleRange(layer, out var status, fromZoom: 4, toZoom: 8);
        _out.WriteLine($"status: {status}");

        foreach (var zoom in new[] { 3, 4, 6, 8, 9 })
            _out.WriteLine($"  zoom {zoom} -> {DrawnAtZoom(layer, zoom)} px");

        Assert.Equal(0, DrawnAtZoom(layer, 3));
        Assert.True(DrawnAtZoom(layer, 4) > 0, "the FROM level must be inside the range");
        Assert.True(DrawnAtZoom(layer, 6) > 0, "and so must the middle");
        Assert.True(DrawnAtZoom(layer, 8) > 0, "the TO level must be inside the range");
        Assert.Equal(0, DrawnAtZoom(layer, 9));
    }

    /// <summary>
    /// Zoom runs one way and resolution the other, which is the whole reason this node exists.
    /// </summary>
    [Fact]
    public void From_Zoom_sets_MaxVisible_because_a_higher_zoom_is_a_smaller_resolution()
    {
        var layer = BigSquare();
        VisibleRangeNodes.VisibleRange(layer, out _, fromZoom: 4, toZoom: 8);

        _out.WriteLine($"fromZoom 4 -> MaxVisible {layer.MaxVisible:0.##}");
        _out.WriteLine($"toZoom   8 -> MinVisible {layer.MinVisible:0.##}");

        Assert.True(layer.MaxVisible > layer.MinVisible,
            "the coarse end must be the larger resolution, or the range is empty");
    }

    /// <summary>An untouched layer draws everywhere — the range is opt-in.</summary>
    [Fact]
    public void Without_the_node_a_layer_draws_at_every_zoom()
    {
        var layer = BigSquare();
        foreach (var zoom in new[] { 3, 6, 9 })
            Assert.True(DrawnAtZoom(layer, zoom) > 0, $"zoom {zoom} should draw");
    }

    [Fact]
    public void One_level_is_a_legal_range()
    {
        var layer = BigSquare();
        VisibleRangeNodes.VisibleRange(layer, out var status, fromZoom: 6, toZoom: 6);

        Assert.Contains("only at zoom 6", status);
        Assert.True(DrawnAtZoom(layer, 6) > 0);
        Assert.Equal(0, DrawnAtZoom(layer, 5));
        Assert.Equal(0, DrawnAtZoom(layer, 7));
    }

    /// <summary>
    /// **A backwards range is refused and said out loud, not silently obeyed.**
    /// </summary>
    /// <remarks>
    /// Obeying it would make the layer invisible at every zoom, which looks exactly like data that
    /// failed to load. Since the two pins run in opposite directions to the numbers underneath,
    /// getting them the wrong way round is the predictable mistake.
    /// </remarks>
    [Fact]
    public void A_backwards_range_is_refused_rather_than_making_the_layer_vanish()
    {
        var layer = BigSquare();
        VisibleRangeNodes.VisibleRange(layer, out var status, fromZoom: 12, toZoom: 4);

        _out.WriteLine($"status: {status}");
        Assert.Contains("empty", status);
        Assert.True(DrawnAtZoom(layer, 6) > 0, "the layer must be left alone, not hidden");
    }

    [Fact]
    public void Nothing_connected_says_so()
    {
        Assert.Null(VisibleRangeNodes.VisibleRange(null, out var status));
        Assert.Equal("nothing connected", status);
    }
}
