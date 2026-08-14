using BruTile.Cache;
using BruTile.Web;

namespace VL.Mapsui.Tests;

/// <summary>
/// Nobody but us attaches a cache, and these are the tripwires that say so.
/// </summary>
/// <remarks>
/// Written on 2026-08-14 while looking for whoever wrote 444 tiles next to two repositories. The
/// answer turned out to be us, handed the wrong folder — but ruling out a cache we had not
/// attached was what made that conclusion trustworthy rather than assumed, and the same question
/// will come back the next time a tile appears somewhere unexpected. Cheaper as two tests than as
/// an afternoon.
///
/// The surviving remains of a throwaway probe: what it *found* is here, what it searched with is
/// not.
/// </remarks>
public class ForeignCacheDefaultsTests
{
    [Fact]
    public void Mapsui_ships_the_OSM_tile_source_with_no_persistent_cache()
    {
        // If a future Mapsui attached one of its own, tiles would start appearing in a folder we
        // never chose and every pin here would still read correctly.
        var raw = global::Mapsui.Tiling.OpenStreetMap.CreateTileLayer("VL.Mapsui test");

        var cache = (raw.TileSource as HttpTileSource)?.PersistentCache;

        Assert.IsType<NullCache>(cache);
    }

    [Fact]
    public void The_one_global_cache_hook_in_the_stack_is_unset()
    {
        // Mapsui.Tiling.OpenStreetMap.DefaultCache is public and static: anything loaded into the
        // same vvvv could set it, and every OSM layer built afterwards would quietly write
        // somewhere else. It is the only such hook in Mapsui, Mapsui.Tiling, Mapsui.Rendering.Skia
        // and BruTile - found by reflecting over all four for cache-typed statics.
        Assert.Null(global::Mapsui.Tiling.OpenStreetMap.DefaultCache);
    }
}
