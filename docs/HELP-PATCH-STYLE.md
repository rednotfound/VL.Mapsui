# Help patch style — measured from the VL community, not invented here

**Read this before writing or rewriting a help patch.** The first fifteen patches in this repository
(2026-09-24) were written as essays: a 900-pixel box of prose at the top opening with "One idea:",
then two or three more paragraphs of 300–600 characters each. That is not how the community writes
help, and the user noticed before the author did. Everything below was measured on 2026-09-24 against
the packs shipped with vvvv gamma 7.4 and the community packs in the local NuGet cache; the numbers are
reproducible with the PowerShell in the appendix.

## What was measured

| corpus | files | notes per patch | note length p50 / p90 (chars) | notes starting with `<` | body font |
|---|---|---|---|---|---|
| VL.CoreLib + VL.Skia + VL.Stride (vvvv 7.4) | 317 HowTo | 3 | 34 / 149 | 303 of 1859 | 9pt (71 % of all boxes) |
| VL.Fuse 1.0.0-alpha04 | 124 | 7.8 | 25 / 193 | few | 9pt, 13pt |
| VL.Elementa 5.0.12 | 79 | 7.8 | 50 / 184 | few | 9pt |
| VL.PolyTools 1.4.0 | 75 | 5.6 | 76 / 293 | 167 of 407 | 9pt |
| VL.OpenCV 2.1.0 | 50 | 9.1 | 38 / 146 | few | 9pt, 13pt |
| VL.ImGui 2023.5.0 | 82 | 3 | 25 / 138 | 24 of 171 | 9pt, headings 14/18pt |
| VL.ExtendedTutorials 1.1.1 | 35 (31 `Explanation`) | 21 | 81 / 325 | 186 of 739 | 9pt |

Across the 1480 annotation boxes in the three core packs: median box **210 × 32 px**, median text
**34 characters**, and only **21 boxes exceed 400 characters**. Our first drafts had fifteen boxes over
400 characters in fifteen patches.

## The shape of a HowTo

1. **A heading box, top-left, one line, 14–20pt.** 235 of 389 annotated core HowTos have one, 219 of
   those at the very top. It is either an instruction — `Use a MonoFlop!`, `Use a Split (Count) node!`,
   `Use Trim PathEffect` — or the topic in two words — `Clipping`, `Vignetting`, `Texture overlay`.
   It is *not* a sentence about ideas. The filename already says what the patch is about.
2. **Optionally one short paragraph under it, 9pt, two to four lines** (100–250 characters): what
   this example shows, in the plainest words. `This example shows how to clip layers (however
   complex they are) by a rectangle.`
3. **The nodes, wired and computing.** Values visible in IOBoxes with short `Comment` labels of one
   or two words: `Period`, `Start`, `Stop`, `Retriggerable`.
4. **Notes beside the nodes, 9pt, 20–150 characters, many starting with `<`** pointing at the thing
   they describe: `< Bang to set the MonoFlop's output to 1 for the given period of time.`,
   `< Disconnect the PathEffect to see the full path.` Imperative where possible: *try*, *connect*,
   *disconnect*, *change*. A note explains one pin, one behaviour, one thing to try — never the
   philosophy of the library.
5. **Numbered steps when order matters**: `1.` `2.` `3.` as 20pt boxes with a 9pt note beside each
   (VL.Stride `HowTo Work with Children`).
6. **Warnings are short and loud**: `THE DISTANCE IS IN THE GEOMETRY'S OWN UNITS` is fine as one
   line; a paragraph about the poles is not.

Layout: heading around (30–140, 76–142); everything else below it; notes to the *right* of the node
or IOBox they belong to, so the eye reads node-then-note. Canvas width rarely exceeds 1000 px.

## The shape of an Explanation Overview

VL.IO.Redis: the package's nodes placed on the canvas, unwired, each with one `< sentence` beside it —
six boxes for six nodes. VL.Audio: one intro paragraph, then category labels with the nodes under each.
Either way the front door **shows the nodes**; it does not describe the package in four essays.

## Help flags — what makes F1 work

Pressing **F1 on a selected node opens the help patch in which that node carries a High help flag**,
and marks the node there with a bubble ("Click this bubble for Node Info"). Nothing else links a node
to a patch: not the filename, not the node merely being used in the patch. Low flags list the patch
under the node's Node Info instead. In the editor the flag is set with **Ctrl+H** on the node (once:
High, twice: Low, three times: cleared). In the `.vl` it is one element right after the node's
`</p:NodeReference>`:

```xml
<p:HelpFocus p:Assembly="VL.Lang" p:Type="VL.Model.HelpPriority">High</p:HelpFocus>
```

