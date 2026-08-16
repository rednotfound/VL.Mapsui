# Hand-authoring a `.vl`

What the shipped code actually does, verified against the files on disk rather than recalled.
Read this before editing a `.vl` by hand; read [`RULES.md`](RULES.md) before adding a node.

**The reason this document exists is the failure mode.** A `.vl` is a graph held together by
22-character strings. Almost nothing you can get wrong produces an XML error: the document parses,
vvvv loads it, and the patch is quietly not what you meant. Two of them do not even fail at compile
time — a link can point at the wrong pin of the right type, and a value can be read at the wrong
*rate*. So every claim below carries the failure it prevents, and the ones that can be checked
mechanically are checked by `tools\Test-VLPackage.ps1` on every help patch.

Counts like "2,920 shipped regions" come from scanning
`C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\packs\` and
`%LOCALAPPDATA%\vvvv\gamma\nugets\` — 1,558 `.vl` files.

---

## The gate

Never commit a hand-edited `.vl` without all of these. Each corresponds to a defect that shipped
here at least once.

| check | what it caught |
|---|---|
| UTF-8 **with BOM** | vvvv will not load the document at all without it |
| every `Id` is 22 chars, first `[A-V]`, rest `[0-9A-Za-z]` | hand-typed ids passed "looks right" three times |
| ids unique within the document | copy-paste duplicates |
| every `Link@Ids` endpoint resolves | wires to deleted pins |
| every `Pad@SlotId` resolves | a pad glyph belonging to no slot |
| every `Fragment@Patch` resolves | an operation registered against a missing patch |
| every `Patch@ParticipatingElements` resolves | **deleting nodes made a Create seed link dangle, the sweep removed it, and Create named nothing** |
| XML parses | the cheap one, and not sufficient |
| `vvvvc` compiles it | pin names, node names, type mismatches |
| **read the generated C#** | an unresolved type is dropped in silence… but see *Two runtime shapes* below, where this check lied |
| a GUI round | the rate at which things run, and anything visual |

**Anchor every insertion on a match that occurs exactly once and fail loudly otherwise.** A
`.Replace()` that silently matches twice is how a patch gets two of something.

**A cleanup pass needs the same validation as an edit pass.** Deleting elements creates dangling
references of its own.

### Write multi-step edits into a script FILE, never an inline command block

On 2026-08-16 a here-string terminator was followed by an argument on the same line:

```powershell
$t = Swap $t $anchor @'
   …replacement XML…
'@ 'what this step does'          # <-- the terminator is not alone on its line
```

PowerShell did not error. It swallowed **the rest of the script as here-string content** and wrote
it into the `.vl`, so the patch ended up containing lines of PowerShell source. Then:

- the **XML still parsed** — it landed inside element content;
- **`vvvvc` compiled it**, exit 0, no warning;
- only `tools\Test-VLPackage.ps1` objected, and only because the swallowed text happened to repeat
  an id: *"uses Id UX2Q4zuDSJ3FicIQjybU4g 3 times"*.

Two rules from it. **Put the edit in a `.ps1` under the scratchpad and run the file** — a real file
gets parsed as a whole before anything executes, so this failure becomes a syntax error instead of
a corrupted patch. And **assign every here-string to a variable first**, so the terminator is
always alone on its line:

```powershell
$replacement = @'
   …replacement XML…
