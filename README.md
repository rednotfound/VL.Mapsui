# VL.Mapsui

[Mapsui](https://mapsui.com) as nodes for [vvvv gamma](https://vvvv.org): a tile layer, a map to
put layers on, navigation operations, and a node that draws the map into VL.Skia.

Companion to [VL.GIS](https://github.com/rednotfound/vvvv-gis), which is a *toolbox* — geometry,
projection, formats, tile indexing — where this is a *map engine*. VL.GIS computes and lets the
patch draw; VL.Mapsui hands over a map that draws itself.

## ⚠️ Status: a spike, not a release

Nothing is published. The package builds, installs into a local repository and draws an
OpenStreetMap map inside vvvv, and that is the whole of what has been shown.

| | |
|---|---|
| ✅ Verified | A map renders in vvvv 7.4, panning and zooming work through the navigation nodes |
| ✅ Verified | 63 tests covering node lifetime, navigation arithmetic, the disk cache and the pixel-space bridge. No test touches the network, checked by watching the test process's own connections |
| ⚠️ Thin | One tile source, no vector layers, no styling, no widgets |
| ❌ Missing | CI, a published package, custom tile sources |

**It cannot be loaded next to VL.GIS.** `Mapsui.Tiling` pins BruTile to 5.x and VL.GIS uses 6;
`BruTile.Attribution` changed layout between them, so mixing throws `TypeLoadException`. This
resolves itself when vvvv moves to SkiaSharp 3 and Mapsui 5 becomes usable, since 5.x uses
BruTile 6 and matches VL.GIS exactly.

## Not one map node

```
Mapsui.Layers    OpenStreetMap   Enabled, Cache To Disk, Cache Folder
                                                           -> a tile layer + Layers Built, Cache Status
Mapsui.Layers    CacheFolder     Folder                    -> where tiles go + Tiles, Size MB
Mapsui           Map             Layers, initial view      -> a map
Mapsui.Navigate  CenterOn  ZoomToLevel  Drag  ZoomAt  ZoomByWheel  ZoomIn  ZoomOut  Refresh
Mapsui           ViewportInfo  LayerInfo                    (readers)
Mapsui.Skia      ToSkiaLayer     Map                       -> a VL.Skia layer
```

A single all-in-one map node would have been less to wire, and it is deliberately not what this
is. **Nothing here decides for you what the mouse does.** Read it with VL.Skia's `MouseState`
and wire it to `Navigate`, or drive the map from an LFO, an OSC message, a keyboard or a
timeline instead. Composing that is the reason to reach for a patching environment, and an
earlier version of this package took the choice away by handling drag and wheel internally.

`help\VL.Mapsui\HowTo Show a map.vl` is the wired-up example. Beginners start from a help patch,
not from a fatter node.

## Manners

`Enabled` starts **off** on anything that fetches. Opening a document in vvvv runs it, so a map
that fetched on open would give whoever opened it no chance to decline.

Tiles that were drawn are cached under `%LOCALAPPDATA%\VL.Mapsui\tiles` — a session is a few
megabytes; delete the folder to reset. That is what
[OpenStreetMap's tile policy](https://operations.osmfoundation.org/policies/tiles/) asks for.
What it forbids is the opposite: fetching tiles nobody is looking at. Requests carry a
User-Agent naming this package.

`TileCache` is the node that decides where they go — beside your project so it travels with it,
onto a fast disk, or shared between patches — and it is the only thing that decides. Hand its
output to a layer's `Cache` pin, or leave that pin unconnected for the default above. It reads the
disk and never the network, so it also answers how much is cached with the layer switched off.

**Leave its `Folder` pin unconnected for the default; do not connect an empty Path IOBox.** There is
no such thing as an empty one: VL resolves an empty Path against the document and hands the node
your patch's own folder. That wrote 444 tiles into two repositories on 2026-08-14 while every pin
still read correctly. A folder that cannot be used is reported and the cache left off, rather than
quietly writing somewhere you did not ask for.

`Layers Built` is an output pin, and it should reach 1 and stay. A number that climbs frame after
frame means the layer is being rebuilt every frame and every rebuild starts a fresh round of tile
requests — which once exhausted a machine's ephemeral ports and took a home network down. Close
vvvv if you see it climb.

## Building

```powershell
dotnet test test\VL.Mapsui.Tests\VL.Mapsui.Tests.csproj   # 30 tests, ~1s, no network
.\build.ps1                                                # build + stage dist\
.\tools\Test-VLPackage.ps1                                 # static checks, no vvvv needed
.\pack.ps1                                                 # + a .nupkg in dist\feed

vvvv.exe "help\VL.Mapsui\HowTo Show a map.vl" --package-repositories dist
```

vvvv must be closed while building: a running one holds the staged assemblies open, and would
not pick up the change anyway.

## Reading

- [NOTES.md](NOTES.md) — what was measured, with dates
- [CLAUDE.md](CLAUDE.md) — the rules that matter in this repository
- [VL.GIS's docs](https://github.com/rednotfound/vvvv-gis/tree/main/docs) — `VL-RUNTIME.md` on
  how a VL node is evaluated and why that matters for anything holding a resource, and
  `VL-PACKAGING.md` on everything that silently breaks when packaging for vvvv

## Licence

MIT. Mapsui is MIT. OpenStreetMap data is © OpenStreetMap contributors, ODbL.
