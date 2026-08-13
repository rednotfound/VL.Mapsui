using System;
using System.Collections.Generic;
using Mapsui;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// Moving the map must never rebuild anything.
/// </summary>
/// <remarks>
/// **Every map here is empty on purpose, and that is not laziness.** The navigation nodes call
/// <c>Map.Refresh</c> so the layers fetch what the new view needs — which is the fix for tiles
/// not loading after a drag — and a map carrying a real OSM layer would therefore hit the
/// network from a unit test. A map with no layers has nothing to fetch, so these exercise the
/// arithmetic and the lifetime without touching tile.openstreetmap.org.
///
/// The property under test is the one that made interaction dangerous in the first place:
/// dragging changes the centre on every frame, so if moving the map rebuilt it, adding drag
/// would have re-created the per-frame rebuild by another route.
/// </remarks>
public class NavigateTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;

    [Fact]
    public void Two_hundred_frames_of_movement_still_build_one_map()
    {
        // What a drag looks like.
        using var node = new MapNode();
        var map = node.Update();

        for (int frame = 0; frame < 200; frame++)
        {
            NavigateNodes.CenterOn(map, Lon + frame * 0.001, Lat + frame * 0.001);
            node.Update();
        }

        Assert.Equal(1, node.MapsBuilt);
    }

    [Fact]
    public void Dragging_does_not_disturb_the_layers()
    {
        // The layer node is not even in this graph, which is the point: where the map looks and
        // what it is made of are separate concerns now.
        using var node = new MapNode();
        var map = node.Update();

        for (int frame = 0; frame < 100; frame++)
            NavigateNodes.DragBetween(map, 100f + frame, 100f, 100f + frame - 1, 100f);

        Assert.Equal(1, node.MapsBuilt);
    }

    [Fact]
    public void A_drag_that_did_not_move_does_nothing()
    {
        // A mouse position repeats while the button is held still, and that arrives here every
        // frame. Passing it through would ask Mapsui to refresh sixty times a second for no
        // change at all.
        using var node = new MapNode();
        var map = node.Update();

        NavigateNodes.CenterOn(map, Lon, Lat);
        var before = map.Navigator.Viewport;

        for (int frame = 0; frame < 50; frame++)
            NavigateNodes.DragBetween(map, 100f, 100f, 100f, 100f);

        Assert.Equal(before.CenterX, map.Navigator.Viewport.CenterX, 9);
        Assert.Equal(before.CenterY, map.Navigator.Viewport.CenterY, 9);
    }

    [Fact]
    public void The_first_frame_of_a_drag_only_records_where_it_started()
    {
        // Otherwise pressing the button would jump the map by however far the pointer happened
        // to be from wherever it was last time.
        using var node = new MapNode();
        var map = node.Update();
        NavigateNodes.CenterOn(map, Lon, Lat);
        var before = map.Navigator.Viewport;

        var drag = new DragNode();
        drag.Update(map, 500f, 400f, dragging: true);

        Assert.Equal(before.CenterX, map.Navigator.Viewport.CenterX, 9);
        Assert.Equal(before.CenterY, map.Navigator.Viewport.CenterY, 9);
    }

    [Fact]
    public void A_drag_that_is_not_dragging_never_moves_the_map()
    {
        // The pointer moves across the window all the time. Only the gate decides.
        using var node = new MapNode();
        var map = node.Update();
        NavigateNodes.CenterOn(map, Lon, Lat);
        var before = map.Navigator.Viewport;

        var drag = new DragNode();
        for (int frame = 0; frame < 50; frame++)
            drag.Update(map, 100f + frame * 5f, 200f, dragging: false);

        Assert.Equal(before.CenterX, map.Navigator.Viewport.CenterX, 9);
    }

    [Fact]
    public void Releasing_and_pressing_again_starts_a_fresh_gesture()
    {
        // The position at the moment of the new press is the new origin; the gap between
        // gestures must not be applied as movement.
        using var node = new MapNode();
        var map = node.Update();
        var drag = new DragNode();

        drag.Update(map, 100f, 100f, dragging: true);
        drag.Update(map, 110f, 100f, dragging: true);
        drag.Update(map, 900f, 900f, dragging: false);   // pointer wanders away, button up
        NavigateNodes.CenterOn(map, Lon, Lat);
        var before = map.Navigator.Viewport;

        drag.Update(map, 900f, 900f, dragging: true);    // press again, far away

        Assert.Equal(before.CenterX, map.Navigator.Viewport.CenterX, 9);
    }

    [Fact]
    public void Zooming_by_zero_steps_does_nothing()
    {
        // A wheel that is not being turned reports zero on every frame.
        using var node = new MapNode();
        var map = node.Update();

        var before = map.Navigator.Viewport.Resolution;
        for (int frame = 0; frame < 50; frame++)
            NavigateNodes.ZoomAt(map, 100f, 100f, steps: 0);

        Assert.Equal(before, map.Navigator.Viewport.Resolution, 9);
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────
    //
    // These give the navigator a size first. Zoom is meaningless without one - a viewport with
    // no width has no resolution to change, and Mapsui reports 1 and stays there. In a patch the
    // renderer supplies the size; a test has to say so itself. UpdateAnimations is called for the
    // same reason: Mapsui's wheel zoom eases over a duration, and nothing advances it here.

    static Map SizedMap(MapNode node)
    {
        var map = node.Update();
        map.Navigator.SetSize(800f, 600f);

        // Zoom levels normally come from a tile layer's schema, and an empty map therefore has
        // none: Mapsui reports resolution 1 and every zoom is a no-op. Attaching a real OSM layer
        // would fix that and would also make Refresh fetch tiles from a unit test, which is the
        // one thing this suite must not do. OverrideResolutions supplies the ladder directly.
        // These are the standard web-mercator resolutions: 156543.034 / 2^level.
        var ladder = new List<double>();
        for (int level = 0; level <= 20; level++) ladder.Add(156543.033928 / Math.Pow(2, level));
        map.Navigator.OverrideResolutions = ladder;

        // Mapsui eases a wheel zoom over MouseWheelAnimation.Duration, so the resolution has not
        // moved by the time the call returns and a test would read the old value. Duration is
        // public and settable, so zero it: the zoom lands immediately and the assertion can be
        // about the thing that actually matters rather than about an animation being in flight.
        map.Navigator.MouseWheelAnimation.Duration = 0;

        NavigateNodes.ZoomToLevel(map, 12);
        return map;
    }

    [Fact]
    public void A_wheel_that_did_not_move_does_not_zoom()
    {
        // FrameDifference reports zero on every frame the wheel is not being turned, which is
        // most of them.
        using var node = new MapNode();
        var map = node.Update();
        var before = map.Navigator.Viewport.Resolution;

        for (int frame = 0; frame < 50; frame++)
            NavigateNodes.ZoomByWheel(map, 100f, 100f, wheelDelta: 0f);

        Assert.Equal(before, map.Navigator.Viewport.Resolution, 9);
    }

    [Theory]
    [InlineData(0f, 120f, 0)]        // wheel not turned: the usual case, every frame
    [InlineData(120f, 120f, 1)]      // one Windows notch forward
    [InlineData(-120f, 120f, -1)]    // one notch back
    [InlineData(360f, 120f, 3)]      // a flick
    [InlineData(1f, 1f, 1)]          // what VL actually reports: one notch is 1, measured in 7.4
    [InlineData(-1f, 1f, -1)]        // and back
    [InlineData(1f, 120f, 0)]        // the same source read as raw Windows: silently nothing.
                                     // This was the default for one round, and the symptom was a
                                     // wheel that turned and a map that did not move.
    [InlineData(60f, 120f, 1)]       // half a notch rounds to one rather than vanishing
    [InlineData(50f, 120f, 0)]       // less than half rounds away
    [InlineData(120f, 0f, 0)]        // a zero notch size must not divide by zero
    public void Wheel_units_become_whole_steps(float wheelDelta, float notchSize, int expected)
    {
        // The arithmetic is ours and is tested directly. Asserting on the resolution instead
        // would mean waiting for Mapsui's zoom easing to advance, which needs wall-clock time and
        // makes a test flaky - the animation is Mapsui's business, not this node's.
        Assert.Equal(expected, NavigateNodes.WheelSteps(wheelDelta, notchSize));
    }

    [Fact]
    public void With_the_default_animation_the_zoom_still_lands()
    {
        // Reproduces the patch, not the convenience: SizedMap zeroes MouseWheelAnimation.Duration
        // so the other tests can assert immediately, which also hides whether the animation is
        // being driven at all. In vvvv the duration is whatever Mapsui defaults to, and something
        // has to call Map.UpdateAnimations on every frame for it to progress - the Map node does.
        //
        // This is the test that tells apart "the call never reached Mapsui" from "it reached it
        // and the animation never advanced", which is the split two rounds of GUI poking could not
        // establish.
        using var node = new MapNode();
        var map = node.Update();
        map.Navigator.SetSize(800f, 600f);
        var ladder = new List<double>();
        for (int level = 0; level <= 20; level++) ladder.Add(156543.033928 / Math.Pow(2, level));
        map.Navigator.OverrideResolutions = ladder;
        NavigateNodes.ZoomToLevel(map, 12);

        var duration = map.Navigator.MouseWheelAnimation.Duration;
        var before = map.Navigator.Viewport.Resolution;

        NavigateNodes.ZoomByWheel(map, 400f, 300f, wheelDelta: 1f);

        // Drive frames the way the patch does, for a little longer than the animation lasts.
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(duration, 100) * 3 + 500);
        while (DateTime.UtcNow < deadline && map.Navigator.Viewport.Resolution == before)
        {
            node.Update();          // the Map node calls UpdateAnimations, as in the patch
            System.Threading.Thread.Sleep(8);
        }

        Assert.True(map.Navigator.Viewport.Resolution < before,
            $"resolution never moved from {before} in {duration}ms of animation driven by Map.Update");
    }

    [Fact]
    public void A_notch_of_wheel_actually_reaches_Mapsui()
    {
        // The test that was missing while the wheel silently did nothing. Asserting on the
        // resolution would not work: Mapsui animates the zoom over MouseWheelAnimation.Duration,
        // so the viewport has not moved by the time this returns. What IS immediate and
        // deterministic is that an animation is now in flight.
        //
        // If this passes and the patch still does nothing, the fault is in the wiring rather than
        // in the node - which is exactly the split that took two rounds to establish by hand.
        using var node = new MapNode();
        var map = SizedMap(node);
        var before = map.Navigator.Viewport.Resolution;

        NavigateNodes.ZoomByWheel(map, 400f, 300f, wheelDelta: 1f);
        map.UpdateAnimations();

        Assert.True(map.Navigator.Viewport.Resolution < before,
            $"zooming in should shrink metres per pixel, went from {before} to {map.Navigator.Viewport.Resolution}");
    }

    [Fact]
    public void Zooming_out_reaches_Mapsui_too()
    {
        using var node = new MapNode();
        var map = SizedMap(node);
        var before = map.Navigator.Viewport.Resolution;

        NavigateNodes.ZoomByWheel(map, 400f, 300f, wheelDelta: -1f);
        map.UpdateAnimations();

        Assert.True(map.Navigator.Viewport.Resolution > before,
            $"zooming out should grow metres per pixel, went from {before} to {map.Navigator.Viewport.Resolution}");
    }

    [Fact]
    public void The_bang_nodes_reach_Mapsui_as_well()
    {
        // These were reported working in the GUI while the wheel was not, so this pins the
        // difference down rather than leaving it to memory.
        using var node = new MapNode();
        var map = SizedMap(node);

        var before = map.Navigator.Viewport.Resolution;
        var zin = new ZoomInNode();
        zin.Update(map, trigger: true);
        map.UpdateAnimations();

        Assert.True(map.Navigator.Viewport.Resolution < before);
    }

    [Fact]
    public void A_trigger_held_down_zooms_once_and_not_once_per_frame()
    {
        // The regression test for the edge detection. A bang is true for a single frame, but a
        // toggle left switched on is true forever, and every zoom asks the layers to refresh -
        // so without an edge this becomes a fetch on every frame, which is the failure that took
        // a home network down wearing different clothes.
        var zoom = new ZoomInNode();
        using var node = new MapNode();
        var map = SizedMap(node);

        for (int frame = 0; frame < 60; frame++) zoom.Update(map, trigger: true);

        Assert.Equal(1, zoom.Zooms);
    }

    [Fact]
    public void Releasing_the_trigger_arms_it_again()
    {
        var zoom = new ZoomInNode();
        using var node = new MapNode();
        var map = SizedMap(node);

        zoom.Update(map, trigger: true);
        zoom.Update(map, trigger: false);
        zoom.Update(map, trigger: true);

        Assert.Equal(2, zoom.Zooms);
    }

    [Fact]
    public void Zooming_never_rebuilds_the_map()
    {
        var zin = new ZoomInNode();
        var zout = new ZoomOutNode();
        using var node = new MapNode();
        var map = node.Update();

        for (int frame = 0; frame < 100; frame++)
        {
            NavigateNodes.ZoomByWheel(map, 100f, 100f, wheelDelta: frame % 2 == 0 ? 120f : -120f);
            zin.Update(map, trigger: frame % 4 == 0);
            zout.Update(map, trigger: frame % 5 == 0);
            node.Update();
        }

        Assert.Equal(1, node.MapsBuilt);
    }

    [Fact]
    public void CenterOn_puts_the_coordinate_at_the_centre()
    {
        // Round trip through spherical mercator, which is what the map works in internally.
        // Coordinate order is longitude first, and getting that backwards is the classic bug
        // in this domain.
        using var node = new MapNode();
        var map = node.Update();

        NavigateNodes.CenterOn(map, Lon, Lat);
        MapInfoNodes.ViewportInfo(map, out var lon, out var lat, out _, out _, out _);

        Assert.Equal(Lon, lon, 6);
        Assert.Equal(Lat, lat, 6);
    }

    [Fact]
    public void CenterOn_is_not_confused_by_swapped_arguments()
    {
        // Tokyo is at 139.7E 35.68N. Reading back 35.68 as the longitude would mean the pair
        // got swapped somewhere, and the map would quietly show the wrong part of the world.
        using var node = new MapNode();
        var map = node.Update();

        NavigateNodes.CenterOn(map, Lon, Lat);
        MapInfoNodes.ViewportInfo(map, out var lon, out var lat, out _, out _, out _);

        Assert.True(lon > 100, $"longitude came back as {lon}, which looks like the latitude");
        Assert.True(lat < 90, $"latitude came back as {lat}, which is off the planet");
    }

    [Fact]
    public void ZoomToLevel_is_clamped_rather_than_throwing()
    {
        // The level is a pin, so anything can arrive on it.
        using var node = new MapNode();
        var map = node.Update();

        NavigateNodes.ZoomToLevel(map, -5);
        NavigateNodes.ZoomToLevel(map, 999);

        Assert.Equal(1, node.MapsBuilt);
    }

    [Fact]
    public void LayerInfo_reports_an_empty_map_as_empty()
    {
        using var node = new MapNode();
        var map = node.Update();

        MapInfoNodes.LayerInfo(map, out var count, out var busy, out var names);

        Assert.Equal(0, count);
        Assert.False(busy);
        Assert.Equal(string.Empty, names);
    }

    [Fact]
    public void Navigation_returns_the_same_map_so_it_can_be_chained()
    {
        // Each node returns the map it was given, so a patch can run several in a row on one
        // wire rather than fanning out from the map node.
        using var node = new MapNode();
        var map = node.Update();

        var chained = NavigateNodes.ZoomToLevel(NavigateNodes.CenterOn(map, Lon, Lat), 10);

        Assert.Same(map, chained);
    }
}
