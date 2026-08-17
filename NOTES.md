# VL.Mapsui — measurement log

Running record of what was actually measured, with dates. Claims without a measurement behind
them do not belong here.

---

## 2026-08-17 — one cache for every tile source, so the second one never fetched

**Switching basemaps did nothing.** Reported by the user in the first minute of testing the patch
whose entire lesson is switching basemaps: change `URL Template`, and the picture does not move.
Their words were "我觉得这里可能有bug", and they were right.

### What was on disk

```
%LOCALAPPDATA%\VL.Mapsui\tiles\
  0  1  10  11  12  13  14  15  16  17  18  19  …      ← zoom levels, and NOTHING else
  2250 tiles, 45.31 MB
```

`TileCache.Create` built one `FileCache(path, "png", Expiry)` and every layer node was handed it.
**BruTile keys a tile on `{level}/{col}/{row}.png` and nothing more**, so all sources shared one
namespace. Point a second service at it and it asks for tile 7/63/41, the first service's copy is
already there, and it is served that. **No HTTP request is ever made.**

### Why it took a human eye

Every signal said fine. The layer really was rebuilt — the guard in `XyzTileLayerNode.Update`
compares the URL and fires correctly. `Layers Built` counts up. `Cache Status` names a real folder
with a real size. No exception, no warning, nothing in the log. The one observable that would have
disagreed is the picture, and only a person was looking at that.

**218 tests missed it because not one of them used two tile sources and a cache at the same time.**
Every cache test used a single `OpenStreetMapLayerNode`. The bug needs two sources to exist at all,
so a suite organised one node at a time could not have contained it — which is an argument about
test *shape*, not test count.

**It found itself the moment a tutorial existed.** First run of `Tutorial 01 Change how the world
looks`, whose whole lesson is that the basemap is a string you own. That is the second job of
VL.Cartography working exactly as claimed, for the second time in two days.

### It is a licensing fault, not only a visual one

`Attribution` carries whatever the pin says while the pixels came from whoever cached first. A
patch can credit the wrong provider in complete good faith, and the screenshot proves compliance
with a service it never contacted.

### The fix, and the trap inside the fix

`TileDiskCache.CacheFor(sourceKey)` gives each source a folder under the cache root:
`tile.opentopomap.org-9f3a2b1c`. Host first so the folder is readable; hash second because one host
serves many styles — `…openstreetmap.fr/hot/` and `…openstreetmap.fr/osmfr/` differ only in a path
segment.

**The hash is SHA-256 and must not be `string.GetHashCode()`.** .NET randomises string hash codes
*per process*. A `GetHashCode`-derived folder name would change on every launch: the cache would
grow without bound, never once hit, and report a perfectly healthy folder the whole time — the same
species of silent failure, reintroduced by the fix for it.

### Verified

224 tests, up from 218. **Negative-tested**: reverting `CacheFor` to hand back the shared cache
fails 5 of them, including `Two_sources_never_read_each_others_tiles`, which writes a tile as one
source and asserts the other cannot find it. One existing test changed with the behaviour —
`Nothing_connected_caches_to_the_default_folder_and_nowhere_else` now asserts the folder is *under*
the default rather than equal to it. Its question is unchanged.

**Old caches are orphaned**, not migrated. They expire in seven days; deleting the folder resets
everything, and the user was asked rather than told.

---

## 2026-08-16 — draw order, and the node that was not built

A 2D map is a stack, so occlusion is the medium rather than an edge case. The question was what
this package should offer for it, and the answer turned out to be **nothing new** — plus one node
about something else entirely.

### There is no z-index anywhere, at either level

| | mechanism |
|---|---|
| `Mapsui.Layers.ILayer` | 13 properties, **not one of them ordering** |
| `LayerCollection` (4.1.9) | `Add`, `Insert(index, …)`, `Move(index, layer)`. Layer **groups are v5** |
| `VL.Skia.dll` | the string `ZIndex` occurs **0 times** |
| `VL.Skia.Group` | variadic pin group — order is pin order |
| `VL.Skia.Group (Spectral)` | one spread — order is spread order |
| `VL.LayerX.Stacker (PinGroup)` / `Stacker` | the same pair again |

**The numeric ordering people remember is real, and it never shipped.** A community `GroupOrder` —
"a wrapper over Group that sorts layers on the basis of their z-index, 0 if not defined" — was
posted for feedback, got one Like and no replies, and auto-closed a year later. Another thread
asking for Skia layer ordering drew "would love to see that too!". Twice wished for, never adopted.

### Two stacking levels, and `Cons` is already the group node

```
Renderer
  └─ Group                     VL.Skia layers - the map is ONE of them
       ├─ ToSkiaLayer(Map)
       │    └─ Map.Layers      Mapsui layers - a second stack, inside
       │         ├─ [0] tiles        bottom
       │         └─ [n] data         top
       └─ your own Skia drawing      above the map
```

The first instinct was a pin group on `Map.Layers`, so it would look like `Group`. **That was the
weaker idea and the user said so.** The analogy shows why:

```
VL.Skia:  Group(layers…) ───────────────→ ILayer → Renderer
here:     Cons(layers…) → Spread<ILayer> → Map   → ToSkiaLayer → ILayer → Group → Renderer
```

`Group` exists to *composite* — `Debug` and `Enabled` pins, and it returns a **single** layer so
groups nest. On this chain the compositor is **`Map` itself**, and `Cons` is already playing the
pin-group half. So a Mapsui `Group` node would do nothing `Cons` does not, and folding the pin
group into `Map` would cost the **spread** — which is the half that matters when layers are
generated, concatenated in bands, or sorted. A numeric z-index would be inventing a mechanism the
field does not use; VL has `OrderBy`, and one visible sorting node beats an ordering hidden inside
a compositor.

**No stacking node was built. The looking is the result.**

### Three measurements that were overdue

**An upper layer buries a lower layer's labels.** Label ink, counted as black pixels:

| | drawn | label ink |
|---|---|---|
| labelled point alone | 1088 px | **28** |
| the same point with a polygon layer **above** it | 91204 px | **0** |
| the same point **above** the polygon layer | 91204 px | 28 |

Confirmed, and structural: Mapsui draws layer by layer, and a label is part of a *feature's style
inside a layer* rather than a layer of its own. So "labels on top", which every cartography text
states as a rule, is not something layer order gives you for free. The fix is Mapbox's: **a
label-only layer, same features, last in the spread.**

**`MinVisible` / `MaxVisible` are honoured, and they are a hard cut.** They read exactly like dead
properties — `get_MinVisible` occurs **zero times** in `Mapsui.Rendering.Skia.dll`, the same
reading as `LabelColumn` (works) and `UnitType` (dead). Rendered instead:

| resolution | with MinVisible 0.5, MaxVisible 2.0 | with no range |
|---|---|---|
| 0.25 | **0 px** | 160000 px |
| 1.0 | 91204 px | 91204 px |
| 4.0 | **0 px** | 6084 px |

**A disabled layer draws nothing**, and `get_Enabled` *does* appear in the Skia renderer, so it is
checked there rather than one assembly along.

### `VisibleRange`, which is the actual answer to "many layers"

Order decides what covers what; it does not reduce anything. A layer that should not be drawn at
this zoom is the only thing that does, and it is what every GIS uses. Three inputs — `Layer`,
`From Zoom`, `To Zoom` — returning **the same layer instance**, so it chains as
`FeatureLayer → VisibleRange → Cons` and rebuilds nothing.

Three things shaped it, all found by reading before writing:

- **`MinVisible`/`MaxVisible` are read-only on `ILayer`** and settable only on `BaseLayer`. The node
  casts, and says so on `Status` when it cannot — rather than accepting a range and ignoring it.
- **Zoom and resolution run in opposite directions**, so `From Zoom` sets `MaxVisible` and `To Zoom`
  sets `MinVisible`. That inversion belongs inside a node, not in everyone's patch.
- **Each end is widened by half a level** (×√2 and ÷√2), so a layer asked for 4 to 8 draws at 4 and
  at 8 and vanishes at 3 and 9 — measured. Setting the boundary exactly would leave whether the
  comparison is inclusive as something a patch author discovers by zooming.

A backwards range is refused and reported rather than obeyed: obeying it hides the layer at every
zoom, which looks exactly like data that failed to load, and getting two inverted pins the wrong
way round is the predictable mistake.

The resolution ladder comes from BruTile's own `GlobalSphericalMercator` level 0 rather than the
widely-copied `156543.03392804` literal — the same number, checked against the formula to ten
decimal places, but taken from the library that also decides which tile a zoom level means.

218 tests. Negative-tested: swapping the two ends inside the node turns three red.

### And a test that was passing on four pixels

The first version of the visibility test used a 300-unit square, which at zoom 4 is smaller than
one pixel of ground: it drew **4 px** and the assertion `> 0` passed. True, and one antialiasing
change away from a mystery failure. The square is a thousand kilometres across now and fills the
viewport at every zoom tested. **A visibility test must fail because the layer is hidden, never
because the shape got too small to see.**

---

## 2026-08-16 — three packages meet, and the fault is in our style model

`VL.GeoJSON` had never been run against anything. One patch — read a GeoJSON string, draw it —
joined it to `VL.Mapsui`, and the interesting part is **where the trouble was not**.

### The join itself was correct before it was tested

| | measured |
|---|---|
| `Read GeoJSON` returns | `Spread<NetTopologySuite.Features.Feature>` |
| `FeatureLayer` accepts | `IEnumerable<NetTopologySuite.Features.Feature>` |
| the link, in the generated C# | `n15.IEnumerable<n14.Feature> Features_30 = (n15.IEnumerable<n14.Feature>)Features_26;` — **`n14` is one alias**, so both packages' `Feature` resolved to one type, and VL inserted the upcast itself |
| coordinates | GeoJSON is WGS84 by mandate (RFC 7946); `FeatureLayer.ToMapsui` projects WGS84 → mercator. Two packages, one convention, no adapter |
| frame loop | `Parses` and `Layers Built` both settled at 1 and stayed |

Six features parsed and drew on the first run. **Neither package references the other**; they
compose because both speak NetTopologySuite, which is a library they share rather than an agreement
they made. That is the whole design working, and it is worth saying because everything below is
about what was wrong *on our side of it*.

Worth noting about VL.GeoJSON's own suite: its `InteropTests` **transcribe** VL.Mapsui's signature
rather than referencing it, and say so in a comment — *"if VL.Mapsui ever changes those signatures,
these tests keep passing while the real thing breaks."* Honest, and it means this patch was the
first time the two assemblies were in one process.

### Predictions, scored

Written down before the run, per the plan.

| # | prediction | outcome |
|---|---|---|
| 1 | headless compile is green | ✅ and the generated C# proved the link exists |
| 2 | `Read GeoJSON` fails in vvvv with a status that reads like a parse error | ❌ **wrong twice over** |
| 3 | moving `NetTopologySuite.Features 2.0.0` aside fixes it | ⬜ never needed |
| 4 | `System.Text.Json` conflicts | ⬜ did not happen |

Prediction 2 was wrong in both halves and both are worth keeping. **The status would not have
misled**: `Defaults.Describe` prints `$"{exception.GetType().Name}: {exception.Message}"`, so a
loader fault would have named `TypeLoadException` first. VL.GeoJSON got that right and I assumed it
had not. **And the failure never happened at all** — see below.

### The landmine that did not fire, because I defused it while predicting it would

