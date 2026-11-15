using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Stride.Core.Mathematics;
using VL.Lib.IO;
using VL.Lib.IO.Notifications;
using Xunit;

using ILayer = global::Mapsui.Layers.ILayer;

namespace VL.Mapsui.Tests;

/// <summary>
/// A map that answers the mouse by itself, and steps aside when a patch wires its own.
/// </summary>
/// <remarks>
/// Decided 2026-11-15: until then a patch needed fourteen nodes before a map would move. The
/// notifications below are the ones VL.Skia's Renderer sends down the scene graph - built by hand,
/// so no window and no vvvv are involved. No network: no map here has a layer.
/// </remarks>
public class InteractionTests
{
    static readonly ILayer[] NoLayers = Array.Empty<ILayer>();
    static readonly Vector2 Client = new(800f, 600f);

    static MouseDownNotification Down(float x, float y, MouseButtons button = MouseButtons.Left)
        => new(new Vector2(x, y), Client, button, Keys.None, sender: null!);
    static MouseUpNotification Up(float x, float y, MouseButtons button = MouseButtons.Left)
        => new(new Vector2(x, y), Client, button, Keys.None, sender: null!);
    static MouseMoveNotification Move(float x, float y)
        => new(new Vector2(x, y), Client, Keys.None, sender: null!);
    static MouseWheelNotification Wheel(float x, float y, int delta)
        => new(new Vector2(x, y), Client, delta, Keys.None, sender: null!);

    /// <summary>A sized map at zoom 12 with instant wheel zoom, and the layer that draws it.</summary>
    static (Map map, MapsuiLayer layer, ToSkiaLayerNode node) Scene(MapNode mapNode, bool interactive = true)
    {
        var map = mapNode.Update(NoLayers);
        map.Navigator.SetSize(Client.X, Client.Y);
        var ladder = new List<double>();
        for (int level = 0; level <= 20; level++) ladder.Add(156543.033928 / Math.Pow(2, level));
        map.Navigator.OverrideResolutions = ladder;
        map.Navigator.OverridePanBounds = new MRect(-20037508, -20037508, 20037508, 20037508);
        NavigateNodes.ZoomToLevel(map, 12);
        map.Navigator.MouseWheelAnimation.Duration = 0;

        var node = new ToSkiaLayerNode();
        var layer = (MapsuiLayer)node.Update(map, interactive: interactive)!;
        return (map, layer, node);
    }

    static void Send(MapsuiLayer layer, params INotification[] notifications)
    {
        foreach (var n in notifications) Assert.False(layer.Notify(n, caller: null!));
    }

    // ── The default ──────────────────────────────────────────────────────────

    [Fact]
    public void A_left_drag_pans_the_map_by_the_distance_dragged()
    {
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);
        var before = map.Navigator.Viewport;

        Send(layer, Down(400, 300), Move(500, 300), Move(500, 250), Up(500, 250));

