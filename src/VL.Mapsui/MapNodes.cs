using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// Scaffolding used to bring this package up. Not features.
/// </summary>
/// <remarks>
/// The map itself is <see cref="OpenStreetMapNode"/>, which is a process node rather than a
/// static method here. Anything that acquires a connection, a file handle or a cache has to be,
/// because a static method in VL is re-evaluated on every frame - see that class for what
/// happened when this one was written the other way.
///
/// A second rule applies to whatever gets added next: **no public node here may mention a
/// Mapsui type in its signature yet.** VL builds a node only for methods whose parameter and
/// return types it has imported, and it learns a foreign library's types from a
/// &lt;NugetDependency&gt; declared in the .vl document. This spike is loaded through a
/// ProjectDependency and declares none, so a node returning Mapsui.Map is simply never created:
/// no error, no red node, nothing in the log, just a node greyed out in the patch and dropped
/// from the compiled program along with every link to it.
///
/// Exposing Map is the better API and returns with the nuspec, which is how VL.GIS surfaces
/// BruTile's IHttpTileSource and TileIndex.
/// </remarks>
[Name("Map")]
public static class MapNodes
{
    /// <summary>
    /// A layer that draws a fixed 200x120 pixel box and prints what it measured about the
    /// space it was given. Scaffolding for bringing this package up; not a feature.
    /// </summary>
    /// <remarks>
    /// Put this into a Renderer before wiring the map. If the box appears at the right size,
    /// pixel-space handling works and any remaining problem is Mapsui's. If it does not, there
    /// is no point looking at Mapsui yet. It touches no network and no Mapsui type, which is
    /// what makes it safe to leave running.
    /// </remarks>
    public static global::VL.Skia.ILayer DiagnosticsLayer() => new DiagnosticsLayer();
}