`%LOCALAPPDATA%\vvvv\gamma\nugets\` holds `NetTopologySuite.Features` **2.0.0**; all three packages
declare **2.1.0**. 2.1.0 differs by addition only — `FeatureExtensions.GetOptionalId` and `IUnique`
— and a metadata scan finds both names referenced by `NetTopologySuite.IO.GeoJSON4STJ 4.0.0` *and*
by `VL.GeoJSON.dll`. Every feature in `cities.geojson` carries an `id`, which is the path that uses
them.

Reproduced offline in seconds, one variable:

```
NTS.Features 2.0.0 -> TypeLoadException: Could not load type 'NetTopologySuite.Features.IUnique'
NTS.Features 2.1.0 -> 6 features, SRID 4326, attributes intact
```

**And then it did not happen in vvvv**, because `tools\Open-HelpPatch.ps1` passes
`vl-geojson\deps` as a package repository and that folder holds 2.1.0. I wrote the launcher and the
prediction in the same session and did not notice one disarmed the other.

That refines a line in `CLAUDE.md` rather than contradicting it: the flat folder wins over *a copy
sitting next to an assembly*; **an explicitly passed `--package-repositories` folder wins over the
flat folder.** Whether a real install — which has no `deps\` — hits it is still unmeasured, and is
the one experiment left over from this round.

**Where the 2.0.0 came from, since "why are we still affected by VL.GIS" deserved an answer rather
than a shrug:** `VL.GIS.nuspec` declares `NetTopologySuite.IO.GeoJSON 3.0.0`, whose own nuspec
requires `NetTopologySuite.Features [2.0.0, 3.0.0-A)`. **NuGet takes the floor of a range**, so
2.0.0 was installed — machine-wide, on 2026-02-28 22:05:57, in the same second as ProjNET, SQLite,
Newtonsoft 9 and System.Reactive. The sharp detail: `NetTopologySuite` has the *same* range and
resolved to 2.6.0, because something else asked for 2.6.0 explicitly. Nobody asked for a
`NetTopologySuite.Features` version, so it got the floor. **One missing explicit declaration, five
months of consequences, and uninstalling never touches it** because the folder lives in the user
profile.

### The real bug: `SymbolStyle` erases everything that is not a point

Five cities drew. The one polygon in the file did not, silently.

| polygon under | pixels |
|---|---|
| `VectorStyle` | 14884 |
| **`SymbolStyle`** | **0** |
| no style at all | 956 |

It draws *less than nothing*. A raw `Mapsui.Styles.SymbolStyle` behaves identically, so the
renderer dispatches on the style's runtime type and `SymbolStyle : VectorStyle` does not help. Our
own documentation had said the opposite — *"it takes VectorStyle's place in the chain"* — reasoned
from the inheritance and never rendered against a polygon.

### The wrong turn, and what it looked like on screen

The first fix was a `Style` input on `SymbolStyle`, stacking a `VectorStyle` underneath so the
polygon renderer would find one. The polygon came back. So did an artifact the user spotted
immediately: **a second circle on every point.**

| Scale | symbol alone | with a VectorStyle behind it |
|---|---|---|
| **0.6** | 22 px wide | **34 px wide** |
| 1 | 34 px | 34 px |
| 2 | 68 px | 68 px |

A `VectorStyle` draws its own 32-pixel marker on a point. Two concentric circles in one colour, and
a `Scale` pin that had stopped meaning anything below 1. The user's objection was the correct one
and it was the industry's: **points get a point style, lines get a line style, polygons get a fill.
You dispatch; you do not stack.**

**Searched rather than assumed**, and not one of them stacks:

| | mechanism |
|---|---|
| OpenLayers | a style function returning `styles[feature.getGeometry().getType()]` |
| Mapbox GL / MapLibre | separate layer *types* — `circle`, `line`, `fill`, `symbol` |
| Leaflet | `pointToLayer` for points, `style` for the rest |
| QGIS / ArcGIS | a layer is single-geometry-type; the renderer hangs off the layer |
| **Mapsui** | `Mapsui.Styles.Thematics.ThemeStyle(Func<IFeature, IStyle>)` |

Mapsui had the OpenLayers shape all along. I invented a worse mechanism without looking for the
one that existed.

### `ThemeStyle`, measured before anything was built on it

`ThemeStyle` appears **zero times** in `Mapsui.Rendering.Skia.dll` — the same reading as
`LabelColumn`, which works, and as `UnitType`, which is dead. The scan cannot tell those apart, so
it was rendered:

| | via `ThemeStyle` | the style used directly |
|---|---|---|
| POINT | 352 px, 22 px wide | 352 px, 22 px wide |
| LINESTRING | 802 px | 802 px |
| POLYGON | 14884 px | 14884 px |

Identical in every case. Also settled in the same pass: **a `null` from `GetStyle` draws nothing and
throws nothing** — a silent disappearance, which is why `FeatureLayer`'s `Status` now names an
unwired pin.

So `StyleByGeometry` wraps it. Two implementation facts worth carrying:

- **`ThemeStyle.GetStyle` cannot be overridden.** Reflection reports it `IsVirtual = true`, which
  is only how an interface implementation looks; the compiler says `CS0506`. The dispatch goes to
  the base constructor as a function over the *parameters*, since C# forbids `this` in a
  base-constructor argument.
- **`GeometryTheme` carries its three styles as properties as well as using them.** A `ThemeStyle`
  is a function and therefore opaque, but `LabelStyle` has to find the marker to lift a label clear
  of it, and `FeatureLayer` has to know what cannot be drawn. Neither has a feature to call the
  function with.

### And a latent bug found on the way: a nested `StyleCollection` draws nothing

`VectorStyle → SymbolStyle → LabelStyle` is three styles and two combining nodes, which nests one
collection inside another. Mapsui walks a collection's members and does not recurse:

| | polygon |
|---|---|
| flat `{ Vector, Symbol }` | 14884 px |
| nested `{ { Vector, Symbol }, Label }` | **156 px** — the label text and none of the shape |

`Styles.Combine` splices instead of wrapping. **This could not have appeared while chains were two
nodes long**, and the chain that found it has since been replaced — which is exactly why the test
stays: the next person to add a fourth style node gets a red test instead of a blank screen.

### Score for the round

Two nodes gained a job, one lost one it never should have had:

- **`StyleByGeometry`** — new, three pins, one style per geometry type
- **`FeatureLayer.Status`** — new output pin. It is the only node that sees the features and the
  style at once, so it is the only one that can say "1 feature needs StyleByGeometry's Polygon pin"
  or "a SymbolStyle draws NOTHING for those"
- **`SymbolStyle`** — the `Style` pin added and removed within the day; back to four inputs and to
  saying plainly that it is for points

212 tests. Negative-tested: making the theme return the point style for every geometry turns two
red; reverting `Styles.Combine` to nesting turns another one red.

---

## 2026-08-16 — the label steps aside, and the node already knew how far

With the white box gone the markers came back, and the label was still sitting **on top of the
point it names**. That is not a leftover of the same bug: `Mapsui.Styles.LabelStyle` ships with
`Offset (0,0)`, `HorizontalAlignment.Center` and `VerticalAlignment.Center`, so a label is centred
on its feature by design. Correct for a polygon. Wrong for a point wearing a marker.

Cartography has settled this for a century: **the marker says where, the label says what, and they
do not share pixels.** A point label goes beside its symbol, clear of it by a small gap, with a
halo so it survives whatever is underneath.

### Four measurements, because the last two properties we trusted were dead

| question | measured |
|---|---|
| does `Offset` move the label? | yes — `(0,-20)` moved the ink from y 194..205 to y 174..185. **Negative Y is up** |
| does `Offset` X work? | yes — `(30,0)` moved x 186..215 to 216..245 |
| what does `VerticalAlignment` measure from? | `Top` puts the text below the anchor, `Center` across it, **`Bottom` puts it above** |
| does the font size move it? | at `Center` yes, at **`Bottom` no**: a 10-point label draws to y 199 and a 30-point one *also* to y 199, growing upward |
| how big is a marker really? | scale 1 covers y 183..216 about a centre of 199.5 — radius **16.5**, which is `32/2 × scale` plus half the 2-pixel outline |
| is `CollisionDetection` honoured? | **no.** Eight labels three pixels apart: **763 px with it on, 763 with it off** |

The `VerticalAlignment.Bottom` result is the one that shaped the code. It pins the text's *bottom
edge* to the offset point, so the clearance arithmetic never has to know the font size — otherwise
every size change would need half a text height added back in.

### The change: no pin, because nothing needs to be asked

`LabelStyleNode` already receives the symbol on its `Style` pin — that is why the node exists, since
a layer takes one style and something has to merge the pair. So it reads `SymbolScale`, computes
`16 × scale + outline/2 + 4`, and lifts the label by that much. A patch adds nothing and gets the
right answer.

**No `SymbolStyle` upstream, nothing moves.** The rule keys on what is actually there rather than
on a guess about the geometry: a `VectorStyle` fill or an empty input leaves Mapsui's centred
default alone, which is where a polygon's label belongs.

### The test that says it, and why the previous one could not

`Adding_a_label_never_reduces_what_is_drawn` stays, but it cannot express this. Total ink can rise
while the marker is still half buried — a label overlapping the top of a disc adds text pixels
outside it and removes marker pixels inside it, and the sum goes either way.

So the new one counts only the pixels **inside the marker's own square**, and requires them
unchanged:

| | marker alone | with a label |
|---|---|---|
| scale 0.5 | 248 | 248 |
| scale 1 | 952 | 952 |
| scale 2 | 3718 | 3718 |

Not one pixel differs. Negative-tested against the finished code: removing the `Place` call turns
two tests red, and the clearance follows the marker rather than being a constant that fits one size
(offset **−13** at scale 0.5, **−37** at scale 2).

### `CollisionDetection` was a third dead property, and it was ours

We set it to `true`. It does nothing on our render path, and now it is not set at all, with the
measurement in the comment. Three in two days — `SymbolStyle.UnitType`, and this — is enough to
make the rule explicit: **before wrapping a Mapsui property, render it.**

The corollary matters just as much, and it is why the string scan is only ever corroboration:
`LabelColumn` also appears **zero times** in `Mapsui.Rendering.Skia.dll` and demonstrably works,
because the read happens inside `Mapsui.dll`. A zero in the scan is a reason to measure, never a
verdict.

**What this means for dense labels:** nothing declutters them. Two hundred labels overlap each
other wherever each one sits, and no pin fixes that. The real answer is to stop drawing them —
`IStyle` carries `MinVisible` / `MaxVisible`, so labels can appear only past a zoom level. Whether
that becomes a pin or its own node is deliberately left open.

199 tests.

---

## 2026-08-16 — two hundred markers behind two hundred white boxes

`SymbolStyle` shipped with ten tests, four of them counting actual pixels, all green — and no
symbol appeared in `HowTo Draw many features.vl`. A minimal probe (one point, `SymbolStyle`
straight into `FeatureLayer`) drew correctly through the real `Map → ToSkiaLayer` path, so both the
node and our rendering were fine. **The bug was in the composition, which nothing tested.**

### The measurement that named it

Bisected offline in seconds rather than in GUI rounds — a throwaway test rendering the same
combinations the patch builds:

| | before | after |
|---|---|---|
| 1 point, scale 2, no label | 3718 | 3718 |
| 1 point, scale 2, **with label** | 3292 | 3696 |
| 1 point, scale 0.5, no label | 248 | 248 |
| 1 point, scale 0.5, **with label** | **106** | **269** |
| 200 points, scale 0.5, no label | 29967 | 29967 |
| 200 points, scale 0.5, **with label** | **8666** | **26286** |

Adding a label *reduced* the drawn pixels by 71%. That is not a subtle number and it is not a
plausible one: **ink is monotone — drawing a word on top of a circle cannot leave less on the
canvas.** Something was painting white.

### The cause

`Mapsui.Styles.LabelStyle.BackColor` defaults to **opaque white**. Our node set `ForeColor`, `Font`
and `Halo` and never touched it, so every label painted a solid box centred on the feature it was
naming. Two hundred labels, two hundred boxes, two hundred markers gone. Fixed with
`BackColor = null` — we had already chosen the halo, and a halo and a box do the same job.

The 200-point case is still 12% under its no-label count, and that part is *not* a bug: with
markers 16 pixels apart, one label's white halo overlaps its neighbours' markers. It is legible on
screen; it is noted here so the number is not mistaken for a residue of the same fault.

### What the suite was doing wrong, twice

The first attempt at a composition test **passed against the broken code**, because its feature had
no attributes: the label had nothing to write, so nothing was painted, so nothing was covered. A
double that cannot exercise the failure is not a test of it.

What replaced it asserts the invariant rather than a number:

```
Assert.True(combined >= alone, "a label covered the marker")
```

Nobody would have written "the marker must be 952 pixels", but everybody can agree a label may only
*add* ink. Negative-tested — restoring `BackColor` turns it red on the spot.

### And the pin that was written, measured, and taken back out

The obvious next feature was a `Unit` pin: pixels for a marker, world units for a measurement.
Mapsui appears to offer exactly that — `Mapsui.Styles.UnitType { Pixel, WorldUnit }`, selected by
`SymbolStyle.UnitType`. It was implemented, and then rendered:

| | resolution 1 | zoomed in 2× |
|---|---|---|
| `UnitType.Pixel` | 1156 px | 1156 px |
| `UnitType.WorldUnit` | 1156 px | 1156 px |

All four identical — 34×34, a 32-pixel square plus its outline. The property is **inert**: the
string `UnitType` occurs **zero times** in `Mapsui.Rendering.Skia.dll` (three times in
`Mapsui.dll`, where it is merely declared). So the pin came out again, because **a pin that does
nothing is worse than no pin** — it is a promise the patch cannot keep, and it breaks silently.

The finding is kept as a test that goes red if a future Mapsui implements it, which is the only
event that could change the answer. The way to get a shape sized on the ground today is to
**buffer the point into a polygon** and style it with `VectorStyle`: it scales with the map because
it is on the map, and unlike a symbol it is pickable, intersectable and measurable. Mercator caveat
worth carrying — a "world unit" is a metre only at the equator and about 0.82 of one at Kyoto.

196 tests.

### Two launches lost to `--package-repositories`, and the script that ends it

Opening the patch by hand failed twice in ten minutes, each time with an error naming something
that was not wrong.

**First, with `dist\` alone**, thrown by VL's symbol loader before any window appeared:

```
The referenced symbol source 'Mapsui.dll' couldn't be found.
```

**Then with `dist\` and `deps\`:**

```
Missing package: VL.NetTopologySuite
The reference is ambiguous: Point   … 25 candidates out of NetTopologySuite.dll
```

The ambiguity is worth reading correctly, because it looks like a second, separate fault and is
not: with `VL.NetTopologySuite` absent there is no `NTS.Geometry.Point`, so VL matches the bare
.NET members instead and finds twenty-five of them. **One missing repository, two error messages,
neither naming it.**

There are **three** package repositories and every one is load bearing:

| folder | what it holds | who puts it there |
|---|---|---|
| `dist\` | VL.Mapsui itself | `pack.ps1` |
| `deps\` | Mapsui, Mapsui.Tiling, BruTile … | `build.ps1` |
| `..\vl-nettopologysuite\dist\` | the package the help patches *create* geometry with | packing that repo |

Every headless compile was green throughout, because `tools\Compile-HelpPatches.ps1` builds all
three. The hand-typed command came from `build.ps1`'s own "Next:" block, which named `dist\` alone.
**A script that prints the wrong instruction is worse than one that prints none** — its output
carries the authority of having just succeeded.

Both are fixed, and the second fix is the one that matters: the list now lives in
`tools\Open-HelpPatch.ps1` rather than in a hint, a README or anyone's memory. It resolves the
patch by name, refuses an ambiguous one, checks each repository exists (vvvv ignores a repository
that does not, which is exactly how a missing folder disguises itself as a missing package), and
refuses to start a second vvvv. **Getting the same command wrong twice is a script-shaped problem,
not a discipline-shaped one.**

---

## 2026-08-16 — the default point marker is a ring, and now you can fill it in

`SymbolStyle` is wrapped: `Shape` / `Scale` / `Fill Color` / `Outline Color`. It is the style **for
points** and replaces `VectorStyle` in the chain rather than joining it, because Mapsui's
`SymbolStyle` *derives from* `VectorStyle` — it inherits `Fill` and `Outline` and adds the shape and
the size.

### The measurement that sharpened an earlier one

On 2026-08-15 this log recorded that a `POINT` with a plain `VectorStyle` draws **180 px**, the same
as one with a `SymbolStyle`, and concluded "points are visible today; `SymbolStyle` buys *choosing*".
True, and it hid something. `SymbolStyle.DefaultWidth` is **32**, so a filled 32-pixel disc should be
about 804 px² — nowhere near 180. Measured rather than reasoned:

| | px |
|---|---|
| plain `VectorStyle` on a point (the fallback) | **180** |
| `SymbolStyle` with a fill | **952** |
| a filled 32 px disc, for arithmetic | 804 |

**The fallback draws a ring, not a disc.** 180 is what a 2-pixel stroke around a 32-pixel circle
comes to, and 952 is the disc plus that stroke. So "a point is already drawn" was accurate and
misleading in the same breath: against a busy basemap an unfilled ring is close to invisible, and
this node is five times the ink before anyone changes a colour.

Also pinned, because "scale" without a unit is a guess: **Scale 1 = 32 pixels**, and doubling it
quadruples the area — measured at **3.91×**, asserted as a ratio rather than a count because
antialiasing moves the exact number. The three shapes differ as they should: rectangle 1156 >
ellipse 952 > triangle 610, which is also the check that the shape pin reaches the renderer at all.

### Two decisions worth their reasons

- **`SymbolType.Image` is not in our enum.** It needs a `BitmapId` from Mapsui's `BitmapRegistry` —
  loading, ownership and disposal of an image — and none of that is a style decision.
- **Four input pins**, which `docs/RULES.md` says wants a reason: shape, size and two colours are
  what "what does a point look like" consists of. `SymbolRotation` would have been a fifth and
  belongs to data-driven markers, not to choosing one.

---

## 2026-08-16 — the install path, measured at last

The one assumption on the publishing path that had only ever been reasoned about: **does a
`<dependency>` in the nuspec actually mean the dependency gets installed?** Specifically
`VL.NetTopologySuite`, which every geometry help patch needs and which nothing in the working-tree
setup proves is reachable — `--package-repositories dist;deps;<sibling>\dist` hands vvvv every
folder we happen to have, and a user has none of them.

Measured with vvvv's own `NuGet.exe`, resolving `VL.Mapsui 0.0.1-alpha` from `dist\feed` into a
throwaway folder:

```
ok    Mapsui came along
ok    Mapsui.Tiling came along
ok    NetTopologySuite came along
ok    NetTopologySuite.Features came along
ok    VL.NetTopologySuite came along        <- the one in question
ok    Mapsui.Rendering.Skia came along
      (34 packages in total)
