using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using NtsFeature = NetTopologySuite.Features.Feature;
using SphericalMercator = global::Mapsui.Projections.SphericalMercator;

namespace VL.Mapsui;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// This is where the packages meet, and they meet through NetTopologySuite rather than through each
// other. VL.GIS computes geometry - reproject, buffer, intersect, read GeoJSON - and hands out plain
// NTS types; this turns those into something Mapsui draws. Neither package references the other,
// and neither needs to: NTS is the vocabulary they already share, which is also why the same node
// works for geometry from any other source. Uses Mapsui and NetTopologySuite.
//
// Coordinates are WGS84 longitude and latitude, x first. That is what VL.GIS produces and what
// every GeoJSON file carries; Mapsui works in spherical mercator, so this projects on the way in.
// Doing it here rather than making the patch do it is the point of the node: which projection a map
// engine happens to draw in is not a decision anyone wants to take.
//
// The SRID on a geometry is read, since 2026-11-15 (VL.PROJ made non-4326 geometry a thing a patch
// can hold): 0 and 4326 are longitude and latitude and are projected, 3857 is already the map's
// own system and is copied as it is, and anything else is NOT drawn and Status says so. It is not
// reprojected here: that needs PROJ, and a map engine has no business requiring a native library
// to draw a point. VL.PROJ's Reproject is one node upstream.
//
// A process node rather than a static method, for the usual reason - see the OpenStreetMap layer.
// Rebuilding a layer every frame is how a map ends up refetching and re-styling sixty times a
// second.

/// <summary>
/// Draws geometry over the map in one colour.
/// </summary>
/// <remarks>
/// Geometry is in longitude and latitude (WGS84), x first.
/// A geometry whose SRID is 3857 is drawn as it is; any other SRID but 0 and 4326 is not drawn.
/// </remarks>
[ProcessNode(Name = "Geometry", Category = "Mapsui.Layers")]
public class GeometryLayerNode : IDisposable
{
    // The shortcut is composed of the three nodes it stands in for rather than being a fourth
    // implementation of the same thing. If they compose here they compose in a patch.
    readonly VectorStyleNode _style = new();
    readonly FeatureLayerNode _layer = new();

    Geometry[] _geometries = Array.Empty<Geometry>();
    int[] _srids = Array.Empty<int>();
    NtsFeature[] _features = Array.Empty<NtsFeature>();

    /// <summary>Layers built by this node. Should settle at 1 and stay there.</summary>
    internal int LayersBuilt => _layer.LayersBuilt;

    /// <summary>How often the shapes on the layer were replaced.</summary>
    internal int FeatureSetsBuilt => _layer.FeatureSetsBuilt;

    // Feed it geometry from VL.GIS - GIS.Geometry, GIS.Serialization, GIS.Projection - in WGS84
    // lon/lat. An empty or null input gives no layer rather than an empty one, so a Map can be wired
    // up before there is anything to draw.
    //
    // Watch Layers Built. A number that climbs frame after frame means one of the inputs is changing
    // every frame; the geometry set is compared by identity, so building new geometry objects each
    // frame rebuilds the layer even when the shapes are the same.

