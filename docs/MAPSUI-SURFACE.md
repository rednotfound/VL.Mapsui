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
| `SymbolStyle`, `StyleByGeometry` | `Mapsui.Styles` | what a point looks like, and one style per geometry type |
| `VisibleRange` | `Mapsui.Layers` | the zoom levels a layer is drawn at — the only thing that reduces a busy map |
| `Pick`, `ScreenToWorld`, `WorldToScreen` | `Mapsui` | asking the map what is under a coordinate, and back |
| `ToFeatures`, `Split` | `Mapsui` | a patch's own records in, geometry and attributes out |
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

### Layers — draw order and visibility

**`LayerCollection` is an ordered list and nothing else.** `Add`, `Insert(index, …)`,
`Move(index, layer)`. No `MoveToTop`, no priority, no groups — Mapsui's layer **groups are a v5
feature** and 4.1.9 does not have them. `ILayer` carries thirteen properties and not one of them is
ordering. Index 0 is the bottom.

So order is position, which in a patch means the spread handed to `Map`. See
`docs/ARCHITECTURE.md` for why that is right and why no `Mapsui.Group` node exists: `Cons` is
already the group node on this chain.

**`MinVisible` / `MaxVisible` are wrapped as `VisibleRange`**, and they were worth checking before
wrapping: `get_MinVisible` occurs **zero times** in `Mapsui.Rendering.Skia.dll`, the same reading as
`LabelColumn` (works) and `UnitType` (dead). Rendered, they are a hard cut — 91204 pixels inside the
range, **0** outside. Two facts the node exists to hide: they are **read-only on the `ILayer`
interface** and settable only on `BaseLayer`, and they are **resolutions**, so a higher zoom level
is a smaller number and the two ends swap over.

**`Enabled` is checked in the Skia renderer itself** (`get_Enabled` appears there), so a disabled
layer costs nothing and draws nothing.

**`Enabled` is also the zero-cost way to switch basemaps, and the only one.** `TileLayer.RefreshData`
short-circuits on `if (Enabled …)` (`TileLayer.cs:105`), so a disabled layer in the collection issues
no requests **and keeps its 200–300 tile memory cache** (`MemoryCache<IFeature?>(200, 300)`, created
per layer in the `TileLayer` constructor). Switching back is then instant and touches neither disk
nor network.

Three things make this worth writing down rather than leaving to instinct:

- **Removing a layer from the collection does *not* preserve it.** `LayerCollection.cs:175-179`
  calls `AbortFetch()` **and `ClearCache()`** on everything it removes, and `Clear()` does the same.
  So "swap which layer is in the map" throws the memory cache away exactly as rebuilding does.
- **There is no `SetSource`.** `TileLayer.TileSource` is a get-only property over a `private
  readonly` field, and the schema, extent and attribution are all baked in at construction. Changing
  the source means a new layer, full stop.
- It is what everyone else does where they can: OpenLayers `setVisible(false)` keeps its 512-tile
  LRU, MapLibre's `visibility: none` retires tiles into `SourceCache._cache`, and Mapsui's own WPF
  `LayerList` sample toggles `Enabled` and `Opacity` and never touches the source. Leaflet cannot do
  it at all and destroys its tiles on `removeLayer` — which is a useful reminder that rebuilding is
  acceptable, not that keeping is unnecessary.

**But reuse the tile SOURCE either way.** Every `HttpTileSource` owns an `HttpClient`, is not
`IDisposable`, and is never released; one per rebuild is a leaked connection pool per rebuild. Both
layer nodes cache their sources for this reason — see NOTES.md, 2026-08-17.

**`Layer.Opacity` is not wrapped yet.** It is the third dimension of stacking after order and
visibility, and it is one settable double.

**Labels do not rise to the top, and no ordering will make them.** A label is part of a feature's
style inside a layer, so a layer above covers the labels of a layer below — measured 2026-08-16,
label ink 28 pixels alone and 0 under a polygon layer. Cartography says labels go last; Mapsui says
layers go in order. The way to have both is a **label-only layer placed last**, which is what
Mapbox GL does with its symbol layers.

### Styles — 5 of 28

`VectorStyle`, `LabelStyle`, `SymbolStyle` and `StyleByGeometry` are their own nodes, which was the
fix for the geometry layer's pin count and the Mapsui-idiomatic shape. Mapsui also has
`CalloutStyle`, `RasterStyle`, `GradientTheme`, plus `Pen`, `Brush`, `Font`, `Offset`, `Sprite`,
`SymbolType`, `PenStyle`, `PenStrokeCap`, `StrokeJoin`, `UnitType`. `StyleCollection` is used but
not exposed: it is how `LabelStyle` carries an upstream style through, since a layer takes one style
and two were needed.

