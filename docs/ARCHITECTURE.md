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

## Draw order: two stacks, both ordered, neither indexed

A 2D map is a stack, so what covers what is the medium rather than an edge case. There are **two
stacking levels here and they are different things**, which the word "layer" hides by meaning both:

```
Renderer
  └─ Group                     VL.Skia layers — the whole map is ONE of them
       ├─ ToSkiaLayer(Map)
       │    └─ Map.Layers      Mapsui layers — a second stack, inside the map
       │         ├─ [0] tiles        bottom, drawn first
       │         └─ [n] data         top, drawn last
       └─ your own Skia drawing      above the map: legends, readouts, UI
```

**Neither level has a z-index.** `Mapsui.Layers.ILayer` has thirteen properties and none of them is
ordering; the string `ZIndex` occurs zero times in `VL.Skia.dll`. Order is position — the index in
`Map.Layers` inside, the spread or pin order in `Group` outside. Mapsui's layer *groups* are a v5
feature; 4.1.9 offers `Add`, `Insert(index, …)` and `Move(index, layer)` and nothing more.

**`Cons` is this chain's group node**, which is why there is no `Mapsui.Group`:

```
VL.Skia:  Group(layers…) ───────────────→ ILayer → Renderer
here:     Cons(layers…) → Spread<ILayer> → Map   → ToSkiaLayer → ILayer → Group → Renderer
```

`Group` exists to composite — it has `Debug` and `Enabled` pins and returns a *single* layer so
groups nest. On this chain the compositor is `Map` itself, and `Cons` supplies the pin group. A
Mapsui-flavoured `Group` would add nothing; folding the pin group into `Map` would cost the spread,
and a spread is what lets layers be generated, concatenated in bands, and sorted with `OrderBy`
when there are many of them.

**The conventional order, bottom to top:** raster → polygon → line → point → labels and
decoration. Build the spread in those bands and concatenate them; that is the v5 layer group done
by hand, and it makes the order legible instead of accidental.

**Two things order alone will not do**, both measured on 2026-08-16 (`NOTES.md`):

- **Labels do not float to the top.** A label belongs to a feature's style *inside* a layer, so an
  upper layer's fill covers a lower layer's labels — label ink went from 28 pixels to 0 with a
  polygon layer above it. The fix is the one Mapbox uses: a **label-only layer, same features,
  last in the spread**.
- **Order does not reduce anything.** Twenty layers all draw whatever the order. `VisibleRange`
  does — it gives a layer the zoom levels it belongs to, and outside them Mapsui skips it entirely
  (a hard cut, measured: 91204 pixels inside the range, 0 outside).

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
VL.Mapsui ──► VL.NetTopologySuite ──► NetTopologySuite     ✅ rendering above processing
VL.Mapsui ──► Mapsui              ──► NetTopologySuite     ✅
VL.NetTopologySuite ──► Mapsui                             ✗ must never happen
VL.GIS              ──► Mapsui                             ✗ does not exist
```

**VL.NetTopologySuite is the package that makes geometry**; this one draws it. The dependency is
declared in the nuspec so `HowTo Draw your own shapes` opens working rather than red — vvvv does not
fetch a missing document dependency by itself. That direction is allowed and the reverse never is:
nothing that computes geometry may learn about a renderer.

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

Everything else is a plain operation: `CenterOn`, `ZoomToLevel`, `ViewportInfo`.

**Every stateful node reports how often it has rebuilt** — `Layers Built`, `Styles Built`,
`Caches Built`. Those pins are not decoration: a number that climbs frame after frame is the
signature of the failure above, and it is the first thing to look at when a map misbehaves.

### A rebuild is not all-or-nothing

Holding state decides *when* a node rebuilds. It does not settle **what a rebuild is entitled to
throw away**, and that turned out to be a second, separate question.

Inside a tile layer node there are two objects with very different economics:

| | cost to remake | who frees it |
|---|---|---|
| `TileLayer` | cheap — `new TileLayer(existingSource)` is legal, and one source may back several layers | `Dispose`, which we call |
| `HttpTileSource` | owns an `HttpClient` and therefore a connection pool | **nobody.** It is not `IDisposable` and BruTile offers no release path |

So the layer nodes rebuild the layer and **keep the source**, keyed by URL. Before that, changing a
basemap leaked a pool per switch — the 17,000-connection incident again, running at one-per-user-
action instead of one-per-frame, which is precisely why it survived a demo.

The general form, worth asking of anything a rebuild constructs: **if a thousand of these exist, who
frees them?** "A finalizer, eventually" and "nobody" are the same answer, and both mean cache it.

Two consequences that are easy to get wrong, and did ship wrong before tests caught them:

- **Settings that live on the reused object must be re-applied every rebuild.** `PersistentCache`
  sits on the source, so a reused source arrives carrying the last one — an early return on "the
  cache is off" left the old `FileCache` attached, giving a pin that reported off while still
  writing.
- **The cache key must cover everything baked in at construction.** BruTile freezes the attribution
  into the source, so a key of URL alone would hand back a source carrying the previous provider's
  credit. Reuse must never change an object's identity behind the caller's back.

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