```

**It works**, and the folder it produces is `<id>.<version>\lib\…` — the same shape as vvvv's own
`%LOCALAPPDATA%\vvvv\gamma\nugets\`. So the installed folder can be handed straight to
`--package-repositories`, and the help patches that shipped *inside the package* can be compiled
against it. A red node in vvvv is an unresolved node, and an unresolved node fails the compile, so
that is the strongest evidence available short of publishing.

`tools\Test-Install.ps1` does both halves and is what `pack.ps1` now points at. It previously
pointed at `test\verify.ps1 -EndToEnd` in three places — **a script that does not exist**. A tool
telling you to run something that was never written is its own small lesson: the instruction was
plausible, printed on every pack, and had never been followed.

### What it still does not prove

- that the patches **run** — resolution and node existence only; a GUI round is still the check
- that **nuget.org behaves like a local folder feed** — indexing delay and prerelease resolution
  rules differ, and that gap can only be closed by publishing

### An ordering constraint that publishing makes irreversible

`VL.Mapsui` declares `<dependency id="VL.NetTopologySuite" version="0.0.1-alpha" />`, and
VL.NetTopologySuite is **not on nuget.org** (its repository has no tags at all). Publishing
VL.Mapsui first would put a package on nuget.org that nobody can install — and a version there can
be unlisted but never deleted. **VL.NetTopologySuite goes first**, and doubles as the cheap test of
the nuget.org half: it has fewer dependencies and less to go wrong.

---

## 2026-08-15 — two hundred features from one ForEach, and the yellow pad explained

`HowTo Draw many features.vl` drew three, each costing its own `Landmark.Create` node — which proves
the type and not the name. It now generates **Count** of them (200 by default) from two
`RandomSpread`s through a hand-authored **ForEach region**, and the patch does not grow with the
data.

### The region serialization, since regions are the one construct that breaks silently

Verified across **2,920 shipped `ForEach` regions**:

```xml
<Node Bounds="x,y,w,h" Id="…">                       <!-- regions carry FOUR-value Bounds -->
  <p:NodeReference LastCategoryFullName="Primitive" LastDependency="Builtin">
    <Choice Kind="StatefulRegion" Name="Region (Stateful)" Fixed="true" />
    <Choice Kind="ApplicationStatefulRegion" Name="ForEach" />
  </p:NodeReference>
  <Pin Id="…" Name="Break" Kind="OutputPin" />        <!-- exactly one, in 2920 of 2920 -->
  <ControlPoint Id="…" Alignment="Top" />             <!-- one per spliced input spread -->
  <ControlPoint Id="…" Alignment="Bottom" />          <!-- one per collected output spread -->
  <Patch Id="…" ManuallySortedPins="true">
    <Patch Name="Create"/><Patch Name="Update"/><Patch Name="Dispose"/>
    …inner Nodes only…
  </Patch>
