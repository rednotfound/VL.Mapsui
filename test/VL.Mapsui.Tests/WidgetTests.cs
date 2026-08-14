using System.Linq;
using Mapsui;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// Widgets, and the one way they can go badly wrong.
/// </summary>
/// <remarks>
/// **<c>Map.Widgets</c> is append only.** A widget node written as a static operation would enqueue
/// sixty widgets a second and nothing could ever remove them, so every test here is a frame loop
/// that counts what ended up on the map - the same shape as the tile layer tests, for the same
/// reason.
///
/// Nothing here renders or fetches. The renderer's own registry is asserted separately, because
/// "the widget is on the map" and "something knows how to draw it" are different claims.
/// </remarks>
public class WidgetTests
{
    static global::Mapsui.Map NewMap() => new();

    [Fact]
    public void The_Skia_renderer_knows_how_to_draw_the_widgets_we_ship()
    {
        // Could not be answered in PowerShell - constructing a MapRenderer needs SkiaSharp's
        // native library - and it decides whether a widget node draws at all. Measured: 9
        // renderers registered, PerformanceWidget deliberately not among them.
        var renderer = new global::Mapsui.Rendering.Skia.MapRenderer();

        Assert.Contains(typeof(global::Mapsui.Widgets.ScaleBar.ScaleBarWidget), renderer.WidgetRenders.Keys);
        Assert.Contains(typeof(global::Mapsui.Widgets.Hyperlink), renderer.WidgetRenders.Keys);
        Assert.Contains(typeof(global::Mapsui.Widgets.Zoom.ZoomInOutWidget), renderer.WidgetRenders.Keys);
    }

    [Fact]
    public void A_hundred_frames_add_one_scale_bar()
    {
        var map = NewMap();
        var node = new ScaleBarWidgetNode();

        for (int frame = 0; frame < 100; frame++) node.Update(map);

        Assert.Equal(1, node.WidgetsAdded);
        Assert.Single(map.Widgets);
    }

    [Fact]
    public void A_hundred_frames_add_one_of_each_widget()
    {
        // Three nodes on one map: three widgets, not three hundred.
        var map = NewMap();
        var bar = new ScaleBarWidgetNode();
        var credit = new AttributionWidgetNode();
        var zoom = new ZoomButtonsWidgetNode();

        for (int frame = 0; frame < 100; frame++)
        {
            bar.Update(map);
            credit.Update(map, out _);
            zoom.Update(map);
        }

        Assert.Equal(3, map.Widgets.Count);
    }

    [Fact]
    public void Toggling_Enabled_does_not_add_another_widget()
    {
        // Enabled is how a widget goes away, because nothing can take it out of the queue.
        var map = NewMap();
        var node = new ScaleBarWidgetNode();

        for (int frame = 0; frame < 10; frame++) node.Update(map, enabled: true);
        for (int frame = 0; frame < 10; frame++) node.Update(map, enabled: false);
        for (int frame = 0; frame < 10; frame++) node.Update(map, enabled: true);

        Assert.Equal(1, node.WidgetsAdded);
        Assert.Single(map.Widgets);
        Assert.True(map.Widgets.Single().Enabled);
    }

    [Fact]
    public void Changing_the_units_does_not_add_another_widget()
    {
        var map = NewMap();
        var node = new ScaleBarWidgetNode();

        node.Update(map, units: ScaleBarUnits.Metric);
        node.Update(map, units: ScaleBarUnits.Imperial);
        node.Update(map, units: ScaleBarUnits.Nautical);

        Assert.Equal(1, node.WidgetsAdded);
    }

    [Fact]
    public void A_new_map_gets_its_own_widget()
    {
        // A map cannot inherit another map's widgets, and the old map is on its way out anyway.
        var node = new ScaleBarWidgetNode();
        var first = NewMap();
        var second = NewMap();

        node.Update(first);
        node.Update(second);

        Assert.Equal(2, node.WidgetsAdded);
        Assert.Single(first.Widgets);
        Assert.Single(second.Widgets);
    }

    [Fact]
    public void The_corner_reaches_the_widget()
    {
        var map = NewMap();
        var node = new ScaleBarWidgetNode();

        node.Update(map, corner: WidgetCorner.TopRight);
        var widget = map.Widgets.Single();

        Assert.Equal(global::Mapsui.Widgets.HorizontalAlignment.Right, widget.HorizontalAlignment);
        Assert.Equal(global::Mapsui.Widgets.VerticalAlignment.Top, widget.VerticalAlignment);
    }

    [Fact]
    public void An_unconnected_map_gives_no_widget_and_no_crash()
    {
        // A Map pin is unconnected while a patch is being built, and a node that threw then would
        // make the patch unusable until it was finished.
        var node = new ScaleBarWidgetNode();

        Assert.Null(node.Update(null));
        Assert.Equal(0, node.WidgetsAdded);
    }

    // ── Attribution, which is compliance rather than decoration ───────────────