Measured on 2026-09-24: **511 of the 689 help patches shipped with vvvv 7.4 carry flags** — 1059
High, 77 Low; Explanation patches carry them too (125 High). Our first fifteen carried none, which
is why F1 found nothing. Now every one of the 39 nodes has exactly one High flag (a HowTo where one
is dedicated to it, the Explanation otherwise), `tools\Test-VLPatch.ps1` audits that, and
`tools\HelpPatchGen.ps1` writes them from one `Set-HelpFlags` line per patch. Verified end to end:
F1 on a Buffer node in a scratch document opened `HowTo Buffer a geometry` with the bubble on Buffer.

Two more things F1 needs: the patches must be **inside the package's `help\` folder as vvvv sees the
package** — `dist\VL.Mapsui\help\` when launched through `tools\Open-HelpPatch.ps1`, so `build.ps1`
must have been run after the patch was written — and vvvv indexes them at start.

Sources: [Providing Help](https://thegraybook.vvvv.org/reference/extending/providing-help.html),
[Finding Help](https://thegraybook.vvvv.org/reference/hde/findinghelp.html).

## How the community organizes a help folder — measured 2026-09-24

Surveyed over the 45 packs shipped with vvvv 7.4 and every `vl.*` in the local NuGet cache (60
folders with a `help\`), for the question the note-style table above does not answer: *how is a
library's help arranged, and what does a library about a thing that draws itself do?*

**Prefix mix.** The Gray Book's own definitions
([Providing Help](https://thegraybook.vvvv.org/reference/extending/providing-help.html)):
`Explanation` — "typically a single patch per library giving an overview of the whole set of
nodes"; `HowTo` — "individual patches demonstrating how to achieve specific things"; `Reference` —
"a patch covering the functionality of one specific node"; `Example` — "more broadly showing a
usecase of a library, not necessarily explaining too much"; `Tutorial` — "most often a link to a
video". In practice:

| pack | Explanation | HowTo | Reference | Example | organizing idea |
|---|---|---|---|---|---|
| VL.CoreLib | 2 | 184 | 5 | 7 | one HowTo per node, topics = categories |
| VL.Skia | 19 | 48 | 0 | 29 | 14 concept Explanations (*Paint*, *Transformations*, *Normalized and pixel space*, *Mouse and Keyboard*) in an `Overview` topic; HowTos under `Topics\<Subtopic>` |
| VL.Stride | 19 | 71 | 14 | 21 | topics = the domain (Models, Materials, Lights, Cameras, Input, Rendering); `Reference What is Roughness` is a concept, not a node |
| VL.ImGui | 7 | 78 | 2 | 3 | `HowTo <WidgetName>` — the node name *is* the title; `Styling` as its own topic |
| VL.Audio | 2 | 5 | 16 | 3 | Reference-first: one patch per node, topics = node categories |
| VL.IO.Redis | 1 | 3 | 2 | 0 | the smallest complete pack: front door, one Reference per stateful node, HowTos for the two workflows |
| VL.Elementa | 3 | 63 | 8 | 3 | `Reference … Overview` patches show a whole family on one canvas |
| VL.OpenCV | 3 | 41 | 2 | 4 | `Tutorial` used for multi-patch procedures (calibrate a camera) |

Explanations are not only front doors: VL.Skia uses one per **concept** a beginner has to hold
(*Fill and Stroke*, *Combining different spaces*), and 31 of VL.ExtendedTutorials' 35 are
Explanations. A `Reference` is used two ways — one node with every pin exercised (VL.Audio,
VL.IO.Redis `Reference RedisClient`) or a family overview (VL.Elementa, VL.Fuse). `Example`s are
big (median 25 nodes in VL.Skia, up to 273) and unannotated on purpose.

**Help.xml.** Two levels: `Topic` and `Subtopic`, plus `UriItem` for a link out (VL.ImGui's first
entry is the upstream library's GitHub; VL.Stride links a Gray Book page). Files may sit in up to
two levels of subfolders mirroring the topics, but the flat folder with `Help.xml` doing the
ordering is equally common and is what both sibling repositories use. Topic names are **domain
words** (`Cameras`, `Widgets`, `Styling`, `Paths`), never prefix names — no pack has a topic
called "HowTo" except this repository's own first draft.

**The host is always in the patch, and it is not counted as boilerplate.** 46 of VL.Skia's 48
HowTos contain a `Renderer`; all 78 of VL.ImGui's contain a `Renderer` and an `ImGui` region;
VL.Stride's contain a `SceneWindow` + `RootScene`. Median node count stays at 8–10 anyway, because
**the host is two or three nodes and nothing else comes along**: no mouse (6 of 48 Skia HowTos, 0
of 78 ImGui, 23 of 184 CoreLib), no second topic. VL.Skia's most-used nodes across its HowTos are
`Renderer` 46, `Group` 20, `Stroke` 17, `Fill` 16 — the host, the collector and the two paints.
The equivalent here is `Map` → `ToSkiaLayer` → `Renderer`, three nodes, which
`HelpPatchGen.ps1`'s `MapWindow` emits as a unit.

**Interaction is a topic, and vvvv teaches it with a node.** VL.Skia: *Explanation Mouse and
Keyboard* ("The Mouse and Keyboard nodes need to be connected to the Renderer they want to
interact with") and *Explanation Camera* — where the whole patch is `Renderer`, `Camera`,
`AxisAndGrid` and one note: *Panning: rightdrag / Zooming: mousewheel / Reset: press and hold R*.
VL.Stride: *Explanation Overview Camera Handling* with `OrbitCamera`, whose pins are all
`Initial …` values and whose note is *Orbit: left drag, Track: middle drag, Dolly: right drag,
Zoom: Ctrl + wheel*. **Neither camera has a mouse pin: the node reads the host's input itself.**
That is the opposite of what `docs/RULES.md` concluded for this package ("what the mouse means must
never be bundled"), and the survey does not settle it — it records that vvvv's own answer for
*driving a view* is a node with defaults, and that the wiring form lives one patch over as an
Explanation. The tension is written down in `docs/HELP-PATCH-PLAN.md` as something the rewrite of
`HowTo Drive the map with the mouse` must measure (how many nodes the wiring takes) before the
package decides.

**Subpatches inside a HowTo are rare** (VL.Skia 6 of 48, VL.ImGui 4 of 78, VL.Stride 17 of 71,
VL.CoreLib 12 of 184) and used for a helper the topic is not about, not to hide the host.

**Help flags** are the norm in vvvv's own packs (VL.Skia 40 of 48 HowTos, VL.ImGui 71 of 78,
VL.CoreLib 147 of 184) and absent from every community pack surveyed except the two siblings —
which is where community F1 silently fails.

## Sizing (measured, so it need not be re-derived)

| | 9pt body | 20pt heading |
|---|---|---|
| line height | ~18 px | ~41 px box for one line |
| glyph width | ~6.3 px → about `width / 6.3` characters per line | — |
| typical box | 210–350 wide, 20–90 tall | 170–370 wide, 30–41 tall |

A Pad's `Comment` label renders to the right of the box at ~6.5 px per character;
`tools\Test-VLPatch.ps1` counts it in the overlap arithmetic.

## What this repository does with it

- Every HowTo: heading (instruction or topic) → optional one-paragraph intro → wired nodes → `<` notes
  beside nodes, 9pt, under 150 characters each, at most one longer warning line.
- No "One idea:" openers, no 900-pixel boxes, no paragraphs about design decisions — those live in
  `docs/ARCHITECTURE.md` and `docs/MAPSUI-SURFACE.md`, which a help patch may name in one line if
  it must.
- The host is `Map` → `ToSkiaLayer` → `Renderer` and nothing else rides along: no mouse chain
  unless the mouse is the topic, no tile layer unless tiles are the topic (a `Graticule` gives an
  offline map its bearings). See D1 and D2 in `docs/HELP-PATCH-PLAN.md`.
- Topics in `Help.xml` are domain words, one node group per HowTo, one `High` flag per node.
- The Explanation shows the nodes by category with one line each.
- `tools\HelpPatchGen.ps1` emits 9pt by default and takes `-FontSize 20` for the heading.

## Appendix — how to re-measure

```powershell
# every annotation box (stringtype Comment) in a folder of .vl files: size, font, length, first chars
Get-ChildItem <folder> -Recurse -Filter *.vl | ForEach-Object {
  $raw = [IO.File]::ReadAllText($_.FullName)
  [regex]::Matches($raw, '<Pad [^>]*Bounds="(\d+),(\d+),(\d+),(\d+)"[^>]*Value="([^"]*)"[^>]*>(?<b>(?:(?!</Pad>).)*)</Pad>', 'Singleline') |
    Where-Object { $_.Groups['b'].Value -match 'StringType">Comment<' } |
    ForEach-Object {
      $fs = 9; if ($_.Groups['b'].Value -match 'fontsize p:Type="Int32">(\d+)<') { $fs = [int]$Matches[1] }
      [pscustomobject]@{ File = $_.Groups[0].Value.Length; W = [int]$_.Groups[3].Value; H = [int]$_.Groups[4].Value; Font = $fs
                         Chars = ([System.Net.WebUtility]::HtmlDecode($_.Groups[5].Value)).Length }
    }
}
```

Shipped packs: `C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\packs\*\help`. Community packs:
`%USERPROFILE%\.nuget\packages\vl.*\<version>\help`.
