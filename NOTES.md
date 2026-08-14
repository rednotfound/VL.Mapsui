# VL.Mapsui — measurement log

Running record of what was actually measured, with dates. Claims without a measurement behind
them do not belong here.

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
