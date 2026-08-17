using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BruTile.Cache;

namespace VL.Mapsui;

/// <summary>
/// A tile cache on disk: a folder, and the BruTile cache attached to it.
/// </summary>
/// <remarks>
/// This is a **value a patch passes around** rather than a setting on the layer node, which is what
/// <c>TileCache</c> produces and <c>OpenStreetMap</c> consumes. The shape matters: it used to be two
/// pins on the layer node (Cache To Disk, Cache Folder) *and* a separate node showing the default,
/// and the two disagreed — one read a pin, the other read nothing at all. One node owns it now.
/// </remarks>
public sealed class TileDiskCache
{
    internal TileDiskCache(string folder, FileCache? cache, string? problem = null)
    {
        Folder = folder;
        Cache = cache;
        Problem = problem;
    }

    /// <summary>The folder tiles are written to.</summary>
    public string Folder { get; }

    /// <summary>Why this cache is off, when it is off because something went wrong.</summary>
    public string? Problem { get; }

    /// <summary>Whether this cache is switched on. A cache that is off is still a value.</summary>
    /// <remarks>
    /// **"Off" has to be something rather than nothing, and so does "this folder did not work".**
    /// An unconnected pin hands a node null, and so would a connected node that returned null when
    /// switched off or when the folder was unusable — the three would be indistinguishable, and a
    /// layer could not tell "nobody said anything, use the default" from "somebody said no". That
    /// is the same ambiguity that let an empty Path IOBox pass for an empty pin and put 444 tiles
    /// next to two repositories, so it is worth not rebuilding one level up. A test caught exactly
    /// that regression here: a failed folder came back as null and the layer fell back to the
    /// default, silently, which is the one thing this package promises not to do.
    /// </remarks>
    public bool IsOn => Cache is not null;

    /// <summary>
    /// The root cache. **Do not attach this to a tile source** — use <see cref="CacheFor"/>, which
    /// keeps each source's tiles apart. This exists to answer <see cref="IsOn"/>.
    /// </summary>
    internal FileCache? Cache { get; }

    /// <summary>
    /// A cache for one tile source, in its own folder under <see cref="Folder"/>.
    /// </summary>
    /// <remarks>
    /// **Every source needs its own folder, and the reason is a bug this shipped with.** BruTile's
    /// <c>FileCache</c> keys a tile on nothing but <c>{level}/{col}/{row}.png</c>. Point two
    /// different services at one folder and the second one never fetches anything: it asks for
    /// tile 7/63/41, the first service's copy is already there, and it is served that instead.
    ///
    /// The symptom is that changing <c>URL Template</c> appears to do nothing at all. The layer
    /// really is rebuilt — <c>Layers Built</c> counts up, every guard reports success — and the
    /// picture does not change, because no request is ever made. Found on 2026-08-17 by writing a
    /// tutorial whose entire lesson was switching basemaps; 218 unit tests had not, because none of
    /// them used two sources and a cache at once.
    ///
    /// It is worse than a wrong picture. The `Attribution` pin says one provider while the tiles on
    /// screen came from another, so a patch can credit the wrong service in perfect good faith.
    /// </remarks>
    internal FileCache? CacheFor(string sourceKey)
        => Cache is null ? null : new FileCache(Path.Combine(Folder, TileCache.Slug(sourceKey)), "png", TileCache.Expiry);

    /// <summary>The folder, and whether it is being written to.</summary>
    public override string ToString() => IsOn ? Folder : $"{Folder} (off)";
}

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
/// Scale, measured rather than guessed: one 256x256 tile is roughly 5 to 50 KB, and a session over
/// Tokyo at zoom 12 came out at 16 tiles and 736 KB. The whole world at zoom 12 would be 16.7
/// million tiles and hundreds of gigabytes, and is exactly what the policy forbids.
///
/// **The folder is a node, not a constant in here.** Where a node writes files is the patch
/// author's business: an installation may want the cache beside the project so it travels with
/// it, on a fast disk, or shared between several patches. Deciding that for them would be the
/// same mistake as deciding what the mouse means.
/// </remarks>
static class TileCache
{
    /// <summary>
    /// Where tiles go when nothing is connected. Under LOCALAPPDATA rather than in a repository,
    /// so it cannot be committed by accident and deleting one folder resets everything.
    /// </summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VL.Mapsui", "tiles");

    /// <summary>
    /// Seven days is the floor the policy sets for a cache that cannot read HTTP caching headers,
    /// which a plain file cache cannot.
    /// </summary>
    internal static readonly TimeSpan Expiry = TimeSpan.FromDays(7);

