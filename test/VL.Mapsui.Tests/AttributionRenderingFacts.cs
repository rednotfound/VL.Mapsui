using System.Collections.Generic;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

using MapRenderer = global::Mapsui.Rendering.Skia.MapRenderer;
using MemoryLayer = global::Mapsui.Layers.MemoryLayer;
using ILayer = global::Mapsui.Layers.ILayer;
using IWidget = global::Mapsui.Widgets.IWidget;
using Hyperlink = global::Mapsui.Widgets.Hyperlink;
using HAlign = global::Mapsui.Widgets.HorizontalAlignment;
using VAlign = global::Mapsui.Widgets.VerticalAlignment;
using Color = global::Mapsui.Styles.Color;
using Viewport = global::Mapsui.Viewport;

namespace VL.Mapsui.Tests;

/// <summary>
/// Who draws a layer's attribution: Mapsui's renderer by itself, or only a widget we add?
/// </summary>
/// <remarks>
/// Written 2026-09-26 after the user switched OSM on in a help patch and saw the credit bottom right
/// **without** the Attribution node, and saw nothing change when that node's pins were changed. The
/// earlier claim - "Map.Widgets starts empty and only the Attribution node fills it" - was reasoned,
/// never measured. An offscreen SKSurface is honest for this question: whether pixels appear, and
/// in which corner, with no widget at all.
/// </remarks>
public class AttributionRenderingFacts
{
    readonly ITestOutputHelper _out;
    public AttributionRenderingFacts(ITestOutputHelper output) => _out = output;

    const int W = 400, H = 300;

    static MemoryLayer LayerCrediting(string text)
    {
        var layer = new MemoryLayer();
        layer.Attribution.Text = text;
        return layer;
    }

    /// <summary>Non-white pixels per quadrant: TL, TR, BL, BR.</summary>
    static int[] Render(IEnumerable<ILayer> layers, IEnumerable<IWidget> widgets)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H));
        surface.Canvas.Clear(SKColors.White);
        new MapRenderer().Render(surface.Canvas, new Viewport(0, 0, 1, 0, W, H), layers, widgets, Color.White);
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var q = new int[4];
        for (var x = 0; x < W; x++)
        for (var y = 0; y < H; y++)
            if (bitmap.GetPixel(x, y) != SKColors.White)
                q[(y < H / 2 ? 0 : 2) + (x < W / 2 ? 0 : 1)]++;
        return q;
    }

    string Say(string what, int[] q)
    {
        var line = $"{what,-48} TL {q[0],5}  TR {q[1],5}  BL {q[2],5}  BR {q[3],5}";
        _out.WriteLine(line);
        return line;
    }

    [Fact]
    public void The_renderer_draws_a_layers_attribution_bottom_right_with_no_widget_at_all()
    {
        var credited = Render(new[] { LayerCrediting("(c) OpenStreetMap contributors") }, new List<IWidget>());
        var silent = Render(new[] { LayerCrediting("") }, new List<IWidget>());
        Say("layer credit, no widgets", credited);
        Say("no credit, no widgets", silent);

        Assert.Equal(new[] { 0, 0, 0, 0 }, silent);
        Assert.True(credited[3] > 0, "Mapsui draws the layer's credit bottom right by itself");
        Assert.Equal(0, credited[0] + credited[1] + credited[2]);
    }

    [Fact]
    public void An_extra_Hyperlink_widget_is_a_second_copy_not_a_switch()
    {
        var layers = new[] { LayerCrediting("(c) OpenStreetMap contributors") };

        var off = new Hyperlink { Text = "(c) OpenStreetMap contributors", Enabled = false };
        var topLeft = new Hyperlink
        {
            Text = "(c) OpenStreetMap contributors",
            HorizontalAlignment = HAlign.Left,
            VerticalAlignment = VAlign.Top,
        };

        var withOff = Render(layers, new List<IWidget> { off });
        var withTopLeft = Render(layers, new List<IWidget> { topLeft });
        Say("layer credit + our widget, Enabled off", withOff);
        Say("layer credit + our widget, top left", withTopLeft);

        // Switching our widget off does not remove the credit: the renderer still draws its own.
        Assert.True(withOff[3] > 0);
        // Moving our widget adds a second credit; the renderer's stays bottom right.
        Assert.True(withTopLeft[0] > 0 && withTopLeft[3] > 0);
    }
}