'@
$t = Swap $t $anchor $replacement 'what this step does'
```

The corrupted file was deleted and rebuilt from the donor rather than repaired: it was twenty
minutes old, and "excise the damage" needs to know the extent of the damage.

---

## Skeleton

```xml
<?xml version="1.0" encoding="utf-8"?>
<Document xmlns:p="property" xmlns:r="reflection" Id="…" LanguageVersion="2025.7.4" Version="0.128">
  <NugetDependency Id="…" Location="VL.CoreLib" Version="2025.7.4" />
  <NugetDependency Id="…" Location="VL.Mapsui" Version="0.0.0" />
  <Patch Id="…">
    <Canvas Id="…" DefaultCategory="Main" CanvasType="FullCategory" />
    <Node Name="Application" Bounds="100,100" Id="…">
      <p:NodeReference>
        <Choice Kind="ContainerDefinition" Name="Process" />
        <FullNameCategoryReference ID="Primitive" />
      </p:NodeReference>
      <Patch Id="…">
        <Canvas Id="…" CanvasType="Group">   <!-- ONE canvas; every node lives here -->
          …
        </Canvas>
        <Patch Id="…" Name="Create" />       <!-- fragments: pins live here, not nodes -->
        <Patch Id="…" Name="Update" />
        <ProcessDefinition Id="…">
          <Fragment Id="…" Patch="…Create…" Enabled="true" />
          <Fragment Id="…" Patch="…Update…" Enabled="true" />
        </ProcessDefinition>
        <Slot Id="…" Name="…" />             <!-- named storage -->
        <Link Id="…" Ids="source,sink" />    <!-- ALL links, at this level -->
      </Patch>
    </Node>
  </Patch>
</Document>
```

Document order of `Canvas` / `Patch Name=…` / `ProcessDefinition` / `Slot` / `Link` inside the
owning `<Patch>` is **free** — shipped files use at least four orderings.

**A document dependency is not an installation.** `<NugetDependency>` only tells VL how to resolve;
vvvv shows a missing one in red and offers to install on right-click. A patch that uses NTS nodes
needs its own `<NugetDependency Location="NetTopologySuite">`, separate from VL.Mapsui's.

Local packages are pinned to `0.0.0`; vvvv rewrites them on save, so run
`tools\Normalize-HelpPatches.ps1` after any GUI session. It also switches `Enabled` back off.

---

## Links

```xml
<Link Id="…" Ids="sourcePinId,sinkPinId" />
<Link Id="…" Ids="src,waypointControlPoint,sink" />   <!-- Ids is a path; waypoints allowed -->
<Link Id="…" Ids="…,…" IsHidden="true" />             <!-- fragment pin ↔ canvas -->
<Link Id="…" Ids="topCP,bottomCP" IsFeedback="true" /><!-- accumulator pairing -->
```

Source first, sink second. **Every link lives in the enclosing patch's list** — including links
whose endpoints are inside a region.

⚠️ **A link can be wrong without being dangling.** Rewiring a patch left an old
`Cons.Result → FeatureLayer.Features` link in place; both endpoints still existed, so no
dangling-reference check could see it, and the layer had two sources of different types. `vvvvc`
caught it: *"Landmark is no Feature"*.

---

## Pads: three forms, never mixed

Across 22,959 shipped pads: 16,288 `isIOBox` only, 5,752 `SlotId` only, 919 bare — and **zero**
carrying both.

```xml
<!-- (a) bare: an anonymous slot. Two-number Bounds. Type inferred from the incoming link. -->
<Pad Id="…" Bounds="487,320" />

<!-- (b) SlotId: named storage. Several glyphs sharing one Slot ARE one variable. -->
<Pad Id="…" SlotId="…" Bounds="414,313" />
<Pad Id="…" SlotId="…" Bounds="414,416" />
<Slot Id="…" Name="Position">
  <p:TypeAnnotation p:Type="TypeReference"><Choice Kind="TypeFlag" Name="Vector2" /></p:TypeAnnotation>
</Slot>

<!-- (c) IOBox: a visible value box. FOUR-number Bounds, plus Value and a TypeAnnotation. -->
<Pad Id="…" Comment="Width" Bounds="536,223,35,28" ShowValueBox="true" isIOBox="true" Value="0.02">
  <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
    <Choice Kind="TypeFlag" Name="Float32" />
  </p:TypeAnnotation>
