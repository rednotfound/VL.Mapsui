# Help patch plan — 2026-09-24

The help patches are this package's second test suite: `dotnet test` proves the arithmetic, a help
patch proves the *design* — whether a node can be reached, wired and understood by someone who did
not write it. This plan says what exists, what VL.NetTopologySuite settled the same day that
transfers here, what is different about a map, and the order of work. It is a plan, so every row
below is either "done" with a date or a to-do; nothing in it is a measurement.

## Where we are

Eleven `HowTo`s and one `Explanation`, all written 2026-08-13 … 2026-09-24, none seen through the
style measured in vl-nettopologysuite's `docs/HELP-PATCH-STYLE.md`:

| symptom | measured here | community median |
|---|---|---|
| one prose box per patch | 1 400 – 6 400 characters | 34 characters per note, 3 notes |
| body font | unset (vvvv default 12) | 9pt, heading 14–20pt |
| `HelpFocus` flags | **0** in 12 documents | 511 of 689 shipped patches carry them |
| nodes-on-canvas Explanation | four essays, no nodes | VL.IO.Redis: the nodes, one line each |

So **F1 opens nothing for any node in this package**, and every patch reads as an essay. Both are the
same two findings NTS made on its first fifteen and fixed the same day.

**The "before", from the carried `tools\Test-VLPatch.ps1` on 2026-09-24** (kept because the old
validator here reported all twelve documents valid): 0 of 36 nodes open a help patch on F1; **four
duplicate IDs** in `HowTo Draw many features`; an **annotation box not set to `Comment`** in that
patch and in `HowTo Label your data` (it renders as a one-line value box); the same **dangling
IOBox** shipped in `HowTo Add widgets to the map` and `HowTo Drive the map with the mouse`; and
label-strip collisions in nine of eleven HowTos. Every prose box exceeds the fit budget. None of
this was visible to `Test-VLPackage.ps1`, which checks the package and not the patch.

**How other libraries organize theirs** is measured in `docs/HELP-PATCH-STYLE.md`, "How the
community organizes a help folder". The three findings that shaped this plan: the host is always in
the patch and is *not* what makes a patch long (VL.Skia's HowTos carry a `Renderer` 46 times out of
48 and still have a median of 10 nodes); topics are domain words and each HowTo is one node group;
and vvvv teaches *driving a view* with a node that reads the mouse itself (`Camera [Skia]`,
`OrbitCamera [Stride]`), with the wiring form one patch over as an Explanation.

### Node coverage (grep of `OperationCallFlag` / `ProcessAppFlag` names over `help\`)

36 public nodes. Never used in any help patch: **`Geometry`** (the rung-1 layer — the simplest way to
see a shape, and no patch shows it), `XYZ` appears once inside the layer-stacking patch, and none of
`ZoomAt`, `Refresh`, `DragBetween`, `ZoomToLayers`, `WorldToScreen`, `LayerInfo`, `DiagnosticsLayer`
appear anywhere. `TileCache` appears in 7 patches, none of them about the cache.

### The boilerplate problem, which is ours and not NTS's

Eight of eleven patches carry the same fourteen nodes before their topic starts: `Renderer`,
`MouseState`, `Console`, `FrameDifference`, `Vector`, `Group`, `Cons`, `Drag`, `ZoomByWheel`,
`OpenStreetMap`, `TileCache`, `Map`, `ToSkiaLayer`, `ViewportInfo`. That is the dataflow equivalent
of the essay: a patch about `LabelStyle` is 45 KB, and the label is one node of twenty. It also makes
every patch a network patch, so every GUI check waits on tiles and every first-time reader sees a
blank window until they find `Enabled`.

## What transfers from VL.NetTopologySuite unchanged

Carried verbatim, because they are measurements of the community and of vvvv, not opinions:

1. **The style** — `docs/HELP-PATCH-STYLE.md` (copy, with the appendix so it can be re-measured).
   Heading 20pt one line → optional 9pt intro under 260 characters → wired nodes → `< notes` under
   150 characters beside the thing they describe. No "One idea:", no design paragraphs; those go to
   `docs/ARCHITECTURE.md` and `docs/MAPSUI-SURFACE.md`, which already hold them.
2. **Help flags** — one `High` per node across the library, `Low` where it is merely used. F1 works
   through nothing else.
