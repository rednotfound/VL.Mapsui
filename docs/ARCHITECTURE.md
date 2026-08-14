# How VL.Mapsui is put together

What this package is responsible for, what it deliberately is not, and the one boundary that keeps
it from becoming isolated. Measurements and dates live in [NOTES.md](../NOTES.md); the rules for
writing a node live in [RULES.md](RULES.md); what is wrapped and what is not lives in
[MAPSUI-SURFACE.md](MAPSUI-SURFACE.md). This file is the shape.

---

## The pipeline

```
        TileCache ──────────┐
                            ▼
                    OpenStreetMap ─────┐
                                       │
NTS Geometry ──► Feature ──┐           ├──► Map ──► ToSkiaLayer ──► VL.Skia Renderer
                           ├──► FeatureLayer                              ▲
            VectorStyle ───┘                                              │
                                                                          │
        Console ──► MouseState ──► Drag · ZoomByWheel · Click · ZoomIn ────┘
                                   (what the mouse means is decided here)
```

`Geometry` still exists as the shortcut for "some shapes, one colour, on the map"; inside, it is a
`VectorStyle` and a `FeatureLayer` composed. It is a convenience, not a fourth implementation.

---

## The boundary that matters

**Geometry crosses package lines as NetTopologySuite, never as ours and never as Mapsui's.**

```
whatever computes geometry            VL.Mapsui                Mapsui
(VL.GIS, a GeoJSON reader, ...)  ──►  adapter          ──►     GeometryFeature
      NTS Geometry / Feature          (FeatureLayer)           MemoryLayer
```

- There is no `VLPoint`, no `VLPolygon`, no `MapsuiPolygonWrapper`. NTS is the shared vocabulary.
- A **feature** is `NetTopologySuite.Features.Feature` — geometry plus an attributes table, and
  nothing about styles, layers or renderers. It already existed and is mature; inventing one here
  would have made this package the definition of the whole domain.
- **The adapter lives on the consuming side.** `FeatureLayerNode.ToMapsui` knows about both worlds
  because Mapsui is what it draws with. Nothing was added to NTS to make this work, and nothing
  ever should be.

Dependency direction, which is the same statement read downwards:

```
VL.Mapsui ──► Mapsui ──► NetTopologySuite        ✅
VL.Mapsui ──► VL.GIS                             ✗ does not exist
VL.GIS    ──► Mapsui                             ✗ does not exist
```

VL.GIS and VL.Mapsui compose **through NTS**, neither referencing the other.
`vvvv-gis\examples\Example Map with data on it.vl` is the patch that proves it, and it lives
outside both packages deliberately: a patch needing two packages cannot ship inside one whose
dependencies do not guarantee the other.

**Coordinates crossing in are WGS84 longitude and latitude, x first** — what GeoJSON carries and
what VL.GIS produces. Mapsui draws in spherical mercator, so the adapter projects on the way in, on
a copy. Which projection an engine happens to draw in is not a decision anyone wants to take.

**A CRS is an identifier** (`"EPSG:3857"`), which is what Mapsui itself uses. No universal CRS
object is invented here.

---

## What decides whether a node holds state

Three reasons, and the first is the expensive one:

1. **It holds a resource.** Connections, file handles, caches, layers, maps. A `public static`
   method is evaluated *every frame* — written that way, the map node opened 17,085 TCP
   connections in 13 minutes and took a home network down. `OpenStreetMap`, `TileCache`, `Map`,
   `FeatureLayer`, `ToSkiaLayer`, every widget.
2. **Its identity is compared downstream.** `VectorStyle` holds nothing, and is still a process
   node: a layer treats a new style object as a change and rebuilds, and Mapsui keys its
   rendered-geometry cache on the style object itself.
3. **It needs the previous frame.** `Drag`, `Click`, `ZoomIn`, `ZoomOut` — a gesture is a
   difference, and making every patch wire a `FrameDelay` for it would be noise.

Everything else is a plain operation: `CenterOn`, `ZoomToLevel`, `Feature`, `ViewportInfo`.

**Every stateful node reports how often it has rebuilt** — `Layers Built`, `Styles Built`,
`Caches Built`. Those pins are not decoration: a number that climbs frame after frame is the
signature of the failure above, and it is the first thing to look at when a map misbehaves.

---

## What this package does not own

| | belongs to |
|---|---|
| geometry processing — buffer, intersect, reproject | NTS, and packages wrapping it |
| file formats — GeoJSON, GeoParquet, Shapefile | focused packages of their own |
| projection infrastructure beyond an identifier | a ProjNet-shaped package |
| the UI host | **vvvv itself.** `Mapsui.UI.*` — the WPF, Avalonia, MAUI and Blazor MapControls — are not wrapped and will not be. `ToSkiaLayer` plus VL.Skia's `Renderer` is the whole of that bridge, and it is what "MapControl" means here |
| what the mouse means | the patch. Every interaction node takes a position and a gate |

---

## Escape hatches

`Map`, `ILayer` and `IStyle` cross node boundaries as Mapsui's own types on purpose. A friendly
wrapper that hides the engine becomes a ceiling; anything Mapsui can do that no node here exposes
is still reachable by a patch that holds the `Map`.
