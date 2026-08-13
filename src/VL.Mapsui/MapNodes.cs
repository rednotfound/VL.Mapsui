using VL.Core.Import;

namespace VL.Mapsui;

/// <summary>
/// Scaffolding used while bringing this package up. Not features.
/// </summary>
/// <remarks>
/// The map itself is composed from separate nodes — a layer, a map to put it on, navigation
/// operations and a node that draws it — rather than a single node that does everything. A
/// patch decides how they connect, which is the reason to reach for a patching environment.
/// The one node here touches no network and no Mapsui type, which is what makes it safe to
/// leave running.
/// </remarks>
[Name("Debug")]
public static class MapNodes
{
    /// <summary>
    /// A layer that draws a fixed 200x120 pixel box and prints what it measured about the
    /// space it was given. Scaffolding for bringing this package up; not a feature.
    /// </summary>
    /// <remarks>
    /// Put this into a Renderer before wiring the map. If the box appears at the right size,
    /// pixel-space handling works and any remaining problem is Mapsui's. If it does not, there
    /// is no point looking at Mapsui yet.
    /// </remarks>
    public static global::VL.Skia.ILayer DiagnosticsLayer() => new DiagnosticsLayer();
}