    /// <summary>
    /// A folder name for one tile source: readable at the front, unambiguous at the back.
    /// </summary>
    /// <remarks>
    /// The host is kept so that looking in the cache folder tells you who the tiles came from. The
    /// hash is what actually separates them, because one host serves many styles — CyclOSM and
    /// osmfr differ only in a path segment.
    ///
    /// **SHA-256 rather than <c>string.GetHashCode</c>, and that is not fussiness.** .NET randomises
    /// string hash codes per process, so `GetHashCode` would name a different folder on every
    /// launch: the cache would look like it worked, grow without bound, and never once produce a
    /// hit. A cache that silently never hits is indistinguishable from no cache at all, which is
    /// precisely the class of failure this file already exists to prevent.
    /// </remarks>
    internal static string Slug(string sourceKey)
    {
        var key = (sourceKey ?? string.Empty).Trim();

        var host = Uri.TryCreate(key, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : "source";

        foreach (var c in Path.GetInvalidFileNameChars())
            host = host.Replace(c, '-');

        var digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key.ToLowerInvariant()));

        return $"{host}-{Convert.ToHexString(digest)[..8].ToLowerInvariant()}";
    }

    /// <summary>
    /// The folder a pin asks for, or the default when nothing is connected.
    /// </summary>
    /// <remarks>
    /// **Only <c>null</c> means "the default", and only an unconnected pin produces null.** An
    /// empty Path IOBox does *not*: VL stores it as an empty path relative to the document and
    /// hands the node the document's own folder, absolute. That is not a theory — it is what put
    /// 444 tiles next to two repositories, with every guard here reporting success, because from
    /// this method's point of view somebody had named a perfectly good folder. See NOTES.md,
    /// 2026-08-14.
    ///
    /// So there is no "empty means default" rule any more. There cannot be one: on a Path pin,
    /// empty has no representation.
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
    public static TileDiskCache Create(string path)
    {
        // A Path IOBox stores a relative path whenever it can and hides that from you (the Gray
        // Book says so outright). If a relative one ever reaches here, there is no honest way to
        // root it: relative to the document, to vvvv's install folder, to whatever the working
        // directory happens to be? CreateDirectory would pick the last of those and write tiles
        // somewhere nobody could predict. Say so instead.
        if (!Path.IsPathRooted(path))
            return new TileDiskCache(path, null, "a relative path has no defined location here - give an absolute folder");

        try
        {
            // Fails fast and with a readable message on illegal characters, a missing drive, or a
            // path the process may not write to.
            Directory.CreateDirectory(path);
            return new TileDiskCache(path, new FileCache(path, "png", Expiry));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                  or ArgumentException or NotSupportedException)
        {
            return new TileDiskCache(path, null, e.Message);
        }
    }

    // ── The default cache ─────────────────────────────────────────────────────
    //
    // Built once and handed out by reference, because the layer node compares caches by reference
    // to decide whether to rebuild. A fresh instance per frame would rebuild the tile layer sixty
    // times a second, which is the failure this package was rebuilt to undo.

    /// <summary>A cache that is switched off, remembering the folder it would have used.</summary>
    public static TileDiskCache Off(string folder) => new(folder, null);

    static TileDiskCache? _default;

    /// <summary>
    /// The cache used when nothing is connected to a layer's Cache pin. Built once and handed out
    /// by reference; it is off, with a problem, if LOCALAPPDATA itself cannot be written to.
    /// </summary>
    public static TileDiskCache Default() => _default ??= Create(DefaultDirectory);

    /// <summary>One line for a status pin: where this cache writes, or why it does not.</summary>
    public static string Describe(TileDiskCache cache)
        => cache.Problem is not null ? $"cannot cache to {cache.Folder}: {cache.Problem}"
         : !cache.IsOn               ? $"off - every restart refetches the same view ({cache.Folder})"
         :                             Describe(cache.Folder);

    // ── Size, without hammering the disk ──────────────────────────────────────
    //
    // Walking a directory is cheap once and ruinous sixty times a second, which is the same
    // mistake that took a network down wearing different clothes. Read it rarely and remember the
    // answer - per folder, since two nodes can be looking at different ones and a single
    // remembered value would have them reading each other's numbers.

    static readonly Stopwatch Since = Stopwatch.StartNew();
    static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    static readonly Dictionary<string, (TimeSpan read, int tiles, long bytes)> Remembered =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tiles on disk and the bytes they occupy, re-read at most every couple of seconds per
    /// folder.
    /// </summary>
    /// <remarks>
    /// Counts <c>*.png</c> and nothing else. It used to count every file, which is how a folder
    /// holding one patch and no tiles at all reported "1 tiles" — a wrong number that looked
    /// plausible enough to read past.
    /// </remarks>
    public static (int tiles, long bytes) Stats(string path)
    {
        var now = Since.Elapsed;
        if (Remembered.TryGetValue(path, out var last) && now - last.read < MinimumInterval)
            return (last.tiles, last.bytes);

        var tiles = 0;
        var bytes = 0L;
        try
        {
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*.png", SearchOption.AllDirectories))
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
    /// One line for a status pin: where the cache is and how much is in it.
    /// </summary>
    public static string Describe(string path)
    {
        var (tiles, bytes) = Stats(path);
        return $"{path} — {tiles} tiles, {bytes / 1024.0 / 1024.0:0.0} MB";
    }
}
