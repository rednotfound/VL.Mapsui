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

### Still open

- nuget.org has nothing newer: Mapsui tops out at 4.1.9 and 5.1.0
- **Unverified: does a map actually render inside vvvv?** That is what `spike\Spike.vl` is for.
  A console program rendering a PNG does *not* count — that draws onto a bare `SKCanvas` and
  bypasses VL's coordinate space, which is the only part that can plausibly break. VL.GIS made
  exactly that mistake and lost a day to it.
- `VL.Skia.Sizing` documents one unit as **100 actual pixels**, which contradicts what VL.GIS's
  notes currently claim about `DIPTopLeft`. The spike patch reads `ClientBounds` so this can be
  settled rather than argued.
