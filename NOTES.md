# VL.Mapsui — measurement log

Running record of what was actually measured, with dates. Claims without a measurement behind
them do not belong here.

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
