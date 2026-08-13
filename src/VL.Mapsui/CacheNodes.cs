using VL.Core.Import;

using VLPath = VL.Lib.IO.Path;

namespace VL.Mapsui;

/// <summary>
/// Where the tile cache is and how much is in it.
/// </summary>
/// <remarks>
/// **This node exists because the default location cannot be a pin's initial value.** A C# default
/// parameter value has to be a compile-time constant (CS1736) and the folder is under LOCALAPPDATA,
/// which is only known at runtime. Hardcoding a literal instead would ship one machine's path
/// inside the node definition — the shape of VL.Audio's Filename pin, which arrives reading
/// <c>C:\temp\foo.wav</c>.
///
/// vvvv's own answer for a machine-dependent path is a node that yields it: <c>SystemFolder</c> in
/// category IO takes a SpecialFolder and outputs a Path. This is the same move. An empty Cache
/// Folder pin means the default, and this node is how you see what that resolved to — without
/// switching a tile layer on, since it reads the disk and never the network.
/// </remarks>
[Name("Layers")]
public static class CacheNodes
{
    /// <summary>
    /// The folder tiles are cached in, plus what is already there.
    /// Leave Folder empty to see the default location.
    /// </summary>
    /// <remarks>
    /// Feed the same value to an OpenStreetMap node's Cache Folder pin and both agree by
    /// construction. Reading the size is throttled, so this is safe to leave in a patch.
    /// </remarks>
    public static VLPath CacheFolder(VLPath? folder, out int tiles, out float sizeMB)
    {
        var resolved = TileCache.Resolve(folder?.Value);
        var (count, bytes) = TileCache.Stats(resolved);

        tiles = count;
        sizeMB = (float)(bytes / 1024.0 / 1024.0);
        return new VLPath(resolved);
    }
}