    /// <summary>
    /// Configures the geometry layer.
    /// </summary>
    /// <remarks>
    /// The geometries are compared by identity, so build them once rather than every frame.
    /// </remarks>
    /// <param name="layersBuilt">How many times the layer was built. It should settle at 1.</param>
    /// <param name="status">The number of shapes, and any that are not drawn because of their SRID.</param>
    /// <param name="geometries">The shapes to draw, in longitude and latitude.</param>
    /// <param name="fillColor">Fill for polygons and points.</param>
    /// <param name="lineColor">Colour of lines and outlines.</param>
    /// <param name="lineWidth">Line width in pixels.</param>
    /// <returns>The layer, for a Map's Layers, or nothing while there is no geometry.</returns>
    public ILayer? Update(
        out int layersBuilt,
        out string status,
        IEnumerable<Geometry>? geometries = null,
        Color4? fillColor = null,
        Color4? lineColor = null,
        float lineWidth = 2f)
    {
        // Empty geometries are skipped as nulls are. Since VL.NetTopologySuite 0.0.8 a node whose input
        // is missing gives an empty geometry rather than null, and Mapsui 4.1.9 turns an empty
        // geometry's null envelope into a box from (-1, -1) to (0, 0) (measured): one empty feature
        // stretched the layer's extent to the mercator origin, off the coast of Africa.
        var incoming = geometries?.Where(g => g is not null && !g.IsEmpty).ToArray() ?? Array.Empty<Geometry>();

        // Features are rebuilt only when the geometry set changes. Building them every frame would
        // hand the layer a new set every frame and rebuild it, which is the whole failure this
        // package guards against - and it would be hidden inside a convenience node, which is
        // worse than having it in the open.
        //
        // The SRIDs are compared beside the geometries, as numbers: NTS's equality looks at
        // coordinates only, and the same coordinates under another SRID are another place. Without
        // this, typing a different SRID into SetSRID upstream left the old shape on the map.
        var srids = Array.ConvertAll(incoming, g => g.SRID);
        if (!incoming.SequenceEqual(_geometries) || !srids.SequenceEqual(_srids))
        {
            _features = incoming.Select(g => FeatureHelper.Feature(g)).ToArray();
            _geometries = incoming;
            _srids = srids;
        }

        // Status was swallowed here until 2026-11-15, when it had only style warnings to give and
        // this node builds its own style. A geometry in a CRS the map cannot take is a thing it can
        // now say, and an empty map with no reason is the failure this family keeps paying for.
        return _layer.Update(out layersBuilt, out status, _features,
            _style.Update(fillColor, lineColor, lineWidth), name: "Geometry");
    }

    /// <summary>
    /// The same geometry in spherical mercator, which is what the map draws in.
    /// </summary>
    /// <remarks>
    /// On a copy, through NTS's own coordinate filter. Projecting in place would mutate geometry
    /// the patch still holds — the caller would find its own WGS84 shapes silently turned into
    /// metres, and every later reprojection would compound it.
    /// </remarks>
    internal static Geometry ToMercator(Geometry geometry)
    {
        var projected = geometry.Copy();
        if (!IsWebMercator(geometry.SRID))
        {
            projected.Apply(new LonLatToMercator());
            projected.GeometryChanged();
        }

        // The copy says what it is. Copy() carries the input's SRID across, and metres labelled
        // 4326 is a lie that ToLonLat's caller would then hand back to the patch.
        projected.SRID = WebMercator;
        return projected;
    }

    internal const int Wgs84 = 4326;
    internal const int WebMercator = 3857;

    // 0 is NTS's "nobody said" (and -1 some readers' spelling of it), which every geometry had
    // before SetSRID existed; it keeps meaning longitude and latitude, as it always did here.
    internal static bool IsLonLat(int srid) => srid <= 0 || srid == Wgs84;

    internal static bool IsWebMercator(int srid) => srid == WebMercator;

    /// <summary>Whether the map can take this geometry: longitude and latitude, or its own EPSG:3857.</summary>
    internal static bool CanDraw(Geometry geometry) => IsLonLat(geometry.SRID) || IsWebMercator(geometry.SRID);

    sealed class LonLatToMercator : ICoordinateSequenceFilter
    {
        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            var (x, y) = SphericalMercator.FromLonLat(seq.GetX(i), seq.GetY(i));
            seq.SetX(i, x);
            seq.SetY(i, y);
        }
    }

    /// <summary>
    /// The same geometry back in WGS84 longitude and latitude — the inverse of <see cref="ToMercator"/>.
    /// </summary>
    /// <remarks>
    /// **The boundary has to be crossed in both directions, or it is not a boundary.** Geometry
    /// enters this package as WGS84 and is projected on the way in; anything handed back out — a
    /// picked feature, most obviously — has to arrive in the coordinates the patch speaks, or the
    /// patch is left holding metres it never asked for and no node to convert them.
    ///
    /// On a copy, for the same reason as the way in.
    /// </remarks>
    internal static Geometry ToLonLat(Geometry geometry)
    {
        var projected = geometry.Copy();
        projected.Apply(new MercatorToLonLat());
        projected.GeometryChanged();
        projected.SRID = Wgs84;
        return projected;
    }

    sealed class MercatorToLonLat : ICoordinateSequenceFilter
    {
        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            var (lon, lat) = SphericalMercator.ToLonLat(seq.GetX(i), seq.GetY(i));
            seq.SetX(i, lon);
            seq.SetY(i, lat);
        }
    }

    /// <summary>Releases the layer it composed.</summary>
    public void Dispose() => _layer.Dispose();
}
