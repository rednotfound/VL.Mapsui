# What Mapsui offers, and how much of it is wrapped

Measured 2026-08-14 by reflecting over the assemblies in `deps\` with an assembly resolver
attached, because loading them without one silently drops every type that mentions
NetTopologySuite — which is most of the interesting ones.

| assembly | public types |
|---|---|
| `Mapsui` | 191 |
| `Mapsui.Nts` | 58 |
| `Mapsui.Rendering.Skia` | 40 |
| `Mapsui.Tiling` | 17 |
| **total** | **306** |

This document exists so "what is missing" is a list rather than a feeling. It is also how the
boundary with VL.GIS gets decided: twice already, something we planned to build in VL.GIS turned
out to be sitting in a Mapsui dependency we already ship.

---

## Wrapped

| node | category | what it is |
|---|---|---|
| `OpenStreetMap` | `Mapsui.Layers` | `TileLayer` over the OSM tile source, taking a cache |
| `XYZ` | `Mapsui.Layers` | any slippy-map URL template — the package stops being about one basemap |
| `Feature` | `Mapsui` | NTS geometry + attributes → `NetTopologySuite.Features.Feature`, the neutral type |
| `FeatureLayer` | `Mapsui.Layers` | features + a style → `MemoryLayer`. **The NTS → Mapsui adapter lives here** |
| `VectorStyle` | `Mapsui.Styles` | fill, outline, width. Stateful on purpose — see below |
| `LabelStyle` | `Mapsui.Styles` | writes an attribute of each feature. `LabelColumn` names an attribute, so one style labels a thousand features differently |
| `Geometry` | `Mapsui.Layers` | the shortcut: shapes and one colour, composed from the three above |
| `TileCache` | `Mapsui.Layers` | the disk cache itself: where tiles go and how much is there |
| `Map` | `Mapsui` | the `Map` and its layer collection |
| `ViewportInfo`, `LayerInfo` | `Mapsui` | readers — centre, resolution, size; layer count and busy |
| `CenterOn`, `ZoomToLevel`, `ZoomAt`, `ZoomByWheel`, `DragBetween`, `Refresh` | `Mapsui.Navigate` | the navigator |
| `ZoomToLayer`, `ZoomToLayers` | `Mapsui.Navigate` | put the view where the data is, on a trigger. Framing every frame would pin the view and the map could not be moved |
| `Drag`, `ZoomIn`, `ZoomOut` | `Mapsui.Navigate` | stateful gestures — they remember the previous frame |
| `ScaleBar`, `Attribution`, `ZoomButtons` | `Mapsui.Widgets` | Mapsui's own furniture, added to a map once each |
| `ToSkiaLayer` | `Mapsui.Skia` | the bridge into VL.Skia's scene graph, including the press a widget gets |

---

## Not wrapped

Ordered by what a map patch actually needs.

### Widgets — 3 of 10

Wrapped: `ScaleBar` (metric / imperial / nautical), `Attribution` (a `Hyperlink` fed from the
layers), `ZoomButtons` (`ZoomInOutWidget`). All three confirmed on screen 2026-08-14, buttons
included.

**Corrected here, because this document said otherwise and it was wrong:** not every widget has a
renderer registered. Measured by constructing a `MapRenderer` and reading `WidgetRenders` — a thing
PowerShell cannot do, since it needs SkiaSharp's native library — **9 are registered**:
`BoxWidget`, `ButtonWidget`, `EditingWidget`, `Hyperlink`, `MapInfoWidget`,
`MouseCoordinatesWidget`, `ScaleBarWidget`, `TextBox`, `ZoomInOutWidget`. **`PerformanceWidget` is
not**, so wrapping it means registering a renderer by hand as well.

Not wrapped, with the reason each is not merely "next":

- **`MouseCoordinatesWidget`** needs the host to feed the map a mouse position. This package
  deliberately routes the mouse to `Navigate` nodes in the patch instead, so wiring it quietly
  would re-make the "the node decides what the mouse means" mistake.
- **`PerformanceWidget`** needs the renderer registered by hand *and* render timings fed in from
  `MapsuiLayer` — the only one of the ten that touches the render loop.
- `TextBox`, `ButtonWidget`, `BoxWidget`, `MapInfoWidget`, `EditingWidget`. `MapInfoWidget` is the
  interesting one: it belongs with `Map.OnInfo` and "what did I click on", not with furniture.

**`Map.Widgets` is a `ConcurrentQueue<IWidget>` — append only, no removal.** So a widget node has
to be a `[ProcessNode]`: enqueue once, then drive it through `Enabled` and its properties. A static
method would enqueue a fresh widget sixty times a second, and nothing could ever take them out.

**Clicks are wired, not swallowed.** `Click [Mapsui.Widgets]` takes `Map, X, Y, Pressed` — the same
shape as `Drag` — and reports `Handled`, so the patch can gate dragging with `Left Pressed AND NOT
Handled` and pressing a button does not also start a pan. A first version did this inside
`MapsuiLayer.Notify`, which quietly decided that a left press is what clicking a widget means; see
NOTES.md, 2026-08-14. vvvv's own statement of the idiom is in `VL.Skia`'s *Explanation Mouse and
Keyboard*: the Mouse node is connected to the Renderer it interacts with, and its position is wired
onward.

### Styles — 3 of 28

`VectorStyle` and `LabelStyle` are their own nodes, which was the fix for the geometry layer's pin
count and the Mapsui-idiomatic shape. Mapsui also has `SymbolStyle`, `CalloutStyle`, `RasterStyle`,
`ThemeStyle`, plus `Pen`, `Brush`, `Font`, `Offset`, `Sprite`, `SymbolType`, `PenStyle`,
`PenStrokeCap`, `StrokeJoin`, `UnitType`. `StyleCollection` is used but not exposed: it is how
`LabelStyle` carries an upstream style through, since a layer takes one style and two were needed.

**A style node is stateful, and the reason is worth carrying to the next one.** It holds no
resource, but its *identity* is compared downstream twice: a layer treats a new style object as a
change and rebuilds, and Mapsui keys its rendered-geometry cache on the style object
(`IFeature.RenderedGeometry` is an `IDictionary<IStyle, object>`). Handing out a fresh style per
frame therefore rebuilds every layer holding it. Reverting the cache to prove it turns 7 tests red.

`LabelStyle` is the difference between shapes on a map and data you can read, and what unblocked it
was not the style but the **attributes**: a label names a column, so it needed a patch to be able to
build `Feature`'s attribute dictionary at all. `Collections.Dictionary`'s `Add` does it, with an
unconnected `Input` for the empty one to start from — measured 2026-08-14, and
`HowTo Label your data.vl` is the example.

`SymbolStyle` is the next one worth doing, and it is what points currently lack: `VectorStyle`
draws lines and fills, so a `POINT` handed to `FeatureLayer` today is labelled but not marked.

### Layers — 2 of ~10

Wrapped: `MemoryLayer`, `TileLayer`. Not: `ImageLayer`, `RasterizingLayer`, `RasterizingTileLayer`,
`WritableLayer`, `MyLocationLayer`, `ObservableMemoryLayer<T>`, `GenericCollectionLayer<T>`,
`VertexOnlyLayer`, `AnimatedPointLayer`.

`WritableLayer` matters for interaction: it is the one the editing tools mutate.

### Data sources — 0

- **`Mapsui.Nts.Providers.Shapefile.ShapeFile`** — reads Shapefiles, with a quadtree index. VL.GIS's
  roadmap has "File I/O via `MaxRev.Gdal.Core` — Shapefile, GeoTIFF" listed as a *Later* item, and
  half of it is already sitting in a package this repository ships.
- **`Mapsui.Providers.Wms.WmsProvider`** — WMS servers, the standard way public authorities publish
  raster layers.
- **`Mapsui.Providers.Wfs`** — WFS, the vector equivalent (22 types of query-building machinery).
- `MemoryProvider`, `ProjectingProvider`, `FilteringProvider`, `StackedLabelProvider`.

Two tile sources are wrapped now — `OpenStreetMap` and the general `XYZ` template, which covers
most public raster services. Still missing: TMS (`TmsTileSourceBuilder` exists, and its Y axis is
flipped, which is the whole reason it is a separate thing) and WMS-as-tiles.

**Interaction is now the largest gap**, and it is the one with the most to gain: see below.

### Interaction — 0

- `MapInfo` / `MapInfoWidget` / `Map.OnInfo` — what did I click on. The line between a map you can
  look at and one you can use.
- `Mapsui.Nts.Editing` — `EditManager`, `EditMode`, `Geomorpher`, `AddInfo`, `DragInfo`,
  `RotateInfo`: drawing and editing geometry on the map with the mouse. Nine types, and it is the
  most patch-shaped feature Mapsui has.

### Projection — used internally, not exposed

`SphericalMercator`, `Projection`, `ProjectionDefaults`, `CrsHelper`, `CrsIdentifier`, `Mercator`.
We call `SphericalMercator.FromLonLat` in three places. Whether any of this should become nodes is
a boundary question with VL.GIS, which already wraps ProjNet for reprojection — see
`docs/RULES.md` on bundling, and VL.GIS's `docs/DESIGN.md` on the division of labour.

### Animation, fetching, rendering internals

`Mapsui.Animations` (9), `Mapsui.Fetcher` (7), `Mapsui.Rendering` (12) and the Skia render stack.
Mostly machinery a patch should not see. `MouseWheelAnimation` is the exception already met once:
its `Duration` is why a wheel zoom eases rather than jumps, and its `GetResolution` reads only the
*sign* of the delta.

---

## Will not be wrapped

- **The UI hosts.** `Mapsui.UI.*` for MAUI, WPF, Avalonia, Blazor and so on. vvvv is the host here,
  and `ToSkiaLayer` is the whole of that bridge.
- **Renderer internals.** `Mapsui.Rendering.Skia`'s style renderers and caches are how Mapsui draws;
  a patch has no business there.
- **The WFS XML plumbing.** 22 filter and XPath types exist to build one request. If WFS is wrapped
  it gets a node that takes a URL and a layer name, not a filter builder.

---

## How this list gets used

Work through it in batches, verifying each in the GUI before starting the next, and update the
tables above as things move from one section to the other. When a batch reveals that Mapsui
already does something VL.GIS planned to build, record it in both repositories — that has happened
twice now and it is the main reason this document exists.
