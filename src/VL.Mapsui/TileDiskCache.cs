using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BruTile.Cache;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// A tile cache on disk: a folder, and the BruTile cache attached to it. This is a value a patch
// passes around rather than a setting on the layer node, which is what TileCache produces and
// OpenStreetMap consumes. The shape matters: it used to be two pins on the layer node (Cache To
// Disk, Cache Folder) AND a separate node showing the default, and the two disagreed - one read a
// pin, the other read nothing at all. One node owns it now.

/// <summary>
/// A tile cache on disk, made by the TileCache node.
/// </summary>
/// <remarks>
/// Hand it to a tile layer's Cache pin.
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

    // "Off" has to be something rather than nothing, and so does "this folder did not work". An
    // unconnected pin hands a node null, and so would a connected node that returned null when
    // switched off or when the folder was unusable - the three would be indistinguishable, and a
    // layer could not tell "nobody said anything, use the default" from "somebody said no". That is
    // the same ambiguity that let an empty Path IOBox pass for an empty pin and put 444 tiles next
    // to two repositories, so it is worth not rebuilding one level up. A test caught exactly that
    // regression here: a failed folder came back as null and the layer fell back to the default,
    // silently, which is the one thing this package promises not to do.

    /// <summary>Whether this cache is switched on.</summary>
    /// <remarks>
    /// Off when the TileCache node is disabled or its folder could not be used; Problem says which.
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
    /// picture does not change, because no request is ever made. Found by writing a
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
