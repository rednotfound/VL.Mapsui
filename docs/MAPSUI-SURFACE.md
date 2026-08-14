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
| `Geometry` | `Mapsui.Layers` | `MemoryLayer` + `GeometryFeature` + `VectorStyle`, from NTS geometry |
| `TileCache` | `Mapsui.Layers` | the disk cache itself: where tiles go and how much is there |
| `Map` | `Mapsui` | the `Map` and its layer collection |
| `ViewportInfo`, `LayerInfo` | `Mapsui` | readers — centre, resolution, size; layer count and busy |
| `CenterOn`, `ZoomToLevel`, `ZoomAt`, `ZoomByWheel`, `DragBetween`, `Refresh` | `Mapsui.Navigate` | the navigator |
| `Drag`, `ZoomIn`, `ZoomOut` | `Mapsui.Navigate` | stateful gestures — they remember the previous frame |
| `ToSkiaLayer` | `Mapsui.Skia` | the bridge into VL.Skia's scene graph |

---

## Not wrapped

Ordered by what a map patch actually needs.

### Widgets — 0 of 10

`MapsuiLayer.Render` already hands `_map.Widgets` to the renderer, so **a widget node draws the
moment it exists**. Every one below has a Skia renderer shipped in `Mapsui.Rendering.Skia`.

`ScaleBarWidget` (metric / imperial / nautical), `ZoomInOutWidget`, `MouseCoordinatesWidget`,
`PerformanceWidget`, `TextBox`, `ButtonWidget`, `BoxWidget`, `Hyperlink`, `MapInfoWidget`,
`EditingWidget`.

**`Map.Widgets` is a `ConcurrentQueue<IWidget>` — append only, no removal.** So a widget node has
to be a `[ProcessNode]`: enqueue once, then drive it through `Enabled` and its properties. A static
method would enqueue a fresh widget sixty times a second.

Attribution is not a feature request. **OSM's tile policy requires the attribution to be
displayed**; our layer node has carried the text since the beginning and nothing has ever drawn it.
`Hyperlink` (it has `Url` and `Text`) is the piece that closes that.

### Styles — 1 of 28

We construct one `VectorStyle` inside the geometry layer and expose three pins for it. Mapsui has
`VectorStyle`, `SymbolStyle`, `LabelStyle`, `CalloutStyle`, `RasterStyle`, `StyleCollection`,
`ThemeStyle`, plus `Pen`, `Brush`, `Font`, `Offset`, `Sprite`, `SymbolType`, `PenStyle`,
`PenStrokeCap`, `StrokeJoin`, `UnitType`.

Splitting the style out of the layer node is also the fix for that node's pin count — a separate
`VectorStyle` node is both the Mapsui-idiomatic shape and the one `docs/RULES.md` asks for.

`LabelStyle` is the difference between shapes on a map and data you can read.

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

Only one tile source is wrapped, and that is the largest capability gap after widgets: no custom
XYZ template, no TMS (`TmsTileSourceBuilder` exists), no WMS-as-tiles.

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
