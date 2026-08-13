using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BruTile.Cache;

namespace VL.Mapsui;

/// <summary>
/// The on-disk tile cache, and a cheap way to see how big it has got.
/// </summary>
/// <remarks>
/// **This stores only tiles that were actually drawn**, which is what OpenStreetMap's tile usage
/// policy asks for: "Cache tiles locally according to HTTP caching headers (or at least 7 days
/// if your cache cannot read them)." What the policy forbids is the opposite thing — "any
/// pre-emptive fetching of tiles other than those a user is actively viewing", such as
/// pre-seeding areas or zoom levels. Nothing here fetches anything; it only keeps what the map
/// already asked for, so restarting vvvv stops meaning downloading the same view again.
///
/// Scale, so it never becomes a mystery: one 256x256 tile is roughly 5 to 50 KB, a single
/// window at one zoom level is about a dozen of them, and a working session is single-digit
/// megabytes. The whole world at zoom 12 would be hundreds of gigabytes and is exactly what the
/// policy forbids. The overlay prints the real number rather than asking anyone to trust this
/// paragraph.
/// </remarks>
static class TileCache
{
    /// <summary>
    /// Where tiles are kept. Under LOCALAPPDATA rather than in the repository, so it cannot be
    /// committed by accident and so deleting the folder is all it takes to reset.
    /// </summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VL.Mapsui", "tiles");

    /// <summary>
    /// Seven days is the floor the policy sets for a cache that cannot read HTTP caching
    /// headers, which a plain file cache cannot.
    /// </summary>
    static readonly TimeSpan Expiry = TimeSpan.FromDays(7);

    public static FileCache Create() => new(Directory, "png", Expiry);

    // ── Size, without hammering the disk ──────────────────────────────────────
    //
    // Walking a directory is cheap once and ruinous sixty times a second, which is the same
    // mistake that took a network down wearing different clothes. Read it rarely and remember
    // the answer.

    static readonly Stopwatch _since = Stopwatch.StartNew();
    static TimeSpan _lastRead = TimeSpan.FromDays(-1);
    static int _tiles;
    static long _bytes;

    static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tiles on disk and the bytes they occupy, re-read at most every couple of seconds.
    /// </summary>
    public static (int tiles, long bytes) Stats()
    {
        var now = _since.Elapsed;
        if (now - _lastRead < MinimumInterval) return (_tiles, _bytes);
        _lastRead = now;

        try
        {
            var files = new DirectoryInfo(Directory).EnumerateFiles("*", SearchOption.AllDirectories);
            _tiles = 0;
            _bytes = 0;
            foreach (var f in files) { _tiles++; _bytes += f.Length; }
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing cached yet. Not an error, and the directory appears on the first write.
            _tiles = 0;
            _bytes = 0;
        }

        return (_tiles, _bytes);
    }
}
