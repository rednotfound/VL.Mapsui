# The rules, carried over

Everything below was learned in `vvvv-gis` (VL.GIS) and applies here unchanged. It is written out
rather than linked because **a rule transfers; a note about another repository's file does not** —
that lesson was itself learned by carrying the packaging rules across and leaving the runtime ones
behind, where they promptly cost a home network.

Each rule is followed by what it cost or the measurement behind it. The long forensics stay where
they were written and are linked at the end; nothing here depends on that repository being present.

---

## Packaging — break these and the package fails *silently*

Nine releases shipped, installed, and contributed **zero nodes**, with no error anywhere.

1. **A `.vl` is generated or edited, never regenerated carelessly.** Every `Id` is exactly 22
   characters, first `[A-V]`, rest `[0-9A-Za-z]`, unique in the document. `tools\New-VLId.ps1`
   makes them. To add a dependency, append one line with a fresh ID — existing IDs are identities
   that must stay stable across releases.
   **Every id comes from the generator; not one is ever typed by hand.** Typing one that happens to
   match the format has now happened three times, and each time it passed every check that exists:
   the format is right, so nothing goes red. Generate more than you need — they are free — and if
   you run out mid-edit, generate again rather than inventing the last one.
2. **A `.vl` is UTF-8 *with* BOM.** Without it vvvv will not load the document. Any tool that
   rewrites one must use `New-Object System.Text.UTF8Encoding($true)`.
3. **Every forwarded assembly needs `[assembly: ImportAsIs(Namespace = "VL")]`.** Without it the
   package loads, compiles, packs and exports with zero warnings — and its methods are demoted to
   raw .NET reflection nodes that the NodeBrowser hides. Indistinguishable from the package not
   loading at all.
4. **A shipped `.vl` must never contain `<ProjectDependency>`.** It forces the package and
   everything downstream to stay editable.
5. **Never repack a version number that already exists locally.** NuGet treats a version as
   immutable and serves a stale copy from `~\.nuget\packages\<id>\<version>` forever. `pack.ps1`
   evicts that directory; keep it that way.
