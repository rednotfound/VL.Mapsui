using VL.Core.Import;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Scaffolding used while bringing this package up, not features. The map itself is composed from
// separate nodes - a layer, a map to put it on, navigation operations and a node that draws it -
// rather than a single node that does everything. A patch decides how they connect, which is the
// reason to reach for a patching environment. The one node here touches no network and no Mapsui
// type, which is what makes it safe to leave running.

/// <summary>
/// Diagnostic nodes for checking the drawing setup.
/// </summary>
[Name("Debug")]
public static class DebugNodes
{
    // Put this into a Renderer before wiring the map. If the box appears at the right size,
    // pixel-space handling works and any remaining problem is Mapsui's. If it does not, there is no
    // point looking at Mapsui yet. Scaffolding for bringing this package up; not a feature.

    /// <summary>
    /// Draws a 200x120 pixel box and prints what it sees of the space it was given.
    /// </summary>
    /// <remarks>
    /// If the box appears at that size, pixel space works and the map can be wired.
    /// </remarks>
    /// <returns>A layer for a Renderer.</returns>
    public static global::VL.Skia.ILayer DiagnosticsLayer() => new DiagnosticsLayer();
}