**`StyleByGeometry` is `Mapsui.Styles.Thematics.ThemeStyle`** — one style per geometry type, chosen
per feature as it is drawn. It exists because `SymbolStyle` draws points and **nothing else**, which
makes every map read from a real file a mixed-geometry problem. Three pins: Point, Line, Polygon.
See "styling mixed geometry" below; it is the one design decision in this package that was taken
twice.

`Mapsui.Styles.Thematics.GradientTheme` is the value-driven sibling — style interpolated across a
numeric attribute, which is what a choropleth is. Not wrapped; it is the obvious next thematic node
and the mechanism is already proven by `StyleByGeometry`.

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

`SymbolStyle` is wrapped now: `Shape` (our own `SymbolShape` — Ellipse, Rectangle, Triangle),
`Scale`, `Fill Color`, `Outline Color`. It is the style **for points** and takes `VectorStyle`'s
place in the chain rather than sitting beside it, because Mapsui's `SymbolStyle` *derives from*
`VectorStyle`.

What it buys is not visibility but *choosing* — and the measurement behind that sentence was
sharpened on 2026-08-16. A point with no symbol style is drawn as a **ring**: the fallback covers
**180 pixels** where a filled marker covers **952**, which is a 32-pixel disc plus its outline. So
"a point is already drawn" was true and misleading; against a busy basemap a ring is close to
invisible, and this node is five times the ink before anyone picks a colour. `Scale` multiplies
`SymbolStyle.DefaultWidth`, which is **32** — doubling it quadruples the area, asserted as a ratio
in `SymbolStyleTests` because antialiasing moves the exact count.

`SymbolType.Image` is deliberately absent from our enum: it needs a `BitmapId` from Mapsui's
`BitmapRegistry`, so loading, ownership and disposal of an image come with it, and none of that is a
style decision. A marker made from a file is its own node when it exists.

**`UnitType` is in the list above but will not be wrapped, and the reason is measured rather than
chosen.** It offers `Pixel` and `WorldUnit`, which is exactly the marker-versus-measurement switch a
map wants, and `SymbolStyle.UnitType` selects between them. The Skia renderer never reads it: a
rectangle at scale 1 draws **1156 pixels under both settings at both zoom levels**, and the string
`UnitType` appears **zero times** in `Mapsui.Rendering.Skia.dll`. The pin was written, measured and
removed — a pin that does nothing is worse than no pin. `SymbolStyleTests` keeps the finding as an
assertion that goes red if a future Mapsui implements it.

The way to get a shape whose size means something is to **buffer the point into a polygon** and
style it with `VectorStyle`. That is not a workaround so much as the better answer: it scales with
the map because it is on the map, and it is pickable, intersectable and measurable, which a symbol
never is. Spherical mercator stretches with latitude, so a "unit" is a metre only at the equator and
about 0.82 of one at Kyoto — buffer in a metric projection when the number has to be metres.

**`LabelStyle.BackColor` defaults to opaque white**, which is the one Mapsui default we override
without exposing. Left alone it paints a solid box behind every label, centred on the feature being
labelled — it hid two hundred markers on 2026-08-16 and cut the drawn pixels by 71%. A halo and a
box do the same job and we already ship the halo.

**`Offset`, `HorizontalAlignment` and `VerticalAlignment` are used but not exposed either**, and
they are how a label gets out of its marker's way. Mapsui centres a label on its feature —
`Offset (0,0)`, both alignments `Center` — which is right for a polygon and wrong for a point with
a symbol on it. `LabelStyleNode` looks for a `SymbolStyle` on its `Style` input (recursively, since
`StyleCollection` nests), reads `SymbolScale`, and lifts the label by `16 × scale + outline/2 + 4`
pixels. **No pin, because nothing needs to be asked** — the node is already holding the symbol whose
size is the answer, and a patch that adds nothing gets the cartographically correct result. No
symbol upstream and nothing moves, which keeps a polygon's label at its centroid where it belongs.

`VerticalAlignment.Bottom` is what makes the arithmetic simple: measured 2026-08-16, it pins the
text's *bottom edge* to the offset point, so a 10-point and a 30-point label both end at y 199 and
the clearance never has to include half a text height.