3. **`Help.xml` orders by Topic**, files carry a prefix and never a number.
4. **The tooling** — `HelpPatchGen.ps1` (scaffold; already handles `ProcessAppFlag`, regions and
   `Set-HelpFlags`), `Test-VLPatch.ps1` (BOM, IDs, dangling links, unwired label pads, label-strip
   overlap, annotation fit, help-flag audit, `Help.xml` ↔ disk in both directions). Both take the
   package name as the only edit; the geojson and NTS copies differ by 65 lines of exactly that.
5. **The gate per patch**, in this order and never combined with the commit:
   `Test-VLPatch` → `pack.ps1` → `Compile-HelpPatches -Patch` and **read the C#** (every
   `[ProcessNode]` constructed in `Create`, every wired pin present, no `default(...)` where a value
   was meant) → GUI through `tools\Open-HelpPatch.ps1`, screenshot by `PrintWindow` → close vvvv →
   `Normalize-HelpPatches` → commit. F1 on the flagged node is the last check, and needs `build.ps1`
   to have restaged `dist\` first.

## What is different here, and the decisions it forces

Decisions D1–D5 were put to the user on 2026-09-24 with these recommendations and the go-ahead was
given the same day ("准备好了的话我们就开干"); D5's premise (no patch here was hand-arranged) is
what `git log -- help/` shows and was not contradicted.

**D1. A patch shows its topic and nothing else. Mouse driving is a topic, not a preamble.**
`HowTo Drive the map with the mouse` and `HowTo Pick what you clicked` need the mouse; `Add widgets`
needs `Click`. Every other patch gets its view from `CenterOn` + `ZoomToLevel` or `ZoomToLayer` and
carries no `MouseState`. This halves most patches and is itself a design test: a topic that cannot
be shown without the mouse is a topic with a hidden dependency.

**D2. Offline by default.** A patch about a style, a layer, a label, picking, or the viewport draws on
a `Graticule` (or on nothing) and issues no request. Only the patches whose topic *is* tiles
(`Show a map`, `Use any tile service`, `Cache tiles`, `Add widgets` for the attribution) carry
`OpenStreetMap` or `XYZ`, with `Enabled` off and the first note saying so. Cheaper GUI rounds, and
the reader sees something at once.

**D3. Every node gets one home.** The table below assigns the `High` flag; a node used elsewhere is
`Low` there. The audit in `Test-VLPatch` turns red for a node with no `High` anywhere, which is how
`Geometry` would have been noticed a month ago.

**D4. Long patches split by node group, the way NTS split by node group.** `Stack several layers`
teaches order, `Enabled`, `XYZ`, `VisibleRange` and `ZoomToLayer` in one 48 KB document; that is four
patches. `Draw many features` stays one — `ToFeatures` *is* the topic and the record is its subject.

**D5. Edit in place where the layout was arranged by hand; regenerate where it was not.** `git log
-- help/` shows every patch here authored by the tooling on 2026-08-13 … 09-24 and none hand-tidied
in the GUI, unlike vl-overworld's. So regenerating from `HelpPatchGen` is safe here **once the user
confirms none was rearranged by hand** — after which the `.vl` is the truth again.

## Target set — 20 documents for 36 nodes

Topics are `Help.xml` topics. **bold** = the node's `High` flag lives here. ★ = new patch.

| Topic | patch | High flags | offline? |
|---|---|---|---|
| Start here | Explanation Overview of available nodes | nodes on canvas by category, one `<` line each; `High` for nothing that has a HowTo, `High` for `DiagnosticsLayer`, `LayerInfo` | yes |
| A map on screen | HowTo Show a map | **OpenStreetMap, Map, ToSkiaLayer, ViewportInfo** | no |
| | ★ HowTo Cache tiles | **TileCache** — folder, size, `IsOn`; unconnected means default | no |
| | ★ HowTo Use any tile service | **XYZ** — the URL template, attribution text, a second source beside OSM | no |
| | HowTo Draw a graticule | **Graticule** | yes |
| Your data on the map | ★ HowTo Draw a geometry | **Geometry** — a WKT, one colour, the shortest chain there is | yes |
| | HowTo Draw your own shapes | **FeatureLayer, VectorStyle** — the same shape gains a name | yes |
| | HowTo Label your data | **LabelStyle** | yes |
| | HowTo Draw many features | **ToFeatures, SymbolStyle** | yes |
| Styling | HowTo Style mixed geometry | **StyleByGeometry** | yes |
| | HowTo Style by a value | **StyleByValue** | yes |
| Layers | HowTo Stack several layers | order = spread order, `Enabled` as the switch (no High of its own) | yes |
| | ★ HowTo Show a layer only at some zooms | **VisibleRange** | yes |
| | ★ HowTo Frame your data | **ZoomToLayer, ZoomToLayers** | yes |
| Where the map looks | ★ HowTo Set the view | **CenterOn, ZoomToLevel, ZoomAt, Refresh** | yes |
| | HowTo Drive the map with the mouse | **Drag, ZoomByWheel, ZoomIn, ZoomOut, DragBetween** | yes |
| Asking the map | HowTo Pick what you clicked | **Pick** (+ `Split` from NTS, Low) | yes |
| | ★ HowTo Convert screen and world | **ScreenToWorld, WorldToScreen** — cursor → lon/lat, lon/lat → a Skia circle on the map | yes |
| Widgets | HowTo Add widgets to the map | **ScaleBar, Attribution, ZoomButtons, Click** | no |

Twenty against thirty-six nodes is 55 % — above NTS's 38 % because a map has more process nodes with
behaviour to show, and inside the 16–24 %-of-node-count band only if node count were four times
larger. That ratio is a reason to keep patches short, not to write fewer.

Not a patch: `Refresh`'s `continuous` pin, the diagnostics overlay's smoke-alarm line, the OSM
User-Agent. Each is one `< note` in the patch that already has the node.

## What the patches are expected to find (the "test the design" half)

Written down before starting, so the finding can be compared with the prediction:

- **`ZoomByWheel` has five inputs** and is already on RULES.md's to-do list. Writing its note will
  say whether `notchSize` should be a pin at all.
- **The mouse chain is fourteen nodes**, most of them VL.Skia's. If D1 leaves `Drive the map with
  the mouse` still needing all fourteen, that is the number to put beside vvvv's own answer, which
  the survey found to be **a node, not wiring**: `Camera [Skia]` and `OrbitCamera [Stride]` read the
  host's mouse themselves and expose only `Initial …` pins, and their help patch is three nodes and
  one note listing the gestures. RULES.md concluded the opposite for this package after the
  all-in-one node incident, and the two are not the same claim — the incident was a node that
  *rebuilt the map* on movement, not a node that *interpreted the mouse*. The rewrite measures the
  wiring form first; whether a `MouseNavigate`-style node follows is a RULES.md decision to take
  with that number in hand, not here.
- **`Geometry` versus `FeatureLayer`**: whether a reader can tell from two adjacent patches when to
  climb the rung. If the notes need more than one line each to say so, the split needs a rethink.
- **`Enabled` off + `Cache` unconnected**: the first thing a reader of `Show a map` sees is a blank
  window. The first note must be the switch, and the patch must say what the window shows before
  it is on.
- **Whether any node needs a value the patch cannot produce without a second package.** That is a
  node designed for VL.Overworld rather than for this package, and the answer is a move, not a
  dependency.

## Order of work

| step | what | gate |
|---|---|---|
| 0 | **done 2026-09-24.** `HELP-PATCH-STYLE.md` carried and extended with the organization survey; `HelpPatchGen.ps1` (dependency default `VL.Mapsui.vl`, `MapWindow` for the three host nodes, `Save-Doc -Nts`), `Test-VLPatch.ps1` (36-node F1 audit) and `Compile-HelpPatches.ps1` (reads the C#, 24 process nodes must be built in `Create`; the old exit-code-only script moved to `tools\legacy\`) carried; scaffold smoke-tested (a generated graticule patch passes `Test-VLPatch`); the "before" recorded above | the audit reports 0 of 36 nodes on F1 ✅ |
| 1 | the seven ★ patches — new ground first, because they test nodes nothing has tested and cost no rewrite | each through the per-patch gate |
| 2 | rewrite the Explanation as nodes on canvas | GUI |
| 3 | rewrite the eleven existing patches in the style, applying D1/D2/D4 | each through the gate; `Compile-HelpPatches` with no filter at the end, because deleting boilerplate across patches is exactly the cross-patch break it exists for |
| 4 | `Help.xml` topics and tags; nuspec line 25 ("Nine help patches") corrected; F1 verified on one node per category | `Test-VLPackage`, `Test-VLPatch`, both green in a separate step before the commit |

One patch per commit. The step-1 patches also tell us whether `HelpPatchGen` needs anything a map
patch has and a geometry patch did not (a `Renderer` node, a `Group`, a `Skia` dependency line) —
find that out on a new patch, not while rewriting a working one.