</Pad>
```

A pad is **the only thing that survives from one operation call to the next**. The Gray Book puts it
as: *"modified Records always need to be written back into a Pad, for their changes to survive to
the next frame."*

⚠️ **An empty Path IOBox is not empty.** `Value=""` on a `VL.Lib.IO.Path` means the path *relative to
the document* — vvvv hands the node the patch's own folder, absolute. Cost: 444 tiles written next
to two repositories while every guard reported success. Only an **unconnected** pin gives `null`.

⚠️ **A pin with no pad connected is unreachable**, and `vvvvc` compiles it without complaint as a
literal. A compile proving a pin exists proves nothing about whether anyone can set it.

---

## Calling a node

```xml
<Node Bounds="79,340,150,19" Id="…">
  <p:NodeReference LastCategoryFullName="Mapsui.Layers" LastDependency="VL.Mapsui.vl">
    <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
    <Choice Kind="ProcessAppFlag" Name="OpenStreetMap" />   <!-- a [ProcessNode] -->
  </p:NodeReference>
  <Pin Id="…" Name="Enabled" Kind="InputPin" />
  <Pin Id="…" Name="Result" Kind="OutputPin" />
</Node>
```

- `ProcessAppFlag` = a `[ProcessNode]` class; `OperationCallFlag` = a static method.
- A call on a *type* adds `<CategoryReference Kind="RecordType" Name="Dictionary" NeedsToBeDirectParent="true" />`.
- **Pin names come from the C# parameter names**, camelCase split at capitals. An acronym cannot
  survive that: `urlTemplate` becomes `Url Template`, so the pin needs `[Pin(Name = "URL Template")]`.
- **`Result` vs `Output`**: a fluent static operation (return type equals the first parameter type)
  gets `Output`; everything else, including a `[ProcessNode]`'s return, gets **`Result`**.
- Declare all pins you use; unconnected ones may be omitted.

Both naming rules were established by `vvvvc` rejecting the wrong one, not by reading anything.

---

## A record definition

`Create` and `Split` are **real patched operations with wired bodies, not auto-generated** — checked
across 203 shipped records with slots, and zero have slots without links. Per property:
1 `<Slot>`, 1 `<Pad SlotId>`, 2 `<ControlPoint>`, 4 `<Link>`, 2 `<Pin>` — roughly `9N+6` elements
for N properties.

```xml
<Node Name="Landmark" Bounds="1340,430" Id="…">
  <p:NodeReference LastCategoryFullName="Primitive" LastDependency="Builtin">
    <Choice Kind="RecordDefinition" Name="Record" />        <!-- ClassDefinition for a mutable one -->
  </p:NodeReference>
  <Patch Id="…">
    <Canvas Id="…" CanvasType="Group">
      <Pad Id="…" SlotId="…Name…" Bounds="949,738" />
      <ControlPoint Id="…" Bounds="949,708" />              <!-- write side -->
      <ControlPoint Id="…" Bounds="949,768" />              <!-- read side -->
    </Canvas>
    <Patch Id="…C…" Name="Create"><Pin Id="…" Name="Name" Kind="InputPin" /></Patch>
    <ProcessDefinition Id="…" IsHidden="true">
      <Fragment Id="…" Patch="…C…" Enabled="true" />
      <Fragment Id="…" Patch="…S…" />
    </ProcessDefinition>
    <Slot Id="…Name…" Name="Name">
      <p:TypeAnnotation p:Type="TypeReference"><Choice Kind="TypeFlag" Name="String" /></p:TypeAnnotation>
    </Slot>
    <Patch Id="…S…" Name="Split"><Pin Id="…" Name="Name" Kind="OutputPin" /></Patch>
    <Link Id="…" Ids="createPin,writeCP"  IsHidden="true" />
    <Link Id="…" Ids="writeCP,pad" />
    <Link Id="…" Ids="pad,readCP" />
    <Link Id="…" Ids="readCP,splitPin"    IsHidden="true" />
  </Patch>
