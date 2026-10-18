using System;
using VL.Core.Import;

using VLPath = VL.Lib.IO.Path;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// One node owns the cache, and the layer only consumes it. It used to be two pins on the layer
// node plus a separate node that showed the default, which meant two places to set the same thing
// and no guarantee they agreed - they did not, and 444 tiles ended up next to two repositories
// because of it.
//
// A process node rather than a static one: it holds a folder and a BruTile FileCache, and a static
// method is evaluated every frame, so it would build a fresh cache sixty times a second.
//
// Leave Folder unconnected for the default location. Do not connect an empty Path IOBox: there is
// no such thing. VL stores an empty Path as a path relative to the document and hands the node the
// document's own folder, so an "empty" IOBox means "write the tiles next to this patch". That is
// not a hypothetical - it is exactly what happened, silently, with every guard reporting success.
//
// It reads the disk and never the network, so it answers "where do tiles go, and how much is
// there?" without switching a tile layer on. Reading the size is throttled to once every couple of
// seconds, so it is safe to leave sitting in a patch.

/// <summary>
/// Chooses the disk folder tile layers keep their tiles in.
/// </summary>
/// <remarks>
/// Hand the output to a tile layer's Cache pin.
/// Leave Folder unconnected for the default; do not connect an empty Path IOBox, which means the patch's own folder.
/// </remarks>
[ProcessNode(Name = "TileCache", Category = "Mapsui.Layers")]
public class TileCacheNode
{
    TileDiskCache? _cache;
    string _folder = string.Empty;
    bool _enabled;

    /// <summary>Caches built by this node. It should reach 1 and stay there.</summary>
    internal int CachesBuilt { get; private set; }

    // Enabled defaults to ON, unlike a layer node's: a cache touches nobody's network and keeping
    // tiles that were already drawn is what OpenStreetMap's policy asks for. Switching it off means
    // every restart refetches the same view, which is the behaviour the policy is against.
    //
    // Status names the folder actually in use and how much is in it, or says why the cache is off.
    // A folder that cannot be used does NOT silently fall back to the default: files appearing
    // somewhere nobody asked for, with nothing to say so, is the worse outcome.
    //
    // While Enabled is off the node still hands out a cache, one that is off - never null - so a
    // layer can tell "switched off" from "nothing connected" (see TileDiskCache.IsOn).

    /// <summary>
    /// Configures the tile cache.
    /// </summary>
    /// <remarks>
    /// Reads the disk only, never the network, so it is safe to leave in a patch.
    /// </remarks>
    /// <param name="status">The folder in use and how much is in it, or why the cache is off.</param>
    /// <param name="tiles">How many tiles are in the folder.</param>
    /// <param name="sizeMB">How much disk the tiles take, in megabytes.</param>
    /// <param name="enabled">Keeps drawn tiles on disk. On by default.</param>
    /// <param name="folder">Where tiles go. Unconnected uses %LOCALAPPDATA%\VL.Mapsui\tiles.</param>
    /// <returns>The cache, for a tile layer's Cache pin.</returns>
    public TileDiskCache Update(
        out string status,
        out int tiles,
        out float sizeMB,
        bool enabled = true,
        VLPath? folder = null)
    {
        // Resolved even when disabled, so Tiles and Size MB still answer "how much is cached?"
        // without anything being switched on.
        var wanted = TileCache.Resolve(folder?.Value);

        // Rebuild only when what the cache *is* changes. Handing out a fresh instance per frame
        // would rebuild every layer holding it sixty times a second, since a layer compares caches
        // by reference - the per-frame rebuild this package exists to keep out.
        if (_cache is null || enabled != _enabled || !string.Equals(wanted, _folder, StringComparison.OrdinalIgnoreCase))
        {
            // Off is a value and so is a folder that did not work, never null: see
            // TileDiskCache.IsOn. The default folder is handed out as one shared instance, so a
            // layer with nothing connected and a layer wired to this node share a cache rather
            // than opening two on the same directory.
            _cache = !enabled ? TileCache.Off(wanted)
                   : string.Equals(wanted, TileCache.DefaultDirectory, StringComparison.OrdinalIgnoreCase)
                       ? TileCache.Default()
                       : TileCache.Create(wanted);

            _enabled = enabled;
            _folder = wanted;
            CachesBuilt++;
        }

        status = TileCache.Describe(_cache);

        var (count, bytes) = TileCache.Stats(wanted);
        tiles = count;
        sizeMB = (float)(bytes / 1024.0 / 1024.0);
        return _cache;
    }
}
