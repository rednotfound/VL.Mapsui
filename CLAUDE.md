# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## Project overview

**VL.Mapsui** wraps [Mapsui](https://mapsui.com) — a real map engine — as nodes for
[vvvv gamma](https://vvvv.org). One package per wrapped library, so this one draws maps and does
nothing else.

**It is one of four repositories, and knowing which does what saves guessing:**

| | what it is |
|---|---|
| `vl-mapsui` (here) | draws maps: tiles, layers, styles, picking |
| `D:\2026_Projects\vl-nettopologysuite` | geometry — points, lines, polygons, operations |
| `D:\2026_Projects\vl-geojson` | reads and writes the format data arrives in |
| `D:\2026_Projects\vl-cartography` | **the course.** No nodes; it declares the other three and holds every patch that needs more than one of them |

They compose through **NetTopologySuite**, a library they share rather than an agreement they made:
none of them references another. `D:\2026_Projects\vvvv-gis` holds the retired `VL.GIS`, whose
published `0.2.0-alpha` still declares BruTile 6 and conflicts with this package — see
`vl-cartography\README.md` for what a user has to delete by hand.

**Current state (2026-08-14): a working package, not yet published.** A map renders in vvvv 7.4,
pans, zooms and takes geometry from any NTS source. `VL.Mapsui.nuspec`, `build.ps1`, `pack.ps1`,
`tools\Test-VLPackage.ps1` and **186 tests** exist. Nothing is on nuget.org.

Node count is the honest measure of how far this is from finished: **Mapsui exposes 306 public
types and we wrap a few dozen**. See [docs/MAPSUI-SURFACE.md](docs/MAPSUI-SURFACE.md) for what is
wrapped, what is not, and what will not be.

**The stray-tile bug is fixed, and its cause is worth carrying: an empty Path IOBox is not empty.**
`Value=""` in a `.vl` means *relative to this document*, and the empty relative path is the
document's own folder — so the layer was handed a perfectly good folder and wrote 444 tiles next to
two repositories while every guard reported success. Only an **unconnected** pin produces the `null`
that can mean "the default". The cache is a value now (`TileCache` produces it, `OpenStreetMap`
consumes it) and no patch here contains a Path IOBox. See NOTES.md, 2026-08-14, and rule 8 below.

Measurements and their dates live in [NOTES.md](NOTES.md). Claims without a measurement behind
them do not belong there or here.

## The rules that matter most here

Both of the expensive mistakes in this repository were about **when a node runs**, not about
what it computes. Read [`docs/RULES.md`](docs/RULES.md) before
writing any node; the four questions at the top of it would have prevented both.

1. **A `public static` method is evaluated every frame.** Opening a `.vl` *is* running it.
   Anything holding a connection, file handle, cache or thread must be a `[ProcessNode]` class.
   Written as a static method, the map node opened 17,000 TCP connections in 13 minutes,
   exhausted the machine's 16,384 ephemeral ports and **took down the author's home network** —
   and the same bug is why nothing was ever drawn, since tiles arrived for maps already
   discarded.
2. **`Enabled` is off by default** on anything that fetches. Whoever opens the patch has not
   agreed to anything yet. OpenStreetMap runs on donated hardware and its tile policy forbids
   bulk downloading; we send a User-Agent naming this package.
3. **Never leave vvvv running unattended, and never start it in the background.** Launch, read
   the value, close. Leaks accumulate across sessions — this one grew over several. **Launch only
   through `tools\Open-HelpPatch.ps1`** — there are three package repository folders, and every
   one omitted produces an error naming something else entirely. Two launches were lost to that in
   one afternoon.
4. **Only a change to what the tile source *is* may rebuild the map.** Where it looks goes
   through `Navigator.CenterOn` / `ZoomToLevel`, because dragging changes the centre on every
   frame and a rebuild-on-move design becomes a per-frame rebuild the moment interaction exists.
   **And when that rebuild does happen, it must not rebuild the tile source.** `HttpTileSource`
   owns an `HttpClient`, is not `IDisposable`, and is released by nothing, so one per rebuild is
   one leaked connection pool per rebuild — rule 1's incident at a slower clock. Both layer nodes
   cache their sources; `new TileLayer(existingSource)` is legal and cheap. Ask of anything a
   rebuild creates: **if a thousand of these exist, who frees them?** See `docs/RULES.md` rule 12
   and NOTES.md, 2026-08-17.
5. **The overlay's first line is the smoke alarm.** It turns red on a rebuild across **two
   consecutive frames**, which nothing done by hand can produce. A raw count is not the alarm —
   toggling `Enabled` rebuilds, quite correctly, and an earlier version cried wolf for it. If
   the line goes red, close vvvv immediately.
6. **A machine-dependent default is a node, not a pin's initial value**, and a path pin is
   `VL.Lib.IO.Path` rather than `string`. Both are settled facts, not preferences: a C# default
   parameter value must be a compile-time constant (`CS1736`), so a folder built from
   `Environment.GetFolderPath` cannot be one — and hardcoding a literal would ship this machine's
   path inside the node, which is what `VL.Audio.vl`'s `Filename` pin does (`C:\temp\foo.wav`).
   vvvv's own answer is `SystemFolder [IO]`, a node that *yields* the path; `Mapsui.Layers.TileCache`
   copies it. Type the pin `VLPath?` with `= null` (`null` is a constant). Details in `NOTES.md`,
   2026-08-13.
7. **Adding a pin is not finished until an IOBox reaches it.** A pin with no pad connected is
   unreachable in the patch, and `vvvvc` compiles it without complaint as a literal
   (`string Cache_Folder_11 = @"";`). A compile proving the pin exists proves nothing about
   whether anyone can set it — that cost a whole test round here.
8. **An empty Path IOBox is not empty, and "off" is not `null`.** `Value=""` in a `.vl` means the
   path *relative to the document*, so VL hands the node the patch's own folder, absolute — which is
   how 444 tiles were written next to two repositories with `IsPathRooted`, `CreateDirectory` and
   the status pin all reporting success. Only an **unconnected** pin gives the `null` that can mean
   "the default", so never wire an empty Path IOBox in a shipped patch and never write "leave it
   empty" on a pin. The same ambiguity repeats one level up wherever `null` would have to mean three
   things at once — unconnected, switched off, and failed — so a thing that can be off needs a value
   saying so (`TileDiskCache.IsOn`), not an absence. Details in `NOTES.md`, 2026-08-14.

## Node design rules inherited from VL.GIS

[`docs/RULES.md`](docs/RULES.md) — what earns a node, how many
pins one may have, what may be bundled and what must never be. Read it before adding a node, and
especially before "this is confusing, let me make one node that does it all": that instinct
produced the all-in-one map node this package was rebuilt to undo. Measured summary: 94% of the
ecosystem's nodes take three inputs or fewer, and the libraries people learn from carry more help
patches than nodes.

`ZoomByWheel` (5 inputs) is this package's entry on that document's to-do list.

**Where a patch lives: one package → this repo's `help\`; two or more → `VL.Cartography`.**
Everything under `help\` is packed, so a patch needing a package this one does not depend on opens
red for anyone who installed VL.Mapsui alone — and the cure is not to add the dependency, because a
map engine has no business requiring a GeoJSON reader. `D:\2026_Projects\vl-cartography` is the
course that owns those, declares the whole family in its nuspec, and compiles them all as a
standing integration test. `Test-VLPackage.ps1` here refuses a help patch that names a foreign
`VL.*` package, and refuses an `examples\` folder coming back.

## Packaging rules inherited from VL.GIS

These apply the moment a `.nuspec` appears here. All of them are silent when broken —
see [`docs/RULES.md`](docs/RULES.md).

- Every forwarded assembly needs `[assembly: ImportAsIs(Namespace = "VL")]`; without it nodes
  are invisible with no warning anywhere. Already in `src/VL.Mapsui/AssemblyInfo.cs`.
- A `.vl` is **UTF-8 with BOM**, and every `Id` is exactly 22 characters, first `[A-V]`, rest
  `[0-9A-Za-z]`, unique in the document. `tools\New-VLId.ps1` generates them.
- **The checked-in `.vl` is the source of truth; the generators under `tools\legacy\` are
  retired.** This reverses the earlier rule "regenerate, never edit in place", which was written
  when patching in place produced thirteen duplicated nodes and six colliding IDs. Regenerating
  turned out to be the more destructive of the two once a patch was in use: it discarded a layout
  arranged by hand in the GUI. So editing in place is now the only route, and the mitigation moved
  from *avoid it* to **validate every edit**: anchor each insertion on a match that occurs exactly
  once (fail loudly otherwise), then check ID legality and uniqueness, dangling link endpoints, the
  BOM, an XML parse, `tools\Test-VLPackage.ps1`, and a `vvvvc` compile of each patch.
- `VL.Core` and `VL.Core.Skia` are referenced with `ExcludeAssets="runtime"` — they ship inside
  vvvv and our copies must never be distributed. **SkiaSharp is deliberately not pinned**:
  Mapsui.Rendering.Skia wants ≥ 2.88.9 and vvvv has 2.88.8, so pinning vvvv's version fails
  restore with NU1605. It works because the whole 2.88.x line carries assembly version
  `2.88.0.0`; the same is true of HarfBuzzSharp (`1.0.0.0` on both sides despite file versions
  7.3.0.1 vs 7.3.0.3). **Compare assembly versions, never file versions.**

## The tile cache

`%LOCALAPPDATA%\VL.Mapsui\tiles`, as `{zoom}/{x}/{y}.png`, expiring after 7 days. Delete the folder
to reset.

**One node owns it.** `Mapsui.Layers.TileCache` produces a `TileDiskCache` and `OpenStreetMap`
consumes it on a `Cache` pin; the layer node has no folder pin at all. Leave `Cache` unconnected for
the default, and leave `TileCache`'s `Folder` unconnected for the same — **unconnected is the only
thing that means "the default"**, which is why there used to be two places to set the folder and no
guarantee they agreed. `TileCache` reads the disk and never the network, so it still answers "where
do tiles go, and how much is there?" with the layer switched off.

The cache is part of the layer's **rebuild identity** (it is attached at construction) and is
compared by reference, since one node hands out one instance; the folder inside it is compared
case-insensitively and trimmed, because Windows paths differing only in case are the same folder and
refetching every tile for that would be absurd. Three failures are deliberately *not* silent: an
unusable folder, a **relative** one, and a cache that is simply switched off are each reported
rather than replaced by the default. They are also three distinct **values** rather than `null`,
because a consumer must be able to tell "nobody said anything" from "somebody said no".

**Only tiles that were drawn are stored**, which is what OSM's policy requires; the thing it
forbids is pre-emptive fetching of tiles nobody is looking at. Measured: a session over Tokyo at
zoom 12 produced **16 tiles, 736 KB**. The overlay prints the live figure so it is never taken
on trust, and the directory walk behind it is throttled to once every two seconds because doing
it per frame is the same mistake in different clothes.

## VL.GIS can now be loaded at the same time — fixed 2026-08-14

It could not before, and worse: installing VL.GIS *broke* this package. `Mapsui.Tiling` pins
BruTile to `[5.0.6, 6.0.0)` while VL.GIS used BruTile 6, and `BruTile.Attribution` changed layout
between them, so mixing threw `TypeLoadException`.

**The conflict was machine-wide.** `%LOCALAPPDATA%\vvvv\gamma\nugets\` is a flat folder with one
version of each library, shared by everything vvvv loads, and it wins over a copy sitting next to
our own assembly. **Uninstalling does not remove a package's dependencies**, nor does reinstalling
vvvv, since that folder lives in the user profile — VL.GIS's BruTile 6 outlived VL.GIS by five
months. The published `VL.GIS 0.2.0-alpha` still carries it, so anyone who installed that version
must delete `%LOCALAPPDATA%\vvvv\gamma\nugets\BruTile.6.0.0` by hand; upgrading will not.

**VL.GIS dropped BruTile entirely** (its commit `15d40f5`), which was the whole conflict. Compared
by assembly version rather than file version, every other shared library resolves to one identity:
NetTopologySuite 2.5 and 2.6 are both `2.0.0.0`, and SkiaSharp 2.88.8 and 2.88.9 are both
`2.88.0.0`. Verified in one vvvv with both packages loaded — no exception, both node sets present,
map renders, which also clears the risk that Mapsui 4.1.9 (compiled against NTS 2.5) would object
to VL.GIS's 2.6.

The two packages compose through **NetTopologySuite**, not through each other: VL.GIS computes
geometry and `Mapsui.Layers.Geometry` draws it. Neither references the other.
`vvvv-gis\examples\Example Map with data on it.vl` is the patch that proves it — it lives there,
outside either package's `help\`, because a patch needing two packages cannot ship inside one whose
dependencies do not guarantee the other.

## Repository layout

```
vl-mapsui/
├── NOTES.md                      # measurement log, with dates
├── docs/RULES.md                 # ⭐ the rules carried over from VL.GIS - read before any node
├── docs/ARCHITECTURE.md          # the pipeline, the NTS boundary, why a node holds state
├── docs/MAPSUI-SURFACE.md        # what Mapsui offers, what we wrap, what we will not
├── docs/VL-PATCH-XML.md          # ⭐ hand-authoring a .vl - read before editing one
├── VL.Mapsui.vl / .nuspec        # the package. .vl is hand-edited but never regenerated
├── src/VL.Mapsui/
│   ├── LayerNodes.cs             # [ProcessNode] OpenStreetMap - tile layer, cache, attribution
│   ├── FeatureNodes.cs           # Feature and Split - NTS geometry + attributes, the neutral type
│   ├── PickNodes.cs              # [ProcessNode] Pick + ScreenToWorld - the only nodes handing data BACK
│   ├── FeatureLayerNodes.cs      # [ProcessNode] FeatureLayer - THE NTS to Mapsui adapter
│   ├── StyleNodes.cs             # [ProcessNode] VectorStyle + Styles.Combine (FLATTENS - nested draws nothing)
│   ├── GeometryThemeNodes.cs     # [ProcessNode] StyleByGeometry - one style per geometry type
│   ├── GeometryLayerNodes.cs     # [ProcessNode] Geometry - the shortcut, composed from those three
│   ├── MapNode.cs                # [ProcessNode] Map + ViewportInfo / LayerInfo readers
│   ├── NavigateNodes.cs          # CenterOn, ZoomToLevel, ZoomByWheel, Refresh …
│   ├── DragNode.cs, ZoomNodes.cs # [ProcessNode] - they remember the previous frame
│   ├── WidgetNodes.cs            # [ProcessNode] ScaleBar, Attribution, ZoomButtons
│   ├── WidgetInput.cs            # the hit test behind Click - arithmetic only, testable alone
│   ├── SkiaNodes.cs              # ToSkiaLayer
│   ├── MapsuiLayer.cs            # VL.Skia.ILayer - draws it, plus the diagnostics overlay
│   ├── PixelSpace.cs             # pixel/VL space bridge
│   └── TileCache.cs              # the disk cache, its folder and its size
├── help/VL.Mapsui/               # Explanation Overview + 8 HowTos + Help.xml (ordering and tags)
├── test/VL.Mapsui.Tests/         # 186 xunit tests, no network, no vvvv
├── build.ps1, pack.ps1           # build + stage dist\, pack into dist\feed\
├── NuGet.config                  # sources pinned to nuget.org
└── tools/
    ├── Open-HelpPatch.ps1        # the ONLY way to launch vvvv here - three package repositories
    ├── Test-VLPackage.ps1        # static package validator
    ├── Normalize-HelpPatches.ps1 # run after any GUI session - vvvv repins deps AND saves Enabled=True
    ├── Compile-HelpPatches.ps1   # headless vvvvc over every help patch; needs pack.ps1 first
    ├── New-VLId.ps1              # 22-char VL document IDs
    └── legacy/                   # retired generators; the checked-in .vl is the truth
```

## Tests

`dotnet test test\VL.Mapsui.Tests\VL.Mapsui.Tests.csproj` — 186 tests, well under a second. No
network and no vvvv: the tile source is faked, and the geometry tests use a MemoryLayer.

They exist because the expensive bug here was a **lifetime** bug, not an arithmetic one, so
every test is shaped like a frame loop: call `Update` many times and assert on how much got
built. `A_hundred_frames_with_unchanged_inputs_build_one_map` is the regression test; under the
old code that number was 100.

**Negative-tested**, per the rule that a check which has never failed on known-bad input is not
a check: reverting the guard in `OpenStreetMapNode.Update` to rebuild unconditionally fails 6 of
the 15. Confirmed 2026-08-13.

Two things the suite is careful about:

- **No test touches the network**, and that is load bearing rather than tidy. Building a map
  creates a tile source but issues no request; fetching starts only when a viewport size and a
  refresh arrive, which is `MapsuiLayer.Render`'s job and is never called. Verified by watching
  the machine's TCP connections across a run rather than by assuming.
- **`PixelSpaceTests` hands the caller a transformation that would ruin the result if it were
  honoured.** Drawing onto a bare `SKCanvas` with no transformation would prove nothing — that
  is the false proof already made once on this stack.

The test project references `VL.Core` and `VL.Core.Skia` **without** `ExcludeAssets`, unlike
`src\VL.Mapsui`. The package excludes them because vvvv supplies its own; a test host supplies
nothing, and without them every test fails on `Could not load file or assembly 'VL.Core.Skia'`
before reaching an assertion.

## Node categories

`category = (.NET namespace minus the "VL" prefix) + type name`, from
`[assembly: ImportAsIs(Namespace = "VL")]`. Namespace `VL.Mapsui` therefore gives `Mapsui`.
`[ProcessNode(Name, Category)]` and `[Name("...")]` override it.

**No public node may mention a Mapsui type in its signature yet.** VL builds a node only for
methods whose types it has imported, and it learns a foreign library's types from a
`<NugetDependency>` in the `.vl`. This spike is loaded through a `ProjectDependency` and declares
none, so a node returning `Mapsui.Map` is silently never created — greyed out in the patch,
absent from the compiled program, every link to it dropped, nothing in the log. Exposing `Map` is
the better API and returns with the nuspec.

## Commands

```powershell
# vvvv must be closed first - it holds the built assembly open
dotnet build src\VL.Mapsui\VL.Mapsui.csproj -c Release
.\tools\Build-SpikePatch.ps1

# NEVER type the vvvv launch by hand. THREE package repositories are needed - dist\, deps\ and
# ..\vl-nettopologysuite\dist\ - and omitting one produces an error naming something else
# entirely ("The referenced symbol source 'Mapsui.dll' couldn't be found", or "Missing package"
# followed by 25 ambiguous Point candidates). Two launches were lost to this on 2026-08-16.
# Cross-package patches are VL.Cartography's; its launcher carries six folders.
.\tools\Open-HelpPatch.ps1 "Draw many features"     # -List shows them all

# Compile every help patch headlessly, then READ THE GENERATED C# before opening any window:
#   new OpenStreetMapNode() must appear in Create, never in Update
.\pack.ps1                                  # dist\feed\*.nupkg is what restore reads
.\tools\Compile-HelpPatches.ps1 -OutputDirectory <abs-dir>

# It needs BOTH `--package-repositories dist` (how vvvv finds the package folder) and a NuGet
# source for dist\feed (how restore finds the nupkg). Passing one gives a confident error about
# the other, and it only ever seemed to need one because a stale package sat in
# %USERPROFILE%\.nuget\packages -- exactly what build.ps1 now evicts. NOTES.md, 2026-08-14.

# Launch with Enabled off, confirm zero connections, then turn it on and watch
Get-NetTCPConnection | Where-Object { $_.OwningProcess -eq (Get-Process vvvv).Id }
```

## Working style

- **Change one variable at a time.** Two guesses were spent on this spike's grey nodes before
  the compiled C# settled it in one look.
- **Before believing a green result, name the mechanism by which it could have gone red.** A
  console program rendering a PNG proves nothing about vvvv: it draws onto a bare `SKCanvas` and
  bypasses the coordinate space, which is the only part that plausibly breaks.
- **Before writing a node of an unfamiliar kind, read a shipped package that ships that kind.**
  `VL.ImGui.ToSkiaLayer` supplied the whole pixel-space approach here. `VL.IO.Redis` uses
  `[ProcessNode]` for a node owning a connection, and reading it first would have prevented the
  incident above.
- **Opening a help patch in vvvv rewrites its `NugetDependency` version to whatever is installed,
  and saving keeps it.** `0.0.0` became `0.0.1-alpha` that way, which would ask every user for that
  exact version forever. Run `tools\Normalize-HelpPatches.ps1` after any GUI session.
- **Validate before committing, in a separate step.** The rewrite above was caught by
  `Test-VLPackage.ps1` in the same command block as the commit, so it was already pushed by the
  time the FAIL printed. A check whose result arrives after the irreversible action is not a gate.
- **Trace the whole chain, not one hop.** Surveying every shipped patch for what VL's
  `Wheel State` connects to found `FrameDifference`, which answered "it accumulates". Stopping
  there left the magnitude to a guess — Windows sends 120 per notch, VL sends 1 — and the symptom
  was a wheel that turned and a map that did not move, with nothing on screen to say why. One
  link further in the same patch sat `FrameDifference → Sign`: vvvv's own answer being that the
  magnitude is not meaningful at all. What was guessed was two hops from what was read.