        // Dragging right moves the world right, so the centre goes west; dragging up moves the
        // world up, so the centre goes south. In map units: pixels times resolution.
        var after = map.Navigator.Viewport;
        Assert.Equal(before.CenterX - 100 * before.Resolution, after.CenterX, 3);
        Assert.Equal(before.CenterY - 50 * before.Resolution, after.CenterY, 3);
        Assert.Equal(before.Resolution, after.Resolution);
    }

    [Fact]
    public void Moving_without_a_press_and_dragging_with_another_button_do_nothing()
    {
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);
        var before = map.Navigator.Viewport;

        Send(layer, Move(100, 100), Move(300, 300));
        Send(layer, Down(400, 300, MouseButtons.Right), Move(500, 400), Up(500, 400, MouseButtons.Right));
        Send(layer, Down(400, 300, MouseButtons.Middle), Move(500, 400), Up(500, 400, MouseButtons.Middle));

        Assert.Equal(before.CenterX, map.Navigator.Viewport.CenterX);
        Assert.Equal(before.CenterY, map.Navigator.Viewport.CenterY);
    }

    [Fact]
    public void A_release_ends_the_drag()
    {
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);

        Send(layer, Down(400, 300), Move(450, 300), Up(450, 300));
        var released = map.Navigator.Viewport.CenterX;
        Send(layer, Move(700, 500), Move(100, 100));

        Assert.Equal(released, map.Navigator.Viewport.CenterX);
    }

    [Theory]
    [InlineData(120, true)]     // one notch forward on a Windows wheel
    [InlineData(-120, false)]
    [InlineData(1, true)]       // whatever the magnitude, the sign is what counts
    [InlineData(-360, false)]
    public void The_wheel_zooms_one_step_per_notification(int delta, bool zoomsIn)
    {
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);
        var before = map.Navigator.Viewport.Resolution;

        Send(layer, Wheel(400, 300, delta));
        mapNode.Update(NoLayers);       // the frame tick that lands the zoom

        var after = map.Navigator.Viewport.Resolution;
        Assert.Equal(zoomsIn ? before / 2 : before * 2, after, 6);
    }

    [Fact]
    public void Nothing_is_marked_handled_so_a_Console_in_the_same_Group_still_hears_it()
    {
        using var mapNode = new MapNode();
        var (_, layer, _) = Scene(mapNode);
        var press = Down(400, 300);

        Assert.False(layer.Notify(press, caller: null!));
        Assert.False(press.Handled);
    }

    [Fact]
    public void A_press_something_else_already_took_does_not_start_a_drag()
    {
        // An ImGui button drawn over the map, say.
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);
        var before = map.Navigator.Viewport.CenterX;
        var press = Down(400, 300);
        press.Handled = true;

        Send(layer, press, Move(600, 300), Up(600, 300));

        Assert.Equal(before, map.Navigator.Viewport.CenterX);
    }

    // ── The first way out: the switch ────────────────────────────────────────

    [Fact]
    public void With_Interactive_off_the_map_ignores_the_mouse()
    {
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode, interactive: false);
        var before = map.Navigator.Viewport;

        Send(layer, Down(400, 300), Move(600, 400), Up(600, 400), Wheel(400, 300, 120));
        mapNode.Update(NoLayers);

        Assert.Equal(before.CenterX, map.Navigator.Viewport.CenterX);
        Assert.Equal(before.Resolution, map.Navigator.Viewport.Resolution);
    }

    [Fact]
    public void Switching_Interactive_off_mid_drag_lets_go_of_the_map()
    {
        using var mapNode = new MapNode();
        var (map, layer, node) = Scene(mapNode);

        Send(layer, Down(400, 300), Move(450, 300));
        node.Update(map, interactive: false);
        Send(layer, Move(500, 300));
        node.Update(map, interactive: true);
        var held = map.Navigator.Viewport.CenterX;
        Send(layer, Move(700, 300));        // no new press: must not resume

        Assert.Equal(held, map.Navigator.Viewport.CenterX);
    }

    [Fact]
    public void The_switch_does_not_rebuild_the_layer()
    {
        using var mapNode = new MapNode();
        var (map, layer, node) = Scene(mapNode);

        for (int frame = 0; frame < 100; frame++)
            Assert.Same(layer, node.Update(map, interactive: frame % 2 == 0));
    }

    // ── The second way out: wire the node ────────────────────────────────────

    [Fact]
    public void A_wired_Drag_node_takes_the_drag_over_and_the_map_moves_once()
    {
        // What every patch written against 0.0.2-alpha looks like: MouseState into Drag. If the
        // default did not step aside, one drag would pan twice.
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);
        var drag = new DragNode();
        var before = map.Navigator.Viewport;

        drag.Update(map, 400, 300, dragging: false);            // the node has been running
        Send(layer, Down(400, 300));
        drag.Update(map, 400, 300, dragging: true);
        Send(layer, Move(500, 300));
        drag.Update(map, 500, 300, dragging: true);
        Send(layer, Up(500, 300));
        drag.Update(map, 500, 300, dragging: false);

        Assert.Equal(before.CenterX - 100 * before.Resolution, map.Navigator.Viewport.CenterX, 3);
    }

    [Fact]
    public void A_wired_ZoomByWheel_takes_the_wheel_over_and_the_map_zooms_once()
    {
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);
        var before = map.Navigator.Viewport.Resolution;

        NavigateNodes.ZoomByWheel(map, 400, 300, wheelDelta: 0f);   // the node has been running
        Send(layer, Wheel(400, 300, 120));
        NavigateNodes.ZoomByWheel(map, 400, 300, wheelDelta: 1f);
        mapNode.Update(NoLayers);

        Assert.Equal(before / 2, map.Navigator.Viewport.Resolution, 6);
    }

    [Fact]
    public void Taking_one_gesture_over_leaves_the_others_to_the_map()
    {
        using var mapNode = new MapNode();
        var (map, layer, _) = Scene(mapNode);
        var before = map.Navigator.Viewport;

        NavigateNodes.ZoomByWheel(map, 400, 300, wheelDelta: 0f);   // the patch owns the wheel only
        Send(layer, Down(400, 300), Move(500, 300), Up(500, 300));

        Assert.Equal(before.CenterX - 100 * before.Resolution, map.Navigator.Viewport.CenterX, 3);
    }

    [Fact]
    public void A_node_on_one_map_does_not_silence_another()
    {
        using var mapNodeA = new MapNode();
        using var mapNodeB = new MapNode();
        var (mapA, _, _) = Scene(mapNodeA);
        var (mapB, layerB, _) = Scene(mapNodeB);
        var before = mapB.Navigator.Viewport.CenterX;

        new DragNode().Update(mapA, 0, 0, dragging: false);
        Send(layerB, Down(400, 300), Move(500, 300), Up(500, 300));

        Assert.NotEqual(before, mapB.Navigator.Viewport.CenterX);
    }

    [Fact]
    public void When_the_node_is_gone_the_map_answers_the_mouse_again()
    {
        // Delete the Drag node from a running patch and the stamp stops being renewed.
        using var mapNode = new MapNode();
        var map = mapNode.Update(NoLayers);
        map.Navigator.SetSize(Client.X, Client.Y);
        map.Navigator.OverridePanBounds = new MRect(-20037508, -20037508, 20037508, 20037508);
        var interaction = new MapInteraction();
        var before = map.Navigator.Viewport.CenterX;

        new DragNode().Update(map, 0, 0, dragging: false);
        interaction.Handle(map, Down(400, 300), Client.X, Client.Y);
        interaction.Handle(map, Move(500, 300), Client.X, Client.Y);
        interaction.Handle(map, Up(500, 300), Client.X, Client.Y);
        Assert.Equal(before, map.Navigator.Viewport.CenterX);

        interaction.Clock = () => Environment.TickCount64 + MapInteraction.StepAsideMs + 1;
        interaction.Handle(map, Down(400, 300), Client.X, Client.Y);
        interaction.Handle(map, Move(500, 300), Client.X, Client.Y);
        interaction.Handle(map, Up(500, 300), Client.X, Client.Y);
        Assert.NotEqual(before, map.Navigator.Viewport.CenterX);
    }

    // ── Rule 4, still ────────────────────────────────────────────────────────

    [Fact]
    public void A_thousand_drag_frames_build_one_map_and_no_layers()
    {
        // The incident this package was rebuilt around was a map REBUILT on movement. Interaction
        // by default must not bring that back by another door.
        using var mapNode = new MapNode();
        var (map, layer, node) = Scene(mapNode);

        Send(layer, Down(400, 300));
        for (int frame = 0; frame < 1000; frame++)
        {
            Send(layer, Move(400 + frame % 50, 300 + frame % 30));
            Assert.Same(map, mapNode.Update(NoLayers));
            Assert.Same(layer, node.Update(map));
        }

        Assert.Equal(1, mapNode.MapsBuilt);
        Assert.Empty(map.Layers);
    }
}