</Node>
```

Copy `MyDataRecord` from `VL.ExtendedTutorials.1.1.1\help\HowTo Exporting Data to a Spreadsheet.vl`
(lines 285–353) and edit names, types and ids. **Copying a proven shape beats composing one.**

The call site is an ordinary node with `<CategoryReference Kind="RecordType" Name="Landmark" />` and
`<Choice Kind="OperationCallFlag" Name="Create" />`; its output pin is `Kind="StateOutputPin"`.

### Two runtime shapes — the one that cost the most

A record compiles **differently in the editor than when exported**:

| | vvvv editor | `vvvvc` export |
|---|---|---|
| public CLR members | `__State:Object, Context:NodeContext, Identity:UInt32, __Program__:VLObjectProgram` | `public string Name; public Geometry Geometry; …` |
| how to read a property | **only** `IVLObject.Type.Properties` | CLR reflection also works |

So a C# node that reflects over a record works in every test and fails in the only place that ships
— and **reading the generated C#, this repository's most trusted check, showed the exported shape
and said all was well**. Read a patched value through `IVLObject`, key attributes by
`IVLPropertyInfo.OriginalName` (`Name` is `[Obsolete]`; VL's own message says to prefer `OriginalName`
because it can contain spaces).

A test double must hide its fields the way the editor does, or it tests the wrong thing.

### The yellow slot

`This field could mutate at runtime.` is severity **Warning**, emitted by
`PatchedSlotSymbol::CollectMessages` when a Record's slot holds a type VL classifies mutable — which
is every imported .NET `class` that is not a C# `record`, not a `DynamicEnum`, and whose name does
not literally start with `Immutable`. `ViolatesImmutability` is referenced from that one diagnostics
pass and nowhere else in `VL.Lang.dll`: **no codegen path consumes it.** Shipped records do this
constantly (`VL.Skia`'s `SkiaPaint.Shader : SKShader`).

The one real hazard is serialization; the fix shipped code uses is `NonSerializedAttribute` on the
slot:

```xml
<Slot Id="…" Name="Geometry">
  <p:Attributes><AttributeData>
    <p:TypeReference LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
      <Choice Kind="TypeFlag" Name="NonSerializedAttribute" />
    </p:TypeReference>
  </AttributeData></p:Attributes>
  <p:TypeAnnotation p:Type="TypeReference"><Choice Kind="TypeFlag" Name="Geometry" /></p:TypeAnnotation>
</Slot>
```

The consequence to actually respect: VL records have **no `Equals` override**, so they compare by
reference and two records made from one share the same field instance.

---

## What runs once: `ParticipatingElements`

```xml
<Patch Id="…" Name="Create" ParticipatingElements="<linkId>" />
<Patch Id="…" Name="Update" />
```

A comma-separated list of **Node or Link ids**; **everything not named runs in Update**. There is no
per-operation canvas and no attribute on the node.

The idiom is to name the **link that writes a value into a Pad** — the producing subgraph comes
along by dependency closure. 849 shipped seeds are link→Pad, 326 are nodes. Entries may only
reference direct children of the same `<Patch>`; never a Pad id, never something inside a region.

Shipped ratio: **2,423** process definitions have an empty Create (everything every frame), **288**
use `ParticipatingElements`. An empty Create is the norm, not a mistake — but it means nothing can
run once.

`Cache` is the other tool and answers a different question: Create runs once *ever*, a `Cache`
region re-runs when its inputs change. VL.Skia's help states the preference for derived values:
*"Usually it's better to put all the path construction nodes into a Cache region, so that the path
will be rebuilt only on changes."*

---

## Regions — `ForEach`, `Cache`, `If`, `Repeat`

Verified across 2,920 shipped `ForEach` regions.

```xml
<Node Bounds="x,y,w,h" Id="…">                  <!-- regions carry FOUR-value Bounds -->
  <p:NodeReference LastCategoryFullName="Primitive" LastDependency="Builtin">
    <Choice Kind="StatefulRegion" Name="Region (Stateful)" Fixed="true" />
    <Choice Kind="ApplicationStatefulRegion" Name="ForEach" />
  </p:NodeReference>
  <Pin Id="…" Name="Break" Kind="OutputPin" />  <!-- exactly one, in 2920 of 2920 -->
  <ControlPoint Id="…" Bounds="x,y" Alignment="Top" />     <!-- one per spliced input spread -->
  <ControlPoint Id="…" Bounds="x,y" Alignment="Bottom" />  <!-- one per collected output spread -->
  <Patch Id="…" ManuallySortedPins="true">
    <Patch Id="…" Name="Create"  ManuallySortedPins="true" />
    <Patch Id="…" Name="Update"  ManuallySortedPins="true" />
    <Patch Id="…" Name="Dispose" ManuallySortedPins="true" />
    …inner Nodes only…
  </Patch>
