# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## Project overview

**VL.Mapsui** wraps [Mapsui](https://mapsui.com) — a real map engine — as nodes for
[vvvv gamma](https://vvvv.org). It is the companion to `VL.GIS` (`D:\2026_Projects\vvvv-gis`),
which is a *toolbox* rather than an engine: VL.GIS computes and lets the patch draw, while this
package hands over a map that draws itself.

Separate repository on purpose. VL.GIS's rule is **one package per wrapped library**, and Mapsui
is its own library.

**Current state: a spike, and nothing more.** One question — does a Mapsui map reach the screen
inside vvvv — plus the scaffolding needed to answer it. **No nuspec, no build or pack scripts,
no CI, no tests, nothing published.** That is deliberate: VL.GIS shipped nine releases that
installed cleanly and contributed zero nodes because the packaging was built around something
never verified.

Measurements and their dates live in [NOTES.md](NOTES.md). Claims without a measurement behind
them do not belong there or here.

## The rules that matter most here

Both of the expensive mistakes in this repository were about **when a node runs**, not about
what it computes. Read [`../vvvv-gis/docs/VL-RUNTIME.md`](../vvvv-gis/docs/VL-RUNTIME.md) before
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
   the value, close. Leaks accumulate across sessions — this one grew over several.
4. **The `maps built` counter in the diagnostics overlay must reach 1 and stop.** If it climbs,
   close vvvv immediately. It is the cheapest possible smoke alarm and it exists because the
   first warning we got was a dead network.

## Packaging rules inherited from VL.GIS

These apply the moment a `.nuspec` appears here. All of them are silent when broken —
see [`../vvvv-gis/docs/VL-PACKAGING.md`](../vvvv-gis/docs/VL-PACKAGING.md).

- Every forwarded assembly needs `[assembly: ImportAsIs(Namespace = "VL")]`; without it nodes
  are invisible with no warning anywhere. Already in `src/VL.Mapsui/AssemblyInfo.cs`.
- A `.vl` is **UTF-8 with BOM**, and every `Id` is exactly 22 characters, first `[A-V]`, rest
  `[0-9A-Za-z]`, unique in the document. `tools\New-VLId.ps1` generates them.
- **Regenerate a `.vl`, never edit one in place.** `tools\Build-SpikePatch.ps1` emits the whole
  document and parses it before writing; patching in place once produced thirteen duplicated
  nodes and six colliding IDs elsewhere.
- `VL.Core` and `VL.Core.Skia` are referenced with `ExcludeAssets="runtime"` — they ship inside
  vvvv and our copies must never be distributed. **SkiaSharp is deliberately not pinned**:
  Mapsui.Rendering.Skia wants ≥ 2.88.9 and vvvv has 2.88.8, so pinning vvvv's version fails
  restore with NU1605. It works because the whole 2.88.x line carries assembly version
  `2.88.0.0`; the same is true of HarfBuzzSharp (`1.0.0.0` on both sides despite file versions
  7.3.0.1 vs 7.3.0.3). **Compare assembly versions, never file versions.**

## VL.GIS cannot be loaded at the same time

`Mapsui.Tiling` pins BruTile to `[5.0.6, 6.0.0)`; VL.GIS uses BruTile 6. `BruTile.Attribution`
changed layout between them, so mixing throws `TypeLoadException` at runtime. Accepted for now,
and it dissolves when vvvv reaches SkiaSharp 3 and Mapsui 5 becomes usable — 5.x uses BruTile 6
and matches VL.GIS exactly.

**The conflict is machine-wide.** `%LOCALAPPDATA%\vvvv\gamma\nugets\` is a flat folder with one
version of each library, shared by everything vvvv loads, and it wins over a copy sitting next to
our own assembly. Installing VL.GIS from nuget.org puts BruTile 6 there — and **uninstalling does
not remove it**, nor does reinstalling vvvv, since that folder lives in the user profile rather
than the install directory. Both BruTile packages currently sit in
`%LOCALAPPDATA%\vvvv\gamma\_nugets-backup-VL.GIS\`; if VL.GIS is ever installed from nuget.org
again they come back and this package breaks again.

Everyday VL.GIS work goes through its `start.ps1` with `--package-repositories dist`, which never
touches that folder.

## Repository layout

```
vl-mapsui/
├── NOTES.md                      # measurement log, with dates
├── src/VL.Mapsui/
│   ├── OpenStreetMapNode.cs      # [ProcessNode] - the map. Owns the Map's lifetime
│   ├── MapsuiLayer.cs            # VL.Skia.ILayer - draws it, and the diagnostics overlay
│   ├── PixelSpace.cs             # pixel/VL space bridge + a layer that reports its own inputs
│   └── MapNodes.cs               # scaffolding only
├── spike/Spike.vl                # GENERATED - never hand-edit
├── test/VL.Mapsui.Tests/         # 15 xunit tests, ~1s, no network, no vvvv
├── NuGet.config                  # sources pinned to nuget.org
└── tools/
    ├── Build-SpikePatch.ps1      # emits spike/Spike.vl whole
    └── New-VLId.ps1              # 22-char VL document IDs
```

## Tests

`dotnet test test\VL.Mapsui.Tests\VL.Mapsui.Tests.csproj` — 15 tests, about a second.

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

# Compile headlessly, then READ THE GENERATED C# before opening any window:
#   new OpenStreetMapNode() must appear in Create, never in Update
& "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvvc.exe" `
    (Resolve-Path .\spike\Spike.vl).Path --output-directory <abs-dir>

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
