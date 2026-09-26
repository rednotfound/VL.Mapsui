# VL.Mapsui

[Mapsui](https://mapsui.com) — a real map engine — as nodes for [vvvv gamma](https://vvvv.org):
tile layers, your own geometry and features on top, styles, labels, picking, widgets, and a node
that draws the map into VL.Skia.

## ⚠️ Status: 0.0.1-alpha, an early preview

> **EARLY — not ready for real work yet.** Node names, pins and behaviour may change between
> prereleases without a migration path, so do not build a project you have to deliver on it yet.
> The MIT licence lets you use it for anything, commercial work included; it comes as is, with no
> warranty and no support promise.

`0.0.1-alpha` is the first release, and it is a **prerelease**; how it gets to nuget.org, and what
each check before that proves, is in [docs/RELEASE.md](docs/RELEASE.md). It works — a map renders in vvvv
7.4, pans, zooms, draws geometry from any NetTopologySuite source and tells you which feature is
under the mouse — but the node surface can still change between versions, and Mapsui is far larger
than what is wrapped (a few dozen of its 306 public types; see
[docs/MAPSUI-SURFACE.md](docs/MAPSUI-SURFACE.md)).

| | |
|---|---|
| ✅ | 32 nodes: tile layers (OpenStreetMap, any XYZ service), a disk cache, geometry and feature layers, a lat/lon graticule, five styles, navigation, picking, pixel↔degree conversion, widgets |
| ✅ | 19 help patches, every node opens one on F1; each compiles headlessly and was opened in vvvv before release |
| ✅ | 244 tests, no network, shaped like frame loops because the expensive bugs here were about lifetime |
| ⚠️ | Mapsui 4.1.9, not 5.x: Mapsui 5 needs SkiaSharp 3 and vvvv ships 2.88 |
| ❌ | Not wrapped yet: editing geometry on the map, WMS/WFS, image and rasterizing layers, TMS, layer opacity. The map is WebMercator; reprojection is not exposed |

## Install

vvvv gamma **7.4 or newer**. In vvvv: Quad menu → Manage Nugets → Commandline, then

```
nuget install VL.Mapsui -pre
```

`-pre` is needed because this is a prerelease. It brings
[VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite) along, which is what
*makes* the geometry this package draws. Its help patches appear in the Help Browser straight
away; to use its nodes in your own document, add VL.Mapsui there through the **Dependencies**
menu — installing does not reference it by itself. Then press F1 on any node.

**If you ever installed VL.GIS 0.2.0-alpha**, delete `%LOCALAPPDATA%\vvvv\gamma\nugets\BruTile.6.0.0`
by hand. VL.GIS declared BruTile 6, Mapsui needs 5, the folder is shared by everything vvvv loads,
and uninstalling VL.GIS does not remove it. The symptom is a `TypeLoadException` naming
`BruTile.Attribution`.

## Not one map node

```
Mapsui              Map  ViewportInfo  LayerInfo  Pick
Mapsui.Layers       OpenStreetMap  XYZ  TileCache  Geometry  FeatureLayer  Graticule  VisibleRange
Mapsui.Styles       VectorStyle  SymbolStyle  LabelStyle  StyleByGeometry  StyleByValue
Mapsui.Navigate     CenterOn  ZoomToLevel  ZoomByWheel  DragBetween  Drag  ZoomIn  ZoomOut
                    ZoomToLayer  ZoomToLayers
Mapsui.Project      ScreenToWorld  WorldToScreen
Mapsui.Widgets      ScaleBar  ZoomButtons  Click
Mapsui.Skia         ToSkiaLayer
Mapsui.Debug        DiagnosticsLayer
```

A single all-in-one map node would have been less to wire, and it is deliberately not what this
is. **Nothing here decides for you what the mouse does.** Read it with VL.Skia's `MouseState` and
wire it to `Navigate`, or drive the map from an LFO, an OSC message, a keyboard or a timeline
instead. Composing that is the reason to reach for a patching environment.

`Explanation Overview of available nodes` is the front door; `HowTo Show a map` is the smallest
complete map, and each HowTo after it is one topic. Beginners start from a help patch, not from a
fatter node.

**Geometry crosses the boundary as NetTopologySuite.** `Feature` is a geometry plus attributes,
VL.NetTopologySuite's own type, and a `FeatureLayer` draws a spread of them — so whatever produced
them (a file, a service, your own `ForEach` over a record) never has to know that Mapsui will draw
them. `HowTo Draw many features` builds two hundred from a record of your own.

## Manners

`Enabled` starts **off** on anything that fetches. Opening a document in vvvv runs it, so a map
that fetched on open would give whoever opened it no chance to decline.

Tiles that were drawn are cached under `%LOCALAPPDATA%\VL.Mapsui\tiles` for 7 days — a session
over one city at zoom 12 measured 16 tiles, 736 KB; delete the folder to reset. That is what
[OpenStreetMap's tile policy](https://operations.osmfoundation.org/policies/tiles/) asks for when a
cache cannot read the server's caching headers. What it forbids is the opposite: fetching tiles
nobody is looking at, and offline use. Requests carry a User-Agent naming this package, as the
policy requires.

**The credit is on the map by itself.** The policy asks for "© OpenStreetMap contributors"
clearly on the map, not hidden behind a toggle, and Mapsui's renderer prints every layer's
attribution bottom right without being asked — there is no node to add and none that can hide it.
A tile layer carries its credit: `OpenStreetMap` has OSM's built in, and `XYZ` prints whatever its
`Attribution` pin says, so fill that pin in. Other services set their own terms — OpenTopoMap, used
in `HowTo Use any tile service`, is CC-BY-SA and asks for its own credit line.

`TileCache` is the one node that decides where tiles go. Hand its output to a layer's `Cache` pin,
or leave that pin unconnected for the default above. **Leave its `Folder` pin unconnected for the
default; never connect an empty Path IOBox** — VL resolves an empty Path against the document and
hands the node your patch's own folder.

`Layers Built` should reach 1 and stay. A number that climbs frame after frame means a layer is
rebuilt every frame, and every rebuild starts a fresh round of tile requests — which once exhausted
a machine's ephemeral ports and took a home network down. Close vvvv if you see it climb; the
diagnostics overlay's first line turns red for exactly that.

## The family

VL.Mapsui draws maps and nothing else. Its siblings compose with it through NetTopologySuite, a
library they share rather than a dependency on each other:

| package | what it does |
|---|---|
| [VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite) | geometry: points, lines, polygons, operations |
| [VL.GeoJSON](https://github.com/rednotfound/VL.GeoJSON) | reads and writes the format data arrives in |
| [VL.Overworld](https://github.com/rednotfound/VL.Overworld) | the course: no nodes, every patch that needs more than one package |

[VL.GIS](https://github.com/rednotfound/vvvv-gis) was the first attempt at all of this in one
package and is retired.

## Building

```powershell
dotnet test test\VL.Mapsui.Tests\VL.Mapsui.Tests.csproj   # 244 tests, ~2 s, no network
.\build.ps1                                                # build + stage dist\
.\pack.ps1                                                 # + a .nupkg in dist\feed
.\tools\Test-VLPackage.ps1                                 # static package checks
.\tools\Test-VLPatch.ps1                                   # every help patch, and F1 for every node
.\tools\Compile-HelpPatches.ps1                            # vvvvc over every help patch, reads the C#
.\tools\Test-Install.ps1 -FromNuGetOrg                     # install like a user, compile the help from it
.\tools\Open-HelpPatch.ps1 "Show a map"                    # the only way to launch vvvv here
```

vvvv must be closed while building: a running one holds the staged assemblies open. Launch through
`Open-HelpPatch.ps1` (or double-click `Open-HelpPatch.cmd`), never by hand — it needs three package
repository folders, and a missing one fails with an error naming something else.

## Reading

- [NOTES.md](NOTES.md) — what was measured, with dates
- [docs/RULES.md](docs/RULES.md) — what earns a node, and when a node runs
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — the pipeline and the NetTopologySuite boundary
- [docs/MAPSUI-SURFACE.md](docs/MAPSUI-SURFACE.md) — what Mapsui offers, what is wrapped, what will not be
- [docs/HELP-PATCH-STYLE.md](docs/HELP-PATCH-STYLE.md) — the help style, measured across 60 community packs
- [docs/RELEASE.md](docs/RELEASE.md) — the release checklist, what each step proves
- [CLAUDE.md](CLAUDE.md) — the rules that matter in this repository

## Licence

VL.Mapsui is MIT — see [LICENSE](LICENSE). The package contains only its own assembly, help
patches and docs; everything else arrives as a NuGet dependency under its own licence:
[Mapsui](https://github.com/Mapsui/Mapsui) (MIT), [BruTile](https://github.com/BruTile/BruTile)
(Apache-2.0), [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) and
NetTopologySuite.Features (BSD-3-Clause), SkiaSharp (MIT, supplied by vvvv).

Map data from OpenStreetMap is © OpenStreetMap contributors, available under the
[ODbL](https://www.openstreetmap.org/copyright). OpenTopoMap tiles are CC-BY-SA.
