using System;
using System.Linq;
using BruTile;
using BruTile.Web;
using VL.Mapsui;

namespace VL.Mapsui.Tests;

/// <summary>
/// A tile layer from any XYZ template, which is what stops this package being about one basemap.
/// </summary>
/// <remarks>
/// **No test fetches anything.** Building a tile source issues no request; the URL assertions build
/// a Uri and compare strings, which is the whole point — a template that addresses the wrong tile
/// is the failure that a network test would hide behind a plausible-looking image.
/// </remarks>
public class XyzLayerTests : IDisposable
{
    const string Template = "https://tile.example.com/{z}/{x}/{y}.png";

    /// <summary>
    /// A cache that is switched off, for the tests that are not about caching.
    /// </summary>
    /// <remarks>
    /// Since each tile source gets its own folder (2026-08-17), a test that lets the cache default
    /// creates a directory under the real %LOCALAPPDATA%\VL.Mapsui\tiles - in the profile of
    /// whoever ran `dotnet test`, named after a host that does not exist. Empty and harmless, and
    /// still the wrong thing: this package has an incident about files appearing where nobody asked
    /// for them. Passing this also states the intent, which "no cache argument" never did.
    /// </remarks>
    static readonly TileDiskCache NoDisk = TileCache.Off(System.IO.Path.GetTempPath());

    static HttpTileSource? SourceOf(global::Mapsui.Layers.ILayer? layer)
        => (layer as global::Mapsui.Tiling.Layers.TileLayer)?.TileSource as HttpTileSource;

    [Fact]
    public void The_template_addresses_the_tile_it_was_asked_for()
    {
        // z, x and y land where the provider's documentation says they will. Asserted on the Uri
        // rather than on a fetch, so this says something exact and touches no network.
        using var node = new XyzTileLayerNode();
        var layer = node.Update(out _, out _, Template, enabled: true, cache: NoDisk);

        var uri = SourceOf(layer)!.GetUri(new TileInfo { Index = new TileIndex(3, 5, 7) });

        Assert.Equal("https://tile.example.com/7/3/5.png", uri!.ToString());
    }

    [Fact]
    public void A_template_without_placeholders_is_refused_rather_than_used()
    {
        // Every tile would resolve to the same URL: the map would fill with one image repeated and
        // the server would be asked for it over and over. That fails invisibly, which is worse than
        // a wrong host failing on the first fetch.
        using var node = new XyzTileLayerNode();

        var layer = node.Update(out _, out var status, "https://tile.example.com/tile.png", enabled: true, cache: NoDisk);

        Assert.Null(layer);
        Assert.StartsWith("cannot use", status);
    }

    [Theory]
    [InlineData("https://tile.example.com/{z}/{x}.png")]
    [InlineData("https://tile.example.com/{x}/{y}.png")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_incomplete_template_gives_no_layer(string template)
    {
        using var node = new XyzTileLayerNode();

        Assert.Null(node.Update(out _, out _, template, enabled: true, cache: NoDisk));
    }

    [Fact]
    public void Nothing_is_built_while_Enabled_is_off()
    {
        // The rule everywhere in this package: opening a document runs it, and whoever opened it has
        // agreed to nothing. It matters more here - this is a server someone else chose.
        using var node = new XyzTileLayerNode();

        Assert.Null(node.Update(out var built, out _, Template, enabled: false));
        Assert.Equal(0, built);
    }

    [Fact]
    public void A_hundred_frames_with_unchanged_inputs_build_one_layer()
    {
        using var node = new XyzTileLayerNode();

        for (int frame = 0; frame < 100; frame++)
            node.Update(out _, out _, Template, "© Example", enabled: true, cache: NoDisk);

        Assert.Equal(1, node.LayersBuilt);
    }

    [Fact]
    public void Changing_the_template_rebuilds_once()
    {
        // The URL is what the tile source *is*, so it belongs to the layer's identity.
        using var node = new XyzTileLayerNode();

        for (int frame = 0; frame < 10; frame++) node.Update(out _, out _, Template, enabled: true, cache: NoDisk);
        for (int frame = 0; frame < 10; frame++)
            node.Update(out _, out _, "https://other.example.com/{z}/{x}/{y}.png", enabled: true, cache: NoDisk);

        Assert.Equal(2, node.LayersBuilt);
    }

    [Fact]
    public void The_attribution_reaches_the_layer_where_the_widget_reads_it()
    {
        // Nearly every tile service requires attribution, and the Attribution widget draws whatever
        // the layers carry. A layer with none contributes nothing to it, silently - so this asserts
        // the text arrives where the widget will look, not merely that the pin exists.
        using var node = new XyzTileLayerNode();

        var layer = node.Update(out _, out _, Template, "© Example contributors", enabled: true, cache: NoDisk);

        Assert.Equal("© Example contributors", layer!.Attribution.Text);
    }

    [Fact]
    public void The_cache_is_attached_like_the_OpenStreetMap_layer_s()
    {
        using var node = new XyzTileLayerNode();

        var layer = node.Update(out _, out var status, Template, enabled: true, cache: TileCache.Default());

        Assert.NotNull(SourceOf(layer)!.PersistentCache as BruTile.Cache.FileCache);
        Assert.StartsWith(TileCache.DefaultDirectory, status);
    }

    [Fact]
    public void A_cache_that_is_off_leaves_the_source_without_one()
    {
        using var node = new XyzTileLayerNode();

        var layer = node.Update(out _, out var status, Template, enabled: true,
            cache: TileCache.Off(TileCache.DefaultDirectory));

        Assert.Null(SourceOf(layer)!.PersistentCache as BruTile.Cache.FileCache);
        Assert.StartsWith("off", status);
    }
    /// <summary>
    /// Removes the one folder <see cref="The_cache_is_attached_like_the_OpenStreetMap_layer_s"/>
    /// creates in the real cache directory. That test is ABOUT the default folder, so it has to use
    /// it; leaving the folder behind afterwards is a different matter.
    /// </summary>
    public void Dispose()
    {
        var stray = System.IO.Path.Combine(TileCache.DefaultDirectory, TileCache.Slug(Template));
        try
        {
            if (System.IO.Directory.Exists(stray) && System.IO.Directory.GetFiles(stray).Length == 0)
                System.IO.Directory.Delete(stray, recursive: true);
        }
        catch (System.IO.IOException) { /* a stray empty folder is not a test failure */ }
    }
}