6. **nuget.org is not a test environment.** The loop is local: `dist\` as a package repository,
   `dist\feed` as a feed. A published version can never be replaced.
7. **An upstream library must be a *package in a package repository*, not just a
   `<NugetDependency>` line.** Declaring it is necessary and not sufficient. Without the package
   present VL cannot resolve its types, so a node whose signature mentions a foreign type is built
   with no working pins and every link to it is dropped — `vvvvc` exits 0 and nothing is red.
   `build.ps1` installs them into `deps\`, kept apart from `dist\` because pointing
   `--package-repositories` at the document being compiled fails with *"Entry point for document
   X.vl not found"*.

**Installing without pruning is the same bug one level up.** `deps\` is handed to vvvv, so a
package nobody declares any more is still offered to everything that loads. `build.ps1` prunes by
reachability, reading each manifest out of the `.nupkg` — there are no `.nuspec` files on disk, and
the first version of that prune treated an unreadable manifest as a leaf and deleted the whole
transitive closure.

---

## Runtime — break these and something *outside* the package fails

8. **A `public static` method is evaluated on every frame.** Sixty times a second, from the moment
   the document is opened — opening a `.vl` *is* running it. Anything that acquires a connection,
   file handle, GPU resource, cache, thread or subscription must be a `[ProcessNode]` class,
   built once and rebuilt only when an input actually changes. Written as a static method, a map
   node opened **17,000 TCP connections in 13 minutes**, exhausted the machine's 16,384 ephemeral
   ports and took down a home network.
9. **Never block on a task inside a node.** vvvv's runtime thread owns a `SynchronizationContext`,
   so `.Result` / `.Wait()` deadlocks it — the window closes without the process exiting. Return
   `IObservable`, or wrap in `Task.Run`. Testing sync-over-async in a host without a context
   (PowerShell) proves nothing about a host that has one.
10. **A node pointing at free public infrastructure is off by default.** Zero requests on open, a
    disk cache, a User-Agent naming the package, and an on-screen count of what has been built.
    OpenStreetMap's tile policy forbids bulk downloading, and whoever opens a patch has not agreed
    to anything yet.
11. **Never leave vvvv running unattended, and never start it in the background.** Launch, read
    the value, close. Leaks accumulate across sessions.

---

## Node design — what earns a node

Measured across the 45 packs shipped with vvvv 7.4 and 17 community packages.

- **Three questions, in order.** Can a patch reach the same result by wiring three existing nodes
  (then it is a help patch, not a node)? Does it hold a resource (then it is a `[ProcessNode]`)?
  Is the thing it decides something the user cares about (if not, put it behind a pin)?
- **Three inputs is the target, four wants a reason, five means two decisions are wearing one
  node.** Of VL.CoreLib's 901 static nodes, 493 take one input and **94% take three or fewer**;
  every node above five is a machine-generated arity family, so **not one designed node exceeds
  five**.
- **Bundle a choice the user does not want to make; never a concept they have to understand
  anyway.** Heron covers the whole GIS domain in 37 components and reads seven file formats in one
  of them. What must never be bundled, each already paid for here: what the mouse means, where the
  view looks, where files go, which renderer draws it.
- **Help is the teaching surface, not a fatter node.** VL.Skia ships 4 C# static nodes and **98
  help patches**; VL.Stride ships none and 125. In libraries people learn from, help runs 16–24%
  of node count. Five prefixes, not interchangeable: `Explanation` (one per library, the front
  door — 57 in vvvv's own packs), `HowTo`, `Reference`, `Example`, `Tutorial`. `Help.xml` orders
  them and carries search tags.
- **Naming, from the Gray Book:** process nodes are nouns, operation nodes prefer verbs; never
  start a name with `As..`, use `To..`/`From..`; a container datatype gets a **`Split`**/`Join`
  pair (`Split` appears 194 times in shipped help patches — it is what a user types); `Create` is
  for complex types, not property bags; avoid excessive subcategories.
- **A path pin is `VL.Lib.IO.Path`, never `string`** — its IOBox opens a directory chooser on
  SHIFT+rightclick. And **a machine-dependent default cannot be a pin's initial value**: a C#
  default must be a compile-time constant (`CS1736`). Expose it through a node, the way
  `SystemFolder [IO]` does. Beware that a Path IOBox stores a *relative* path whenever it can and
  hides that from you, so a node that writes files must refuse a non-rooted path rather than guess.
- **An empty Path IOBox is not empty, so "empty means the default" cannot be a Path pin's rule.**
  `Value=""` in a `.vl` means *the path relative to this document*, and the empty relative path is
  the document's own folder; `vvvvc` compiles it to
  `CompilationHelper.Deserialize<Path>("", false, <documentId>, …)` and VL hands the node that
  folder, absolute. Only an **unconnected** pin produces the `null` that can mean "the default".
  Cost: 444 tiles written next to two repositories with every guard reporting success — the path
  was rooted, the folder existed, the status pin named it honestly, and nothing was wrong except
  the assumption. So: never wire an empty Path IOBox in a shipped patch, and never document a pin
  as "leave it empty" — that sentence is what causes it.
- **An option that can be off needs a value for "off", not `null`.** An unconnected pin, a switched
  off producer and a producer that failed all arrive as `null`, and a consumer cannot tell "nobody
  said anything, use the default" from "somebody said no". Give the type an `IsOn` and carry the
  reason. Same ambiguity as the empty IOBox, one level up, and it cost a silent fallback to the
  default in the very commit that fixed the first one.
- **Tags are not a C# attribute.** `VL.Core.Import` has no `TagsAttribute`; `Tags=` and `Summary=`
  live on a node definition in a `.vl`, including on a `ForwardDefinition` — vvvv's own
  `VL.Skia.vl` does this for `Console`. Across every shipped `.vl`, **251 multi-term tags use
  commas and none use spaces**, which contradicts the guidelines; follow the shipped code.
- **Fluent operations** (return type equals the first parameter type) get an output pin named
  `Output`; everything else gets `Result`. `vvvvc` rejects the wrong one, which is how this was
  established rather than guessed.
- **Before editing a `.vl` by hand, read [`VL-PATCH-XML.md`](VL-PATCH-XML.md).** It is the verified
  serialization of every construct used here — records, fragments, pads, regions, interfaces — with
  the failure each rule prevents, and the validation gate that catches the mechanical ones. Almost
  nothing you can get wrong in a `.vl` produces an XML error.
- **Read a patched record through `IVLObject`, never through `System.Reflection`.** A record has
  **two runtime shapes**: exported by `vvvvc` it has real `public string Name;` fields, but **inside
  the vvvv editor its values live in `__State` and it exposes no CLR members for them at all** — a
  `Landmark` with `Name`, `Type` and `Geometry` reported `__State:Object, Context:NodeContext,
  Identity:UInt32, __Program__:VLObjectProgram`. Reflection therefore works in every place you can
  test and fails in the only place that ships. Use `IVLObject.Type.Properties`, and
  `IVLPropertyInfo.Type.ClrType` when you need the declared type; key attributes by `OriginalName`
  (`Name` is `[Obsolete]`, and VL's own message says to prefer it because it can contain spaces).
  All three shipped libraries that consume user records do exactly this. Cost here: a patch that
  drew nothing while every static check passed, including **reading the generated C#** — which
  showed the exported shape. `NOTES.md`, 2026-08-15.
- **A test double that is easier than the real thing tests the wrong thing.** The double for the
  above had real public fields, so it passed while the patch was broken. It now hides its values in
  a private field and exposes only `__State`, mirroring the editor — and the negative test (remove
  the `IVLObject` branch) turns it red, which the old one could not.
- **"Runs once" is `ParticipatingElements` on the Create fragment**, a comma-separated list of
  **Node or Link Ids**; everything unnamed runs in Update. The idiom is to name the *link* that
  writes a value into a `Pad`, and the producing subgraph is pulled in with it. A `Pad` with a
  `SlotId` is the storage that carries the value from Create into Update — several `Pad` glyphs
  sharing one `<Slot>` are one variable. Measured across the shipped packs: 2423 process definitions
  have an empty Create (everything every frame), 288 use `ParticipatingElements`. Never put
  `isIOBox="true"` and `SlotId` on the same `Pad`; that combination appears zero times in 22959
  shipped pads.

---

## Working style

- **Change one variable at a time.** Nine failed releases came from editing the `.vl`, the csproj
  and the nuspec in one round with no idea which mattered.
- **Before believing a green result, name the mechanism by which it could have gone red.** A
  console program rendering a PNG proves nothing about vvvv: it draws onto a bare `SKCanvas` and
  bypasses the coordinate space, which is the only part that plausibly breaks.
- **A check that has never gone red is not a check.** The `.gitignore` rule added to stop stray
  tiles did not work on the first attempt — a pattern containing a slash is anchored to the
  repository root — and only planting a file and watching git ignore it caught that.
- **Test the composition, not the piece.** Ten green tests on `SymbolStyle` — four of them counting
  actual pixels — coexisted with two hundred invisible markers, because every one of them put a
  single style on a layer while the patch wires `SymbolStyle → LabelStyle → FeatureLayer`. The bug
  was in the pair. **Whatever shape the help patch builds is the shape at least one test must
  build.**
- **Assert the invariant, not the number.** Nobody would have written "the marker must be 952
  pixels", and such a test would break on the next SkiaSharp. What caught the bug is a sentence
  anyone would agree with before seeing the code: *a label may only add ink*. Monotonicity,
  ordering, "this is bigger than that" — these survive a version bump and still fail on a real
  fault.
- **A double that cannot exercise the failure is not a test of it.** The first composition test
  passed against the broken code: its feature had no attributes, so the label had nothing to write,
  so nothing was painted over the marker. The same mistake as the record double whose values sat in
  real public fields — twice now, a stand-in too simple to reach the bug.
- **A pin that does nothing is worse than no pin.** `SymbolStyle.UnitType` selects pixels or world
  units and Mapsui's Skia renderer never reads it — measured, four renders, all 1156 pixels. The pin
  was written, measured and removed. Shipping it would have been a promise the patch cannot keep,
  failing silently, which is the failure mode this whole document exists for. **Before wrapping a
  property, render it.** `LabelStyle.CollisionDetection` was the second: 763 pixels with it on, 763
  with it off. A property that exists is not a property that works, and this library has two.
- **A metadata scan is a hint, never a verdict.** Searching `Mapsui.Rendering.Skia.dll` for
  `UnitType` and finding zero occurrences was good corroboration — but `LabelColumn` scores zero
  there too and works perfectly, because the read happens one assembly along. A zero means *go and
  measure*, and the measurement is the answer.
- **A mechanism the community proposed and did not adopt is data, not a gap.** A numeric z-index
  for Skia layers was written up as `GroupOrder`, posted for feedback, and drew one Like and no
  replies before the thread auto-closed; a second thread asking for the same thing got "would love
  to see that too!". Twice wished for, never shipped, and no library in the ecosystem sorts layers
  by a number — they all use pin order or spread order. Building it here would make this package
  the odd one out for a problem `OrderBy` already solves visibly.
- **Before adding a node, check whether an existing one already plays that part.** A Mapsui `Group`
  was nearly built for stacking layers; `Cons` already was it, and `Map` was already the compositor.
  The way to see it was to draw the two chains side by side rather than to reason about the words.
- **Look for the mechanism before inventing one.** `SymbolStyle` cannot draw a polygon, so a `Style`
  pin was added to stack a `VectorStyle` under it. Mapsui already had `ThemeStyle` — style chosen
  per feature — which is the same shape as OpenLayers' style function, Leaflet's `pointToLayer` and
  Mapbox GL's layer types. Half an hour of searching would have replaced a day of building the
  wrong thing. **When a problem is one every library in the field has, the field has an answer, and
  so probably does the library you are wrapping.**
- **A fix that adds an artifact is not a fix.** Stacking rescued the polygons and put a second
  concentric circle on every point, and made `Scale` below 1 mean nothing. It passed its tests: the
  polygon drew. Nobody had asked "and what does this do to the things that already worked" — which
  is the question a fix owes, every time.
- **Derive it instead of asking for it, when the node is already holding the answer.** `LabelStyle`
  needed the label to clear the marker, which looks like an offset pin and is not: the node already
  receives the `SymbolStyle` on its `Style` input, so it can read the scale and work the clearance
  out. No pin, no arithmetic for the patch author, and the correct result by default. **Check what
  a node can already see before adding an input for it.**
- **Compile every help patch, not the one you edited.** Deleting a node that another patch still
  used produced `Not found: ToAttributes` in a file nobody had touched, and compiling only the
  changed patch would have shipped it. `tools\Compile-HelpPatches.ps1` with no `-Patch` filter takes
  a few minutes and is the only thing that sees a cross-patch break.
- **When a check finds one instance, run it over everything before believing that was the only one.**
  The "empty IOBox connected to nothing" check was written for one leftover pad and immediately
  found a second — a `Cache Status` readout in another patch that had never been wired at all, and
  had been shipping blank.
- **Run the negative test *after* the fix, not only before it.** A test that goes red on the broken
  code can still be measuring the wrong thing once the code is fixed: the flicker fix kept the layer
  alive, which made the counter the test asserted on stay at 1 whether or not the fix was present.
  Reverting the fix left all 115 green. The rule is not "make it fail once" but **"make it fail
  against the finished code"**.
- **Validate before committing, in a separate step.** A validator run in the same command block as
  the commit reported its failure after the push had already happened.
- **Trace the whole chain, not one hop.** Surveying shipped patches for what `Wheel State`
  connects to found `FrameDifference`, which answered "it accumulates" — and stopping there left
  the magnitude to a guess. One link further sat `FrameDifference → Sign`.
- **Before writing a node of an unfamiliar kind, read a shipped package that ships that kind.**
  `VL.ImGui.ToSkiaLayer` supplied the pixel-space approach here; `VL.IO.Redis` is the precedent
  for a `[ProcessNode]` owning a connection.
- **Copy node XML verbatim from a shipped patch** rather than composing it. And an XML comment
  cannot contain `--`; that has cost three rounds.

---

## The long version

Full forensics, kept where they were written:

- [`../../vvvv-gis/docs/VL-PACKAGING.md`](../../vvvv-gis/docs/VL-PACKAGING.md) — how a node comes
  to exist, and every way that fails quietly
- [`../../vvvv-gis/docs/VL-RUNTIME.md`](../../vvvv-gis/docs/VL-RUNTIME.md) — what happens once it
  runs; both incidents dissected
- [`../../vvvv-gis/docs/NODE-DESIGN.md`](../../vvvv-gis/docs/NODE-DESIGN.md) — the survey behind
  the node-design section above
- [`../../vvvv-gis/docs/DESIGN.md`](../../vvvv-gis/docs/DESIGN.md) — why VL.GIS is shaped the way
  it is, and the division of labour between the two packages

Those links assume both repositories sit side by side under `D:\2026_Projects\`. If they do not,
this file is still complete on its own.
