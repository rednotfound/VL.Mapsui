using System;
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
            NavigateNodes.Drag(map, 100f + frame, 100f, 100f + frame - 1, 100f);

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
            NavigateNodes.Drag(map, 100f, 100f, 100f, 100f);

        Assert.Equal(before.CenterX, map.Navigator.Viewport.CenterX, 9);
        Assert.Equal(before.CenterY, map.Navigator.Viewport.CenterY, 9);
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
