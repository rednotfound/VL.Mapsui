using VL.Mapsui;
using Xunit;

namespace VL.Mapsui.Tests;

/// <summary>The basemap offered to VL.NetTopologySuite's GeometryEditor window. No network.</summary>
public class OsmBasemapTests
{
    [Fact]
    public void Tiles_are_asked_for_in_the_xyz_scheme_from_openstreetmap()
    {
        // y counts from the NORTH, as every slippy map and IBasemap do: the north-east quarter of the
        // world at zoom 1 is column 1, row 0.
        var uri = OsmBasemap.UriOf(1, 1, 0);
        Assert.NotNull(uri);
        Assert.Contains("openstreetmap.org", uri!.Host);
        Assert.EndsWith("/1/1/0.png", uri.AbsolutePath);
        Assert.EndsWith("/16/58214/25805.png", OsmBasemap.UriOf(16, 58214, 25805)!.AbsolutePath);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(20, 0, 0)]      // deeper than OSM's 19
    [InlineData(1, 2, 0)]       // only 2 columns at zoom 1
    [InlineData(1, 0, -1)]
    public void A_tile_outside_the_world_is_never_asked_for(int z, int x, int y)
        => Assert.Null(OsmBasemap.Instance.TryGetTile(z, x, y));

    [Fact]
    public void It_says_who_drew_the_map()
    {
        Assert.Equal("© OpenStreetMap contributors", OsmBasemap.Instance.Attribution);
        Assert.Equal(19, OsmBasemap.Instance.MaxZoom);
    }
}