</Node>
```

- **No `<Canvas>` and no `<Link>` inside a region.** Inner nodes are direct children of its `<Patch>`.
- **A control point id is one graph node**: the outer link points *at* it, the inner links come
  *from* it. There is no "item" element.
- **No attribute marks a splice.** It is purely topological — `VL.Lang`'s `IsSplicer` never reaches
  the file.
- **A value that must not be spliced crosses directly**, outer pin → inner pin, no control point.
  1,844 shipped links do this. There are **zero** direct inner→outer links: getting a value out must
  go through a Bottom control point.
- A Bottom control point paired to a Top one by `IsFeedback="true"` becomes a **fold**; unpaired it
  collects a `Spread<T>`.
- `Index`, `Keep`, `Break` are optional `<Pin>`s of the region's inner **Update fragment**, not of
  the region node.
- Other regions are the same shell with a different `Name`: `Cache` (fragments `Create`/`Then`, no
  `ProcessDefinition`), `If`, `Repeat` (has an `Iteration Count` pin), `Using`, `ForEach (Max)`.

Copy from `VL.Stride\help\Misc\Example World Cities.vl` (region `CJC9JEn2v8CPwY46s8RHrJ`) — spread
in, a **record built per item**, `Spread<Record>` out.

---

## Interfaces

A patched **record or class** implements one through a `<p:Interfaces>` block, sibling of
`<p:NodeReference>`:

```xml
<p:Interfaces>
  <TypeReference LastCategoryFullName="IO.OSC.Modules" LastDependency="OSCModule.vl">
    <Choice Kind="InterfaceTypeFlag" Name="IOSCConfiguration" />
  </TypeReference>
</p:Interfaces>
```

`InterfaceTypeFlag` / `MutableInterfaceType` / `ImmutableInterfaceType` are serialization variants,
**not constraints** — the same `IWidget` appears both ways on records in one file. `Spread<IFoo>`
pins are ordinary. C#-defined interfaces work too (25 shipped records implement `VL.UI.IWidget`).

⚠️ **A C# interface's property member is NOT satisfied by a Slot of the same name.** The implementer
must patch an explicit getter operation — `<Patch Name="Enabled"><Pin Name="Enabled"
Kind="OutputPin"/></Patch>` — so an interface with six properties costs the user six operations. A
`.vl`-side interface declaring a **`Split`** instead (the `ITooltip` pattern in VL.HDE) is satisfied
by a record's auto-`Split` for free, but cannot be a C# pin type.

---

## Object identity is VL's only change signal

Read out of the IL: `CacheManager.InputsChanged` compares with `ValueTuple.Equals` →
`EqualityComparer<T>.Default`, and `Spread<T>` is a **class with no `Equals` override** — so that is
`ReferenceEquals`. `Changed` and `ChannelFlange` reduce to the same thing. There is no version,
revision, dirty flag or invalidate on any VL data type.

The contract this implies: **immutable values from producers that are cached, so they hand out the
same instance.** A node that mints a new object every frame breaks it — which is what a `public
static` VL node does by definition, and why `FeatureLayerNode` has to compare features by value.

Measured, 1,000 features of 500 vertices: the same spread every frame costs **0.013 ms/frame**
downstream; rebuilding them every frame costs **52.9**, of which 43 is just constructing them. Four
thousand times, decided by nothing but whether the patch builds its data once.

---

## What is not established

- The exact closure algorithm around `ParticipatingElements` seeds — inferred from both observed
  directions, documented nowhere.
- Whether "unclaimed ⇒ Update" is a rule or a consequence. It holds in all 2,423 empty-Create
  patches; no file states it.
- The marker that makes an operation **adaptive** (VL's type-class mechanism). Every package-level
  declaration uses the `(Adaptive)` name suffix and it is stripped from the emitted interface name,
  but CoreLib's 75 math adaptives do not use it. No documentation exists anywhere in the install.
- Whether an "Immutable Forward" of a type already imported via a nuget compiles, or trips
  `"has already been forwarded by"`.