    [Fact]
    public void The_attribution_comes_from_the_layers_rather_than_a_pin()
    {
        // OpenStreetMap's policy requires the attribution to be displayed. Reading it from the
        // layers is what keeps it true when the layers change - and what stops it being typed in
        // wrongly.
        var map = NewMap();
        var layer = new global::Mapsui.Layers.MemoryLayer { Name = "credited" };
        layer.Attribution.Text = "© OpenStreetMap contributors";
        layer.Attribution.Url = "https://www.openstreetmap.org/copyright";
        map.Layers.Add(layer);

        var node = new AttributionWidgetNode();
        node.Update(map, out var shown);

        Assert.Contains("OpenStreetMap", shown);
        var widget = (global::Mapsui.Widgets.Hyperlink)map.Widgets.Single();
        Assert.Contains("OpenStreetMap", widget.Text);
        Assert.Equal("https://www.openstreetmap.org/copyright", widget.Url);
    }

    [Fact]
    public void An_attribution_that_arrives_late_still_gets_shown()
    {
        // The widget is built on the first frame and layers are added on some later one, so
        // reading the text once at construction would leave it permanently empty.
        var map = NewMap();
        var node = new AttributionWidgetNode();

        node.Update(map, out var before);
        Assert.Equal(string.Empty, before);

        var layer = new global::Mapsui.Layers.MemoryLayer { Name = "late" };
        layer.Attribution.Text = "© Someone";
        map.Layers.Add(layer);

        node.Update(map, out var after);

        Assert.Contains("Someone", after);
        Assert.Equal(1, node.WidgetsAdded);
    }

    // ── Clicks, which only the host can deliver ───────────────────────────────

    [Fact]
    public void A_press_inside_a_widget_reaches_it()
    {
        var map = NewMap();
        new ZoomButtonsWidgetNode().Update(map);
        var widget = map.Widgets.Single();

        // Envelope is set by the renderer while drawing; nothing has drawn here, so it stands in
        // for what the renderer would have written.
        widget.Envelope = new MRect(10, 10, 60, 110);

        Assert.True(WidgetInput.Route(map, new MPoint(30, 30)));
    }

    [Fact]
    public void A_press_outside_every_widget_is_left_alone()
    {
        // The press has to stay available to the rest of the scene graph, or adding a zoom button
        // would quietly break dragging the map.
        var map = NewMap();
        new ZoomButtonsWidgetNode().Update(map);
        map.Widgets.Single().Envelope = new MRect(10, 10, 60, 110);

        Assert.False(WidgetInput.Route(map, new MPoint(400, 400)));
    }

    [Fact]
    public void A_disabled_widget_does_not_take_the_press()
    {
        var map = NewMap();
        var node = new ZoomButtonsWidgetNode();
        node.Update(map, enabled: false);
        map.Widgets.Single().Envelope = new MRect(10, 10, 60, 110);

        Assert.False(WidgetInput.Route(map, new MPoint(30, 30)));
    }

    [Fact]
    public void A_press_before_anything_has_been_drawn_is_harmless()
    {
        // Envelope is null until the renderer has laid the widget out, and a click can arrive
        // first.
        var map = NewMap();
        new ZoomButtonsWidgetNode().Update(map);

        Assert.False(WidgetInput.Route(map, new MPoint(30, 30)));
    }

    // ── The Click node, which is what makes a press something a patch wires ───

    [Fact]
    public void Only_the_rising_edge_of_a_press_reaches_a_widget()
    {
        // A press held down would zoom on every frame - a fresh round of tile requests sixty times
        // a second, which is this package's oldest failure wearing a mouse button.
        var map = NewMap();
        new ZoomButtonsWidgetNode().Update(map);
        map.Widgets.Single().Envelope = new MRect(10, 10, 60, 110);
        var click = new WidgetClickNode();

        var handledOnFrames = 0;
        for (int frame = 0; frame < 100; frame++)
        {
            click.Update(map, out var handled, x: 30, y: 30, pressed: true);
            if (handled) handledOnFrames++;
        }

        Assert.Equal(1, handledOnFrames);
    }

    [Fact]
    public void Releasing_and_pressing_again_is_a_second_click()
    {
        var map = NewMap();
        new ZoomButtonsWidgetNode().Update(map);
        map.Widgets.Single().Envelope = new MRect(10, 10, 60, 110);
        var click = new WidgetClickNode();

        var count = 0;
        foreach (var pressed in new[] { true, true, false, false, true, true })
        {
            click.Update(map, out var handled, x: 30, y: 30, pressed: pressed);
            if (handled) count++;
        }

        Assert.Equal(2, count);
    }

    [Fact]
    public void A_press_that_misses_every_widget_is_not_handled()
    {
        // Handled is what lets a patch keep panning: gate Dragging with NOT Handled and a press on
        // a button stops being a drag as well. With the press swallowed inside the layer, as it
        // was first written, a patch could not know.
        var map = NewMap();
        new ZoomButtonsWidgetNode().Update(map);
        map.Widgets.Single().Envelope = new MRect(10, 10, 60, 110);

        new WidgetClickNode().Update(map, out var handled, x: 400, y: 400, pressed: true);

        Assert.False(handled);
    }

    [Fact]
    public void The_Click_node_passes_the_map_on_and_survives_an_unconnected_one()
    {
        var map = NewMap();
        var click = new WidgetClickNode();

        Assert.Same(map, click.Update(map, out _));
        Assert.Null(click.Update(null, out var handled));
        Assert.False(handled);
    }
}

