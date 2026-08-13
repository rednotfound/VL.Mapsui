using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// Locks down the bug that took a home network offline.
/// </summary>
/// <remarks>
/// The map node was a public static method, which VL evaluates on every frame. At 60fps it
/// built a fresh Map and tile layer sixty times a second and released none: 17,085 TCP
/// connections in 13 minutes, the machine's 16,384 ephemeral ports exhausted, DNS dead for
/// every program on it. The same bug is why nothing ever rendered, since tiles kept arriving
/// for maps that had already been discarded.
///
/// Every test below is a frame loop. That is the shape of the failure, so it is the shape of
/// the test: call Update many times and assert on how much got built.
/// </remarks>
public class LifetimeTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;
    const int Zoom = 12;

    static OpenStreetMapNode Node() => new();

    [Fact]
    public void A_hundred_frames_with_unchanged_inputs_build_one_map()
    {
        // The regression test. Under the old code this number was 100.
        using var node = Node();

        for (int frame = 0; frame < 100; frame++)
            node.Update(Lon, Lat, Zoom, enabled: true);

        Assert.Equal(1, node.MapsBuiltHere);
    }

    [Fact]
    public void The_same_layer_instance_comes_back_every_frame()
    {
        // Identity matters as much as the count: a new layer each frame would throw away
        // whatever the renderer had accumulated, even if the map underneath were reused.
        using var node = Node();

        var first = node.Update(Lon, Lat, Zoom, enabled: true);
        for (int frame = 0; frame < 10; frame++)
        {
            var again = node.Update(Lon, Lat, Zoom, enabled: true);
            Assert.Same(first, again);
        }
    }

    [Fact]
    public void Nothing_is_built_until_enabled()
    {
        // Opening a document in vvvv runs it. A map that fetches on open gives whoever opened
        // it no chance to decline, so the default has to cost nothing.
        using var node = Node();

        for (int frame = 0; frame < 50; frame++)
            Assert.Null(node.Update(Lon, Lat, Zoom, enabled: false));

        Assert.Equal(0, node.MapsBuiltHere);
    }

    [Theory]
    [InlineData(140.7, Lat, Zoom)]
    [InlineData(Lon, 36.68, Zoom)]
    [InlineData(Lon, Lat, 13)]
    public void Changing_an_input_rebuilds_exactly_once(double lon, double lat, int zoom)
    {
        using var node = Node();

        for (int frame = 0; frame < 20; frame++) node.Update(Lon, Lat, Zoom, enabled: true);
        for (int frame = 0; frame < 20; frame++) node.Update(lon, lat, zoom, enabled: true);

        Assert.Equal(2, node.MapsBuiltHere);
    }

    [Fact]
    public void Toggling_diagnostics_does_not_rebuild()
    {
        // Diagnostics is deliberately not part of the map's identity. Rebuilding on it would
        // discard every tile already fetched, which is both slow and rude to the tile server.
        using var node = Node();

        for (int frame = 0; frame < 20; frame++)
            node.Update(Lon, Lat, Zoom, enabled: true, diagnostics: frame % 2 == 0);

        Assert.Equal(1, node.MapsBuiltHere);
    }

    [Fact]
    public void Disabling_releases_the_map_and_re_enabling_builds_a_fresh_one()
    {
        // Switching off has to actually let go, or the gate only stops new work while the old
        // map keeps its connections. The rebuild on the way back is the price of that.
        using var node = Node();

        node.Update(Lon, Lat, Zoom, enabled: true);
        Assert.Equal(1, node.MapsBuiltHere);

        for (int frame = 0; frame < 10; frame++)
            Assert.Null(node.Update(Lon, Lat, Zoom, enabled: false));

        node.Update(Lon, Lat, Zoom, enabled: true);
        Assert.Equal(2, node.MapsBuiltHere);
    }

    [Fact]
    public void An_input_change_while_disabled_does_not_build_anything()
    {
        using var node = Node();

        for (int frame = 0; frame < 20; frame++)
            node.Update(Lon + frame, Lat, Zoom, enabled: false);

        Assert.Equal(0, node.MapsBuiltHere);
    }

    [Fact]
    public void Dispose_is_safe_to_call_twice_and_after_use()
    {
        // VL disposes a process node when it leaves the patch, and a patch can be edited while
        // running. Throwing there would take the whole document down.
        var node = Node();
        node.Update(Lon, Lat, Zoom, enabled: true);

        node.Dispose();
        node.Dispose();
    }

    [Fact]
    public void Update_after_dispose_builds_again_rather_than_throwing()
    {
        // Not a supported sequence, but it must fail softly: Dispose only releases, it does not
        // put the node into a broken state.
        var node = Node();
        node.Update(Lon, Lat, Zoom, enabled: true);
        node.Dispose();

        var layer = node.Update(Lon, Lat, Zoom, enabled: true);

        Assert.NotNull(layer);
        Assert.Equal(2, node.MapsBuiltHere);
        node.Dispose();
    }
}
