using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BruTile.Cache;

namespace VL.Mapsui;

/// <summary>
/// The on-disk tile cache: where it goes, how big it is, and how to describe it.
/// </summary>
/// <remarks>
/// **This stores only tiles that were actually drawn**, which is what OpenStreetMap's tile usage
/// policy asks for: "Cache tiles locally according to HTTP caching headers (or at least 7 days if
/// your cache cannot read them)." What the policy forbids is the opposite thing — "any
/// pre-emptive fetching of tiles other than those a user is actively viewing", such as
/// pre-seeding areas or zoom levels. Nothing here fetches anything; it only keeps what the map
/// already asked for, so restarting vvvv stops meaning downloading the same view again.
///
/// Scale, measured rather than guessed: one 256x256 tile is roughly 5 to 50 KB, a window at one
/// zoom level is about a dozen of them, and a working session that pans and zooms a fair amount
/// came out at 1,163 tiles and 24 MB. The whole world at zoom 12 would be 16.7 million tiles and
/// hundreds of gigabytes, and is exactly what the policy forbids.
///
/// **The folder is a pin, not a constant in here.** Where a node writes files is the patch
/// author's business: an installation may want the cache beside the project so it travels with
/// it, on a fast disk, or shared between several patches. Deciding that for them would be the
/// same mistake as deciding what the mouse means.
/// </remarks>
static class TileCache
{
    /// <summary>
    /// Where tiles go when the pin is left empty. Under LOCALAPPDATA rather than in a repository,
    /// so it cannot be committed by accident and deleting one folder resets everything.
    /// </summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VL.Mapsui", "tiles");

    /// <summary>
    /// Seven days is the floor the policy sets for a cache that cannot read HTTP caching headers,
    /// which a plain file cache cannot.
    /// </summary>
    static readonly TimeSpan Expiry = TimeSpan.FromDays(7);

    /// <summary>
    /// An empty or blank pin means the default location.
    /// </summary>
    /// <remarks>
    /// **The default cannot be the pin's initial value, and that is a language rule rather than a
    /// preference**: a C# default parameter value must be a compile-time constant (CS1736), and
    /// LOCALAPPDATA is only known at runtime. Hardcoding a literal would bake one machine's path
    /// into the node definition — which VL.Audio does, shipping a Filename pin that reads
    /// <c>C:\temp\foo.wav</c>.
    ///
    /// vvvv's own answer for a machine-dependent path is a node that yields it: <c>SystemFolder</c>
    /// in category IO takes a SpecialFolder and outputs a Path. <see cref="CacheNodes.CacheFolder"/>
    /// does the same for this one, so the default is discoverable by patching instead of pre-filled.
    /// </remarks>
    public static string Resolve(string? folder)
        => string.IsNullOrWhiteSpace(folder) ? DefaultDirectory : folder!.Trim();

    /// <summary>
    /// A cache at the given folder, or null with a reason if the path cannot be used.
    /// </summary>
    /// <remarks>
    /// A path arrives from a pin, so it can be anything at all. Falling back to the default on a
    /// bad path would be the worst outcome: files would appear somewhere the author did not ask
    /// for, and nothing would say so. This reports the problem instead and leaves the cache off,
    /// which the node surfaces on its status pin.
    /// </remarks>
    public static FileCache? TryCreate(string path, out string? problem)
    {
        problem = null;

        // A Path IOBox stores a relative path whenever it can and hides that from you (the Gray
        // Book says so outright). If a relative one ever reaches here, there is no honest way to
        // root it: relative to the document, to vvvv's install folder, to whatever the working
        // directory happens to be? CreateDirectory would pick the last of those and write tiles
        // somewhere nobody could predict. Say so instead.
        if (!Path.IsPathRooted(path))
        {
            problem = "a relative path has no defined location here - give an absolute folder";
            return null;
        }

        try
        {
            // Fails fast and with a readable message on illegal characters, a missing drive, or a
            // path the process may not write to.
            Directory.CreateDirectory(path);
            return new FileCache(path, "png", Expiry);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                  or ArgumentException or NotSupportedException)
        {
            problem = e.Message;
            return null;
        }
    }

    // ── Size, without hammering the disk ──────────────────────────────────────
    //
    // Walking a directory is cheap once and ruinous sixty times a second, which is the same
    // mistake that took a network down wearing different clothes. Read it rarely and remember the
    // answer - per folder, since the pin means two nodes can be looking at different ones and a
    // single remembered value would have them reading each other's numbers.

    static readonly Stopwatch Since = Stopwatch.StartNew();
    static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    static readonly Dictionary<string, (TimeSpan read, int tiles, long bytes)> Remembered =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tiles on disk and the bytes they occupy, re-read at most every couple of seconds per
    /// folder.
    /// </summary>
    public static (int tiles, long bytes) Stats(string path)
    {
        var now = Since.Elapsed;
        if (Remembered.TryGetValue(path, out var last) && now - last.read < MinimumInterval)
            return (last.tiles, last.bytes);

        var tiles = 0;
        var bytes = 0L;
        try
        {
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                tiles++;
                bytes += f.Length;
            }
        }
        catch (Exception e) when (e is DirectoryNotFoundException or UnauthorizedAccessException)
        {
            // Nothing cached yet, or not readable. Neither is an error worth throwing into a
            // frame; the numbers stay at zero and the folder appears on the first write.
        }

        Remembered[path] = (now, tiles, bytes);
        return (tiles, bytes);
    }

    /// <summary>
    /// One line for the status pin: where the cache is and how much is in it.
    /// </summary>
    public static string Describe(string path)
    {
        var (tiles, bytes) = Stats(path);
        return $"{path} — {tiles} tiles, {bytes / 1024.0 / 1024.0:0.0} MB";
    }
}