</Node>
```

- **A control point Id is one graph node**: the outer link points *at* it, the inner links come
  *from* it. There is no "item" element and **no attribute marks a splice** — splicing is purely
  topological, and `VL.Lang`'s `IsSplicer` / `SplicerControlPoint` never reach the file.
- **Every link lives in the enclosing patch's list**, outer, inner and border-crossing alike. A
  region's `<Patch>` contains no `<Link>` and no `<Canvas>`.
- **A value that must not be spliced crosses directly**, outer pin → inner pin, no control point.
  1,844 shipped links do this; there are **zero** direct inner→outer links, so getting a value out
  must go through a Bottom control point.
- A Bottom control point paired to a Top one by `IsFeedback="true"` becomes a fold instead. Leave it
  unpaired and it collects a `Spread<T>`.
- `Index`, `Keep` and `Break` are optional `<Pin>`s of the region's inner `Update` fragment, not of
  the region node.

Copied from `packs\VL.Stride\help\Misc\Example World Cities.vl` (region `CJC9JEn2v8CPwY46s8RHrJ`),
which is the exact precedent: a spread in, a **record built per item**, `Spread<TheRecord>` out.

The generated C# is what the design claimed:

```csharp
while (enumerator_16.MoveNext() && enumerator_18.MoveNext())   // the two spreads zip
{
    double X_22 = (double)splicer_17;                          // VL widens Float32→Float64 itself
    var Result_24 = GeometryNodes.Coordinate(x: X_22, y: Y_23);
    var Output_28 = Landmark_R.Create(…, Geometry_In: Result_26);
    builder_29.Add(Output_28);                                 // the Bottom splicer is a builder
}
this.Landmarks = output_30;                                    // all of it inside __Create__
```

The float-to-double question was left for the compiler to settle and it settled it: an explicit
`(double)` cast appears, so no conversion node was needed.

### Deleting elements creates dangling references of its own

Removing the three old chains removed the `Cons` node, which made the Create fragment's seed link
dangle, which the dangling-link sweep then deleted — leaving
`ParticipatingElements="HP6tjLsgFQVMTwxcjYZR08"` pointing at nothing. Caught by validating
`Patch@ParticipatingElements` afterwards, which only exists because the previous round added it.
**A cleanup pass needs the same validation as an edit pass.**

### The yellow pad: `This field could mutate at runtime.`

Read out of `VL.Lang.dll`. It is emitted by `PatchedSlotSymbol::CollectMessages` with severity
`ldc.i4.2` = **Warning** (`.warning { stroke: #FFB200 }` in the shipped stylesheet), from the
predicate *"the slot is in a Record AND its type is classified mutable"*. An imported .NET `class`
is mutable unless it is a C# `record`, is assignable to `DynamicEnum`, or its **name starts with
`Immutable`** — a literal `String.StartsWith` in `ImportedConcreteTypeSymbol::GetKind`. NTS
`Geometry` is none of those, and VL is factually right: `SRID`, `UserData`, `Apply`, `Normalize`
all mutate.

**It is advisory and nothing reads it but the editor.** `ViolatesImmutability` is referenced from
exactly one place in all of `VL.Lang.dll` — that diagnostics pass. No codegen path consumes it.
Shipped records do this constantly: `VL.Skia`'s `SkiaPaint.Shader : SKShader`, HDE widgets holding
`SKImage`, `VL.TPL.Dataflow`'s record holding a Stride `Texture`, `ColorTheme.Custom Colors :
Dictionary`.

The one real hazard is **serialization**, and the one thing shipped code does about it is tag the
slot `NonSerialized` — `VL.Skia` does exactly that on `SkiaPaint.Shader`. `Landmark.Geometry` now
carries it. There is no way to suppress the mark itself short of an "Immutable Forward" of
`Geometry`, which would change type resolution globally for cosmetics; not worth it.

The real consequence to respect is aliasing: VL records have **no `Equals` override**, so they
compare by reference, and two `Landmark`s made from one share the same `Geometry` instance. Mutate
it and both change while every change signal downstream stays quiet.

---

## 2026-08-15 — a patched record has two shapes, and the exported one is the lie

`HowTo Draw many features.vl` drew nothing. The `Status` output built into `ToFeatures` said what
CLR reflection could see of a `Landmark` record **inside the vvvv editor**:

```
Landmark_R has no property of type Geometry - it has
__State:Object, Context:NodeContext, Identity:UInt32, __Program__:VLObjectProgram
```

No `Name`, no `Type`, no `Geometry`. **In the editor a patched record keeps its values inside
`__State` and exposes no CLR members for them at all.** Only `IVLObject.Type.Properties` can see
them.

### Why reading the generated C# did not catch it

Because `vvvvc` emits the *other* shape. The exported `Landmark_R` has

```csharp
public string Name;
public n12.Geometry Geometry;      // n12 = NetTopologySuite.Geometries
this.Geometry = Geometry_In;       // Create really does store it
```

so every check this repository trusts said the record was correct — and it was, in the form that
was checked. **One record, two runtime shapes; the editor's is the one that has to work, and it is
the one `vvvvc` never shows you.** That makes six recorded cases of a green check that could not
have gone red (`docs/RULES.md`, "Working style") and the first where the false proof was *reading the
generated code*.

### The rule that follows

**Read a patched value through `IVLObject`, never through `System.Reflection`.** This is not a style
preference: it is why all three shipped libraries that consume user records —
`VL.Serialization.MessagePack`, `VL.ImGui.Editors`, the built-in `Serialize` — use `IVLPropertyInfo`
and none uses CLR reflection. That was noticed a round earlier and mistaken for taste.

`ToFeaturesNode` now picks its reader from the first *value*, not from `typeof(T)`:
`IVLObject` → `Type.Properties` (using `IVLPropertyInfo.Type.ClrType` to find the geometry, so a
null first instance still resolves); anything else → reflection, which still serves imported .NET
types and exported records.

### The test double was easier than the real thing, so it tested the wrong thing

`ToFeaturesTests`' fake record had real public fields. CLR reflection found them, the test passed,
the patch drew nothing. The double now mirrors the editor: values in a private `_state`, public
surface limited to `__State` and `Identity`. **Negative-tested after the fix** — removing the
`IVLObject` branch turns it red, which the old double could not do.

---

## 2026-08-15 — a thousand features, and the number that answers the whole question

The question behind the question: *a user with hundreds or thousands of features cannot wire a node
per feature — does a rigorous record definition help, and should this package offer that?*

### Two numbers settle it

1,000 features of 500 vertices each, through `FeatureLayerNode.Update` (`ScaleTests`):

| | ms/frame |
|---|---|
| the same feature objects every frame — a spread built once | **0.013** |
| new feature objects every frame, saying the same thing | **52.9** — of which **43.3 building them**, **9.6 comparing them** |

**A factor of four thousand, decided entirely by whether the patch builds its data once.** Nothing
inside these nodes can recover it; a patch that rebuilds a thousand features per frame has spent two
and a half frames before anything is drawn.

**The planned optimisation was dropped because of this measurement.** The plan called for a
spread-level `ReferenceEquals` fast path in `FeatureLayer`. Unnecessary: `SameFeatures` already opens
with a per-*item* `ReferenceEquals`, and a spread built once hands over the same items even when the
enclosing array is rebuilt — hence 0.013 ms. The fast path would have saved an 8 KB allocation and
bought a new way to be wrong.

**The first version of that benchmark was itself wrong**, and instructively so: it reused the same
feature objects in the "expensive" case too and reported 0.013 ms for both rows. A benchmark that
does not do the expensive thing reports that the expensive thing is cheap.

### Why identity is the whole story

Read out of the IL: `CacheManager.InputsChanged` compares with `ValueTuple.Equals` →
`EqualityComparer<T>.Default`. `Spread<T>` is a **class with no `Equals` override** — verified, two
`Spread<int>` over `{1,2,3}` compare `False` — so that is `ReferenceEquals`. `Changed` and
`ChannelFlange.CopyFromUpstream` reduce to the same thing. **There is no version, revision, dirty
flag or invalidate on any VL data type**; `IChannel.Revision` and `IVideoSource2.ChangedTicket` are
the only counters in VL.Core and both are subsystem-specific.

The contract the ecosystem runs on is therefore *immutable values from producers that are cached, so
they hand out the same instance*. VL.Skia states it in its own help: *"Usually it's better to put all
the path construction nodes into a Cache region, so that the path will be rebuilt only on changes."*
VL.Stride's buffer nodes wrap allocation in `Cache` and gate upload behind an explicit `Apply` pin.
VL.Buffers copies unconditionally.

**`SameFeatures` is unique in the whole install** — no other C# node library structurally compares an
incoming collection. It exists because our `Feature` is a static node that mints a new object every
frame, which is exactly the contract the ecosystem expects producers to keep. It stays as the
fallback that cured the flicker; the fix for scale is upstream, in how the patch builds its data.

### So the record is not a matter of taste

A record spread built once keeps its identity; three hand-wired `Feature` chains do not. That makes
`ToFeatures` + a record the shape that scales, and `HowTo Draw many features.vl` the patch that says
so with the two numbers in it.

### The four ways to accept a user's own type, and what each costs

| | user pays | we pay |
|---|---|---|
| **reflection over a generic `T`** ← chosen | nothing: name a slot `Geometry` | one C# node, fully testable without vvvv |
| C# interface | **one getter operation per property** — a Slot does *not* satisfy a C# property member (proved on `PersonEditor`, `EditMode`, `IReadOnlyTreeNode`) | one interface |
| `.vl` interface declaring `Split` | nothing — a record's auto-`Split` satisfies it (the `ITooltip` pattern in VL.HDE) | hand-written interface plus patched glue, and **it cannot be a C# pin type** |
| adaptive node — VL's real type class | one matching operation | **declaration must live in a `.vl`, no documentation exists anywhere**, and the marker that makes an operation adaptive is not visibly serialised |

Adaptive nodes are genuinely VL's answer to "any type that can do X" — 151 `IAdaptive*` interfaces in
`VL.CoreLib.vl.dll`, compiled to C# 11 `static abstract` members plus witness structs, and
third-party packs (VL.AlchemX, VL.Elementa, VL.Kairos) declare their own. `VL.ImGui`'s
`CreateObjectEditor (Adaptive)` is structurally exactly our case. It was still the wrong choice here:
a mechanism with no documentation, whose declaration rule we would have to reverse-engineer, in a
package aimed at people new to vvvv.

### Three of my own mistakes this round, all caught by something other than me

- **`Landmark is no Feature [NetTopologySuite.Features]!`** — rewiring the patch left the old
  `Cons → FeatureLayer.Features` link in place, because **both of its endpoints still existed**, so a
  dangling-link check could not see it. The layer had two sources of different types. Caught by
  `vvvvc`. The lesson for the validation: *a link can be wrong without being dangling.*
- The benchmark that measured the short circuit instead of the comparison, above.
- Claiming "220 `[ProcessNode]` types, 92 generic" from a scan that counted DLLs present at two
  paths twice. The de-duplicated figures are **110 distinct, 46 generic**.

---

## 2026-08-15 — records vs the attribute dictionary, and what the ecosystem actually does

The question was whether VL's **records** should replace the `Dictionary` `Add` chain that
`HowTo Label your data.vl` uses to build a feature's attributes. Asked out of curiosity rather than
dissatisfaction, so the survey came before any opinion. Measured across the shipped assemblies and
**1,558 `.vl` files** (815 shipped, 743 installed).

### The answer is "keep the dictionary, and add one node"

Three things said so, none of them recalled:

- **The Gray Book's design guidelines already describe the current shape.** *"If the datatype you
  create is more or less used as a container for a bunch of properties, it is often useful to have a
  pair of join/split nodes."* `Feature` + `Split` is that pair.
- **The ecosystem's flagship data example does exactly what our help patch does.**
  `packs\VL.Stride\help\Misc\Example World Cities.vl` defines record `City` with typed slots **and a
  `Dictionary<String,String>` slot side by side**, and builds the dictionary with `Dictionary.Add`
  and hard-typed string keys — the CSV header row is thrown away (`Skip 1`). Records and
  dictionaries are not rivals there; they are two halves of one model.
- **No node anywhere converts a record into named key/value pairs.** Not in 1,558 `.vl` files, not in
  any DLL. `VL.ExtendedTutorials` states the prevailing advice outright: *"Don't mess around with
  generics just code it to handle your datatype directly."*

The dictionary has to stay because Mapsui's feature *is* a bag of named values, `LabelColumn` is a
runtime string, and a GeoJSON or Shapefile reader learns its columns when it opens the file. A
record cannot describe that. What a record can do is remove the retyping one step earlier — hence
`ToAttributes`.

### Three shipped libraries read a user's record, and all three do it the same way

`VL.Serialization.MessagePack` (`IVLObjectFormatter<T>`), `VL.ImGui.Editors` (the property grid), and
the built-in `Serialize [Serialization]`. **A generic `T` pin plus `IVLTypeInfo` / `IVLPropertyInfo`
— never an `object` pin.** `ToAttributes<T>` copies that shape.

An earlier reading of the same evidence said "nobody consumes `IVLObject`, so there is no precedent".
That was true of the *pin type* and wrong about the *pattern*: the precedent is generic `T`, and the
reflection happens inside.

### The name question, settled by the compiler rather than by a GUI round

A patched record's property `Some Field` becomes a **public field** `Some_Field` carrying
`[VL.Core.Import.Name("Some Field")]` — verified on a real compiled record (`ColorTheme_R`). Which
of `Name` / `OriginalName` / `NameForTextualCode` to key on was going to need vvvv, until the build
answered it: **`IVLPropertyInfo.Name` is `[Obsolete]`**, and the message reads *"Got replaced by
NameForTextualCode. Also consider using OriginalName, which can contain spaces."*

So `OriginalName` it is. `Serialize [Serialization]` writes the escaped form instead
(`<ADSR_Settings Attack_Curve="Expo" …>` in its shipped sample output) — but only because an XML
attribute name cannot contain a space. A dictionary key can, and the point of the node is that the
name read off the record is the name that can be looked up. Negative-tested: keying on
`NameForTextualCode` turns 3 tests red.

### `AppHost.CurrentOrGlobal` is null outside vvvv

Measured. It rules out `TypeRegistry` for anything that has to stay testable — but `IVLObject.Type`
is an instance property the object carries, so the node needs no `AppHost` and a hand-written
`IVLObject` double covers the mapping with no runtime. **The double is honest about its limits:** it
tests our key selection, not VL's.

### Hand-authoring a record in `.vl`

`Create` and `Split` are **real patched operations with wired bodies, not auto-generated** — checked
across 203 shipped records with slots, and zero of them have slots without links. Per property: 1
`Slot`, 1 `Pad SlotId`, 2 `ControlPoint`, 4 `Link`, 2 `Pin`. The `Landmark` record here was copied
whole from `VL.ExtendedTutorials`' `MyDataRecord` and edited down, per the rule that copying a proven
patch beats composing one.

Two reference kinds appear that no earlier patch here had, so the validation grew to match:
`Pad@SlotId` and `Fragment@Patch` must both resolve, alongside link endpoints.

---

## 2026-08-15 — picking, and four facts read off Mapsui before anything was designed

`Pick` is the first node here that hands something **back**. Everything before it took data in and
drew it. The design was settled by measurement rather than by reading Mapsui's examples, and every
number below comes from a test that is now in the suite (`MapsuiHitTestFacts`), so a Mapsui upgrade
cannot change any of them quietly.

### The four measurements, and what each one decided

| measured | result | what it decided |
|---|---|---|
| `ILayer.IsMapInfoLayer` | **defaults to `false`**; with it off, a hit on the dead centre of a square returns no feature and no error | `FeatureLayer` sets it to `true`, rather than leaving a pin nobody would know to look for |
| the hit edge | screen x 300 hits, **301 misses** — the geometry exactly | `Pick` promises no tolerance |
| Mapsui's `margin` | **0, 4, 8, 32 all miss** five pixels outside a polygon | not exposed as a pin: a tolerance that does nothing is worse than none |
| a miss | still carries `WorldPosition` | `ScreenToWorld` is its own node, not an output of `Pick` |

The first of these is the important one. It is a **silent** failure: no exception, no log line, and a
result — "nothing is there" — indistinguishable from an honest miss. Exactly the class this
repository keeps paying for, and the fifth instance of it recorded here.

### The node has no Pressed pin, deliberately

`Pick` answers about a **position**, not about a click. Gate it with `Sample and Hold` on
`Left Pressed` and it becomes click-to-select; leave it alone and it is hover. This is the rule
already applied to `Drag.Dragging` and to `Click`, and it is what the note of 2026-08-14 was about —
*"把点击的事件在内部消化了肯定是有问题了"*.

### `Split` exists because `Pick` was unreadable without it

Caught while writing the help patch, not while writing the node: `Pick` returns an NTS `Feature`,
whose attributes live in an `IAttributesTable` — **and VL has no nodes for that type**.
VL.NetTopologySuite has none either; it wraps geometry only. So the node would have handed a patch
the very thing that makes a feature interesting, with no way to open it.

`FeatureNodes.Split` converts them to VL's own `ImmutableDictionary`, where `TryGetValue` is waiting.
It is the exact inverse of `Feature`, which is why it is named `Split` — the convention
VL.NetTopologySuite already uses for taking a `Coordinate` apart.

**The test that would have caught it asserts on the attribute, not on the feature object.** A test
that had only checked `Assert.NotNull(picked)` would have passed all the way to the GUI.

### `MouseState.Position` is in device pixels — confirmed on screen, and it had never been tested

`Pick` takes view pixels, and so do `Drag` and `Click`, which have shipped since 2026-08-14 wired
straight to `MouseState.Position` with no conversion. That wiring was never actually *verified*:
**dragging cannot detect a scale error**, because it uses only the difference between two positions,
so a position in VL's ~2.8-by-2 unit space would have dragged the map slowly rather than wrongly.
The widget `Click` was weak evidence — the zoom buttons are ~40 px, so a large error would have made
them unclickable — but nothing had ever depended on an absolute pixel being right.

Picking does. The hit edge is exact, so a coordinate space that is off by any factor misses
*everything*, always, silently. Pointing at a shape and reading its name back is therefore the first
real test of that assumption, and it passes: the labels appear under the pointer.

Worth keeping as a pattern rather than as a fact about the mouse: **a green result can be produced by
a design that cannot fail.** Drag was never evidence for the coordinate space, and treating it as
evidence for three weeks would have been the easy mistake.

### The projection boundary only worked one way

`ToMercator` existed; `ToLonLat` did not. Geometry could get in and not back out — which nothing
noticed for as long as nothing came back. A picked feature arrives in degrees now, the same
coordinates it was written in.

---

## 2026-08-14 — attributes in a patch, and four patches that shipped switched on

### A patch can build the attribute dictionary, and this was not obvious

`Feature`'s `Attributes` pin wants an `ImmutableDictionary<string, object>`, and until now nothing
in a patch could produce one — which is why every example here had geometry and no attributes, and
why `LabelStyle` had no example at all: **a label names an attribute, so a patch that cannot make
attributes cannot demonstrate labels.**

The answer is `Collections.Dictionary`'s `Add`, and the part that had to be measured is what happens
to its **unconnected `Input`**. Compiled a throwaway probe rather than reasoning about it, and read
the generated C#:

```csharp
ImmutableDictionary<string, Object> Input_2 = __v_PSWf5hJTnDxqOO1xxFnlPe;
public static ImmutableDictionary<string, Object> __v_… = Dictionary._Operations_.CreateDefault<string, Object>();
var Result_5 = FeatureNodes.Feature(geometry: Geometry_4, attributes: Output_3);
```

Two facts in three lines. An unconnected `Input` is the **type's default**, an empty dictionary — so
a chain of `Add`s starts from nothing without a node to make the nothing. And VL **propagated the
expected type upstream**: nothing in the patch says `<string, object>`, the `Feature` pin does, and
the generic `Dictionary` node was resolved from the pin it feeds rather than from its own inputs.

Worth stating why the C# was read at all when the exit code was already 0: an unresolved type in a
`.vl` compiles **silently** here (2026-08-13), so an exit code proves the file parsed, not that the
node it names exists. The generated C# naming `FeatureNodes.Feature(attributes: …)` is the proof.

`HowTo Label your data.vl` came out of this — two features, one style, two labels, and the whole
point on screen: `Attribute` names a column rather than carrying text, so a third feature would
label itself.

### Four help patches shipped with `Enabled` switched on

Rule 2 of this repository is that anything which fetches ships **off**, because opening a document
in vvvv runs it. The nodes obey it — `enabled = false` in C# — but **the IOBox in the patch
overrides the node**, and four of the six patches carried `Value="True"`:

```
HowTo Draw your own shapes.vl   True     <- and its own description says "Enabled starts OFF"
HowTo Label your data.vl        True
HowTo Show a map.vl             True
HowTo Stack several layers.vl   True
```

**The capital `T` is the tell.** Every pad written by hand in this repository says `false`; vvvv
writes `True`. So this was not a decision anyone made — it is a GUI round switching the map on to
look at it, and vvvv saving that along with the window positions. The same mechanism that rewrites
`NugetDependency` versions on save, which `tools\Normalize-HelpPatches.ps1` already existed to undo.

Fixed there rather than by hand, because a hand fix would last exactly until the next GUI round.
`Comment="Enabled"` is the tile-layer toggle in every patch here and nothing else, so the rule can
be that precise. **Negative-tested in the order that proves anything:** `-Check` was run *before*
the fix and printed the four files and exited 1; after the fix it exits 0.

### The headless compile needs two things lined up, and only ever had one

Every help patch failed to compile this round, all seven identically, and the package had not
changed. Two different errors depending on what was passed:

```
--package-repositories dist        NU1101: package VL.Mapsui not found      (sources: nuget.org, …)
--package-repositories dist\feed   VL.Lang.CompileException: Missing package: VL.Mapsui
```

They are not two attempts at one setting; they are **two mechanisms that both have to be satisfied**.
`--package-repositories` is how *vvvv* finds a package **folder** (`dist\VL.Mapsui\`). vvvvc then
generates a `.csproj` with a `PackageReference` and runs an ordinary **NuGet restore**, which knows
nothing about that flag and needs the **nupkg** (`dist\feed\`).

**It only ever appeared to work because of a stale package.** Restore was quietly satisfied by
`%USERPROFILE%\.nuget\packages\vl.mapsui`, left over from an earlier `pack.ps1`. `build.ps1` evicts
that on purpose — it is what once made vvvvc insist a node did not exist hours after it was
written — so running `build.ps1` without `pack.ps1` removed the only thing that had been holding
the compile up. A green check resting on a cache nobody named is the same class of false proof this
repository keeps finding.

Fixed by `tools\Compile-HelpPatches.ps1`, which passes `dist\` as the repository and drops a
`NuGet.config` pointing at `dist\feed\` at the output root, where restore finds it by walking up
from the generated project. Both mechanisms, named, in one place.

### Two mistakes of my own, both caught by `vvvvc` rather than by me

- `[Pin(Name = "URL Template")]`. Without it the pin reads **`Url Template`** — VL builds a pin name
  by splitting the C# parameter at its capitals, and `urlTemplate` cannot carry an acronym through
  that. The patch named the pin correctly and the compile failed on the node not having it.
- `Result`, not `Output`. A `[ProcessNode]`'s return pin is `Result`; `Output` is what a fluent
  static operation returns. Written from memory, wrong, and the compile said so.

Both are in the class this repository keeps re-learning: **the pin names in a hand-written `.vl` are
a claim about the C#, and only a compile checks the claim.**

---

## 2026-08-14 — a shape on a map, and three defects the screen found first

`HowTo Draw your own shapes.vl` draws a polygon over OpenStreetMap, the shape stays on its ground
while the map is dragged and zoomed, and `Layers Built` stays at 1 throughout. The geometry comes
from **VL.NetTopologySuite**, a sibling package: `Read WKT` turns a string into an NTS `Geometry`,
`Feature` carries it, `FeatureLayer` draws it. **Nothing in VL.Mapsui creates geometry**, and that
boundary held — the example needed a second package rather than a shortcut node.

Every one of the three defects below was found by looking at the screen, and each was invisible to a
green test suite.

### 1. The map flickered, and the cause was a missing value semantics

`Feature` is a static node, so VL evaluates it every frame and it returns a **new object every
frame**. `FeatureLayer` compared features by reference, so every frame looked like a change: the
layer was rebuilt, the `Map` was handed a new layer and rebuilt in turn, and the whole map
flickered.

**Why the old `Geometry` node never had this:** it compared *geometries*, and
`NetTopologySuite.Geometries.Geometry` **overrides `Equals` with value semantics**.
`NetTopologySuite.Features.Feature` does not. Moving the pipeline from geometries to features
silently dropped the comparison that had been holding it up.

Fixed two ways at once, and both were needed:

- **compare by value** — `Geometry.EqualsExact` plus the attributes, which is far cheaper than
  reprojecting and rebuilding Mapsui's features
- **keep the layer object and replace its contents** — `MemoryLayer.Features` is settable, and the
  layer's *identity* is what a `Map` compares. `Layers Built` now stays at 1 for the life of the
  patch and `Features Sets Built` counts content changes separately

### 2. A test that did not test its own fix

Reverting the value comparison left **all 115 tests green**. Keeping the layer alive makes
`LayersBuilt` 1 whatever the comparison does, so the assertion was measuring the wrong thing. Only
after the assertion moved to `FeatureSetsBuilt` did the negative test fail as it should.

*A check that has never gone red is not a check* — and this time the check went red, was fixed, and
still tested nothing. Run the negative test **after** the fix, not only before it.

### 3. The example contradicted itself

The patch pinned the view to Kyoto with `CenterOn` while the polygon sat in **Tokyo, 350 km away**,
in a window about 8 km wide. The shape was simply off screen whenever the basemap was on — which
read on screen as "the basemap follows the zoom and the geometry does not", and as "toggling the
tile layer fixes it" (it changes the layer set, which calls `Map.Refresh`).

Two theories were killed cheaply before that was understood, and are recorded so nobody re-opens
them: `MemoryLayer.Extent` is `Features.GetExtent()`, recomputed on every access, so replacing
features in place cannot leave a stale extent; and `MapRenderer` holds no viewport-keyed cache of
rendered geometry.

**`CenterOn` and `ZoomToLevel` are operations, so a patch holding them re-pins the view every
frame and the map cannot be dragged at all.** They are the lesson in `HowTo Show a map`; they have
no place in an example whose whole claim is that a shape stays glued to the ground. The starting
view now comes from the `Map` node's `Initial` pins, applied once through `Home`, and the patch
carries `Drag` and `ZoomByWheel` so there is something to test the claim with.

### Using nodes from another package, measured

- **A document must declare `NetTopologySuite` itself** to use NTS nodes. VL.Mapsui declaring it is
  what lets *our* node signatures name NTS types; it does not extend to a patch. Without the line
  the node is `Not found`; with it, it resolves.
- Six rounds of hand-written XML for raw NTS types ended at `The reference is ambiguous` —
  `WKTReader` has three constructors and hand-written XML cannot pick one. **Static members work
  and their output pin is named after the member** (`GeometryFactory.Default`'s pin is `Default`,
  not `Result`). The sibling package made all of it unnecessary: its help patches were placed in the
  GUI, so their node XML is correct by construction and was copied verbatim.
- **VL.Mapsui's nuspec now depends on VL.NetTopologySuite.** Surveyed first: 25+ shipped packs
  declare VL-to-VL dependencies, *and* help patches routinely reference packs their nuspec does not
  (VL.Skia's help uses VL.ImGui; VL.Audio declares no VL dependency at all and its help uses
  VL.Stride). The second only works because those ship inside vvvv. Ours does not, and a missing
  document dependency is **not** fetched automatically — The Gray Book's referencing page says it
  shows red and offers to install on rightclick.

### Still missing, and every geometry example will want it

**There is no way to say "put the view where the data is".** Mapsui has `ZoomToBox` and a layer
knows its `Extent`. Next navigation node.

---

## 2026-08-14 — geometry, style and layer become three things

An ecosystem architecture arrived in writing, and the audit against it found one node genuinely
wrong-shaped: `Geometry` did geometry, style and layer in a single node. It is now three, and the
old node is the three composed rather than a fourth implementation of the same thing.

```
NTS Geometry ─┐
              ├──► Feature ──┐
attributes  ──┘              ├──► FeatureLayer ──► Map
             VectorStyle ────┘
```

### The feature type is NetTopologySuite's, and that was a decision not a default

`NetTopologySuite.Features.Feature` — geometry plus an `AttributesTable`, nothing about rendering —
already existed, is mature, and was **already shipping transitively in `deps\` as 2.1.0** without
being declared anywhere. Inventing a `VLFeature` would have made this package the definition of the
whole domain; using Mapsui's own feature would have made the renderer the definition.

Declaring it mattered and is the reason to write this down: **compiling against a transitively
present assembly proves nothing about whether VL can resolve the type.** It needs the package
declared in the csproj, the nuspec *and* `VL.Mapsui.vl`'s `NugetDependency` list, or the node is
built with no working pins and every link to it is dropped in silence. Verified the only way that
counts — by reading the generated C# of a patch that uses it:

```csharp
using n15 = e232::NetTopologySuite.Features;
var Result_29 = n14.FeatureNodes.Feature(geometry: Output_27, attributes: Attributes_28);
n17._Operations_.Cons<n15.Feature>(…) → Spread<n15.Feature>
var Result_42 = FeatureLayer_40.Update(features: Features_34, style: Result_39, …);
```

Attributes arrive as `ImmutableDictionary<string, Object>`, which is what VL's own `Dictionary` is
underneath — also read off the generated code rather than assumed.

### A third reason for a node to hold state

The rule was "anything holding a resource is a process node". `VectorStyle` holds nothing and is
one anyway, because **its identity is compared downstream**:

- a layer treats a new style object as a change and rebuilds
- Mapsui keys its rendered-geometry cache on the style object — `IFeature.RenderedGeometry` is an
  `IDictionary<IStyle, object>`, so a fresh style every frame is a fresh key every frame, on every
  feature

Negative-tested by removing the cache so a new style is built each frame: **7 tests red**, including
all three feature-count ones.

### Feature counts, which the architecture document asked for

100 / 1,000 / 10,000 features, 60 frames each: one layer built, one style built, at every size. The
point is not speed — it is that a lifecycle mistake only becomes visible when the count is large
enough to hurt, and by then it looks like "the map is slow" rather than like a bug.

### `[SkipCategory]`, not `[Name("")]`

A named static class becomes a category level of its own, so naming the feature node's class would
have put the node one level away from where it belongs and it would have failed to resolve — the
trap already recorded for `MapInfoNodes`, walked into again and caught by reading that note.

### Also settled

- `docs/ARCHITECTURE.md` now exists: the pipeline, the NTS boundary, the dependency direction, and
  the three reasons a node holds state. It was the one document genuinely missing.
- The ecosystem decision: **VL.GIS is frozen** — no new features there, contents move to focused
  packages over time, and the published `0.2.0-alpha` stays where it is because a published version
  can never be deleted.
- Read "MapControl" in that document as "`ToSkiaLayer` + VL.Skia's `Renderer`". `Mapsui.UI.*` are
  the WPF / Avalonia / MAUI hosts; **vvvv is the host here**, which is why they are on the
  will-not-wrap list.

---

## 2026-08-14 — widgets, and a click that had to be watched rather than argued about

Three widget nodes: `ScaleBar`, `Attribution`, `ZoomButtons`. All three confirmed on screen, and
**the zoom buttons respond to a click** — which is the part that was worth the care, because it was
the only claim in the batch that could not be settled by a test.

### What the probe answered before any code was written

Whether a widget draws at all depends on the renderer having one registered, and that **cannot be
asked in PowerShell** — constructing a `MapRenderer` needs SkiaSharp's native library. The test
project can, so the first thing written was the question:

```
WidgetRenders: 9
    BoxWidget  ButtonWidget  EditingWidget  Hyperlink  MapInfoWidget
    MouseCoordinatesWidget  ScaleBarWidget  TextBox  ZoomInOutWidget
```

**`PerformanceWidget` is not among them**, which contradicts what `docs/MAPSUI-SURFACE.md` claimed
("every one below has a Skia renderer") and is now corrected there. Wrapping it means registering a
renderer by hand — the reason it is not in this batch.

### The trap, and the shape of the test that catches it

`Map.Widgets` is a `ConcurrentQueue<IWidget>`: append only, no removal. A widget node written as a
static operation would enqueue sixty widgets a second and **nothing could ever take them out**. So
every widget test is a frame loop that counts what ended up on the map, the same shape as the tile
layer tests. Negative-tested by making the enqueue unconditional: 3 tests red immediately.

`Enabled` is therefore how a widget goes away, not removal — which is worth saying on the pin,
because it is the opposite of how everything else in a patch behaves.

### Clicks: two inferences that agreed, and one reading that settled it

A widget draws itself where only the renderer knows, so only the host can route a press to it.
Whether our press arrived in the right units was **inference from two independent directions**:
`IProjectionSpace.MapFromPixels` documents a notification's position as being in pixels, and
`DragNode` says "a position in view pixels", consumes `MouseState.Position`, and pans correctly.

Both pointed the same way and neither is a reading. The overlay now prints

```
widgets    3, 3 placed by the renderer
last press 412, 88  taken by a widget
```

which separates the three ways a button can look dead: the notification never arrived, it arrived
in the wrong space, or the renderer had not laid the widget out yet (`Envelope` is null until it
has drawn once). Confirmed in the GUI: the buttons zoom, and dragging away from them still pans —
the second half matters just as much, because a widget that swallowed every press would break the
map quietly.

### Corrected the same day: the press is wired, not swallowed

The first version handled the press inside `MapsuiLayer.Notify` and offered it to the widgets. It
worked, and it was wrong — **it decided for every patch that a left press is what clicking a widget
means**, which is the item this repository's own rules list as never to be bundled, next to what the
mouse means for panning. Written down, then broken in the same file that argues against it.

The reference that settles it is in vvvv itself, in `VL.Skia\help\Overview\Explanation Mouse and
Keyboard.vl`, on a comment pad:

> *"The Mouse and Keyboard nodes need to be connected to the Renderer they want to interact with"*
> *"Mouse's 'World Position' is used to translate the Layer."*

The mouse is a value you wire. VL.ImGui does swallow notifications inside its layer — checked, its
`ToSkiaLayer.Update` has pins for Widget, Fonts, Style and no input pin at all — but that is a GUI
toolkit whose whole purpose is to consume input, not a map that a patch drives.

So `Click [Mapsui.Widgets]` takes `Map, X, Y, Pressed`, the same shape as `Drag`, and
`MapsuiLayer.Notify` returns false again.

**`Handled` is what earns the node.** With the press swallowed, a patch could not know a click had
hit a button, so pressing zoom also started a pan and nothing said why. The patch now wires
`Left Pressed AND NOT Handled` into `Drag.Dragging`, and that decision is visible on the canvas.

The pins deliberately did *not* go onto `ZoomButtons` itself: it would have reached six inputs, past
the point `docs/RULES.md` calls two decisions wearing one node, and every clickable widget would
repeat them. One node decides what a press is; the widgets stay at three pins.

Only the rising edge routes — a held press would zoom every frame, which is a fresh round of tile
requests sixty times a second. Negative-tested by routing on the level instead: 2 tests red.

### `Result`, not `Output`, for a process node

`vvvvc` rejected `Output` outright: *"ScaleBar doesn't have a pin called Output"*. The fluent rule —
return type equals the first parameter type, so the output pin is `Output` — **applies to static
operations only**. A `[ProcessNode]` returning its own first argument still gets `Result`, exactly
as `OpenStreetMap` does. Established the same way the rule itself was: by being told.

Also worth keeping: the out parameter has to come *after* the map, not before. `Update(out string,
Map)` is not the fluent shape and would not sit in a chain.

### Help stopped being one patch that teaches everything

`HowTo Show a map.vl` had grown a cache story, a navigation story and then widgets. Split:

- `Explanation Overview of available nodes.vl` — **the front door, and we had none**; vvvv's own
  packs ship 57 of them
- `HowTo Add widgets to the map.vl` — its own topic, with the mouse wiring copied verbatim from the
  mouse patch so the buttons can actually be clicked
- `Help.xml` — ordering and search tags. **It needs its own line in the nuspec**: the existing glob
  takes `**\*.vl` and nothing else, so it would have shipped unordered and untagged with no warning.
  Verified by reading the built `.nupkg` rather than by assuming.

A new document copied from an existing one needs **a new Document Id and nothing else renumbered** —
element ids are scoped by document. Two ids were hand-typed in the first draft rather than generated,
which is exactly the rule `tools\New-VLId.ps1` exists for; both replaced.

---

## 2026-08-14 — an empty Path IOBox is not empty

The stray tiles from the entry below are explained, and the explanation was **read off a pin, not
reasoned out**. Three values from the patch that produced them:

| read | what it says |
|---|---|
| `Cache Folder` IOBox showed `D:\2026_Projects\vvvv-gis\examples` | the "empty" Path IOBox was not handing the node an empty value |
| `Cache Status` showed `D:\2026_Projects\vvvv-gis\examples — 1 tiles, 0.0 MB` | the node was using it, and counting the `.vl` itself as a tile |
| the `CacheFolder` node showed `%LOCALAPPDATA%\VL.Mapsui\tiles`, 1165 tiles | its input was **unconnected**, so C# got `null` and the default applied |

Same `Resolve`, two results, and the difference is only how the pin was reached.

**`Value=""` in a `.vl` does not mean empty. It means "the path relative to this document", and the
empty relative path is the document's own folder.** `vvvvc` compiles the pin to

```csharp
public static n11.Path __slot_SZONokgOOw3PKrH0lXmVdd =
    n10.CompilationHelper.Deserialize<n11.Path>(@"", false, @"I4hIadDDdoVyqg12xj3ejr", @"…");
```

— the document id is passed in, and VL resolves against it. So the node received a rooted, existing,
writable folder and every guard agreed: `IsPathRooted` passed, `CreateDirectory` succeeded, and the
status pin reported the folder honestly. **Nothing was broken except the assumption that a pin can
be empty.** `"empty means the default"` holds for a `string` pin and cannot hold for a `Path` one.

The timeline had said so before the measurement did: `%LOCALAPPDATA%` stopped receiving tiles at
**21:18 on 8-13**, and `d3b9bf5` — which replaced `string cacheFolder = ""` with a
`VL.Lib.IO.Path` pin — landed at **21:53 that evening**. The pin's type changed and the tiles moved.

### What was ruled out first, offline, and why that mattered

Before opening anything, a throwaway probe asked what is attached when we attach nothing:
`BruTile.Cache.NullCache`, and across Mapsui, Mapsui.Tiling, Mapsui.Rendering.Skia and BruTile
there are exactly **three cache-typed statics, all null**. So no third party was writing. Two of
those findings are now tests (`ForeignCacheDefaultsTests`) because
`Mapsui.Tiling.OpenStreetMap.DefaultCache` is public and static: anything loaded into the same vvvv
could set it and silently redirect every tile.

Ruling that out is what made "it was us, handed the wrong folder" a conclusion rather than a guess.

### The fix: the cache is a value, and one node owns it

Two places could set the folder — the layer node's pin and a separate `CacheFolder` node — and they
had no way to agree. Now `TileCache` produces a `TileDiskCache` and `OpenStreetMap` consumes it:

```
TileCache [Mapsui.Layers]                OpenStreetMap [Mapsui.Layers]
  in : Enabled, Folder                     in : Enabled, Cache
  out: Result, Status, Tiles, Size MB      out: Result, Layers Built, Cache Status
```

The layer node has **no Path pin at all** now, and none of the three patches contains a Path IOBox.
`Cache To Disk` is gone as a pin: the cache node has its own `Enabled`.

**Off is a value, and so is "that folder did not work".** Both were `null` in the first attempt, and
a test caught what that costs immediately: a folder that failed came back as null, the layer read
that as "nothing connected", and fell back to the default — silently, which is the one thing this
package promises not to do. An unconnected pin, a switched-off cache and a broken folder have to be
three different things, or the layer cannot tell "nobody said anything" from "somebody said no".
That is the same ambiguity as the empty IOBox, one level up.

### Measured after the fix

Launched from a scratch directory, so the working directory was neither a repository nor the
document's folder — the two coincided in both earlier incidents and had never been told apart:

| | before | after |
|---|---|---|
| `help\VL.Mapsui\` | 2 files | **2** |
| `vvvv-gis\examples\` | 1 file | **1** |
| vvvv's working directory | 0 files | **0** |
| `%LOCALAPPDATA%\VL.Mapsui\tiles` | 1165 tiles | **1184** |

`Layers Built` stayed at 1. Both status pins read
`C:\Users\laval\AppData\Local\VL.Mapsui\tiles — 1184 tiles, 25.2 MB`, and they cannot disagree
because there is one cache object behind both.

### Two guards, both made to go red first

- **A test**: with nothing connected, the `FileCache` attached to the `HttpTileSource` must have
  `DefaultDirectory` as its root — read out of its private field, because the folder is not exposed
  and taking it on trust is what this whole entry is about. Negative-tested by pointing the default
  at `Directory.GetCurrentDirectory()`: red immediately.
- **`Test-VLPackage.ps1` check 11**: no `{zoom}\{x}\{y}.png` under `help\` or `dist\`. A planted
  file made it fail with `found 1 file(s) shaped like cached map tiles`; removing it returned
  `ok no {zoom}\{x}\{y}.png`. Every other check in that file asks whether what should be there is
  there; this is the first that asks whether something else turned up.

80 tests, up from 75.

### A stale cache that made a good build look broken

The first `vvvvc` run after the change failed with *"type TileCacheNode does not exist"* against a
dll that plainly contained it. **A version number is immutable to every cache, and this repository
rebuilds `0.0.1-alpha` over and over.** `pack.ps1` evicted the NuGet one; nobody evicted
`C:\Program Files\vvvv\…\package-cache\VL.Mapsui.0.0.1-alpha`, which is the one that matters for
opening a patch straight after a build. `build.ps1` now evicts both.

Worth knowing for the next headless verification: an exported project restores `VL.Mapsui` as a
real NuGet package, so `dist\feed` has to be a source. Evicting the global cache without packing
first turns the compile into `NU1101 package not found` — correct, and nothing to do with the code.

---

## 2026-08-14 — geometry on the map, and a cache that writes where it likes

The two packages compose now. VL.GIS dropped BruTile, which was the entire conflict, and both load
into one vvvv with the map rendering. `Mapsui.Layers.Geometry` takes NetTopologySuite geometry and
returns a layer, so **they meet through NTS rather than through each other** — neither references
the other, and the same node draws geometry from any source.

### The design I got wrong first

My first version of the overlay read Mapsui's viewport, converted its resolution to a zoom level,
built a VL.GIS `MapView`, turned the geometry into an `SKPath` in pixels, and drew that above the
map through `WithinCommonSpace`. Nine nodes, two coordinate systems held in step by hand, and
nothing on screen. The user asked why the circle was not simply a layer on the map — which is what
Mapsui is for. Four nodes now, and the example patch dropped from 190 ids to 154.

Worth keeping from the wrong path: `ViewportNodes.MercatorResolution` and
`ZoomFromMercatorResolution` in VL.GIS, and the trap they exist to name. VL.GIS's `Resolution`
reports *ground* metres at the centre latitude and carries a cos(latitude) factor; a map engine's
resolution is *projection* metres and does not. Swapping them leaves an overlay 19% out at Tokyo,
31% at Berlin — drifting with latitude, and looking merely a little misaligned.

Unexplained and left alone: `DrawPath`'s output disappeared downstream of `WithinCommonSpace`. The
branch is gone from the example, so the symptom is no longer on any path. **Not diagnosed, and not
guessed at.**

### The cache writes next to the document — still open

444 stray tiles across the two repositories, in `{z}/{x}/{y}.png` form:

| where | count | when |
|---|---|---|
| `vvvv-gis\examples\` | 366 | the two runs of the example patch |
| `vl-mapsui\help\VL.Mapsui\` | 78 | the coexistence test |

In the same window the real cache under `%LOCALAPPDATA%\VL.Mapsui\tiles` gained **nothing**. So the
cache root became the directory vvvv was launched from. `Cache Status` did not report it and the
relative-path guard in `TileCache.TryCreate` did not fire, which means the explanation is not the
obvious one and **must be measured rather than reasoned about**: open the patch and read the pin.

This is precisely the failure the `Cache Folder` design was written against — "files appearing
somewhere nobody asked for, with nothing to say so". Three things made it worse than one bug:

- **38 of them were committed**, in `d3b9bf5`, because I ran `git add -A` without looking.
- `build.ps1` had already staged them into `dist\`, one `pack.ps1` from shipping inside the package.
- `Test-VLPackage.ps1` passed throughout. It has no idea what does not belong in a package.

Fixed so far: removed, and `.gitignore` covers the shape in both repositories. The pattern was
wrong on the first attempt — one containing a slash is anchored to the repository root — and only
planting a file and watching git ignore it caught that. **A check that has never gone red is not a
check.**

### I deleted both help patches

While removing the stray tiles I wrote `$_` where I meant `$d` inside a `foreach`. In PowerShell
`$_` is not the loop variable there, so the path collapsed to the help folder itself and the
command removed both hand-arranged patches. Recovered whole from the index with `git checkout`,
verified by BOM, XML parse and node count.

The lesson is not "be careful with `$_`". It is that **a destructive command should name its
targets explicitly and refuse anything that does not match the shape it expects** — the retry built
the list of directory names first and threw on anything that was not all digits.

### Also settled

- `Map.Widgets` is a `ConcurrentQueue<IWidget>`: append only, no removal. Every widget node must
  therefore be a `[ProcessNode]` that enqueues once and then drives `Enabled`.
- Mapsui's whole surface is now written down in [docs/MAPSUI-SURFACE.md](docs/MAPSUI-SURFACE.md):
  **306 public types**, of which we wrap a few dozen. `Mapsui.Nts` carries a **Shapefile reader**
  and Mapsui carries **WMS and WFS providers** — one of which VL.GIS had on its roadmap as a
  reason to take on GDAL.
- The rules carried over from VL.GIS now live in [docs/RULES.md](docs/RULES.md) rather than behind
  relative paths into the other repository, so a standalone clone is not missing half its
  instructions.

---

## 2026-08-13 — Mapsui is not blocked for a standalone package

VL.GIS's `CLAUDE.md` says "Wrapping Mapsui is not currently possible". **That verdict was
correct for its question and wrong for this one.** It was measured on 2026-08-10 against the
premise of folding Mapsui *into* VL.GIS. A separate package removes the premise.

The BruTile constraint lives in exactly one sub-package:

| sub-package | constraint | vs vvvv 7.4 | vs VL.GIS |
|---|---|---|---|
| `Mapsui` | none at all | ✅ | ✅ |
| `Mapsui.Rendering.Skia` | SkiaSharp `[2.88.9, 3.0.0)` | ✅ | ✅ |
| `Mapsui.NTS` | NetTopologySuite `[2.5, 3.0)` | ✅ | ✅ VL.GIS is on 2.6.0 |
| **`Mapsui.Tiling`** | **BruTile `[5.0.6, 6.0.0)`** | ✅ | ❌ VL.GIS is on 6 |

So VL.Mapsui works on its own and cannot be loaded next to VL.GIS. Accepted for now. It
dissolves by itself when vvvv moves to SkiaSharp 3 and Mapsui 5 becomes usable — 5.x already
matches VL.GIS exactly (BruTile 6, NTS 2.6, GeoJSON4STJ 4).

### Assembly identity, which is what actually decides

vvvv substitutes its own copy of anything it ships, so the question is never "which version
does NuGet restore" but "does the CLR see one identity". **File version is the wrong thing to
look at** and would have produced a false alarm here:

| assembly | vvvv file ver | Mapsui wants | **assembly version** | |
|---|---|---|---|---|
| SkiaSharp | 2.88.8 | ≥ 2.88.9 | `2.88.0.0` both | ✅ |
| HarfBuzzSharp | 7.3.0.1 | ≥ 7.3.0.3 | `1.0.0.0` both | ✅ |
| SkiaSharp.HarfBuzz | 2.88.7 | ≥ 2.88.9 | `2.88.0.0` both | ✅ |
| Topten.RichTextKit | 0.4.167 | ≥ 0.4.166 | — | ✅ satisfied outright |
| Svg.Skia | not shipped | ≥ 1.0.0.3 | — | must be packed, conflicts with nothing |

Consequence for the csproj: **do not pin SkiaSharp.** Pinning vvvv's 2.88.8 fails restore with
NU1605 because Mapsui.Rendering.Skia asks for more. Letting it resolve to 2.88.9 is correct.

### The integration point exists

- `VL.Skia.CallerInfo` (in `VL.Core.Skia.dll`, and `VL.Core.Skia` is on nuget.org) exposes
  `Canvas`, `Surface`, `GRContext`, `Transformation`, `ViewportBounds`
- `VL.Skia.ILayer` is `Render(CallerInfo)`, `Notify(INotification, CallerInfo)`, and
  `Bounds` — which returns **`RectangleF?`**, not an `SKRect`. `null` means "no natural extent"
- `Mapsui.Rendering.Skia.MapRenderer.Render(canvas, viewport, layers, widgets, background)`
- `Mapsui.Navigator` has `SetSize`, `CenterOn`, `ZoomToLevel`, and already has `Drag` and
  `MouseWheelZoom` for when interaction gets wired up

Precedent to copy rather than reinvent: **`VL.ImGui.ToSkiaLayer` implements `ILayer`** and
solves the identical problem of a pixel-based third-party renderer drawing into VL.Skia. Its
shape is `canvas.Save()` → `canvas.SetMatrix(...)` → draw → `canvas.Restore()`.

### Two silent failures found while bringing the spike up

**1. VL builds no node for a method whose signature names a type it has not imported.**

`DiagnosticsLayer()` returned `VL.Skia.ILayer` and worked. `CreateOpenStreetMap()` returned
`Mapsui.Map` and `ToSkiaLayer(Mapsui.Map)` took one, and both were greyed out. The dividing line
was exactly "does the signature mention a Mapsui type", which is what gave it away.

What "greyed out" actually means here: the node is **absent from the compiled program**, and
every link to it is dropped. The compiled export showed
`Renderer_20.Update(Input_In: Input_21, ...)` — a default local — where the map layer should
have been, while the working renderer had `Input_In: Result_7`. No error, no red node, nothing
in the log.

VL learns a foreign library's types from a `<NugetDependency>` in the `.vl`. This spike is
loaded through a `ProjectDependency` and declares none, so the fix for now is to keep Mapsui
types out of public signatures. Exposing `Map` is the better API and returns with the nuspec —
that is how VL.GIS surfaces BruTile's `IHttpTileSource`.

Worth noting what was *not* the cause: a first guess blamed missing assemblies and added
`CopyLocalLockFileAssemblies`. It changed nothing — Mapsui.dll was in the export all along.
Keep the setting anyway, since a class library otherwise emits its assembly alone.

**2. `Mapsui.Map.Home` is the host's job, and there is no host here.**

Mapsui's own `MapControl` calls `Home` once the viewport has a size; `Home` carries the initial
centre and zoom, and cannot run earlier because a zoom level is meaningless without a size. With
nobody calling it the map renders at the default resolution, which shows nothing at all. The
layer now does what a host does: `SetSize` → `Home` once guarded by `HomeIsCalledOnce` →
`OnViewportSizeInitialized` → `Refresh`, then `UpdateAnimations` every frame.

### The real blocker was five months of leftovers, not Mapsui

With both of the above fixed the node finally ran, and threw the exact ABI break from 2026-08-10:

```
TypeLoadException: Could not load type 'BruTile.Attribution' from assembly
'Mapsui.Tiling, Version=4.1.9.0' due to value type mismatch.
```

Our output folder held the correct BruTile 5.0.6, and so did the `vvvvc` export. The BruTile 6
came from `%LOCALAPPDATA%\vvvv\gamma\nugets\BruTile.6.0.0`.

**That folder is flat — one version of each library, shared by everything vvvv loads — and it
wins over a copy sitting next to your own assembly.** It got there on 2026-02-28 when VL.GIS was
installed from nuget.org, together with `BruTile.MbTiles.6.0.0` and an SQLite stack, because
VL.GIS's first build declared MbTiles before commit `c75f12f` removed it five minutes earlier.

**Uninstalling a package does not remove its dependencies.** VL.GIS itself was long gone from
that folder; its dependencies were still sitting there with nothing referencing them. Both
BruTile packages were moved to `_nugets-backup-VL.GIS\` (moved, not deleted).

Checked while we were there: on all of nuget.org the only `VL.*` package that touches BruTile is
VL.GIS, and vvvv's own install directory contains no BruTile at all. There is **no official vvvv
map or GIS pack** — nothing here is duplicating work upstream.

## 2026-08-13 — the incident, and the tests that lock it out

The map node was `public static`, which in VL is a stateless operation evaluated **every frame**.
At 60fps it built a fresh `Map` and tile layer sixty times a second and released none:

```
17,085 TCP connections   87,202 handles   1,294 threads   3.1 GB   in 13 minutes
```

The machine's ephemeral port range is 49152–65535 — 16,384 ports for the whole system. Once
exhausted, every program on it lost DNS. **It took the author's home network down**, and it
breached OpenStreetMap's tile usage policy, which forbids bulk downloading from donated
hardware.

**The same bug is why nothing ever rendered.** Tiles requested on frame N arrived after frame
N's map was already garbage, so the layer stayed permanently busy and permanently blank. Worth
generalising: *a resource bug usually breaks the feature too, so a mysterious blank is a reason
to look at lifetime.*

The evidence had been on screen for an hour. The stack read
`at VL.Mapsui.MapNodes.OpenStreetMapLayer(...)` / `at ...Update__TRACE__(...)` — and
`Update__TRACE__` is the evaluation context, not decoration. **Read a vvvv stack for which
method it was called from**, not only where it threw.

Fixed by making it a `[ProcessNode]` (`VL.Core.Import.ProcessNodeAttribute`, which vvvv itself
uses in VL.Skia, VL.CoreLib and — the precedent for anything networked — VL.IO.Redis). Plus a
second, smaller leak hiding behind the first: `MapsuiLayer` called `Navigator.SetSize` every
frame, and each call raises `ViewportChanged`, which Mapsui answers with a refresh.

Proven from the C# `vvvvc` generates, before any window was opened: `new OpenStreetMapNode()`
appears in `Create` and is held as state, `Update` runs per frame on that instance.

### 15 tests, and they were negative-tested

`dotnet test` — about a second, no network, no vvvv. Every test is a frame loop, because that is
the shape of the failure. Reverting the guard so the map rebuilds unconditionally fails 6 of 15,
so the suite does catch the thing it was written for.

Confirmed no network by watching the machine's TCP connections across a run: 948 before, 948
after.

### The disk cache, measured

`%LOCALAPPDATA%\VL.Mapsui\tiles`, laid out as `{zoom}/{x}/{y}.png`, expiring after 7 days —
the floor OSM's policy sets for a cache that cannot read HTTP headers, which a file cache
cannot.

**Only tiles that were drawn are stored.** That is what the policy asks for; what it forbids is
the opposite, "any pre-emptive fetching of tiles other than those a user is actively viewing".
Before the cache existed we refetched the same view on every restart, which was the
non-compliant behaviour.

Measured after a session that toggled Enabled several times at zoom 12 over Tokyo:

```
16 tiles   736 KB   average 46 KB each   zoom levels 10, 11, 12
```

Under a megabyte. Zoom 10 and 11 appear because Mapsui draws lower-resolution tiles as a
stand-in while the target level loads. For scale, the whole world at zoom 12 would be 16.7
million tiles and hundreds of gigabytes, and is exactly what the policy forbids.

The overlay prints the live count and size, so the number is never something to be taken on
trust. Computing it walks a directory, which is cheap once and ruinous sixty times a second, so
it is throttled to once every two seconds — the same mistake in different clothes — and a test
asserts 10,000 calls stay under 200 ms.

### Panning moves the navigator; it does not rebuild the map

Toggling Enabled by hand turned the runaway counter orange-red, which was a false alarm: a
deliberate rebuild is fine. That exposed something worse waiting for the interaction work.
**Dragging a map changes the centre on every frame**, and the node rebuilt whenever the centre
changed — so adding drag would have re-created the per-frame rebuild by a different route.

Now only a change to what the tile source *is* rebuilds; a change to where the map looks calls
`Navigator.CenterOn` / `ZoomToLevel` and keeps the layer, its memory cache and every tile
already fetched. `A_centre_that_moves_every_frame_still_builds_one_map` pushes 200 frames of
movement through it; under the old design that number was 200.

The alarm was rewritten to match: a count above one proves nothing, since toggling by hand
raises it. **Rebuilding on two consecutive frames** is what no hand can do, and that is what
turns the line red now.

### Also settled today

- `%LOCALAPPDATA%\vvvv\gamma\nugets\` **survives a vvvv reinstall** — it lives in the user
  profile, not the install directory. That is why five-month-old orphans were still shadowing our
  BruTile after a fresh install of vvvv 7.4.
- A globally configured Stride feed timed out for 100s per package and failed a restore with
  NU1301. `NuGet.config` now pins sources to nuget.org, so a restore here does not depend on
  what a machine happens to have configured.

## 2026-08-13 — what VL needs before it will resolve a foreign type

The single most expensive finding of the day, and it wore three disguises before it was caught.

**An upstream library has to be present as a package in a package repository, not merely
restorable as an assembly.** Without that, VL cannot resolve its types — and the failure is the
quiet kind. The node is *constructed*; none of its pins connect; every link to it is dropped;
the compiled program simply does not contain the call. `vvvvc` exits 0. No red node, nothing in
the log.

What made it hard was that three plausible explanations each looked confirmed while the real
cause was still in place:

| what it looked like | what it actually was |
|---|---|
| "VL cannot build a pin for `IEnumerable<>` of a foreign interface" | It builds `Sequence<ILayer>` fine. A single layer was being wired into a spread input, which the compiler said plainly *once types resolved*: `ILayer is no Sequence<ILayer>!` |
| "VL.GIS's BruTile pins were broken all along" | They were fine. Moving `BruTile.6.0.0` out of vvvv's shared `nugets\` that morning is what broke them — my own control experiment was contaminated by my own earlier action |
| "no package sets `IsForward` outside its own wrapper" (written in VL.GIS's notes) | `VL.Rhino.3dm` does: `<NugetDependency Location="Rhino3dm" IsForward="true" />`. The survey behind that claim only looked at `PlatformDependency` |

**Once the types resolved, vvvv's error messages became precise immediately.** Every silent
failure that day traced back to the same unresolved-type root.

The evidence that settled it was a pair of probe nodes differing in exactly one thing:

```csharp
ILayer? Update(int input)               // Update called, wired to the Renderer   ✅
ILayer? Update(global::Mapsui.Map? map) // Update never called, links dropped     ❌
```

A real install gets this for free: NuGet pulls VL.Mapsui's dependencies into
`%LOCALAPPDATA%\vvvv\gamma\nugets\` beside it, which is why `Rhino3dm`, `AssimpNet`,
`OpenCvSharp4` and `BruTile` are all sitting in there. `build.ps1` now reproduces that into
`deps\`, transitively, discovered from each `.vl`'s own NugetDependency lines.

`deps\` is separate from `dist\` because `--package-repositories` takes a semicolon-separated
list, and pointing it at a directory that also holds the document being compiled makes `vvvvc`
treat that document as a package: *"Entry point for document X.vl not found"*.

Missing a transitive dependency has a different and louder signature: `Mapsui.Rendering.Skia`
needs `Mapsui.Nts`, and leaving it out threw `FileNotFoundException` from inside a frame, every
frame, filling the log faster than a window can be closed.

## 2026-08-13 — the mouse, and three guesses that were each wrong

Interaction is wired in the patch, not inside a node: `Console` (a layer, because notifications
travel through the Skia layer graph) exposes `Mouse`, `MouseState` turns that into `Position`,
`Left Pressed` and `Wheel State`, and those drive `Mapsui.Navigate`.

Getting the wheel working took three rounds, and **each round was a link in the chain where I
had inserted a guess instead of a fact**:

1. **"Windows sends 120 per notch, so `Notch Size` defaults to 120."** VL sends **1**. The
   symptom: the wheel turned, the map did not move, nothing on screen said why. `WheelSteps(1,
   120)` is 0. My own test data had `[InlineData(1f, 120f, 0)]` commented "a source that reports
   1, misread as Windows: no movement" — the design was right and the default was wrong.
2. **"Mapsui reads the delta the way Windows sends it."** It reads only the **sign**:
   `MouseWheelAnimation.GetResolution` compares against an epsilon and asks for the next
   resolution in or out, discarding the magnitude. Multiplying by 120 was cargo cult, and
   harmless only because the sign survived it.
3. **"`Map.UpdateAnimations()` drives the viewport animation."** It does not — read it and it is
   a `foreach` over `Layers` and nothing else. The viewport animation lives on the Navigator, so
   `MouseWheelZoom` started an animation nobody advanced. This is why the bang nodes worked while
   the wheel did not: `Navigator.ZoomIn()` lands immediately.

**The methodological lesson is sharper than "search more".** I did search — every shipped patch,
for what `Wheel State` connects to — and found `FrameDifference`, which correctly told me the pin
accumulates. **I stopped at one hop.** One link further in that same patch sat
`FrameDifference → Sign`: vvvv's own answer being that the magnitude is not meaningful at all.
The thing I guessed was two links from the thing I read.

### Three kinds of test, each catching something different

Worth keeping as a pattern:

| | caught |
|---|---|
| Read the upstream source | that only the sign is used |
| `MouseWheelAnimation.Duration = 0`, assert the resolution moved | that the node's own logic is right (50 tests) |
| **Default duration, drive frames like the patch does** | **the actual bug** — it separates "the call never reached Mapsui" from "it reached it and the animation never advanced" |

The third one is the one that mattered, and it turned a problem that took two rounds of clicking
in the GUI into a red test that fixes itself in one. Its runtime went from 2 seconds (waiting out
a timeout) to 92 ms once the fix landed.

### Also settled

- **Fluent nodes' output pin is `Output`, not `Result`.** A static operation whose return type
  equals its first parameter — `CenterOn(Map, …) -> Map` — gets `Output`. `vvvvc` rejects
  `Result` outright, which is how this was established rather than guessed.
- A `Map` takes a **spread** of layers, so a single one is wrapped with `Cons`
  (`Collections.Spread`, 264 uses in shipped patches).
- The generators under `tools/legacy/` are retired: the checked-in `.vl` is the source of truth
  now. Regenerating overwrote node positions someone had arranged by hand.

## 2026-08-13 — the cache folder pin, and what a pin's type is worth

The pin started as `string cacheFolder = ""`, empty meaning the default location. The question
that improved it was **"shouldn't the default path *be* the initial value?"** Two layers of answer,
and the first is a hard stop.

**It cannot be.** A C# default parameter value must be a compile-time constant, and the folder is
built from `Environment.GetFolderPath(LocalApplicationData)` at runtime:

```
error CS1736: Default parameter value for 'folder' must be a compile-time constant
```

Measured with a three-line throwaway project rather than recalled, because the whole point of the
question was whether the current shape was a choice or a constraint.

**It also should not be.** Hardcoding a literal would ship *this machine's* path inside the node
definition. vvvv's own packs hold the counterexample: of the 815 `.vl` files shipped with 7.4,
`VL.Audio.vl` has a `Filename` pin whose initial value is `C:\temp\foo.wav`.

### What vvvv does instead: a node that yields it

`SystemFolder` in category `IO` (`CoreLibBasics.vl`) takes a `VL.Lib.IO.SpecialFolder` and outputs
a `VL.Lib.IO.Path`. **A machine-dependent path is produced by a node, not pre-filled in a pin.**
`Mapsui.Layers.CacheFolder` is the same move: empty in, default out, plus `Tiles` and `Size MB`. It
reads the disk and never the network, so it answers "where do tiles go, and how much is there?"
without switching a tile layer on.

### The larger find: a path in VL is a type, not a string

`VL.Lib.IO.Path` — 54 members of VL.CoreLib take it. It has a public `(string)` ctor, `.Value`,
`Path.Default`, and `IsRooted` / `Exists` / `Parent` / `Children`. The pin is now `VLPath?` with
`= null`, which is legal precisely because `null` *is* a compile-time constant.

Worth knowing: **no C# assembly shipped with vvvv 7.4 uses `Path` as a node parameter** — every
precedent is in `.vl`-defined nodes — so this was verified rather than assumed. `vvvvc`'s generated
C# shows the type flowing end to end:

```csharp
n11.Path __pad_SZONokgOOw3PKrH0lXmVdd_3 = __slot_SZONokgOOw3PKrH0lXmVdd;
var Result_12 = OpenStreetMap_11.Update(…, cacheFolder: __pad_SZONokgOOw3PKrH0lXmVdd_3);
var Output_49 = n17.CacheNodes.CacheFolder(folder: Folder_48, tiles: out int Tiles_50, …);
```

The payoff is in the editor: a Path IOBox opens a **file chooser on rightclick and a directory
chooser on SHIFT+rightclick**, set by
`<p:pathtype p:Assembly="VL.Core" p:Type="VL.Core.PathType">Directory</p:pathtype>`. A string pin
makes the author type the path out. The IOBox XML was copied verbatim from vvvv's own
`Example Sound particles.vl` rather than composed.

### A trap the Gray Book states outright

> "Path IOBoxes always store relative paths if possible but actually hide this fact from you!"

Its own advice for a guaranteed absolute path is a string IOBox plus `ToPath [IO]`. A relative path
reaching a node that writes files **cannot be honestly rooted** — relative to the document, to
vvvv's install folder, to whatever the working directory happens to be? `CreateDirectory` silently
picks the last, and tiles land somewhere nobody could predict. So `TileCache.TryCreate` refuses a
non-rooted path and says why, the same rule as never falling back to the default when a folder is
unusable.

### The gap that made the previous round untestable

The pin existed for a whole test round with **no IOBox connected to it**, so the three things I
asked to be checked could not be typed into at all. A pin with no pad is invisible in a patch:
`vvvvc` compiled it happily as `string Cache_Folder_11 = @"";`. **Wiring is part of adding a pin;
the compile proving the pin exists proves nothing about whether anyone can reach it.**

63 tests. Five new: a relative folder is refused rather than guessed at; `CacheFolder` reports the
default when given nothing, echoes what it is given, measures what is there, and — because it is a
static method, evaluated every frame in every patch that holds it — does not walk the disk 10,000
times in 300 ms.

### Still open

- nuget.org has nothing newer: Mapsui tops out at 4.1.9 and 5.1.0
- **Unverified: does a map actually render inside vvvv?** That is what `spike\Spike.vl` is for.
  A console program rendering a PNG does *not* count — that draws onto a bare `SKCanvas` and
  bypasses VL's coordinate space, which is the only part that can plausibly break. VL.GIS made
  exactly that mistake and lost a day to it.
- `VL.Skia.Sizing` documents one unit as **100 actual pixels**, which contradicts what VL.GIS's
  notes currently claim about `DIPTopLeft`. Measured so far, in the spike's left window:
  `Normalized` gives Pos (-1.267297, -1), Size (2.594595, 2.00) — height is always 2 and width
  is 2 × aspect ratio. `DIPTopLeft` is still unmeasured; switching the Space dropdown and
  reading `ClientBounds` settles it, and VL.GIS's docs should be corrected either way.
- The layer resets the canvas matrix itself rather than trusting `Space`, and that was confirmed
  the way it should be: changing the dropdown does not move the diagnostics box.