**`CollisionDetection` is not set, and that absence is a measurement.** Eight labels three pixels
apart draw 763 pixels with it `true` and 763 with it `false` — inert on our render path, like
`UnitType`. Nothing here declutters labels; the answer for dense ones is not to draw them, which is
what `IStyle.MinVisible` / `MaxVisible` are for and is not wrapped yet.

**Before wrapping any Mapsui property, render it.** Three have now turned out to be inert
(`SymbolStyle.UnitType`, `LabelStyle.CollisionDetection`) or to behave differently from their name.
And the cheap check is only a hint: `LabelColumn` appears **zero times** in
`Mapsui.Rendering.Skia.dll` and works perfectly, because the read happens inside `Mapsui.dll`. A
zero in a metadata scan is a reason to measure, never a verdict — `ThemeStyle` also scores zero and
also works, which is exactly the pair that makes the scan useless on its own.

### Styling mixed geometry — the decision taken twice

**`SymbolStyle` draws points and erases everything else**, measured 2026-08-16: a polygon under one
covers **0 pixels**, where a `VectorStyle` covers 14884 and *no style at all* still manages 956. A
raw `Mapsui.Styles.SymbolStyle` behaves the same, so the renderer dispatches on the style's runtime
type and `SymbolStyle : VectorStyle` buys nothing. Any file with more than one geometry kind — which
is any real file — loses half its contents to this, silently.

**The first fix was wrong and shipped for an afternoon.** A `Style` pin on `SymbolStyle` stacked a
`VectorStyle` underneath so the polygon renderer would find one. Polygons returned; every point
gained a second concentric circle, because a `VectorStyle` draws its own 32-pixel marker there too,
and `Scale` below 1 stopped doing anything — 0.6 measured 22 pixels across alone and 34 stacked,
which is simply the default. **A fix that adds an artifact is not a fix.**

**The right answer already existed in Mapsui and in every other mapping library**: dispatch by
geometry type. OpenLayers takes a style function returning
`styles[feature.getGeometry().getType()]`; Mapbox GL splits `circle` / `line` / `fill` / `symbol`
into layer types; Leaflet has `pointToLayer` beside `style`; a QGIS layer is single-geometry-type
with its own renderer. Mapsui's version is `ThemeStyle`, and `StyleByGeometry` wraps it. Each
feature is drawn once, by the style meant for it — every geometry type through the node puts down
exactly the pixels its style puts down alone.

Two facts about `ThemeStyle` worth knowing before touching it:

- **`GetStyle` cannot be overridden.** Reflection reports it virtual, which is only how an interface
  implementation looks; the compiler answers `CS0506`. Pass the dispatch to the base constructor as
  a function over the constructor's *parameters* — C# forbids `this` there.
- **A `null` from `GetStyle` draws nothing and throws nothing.** So an unwired geometry pin is a
  silent disappearance, and `FeatureLayer`'s `Status` names it: *"1 feature needs StyleByGeometry's
  Polygon pin"*.

### `FeatureLayer.Status`

An output pin, added because this node is **the only one that sees the features and the style at the
same time**. Everything else in a patch can be truthful while the screen is empty: `Parses` reads 1,
`Layers Built` reads 1, and a polygon is missing because the style cannot draw it. It reports the
count normally, and otherwise says which features will not be drawn and what to wire.

The claim it replaces was written from memory about how Mapsui divides work between its renderers,
in a document whose whole purpose is to be trusted about exactly that. Left as a marker: plausible
and wrong is the failure mode this file is most exposed to.

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

**Editing is now the largest gap.** Picking is done; drawing and editing geometry on the map is not.

### Interaction — 1 of 2

`Pick` wraps the hit test, through `IRenderInfo.GetMapInfo` rather than through `Map.Info` — the
event route is what a UI control uses, and an event is the wrong shape for a patch that wants a
value per frame. `ScreenToWorld` / `WorldToScreen` sit beside it on `ViewportExtensions`.

Four things had to be measured first, and each is asserted in `MapsuiHitTestFacts`: a layer must set
`IsMapInfoLayer` (default **false**, silent when off), the hit edge is exact, `margin` does not widen
a geometry hit, and a miss still carries a world position. Details and dates in `NOTES.md`,
2026-08-15.

Not wrapped: `MapInfoWidget` (Mapsui's own on-map readout — a patch that has `Pick` can draw its own,
and better), and `MapInfo.MapInfoRecords`, the full stack of everything under the point. `Pick`
returns the topmost; the rest is one pin away the day something needs it.

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
