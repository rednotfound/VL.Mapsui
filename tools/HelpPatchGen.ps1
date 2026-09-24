<#
.SYNOPSIS
    Library for GENERATING a new help patch (.vl) from a compact PowerShell description. Dot-source it.

.DESCRIPTION
    Carried from vl-nettopologysuite\tools\HelpPatchGen.ps1 on 2026-09-24, where fifteen patches were
    written with it. The XML shapes are copied from shipped patches, IDs are generated, pins are named
    by the caller and checked afterwards by reading the C# that tools\Compile-HelpPatches.ps1
    produces: a wrong pin name does not fail the compile, it makes the wired input read default(...)
    in the generated code.

    WHAT IS DIFFERENT FOR A MAP PATCH: every one ends in Map -> ToSkiaLayer -> Renderer, so MapWindow
    emits those three wired, and Save-Doc always declares VL.Skia (the Renderer) and VL.Mapsui 0.0.0.
    A patch that also takes NTS nodes (Read WKT, Feature) passes -Nts, which is allowed here because
    this package's own nuspec depends on VL.NetTopologySuite; any other VL.* package is refused by
    Test-VLPackage.ps1 and belongs in VL.Overworld.

    THE STYLE IS THE COMMUNITY'S, MEASURED - see docs\HELP-PATCH-STYLE.md. Heading (20pt, one line,
    an instruction or the topic), an optional short Intro (9pt, under 250 characters), the wired
    nodes, and Notes beside them (9pt, "< ..." pointing at the thing, under 150 characters). Not
    essays: the median community note is 34 characters long.

    ONCE A PATCH IS CHECKED IN, THE .vl IS THE SOURCE OF TRUTH, NOT THE SCRIPT THAT MADE IT. Layout
    fixes after a GUI check are made in the .vl (Bounds edits anchored on a match asserted to occur
    once), so a generating script is a one-shot scaffold and is deliberately not kept beside the patch.

    Sizing at 9pt: ~18 px per line, ~6.3 px per character. A Pad's Comment label renders to the RIGHT
    of the box at ~6.5 px per character; Test-VLPatch.ps1 does that arithmetic.

.EXAMPLE
    . .\tools\HelpPatchGen.ps1
    $d = New-Doc
    Set-HelpFlags $d -High 'Graticule' -Low 'Map','ToSkiaLayer'
    Heading $d 60 40 'Use Graticule!'
    Intro   $d 60 90 'A lat/lon grid. It needs no tiles, so this patch is offline.'
    $sp = Pad $d Float64 '60,170,70,15' '10' 'Degrees Spacing'
    $g  = Node $d 'Graticule' 'Mapsui.Layers' '60,210,110,19' -Kind ProcessAppFlag -In 'Degrees Spacing','Show Labels','Line Color','Line Width' -Out 'Result','Lines Built'
    Link $d $sp $g.DegreesSpacing
    $c  = Node $d 'Cons' 'Collections.Spread' '60,260,39,19' -Dependency VL.CoreLib.vl -Spread -In 'Input' -Out 'Result'
    Link $d $g.Result $c.Input
    $w  = MapWindow $d 60 310 -Layers $c.Result -Longitude 139.7 -Latitude 35.68 -Zoom 2
    Note $d 300 210 'Turn Degrees Spacing: 10 is the school atlas, 1 is a mesh. 0 picks one for the zoom.'
    Save-Doc $d '.\help\VL.Mapsui\HowTo Draw a graticule.vl'
    # then: add it to Help.xml, Test-VLPatch, pack + Compile-HelpPatches (read the C#), open it in vvvv.
#>
Set-StrictMode -Version Latest

$script:FirstChars = [char[]]'ABCDEFGHIJKLMNOPQRSTUV'
$script:RestChars  = [char[]]'0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
$script:Rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
function Get-RandomIndex([int]$upperBound) {
    $limit = [int]([Math]::Floor(256 / $upperBound)) * $upperBound
    $buf = [byte[]]::new(1)
    do { $script:Rng.GetBytes($buf) } while ($buf[0] -ge $limit)
    return $buf[0] % $upperBound
}
function New-Id {
    $sb = [System.Text.StringBuilder]::new(22)
    [void]$sb.Append($script:FirstChars[(Get-RandomIndex $script:FirstChars.Length)])
    for ($i = 1; $i -lt 22; $i++) { [void]$sb.Append($script:RestChars[(Get-RandomIndex $script:RestChars.Length)]) }
    $sb.ToString()
}
function Esc([string]$t) {
    $t = ($t -replace "`r`n", "`n").Trim("`n")
    $t = $t.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
    $t.Replace("`n", '&#xD;&#xA;')
}

function New-Doc {
    [pscustomobject]@{
        Elements = [System.Collections.Generic.List[object]]::new()   # strings, or Region objects
        Links    = [System.Collections.Generic.List[string]]::new()
        # bottom edge of the last Note per column (X), so a Note never lands on the one above it
        NoteBottom = @{}
        # node name -> 'High' | 'Low': the help flag every Node of that name in this document gets
        HelpFlags = @{}
    }
}

# HELP FLAGS ARE WHAT MAKES F1 WORK. Pressing F1 on a node opens the help patch in which that node
# carries a High flag (<p:HelpFocus ...>High</p:HelpFocus> right after </p:NodeReference>, which
# is what Ctrl+H writes in the editor); Low flags list the patch under the node's Node Info. 511 of
# the 689 help patches shipped with vvvv 7.4 carry flags. One High per node across the library.
function Set-HelpFlags($d, [string[]]$High = @(), [string[]]$Low = @()) {
    foreach ($n in $High) { $d.HelpFlags[$n] = 'High' }
    foreach ($n in $Low)  { $d.HelpFlags[$n] = 'Low' }
}

# An annotation box: stringtype Comment, no Comment attribute, so Test-VLPatch knows it is prose.
function Box($d, [string]$Bounds, [string]$Text, [int]$FontSize = 9) {
    $id = New-Id
    $d.Elements.Add(@(
        "          <Pad Id=`"$id`" Bounds=`"$Bounds`" ShowValueBox=`"true`" isIOBox=`"true`" Value=`"$(Esc $Text)`">",
        '            <p:TypeAnnotation>',
        '              <Choice Kind="TypeFlag" Name="String" />',
        '            </p:TypeAnnotation>',
        '            <p:ValueBoxSettings>',
        "              <p:fontsize p:Type=`"Int32`">$FontSize</p:fontsize>",
        '              <p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype>',
        '            </p:ValueBoxSettings>',
        '          </Pad>') -join "`r`n")
    [void]$id
}

# The one-line 20pt heading at the top: an instruction ("Use Buffer!") or the topic.
function Heading($d, [int]$X, [int]$Y, [string]$Text) {
    $w = [int](15 * $Text.Length + 30)
    Box $d "$X,$Y,$w,41" $Text 20
}

# Measured in the GUI (2026-09-24): 9pt wraps at ~7.3 px per character (320 px -> 44 characters), 18 px per line.
function Get-TextLines([string]$Text, [int]$Width) {
    $perLine = [Math]::Floor($Width / 7.3)
    $lines = 0
    foreach ($para in ($Text -split "`r?`n")) { $lines += [Math]::Max(1, [Math]::Ceiling($para.Length / $perLine)) }
    $lines
}

# One short 9pt paragraph under the heading. Width 480 -> ~65 characters per line.
function Intro($d, [int]$X, [int]$Y, [string]$Text, [int]$Width = 480) {
    if ($Text.Length -gt 260) { throw "Intro is $($Text.Length) characters - keep it under 260: $Text" }
    Box $d "$X,$Y,$Width,$([int](18 * (Get-TextLines $Text $Width) + 10))" $Text 9
}

# A 9pt note beside a node or IOBox, starting with "< ". Width 320 -> ~44 characters per line.
# If it would land on the previous Note in the same column it is pushed down below it.
function Note($d, [int]$X, [int]$Y, [string]$Text, [int]$Width = 320) {
    if ($Text -notmatch '^<') { $Text = '< ' + $Text }
    if ($Text.Length -gt 170) { throw "Note is $($Text.Length) characters - keep it under 170: $Text" }
    if ($d.NoteBottom.ContainsKey($X) -and $Y -lt $d.NoteBottom[$X] + 8) { $Y = $d.NoteBottom[$X] + 8 }
    $h = [int](18 * (Get-TextLines $Text $Width) + 8)
    $d.NoteBottom[$X] = $Y + $h
    Box $d "$X,$Y,$Width,$h" $Text 9
}

# A value IOBox: Float64 | Integer32 | String | Boolean (Primitive), or e.g. RGBA with
# -Category Color -Dependency CoreLibBasics.vl. Returns its Id, which is also its pin id.
function Pad($d, [string]$Type, [string]$Bounds, [string]$Value, [string]$Comment = '',
             [string]$Category = 'Primitive', [string]$Dependency = 'VL.CoreLib.vl') {
    $id = New-Id
    $d.Elements.Add(@(
        "          <Pad Id=`"$id`" Comment=`"$(Esc $Comment)`" Bounds=`"$Bounds`" ShowValueBox=`"true`" isIOBox=`"true`" Value=`"$(Esc $Value)`">",
        "            <p:TypeAnnotation LastCategoryFullName=`"$Category`" LastDependency=`"$Dependency`">",
        "              <Choice Kind=`"TypeFlag`" Name=`"$Type`" />",
        '            </p:TypeAnnotation>',
        '          </Pad>') -join "`r`n")
    $id
}

# A Boolean IOBox drawn as a button: -Mode Toggle (stays) or Bang (one frame). The shape every
# shipped Enabled switch uses: ImmutableTypeFlag plus a buttonmode setting. Returns its Id.
# (Not named Switch: that is a PowerShell keyword, and a function of that name is a parse error.)
function Button($d, [string]$Bounds, [string]$Value = 'False', [string]$Comment = '',
                [ValidateSet('Toggle', 'Bang')][string]$Mode = 'Toggle') {
    $id = New-Id
    $d.Elements.Add(@(
        "          <Pad Id=`"$id`" Comment=`"$(Esc $Comment)`" Bounds=`"$Bounds`" ShowValueBox=`"true`" isIOBox=`"true`" Value=`"$Value`">",
        '            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">',
        '              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />',
        '            </p:TypeAnnotation>',
        '            <p:ValueBoxSettings>',
        "              <p:buttonmode p:Assembly=`"VL.UI.Forms`" p:Type=`"VL.HDE.PatchEditor.Editors.ButtonModeEnum`">$Mode</p:buttonmode>",
        '            </p:ValueBoxSettings>',
        '          </Pad>') -join "`r`n")
    $id
}

# An output IOBox with no value and no type - VL types it from the link. Returns its Id.
function OutPad($d, [string]$Bounds, [string]$Comment = '') {
    $id = New-Id
    $d.Elements.Add("          <Pad Id=`"$id`" Comment=`"$(Esc $Comment)`" Bounds=`"$Bounds`" ShowValueBox=`"true`" isIOBox=`"true`" />")
    $id
}

# A node. -Kind OperationCallFlag (static method) or ProcessAppFlag (process node).
# -Spread adds the CategoryReference a Collections.Spread node carries; -RecordType 'Dictionary' the
# one a Collections.Dictionary node carries. Returns an object whose properties are the pin ids, named
# after the pins with spaces removed ('Candidate Count' -> CandidateCount).
# -Region places the node inside a ForEach region (see Region). -Defaults sets a pin's value
# without an IOBox, the way the editor does: @{ 'Closed' = 'False|Boolean'; 'Bounds' =
# '60,700,900,450|Rectangle|System.Drawing|System.Drawing.dll' } - value|type[|category|dependency].
# -CategoryRef adds a raw <CategoryReference .../> line (Vector (Join) needs Vector2Type).
function Node($d, [string]$Name, [string]$Category, [string]$Bounds,
              [string[]]$In = @(), [string[]]$Out = @(),
              [string]$Kind = 'OperationCallFlag', [string]$Dependency = 'VL.Mapsui.vl',
              [switch]$Spread, [string[]]$StateIn = @(), [string]$RecordType = '', [string[]]$StateOut = @(),
              [ValidateSet('', 'High', 'Low', 'None')][string]$HelpFocus = '',
              $Region = $null, [hashtable]$Defaults = @{}, [string]$CategoryRef = '') {
    $id = New-Id
    $pins = [ordered]@{}
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("          <Node Bounds=`"$Bounds`" Id=`"$id`">")
    $lines.Add("            <p:NodeReference LastCategoryFullName=`"$Category`" LastDependency=`"$Dependency`">")
    $lines.Add('              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />')
    $lines.Add("              <Choice Kind=`"$Kind`" Name=`"$Name`" />")
    if ($Spread) { $lines.Add('              <CategoryReference Kind="RecordType" Name="Spread" NeedsToBeDirectParent="true" />') }
    if ($RecordType) { $lines.Add("              <CategoryReference Kind=`"RecordType`" Name=`"$RecordType`" />") }
    if ($CategoryRef) { $lines.Add("              $CategoryRef") }
    foreach ($p in $Defaults.Keys) { $lines.Add("              <PinReference Kind=`"InputPin`" Name=`"$p`" />") }
    $lines.Add('            </p:NodeReference>')
    # -HelpFocus None: this instance carries no flag even though the document flags its name
    # (the second Contains in HowTo Test how geometries relate, wired the other way round).
    $flag = if ($HelpFocus -eq 'None') { '' } elseif ($HelpFocus) { $HelpFocus } elseif ($d.HelpFlags.ContainsKey($Name)) { $d.HelpFlags[$Name] } else { '' }
    if ($flag) { $lines.Add("            <p:HelpFocus p:Assembly=`"VL.Lang`" p:Type=`"VL.Model.HelpPriority`">$flag</p:HelpFocus>") }
    # $pid is PowerShell's read-only process id - hence $pinId.
    foreach ($p in $StateIn)  { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"StateInputPin`" />") }
    foreach ($p in $In) {
        $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId
        if ($Defaults.ContainsKey($p)) {
            $parts = $Defaults[$p] -split '\|'
            $cat = if ($parts.Count -gt 2) { $parts[2] } else { 'Primitive' }
            $dep = if ($parts.Count -gt 3) { $parts[3] } else { 'VL.CoreLib.vl' }
            $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"InputPin`" DefaultValue=`"$(Esc $parts[0])`">")
            $lines.Add("              <p:TypeAnnotation LastCategoryFullName=`"$cat`" LastDependency=`"$dep`">")
            $lines.Add("                <Choice Kind=`"TypeFlag`" Name=`"$($parts[1])`" />")
            $lines.Add('              </p:TypeAnnotation>')
            $lines.Add('            </Pin>')
        }
        else { $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"InputPin`" />") }
    }
    foreach ($p in $Out)      { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"OutputPin`" />") }
    foreach ($p in $StateOut) { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"StateOutputPin`" />") }
    $lines.Add('          </Node>')
    # not `$t = if (...) { list } else { list }`: an if-expression enumerates the List into a
    # fixed-size array, and Add then fails with "Collection was of a fixed size"
    if ($Region) { $Region.Elements.Add($lines -join "`r`n") } else { $d.Elements.Add($lines -join "`r`n") }
    [pscustomobject]$pins
}

# A ForEach region, copied from the shape shipped help uses: a Node carrying StatefulRegion +
# ApplicationStatefulRegion ForEach, an inner Patch (Create/Update/Dispose, ManuallySortedPins)
# holding the nodes placed with -Region, and a Top and a Bottom ControlPoint. The Top control point
# is the SPLICER: link the spread into .Top, and .Top into the first inner pin; link the last inner
# output into .Bottom and .Bottom onward. EVERY link lives in the outer patch, region or not - so
# Link works unchanged. A constant the whole loop needs is linked straight from outside to the inner
# pin, with no control point. Nest by passing -Region to Region itself. Bounds are canvas
# coordinates, inner nodes use absolute canvas coordinates inside them.
function Region($d, [string]$Bounds, [string]$TopAt, [string]$BottomAt, $Region = $null) {
    $r = [pscustomobject]@{
        Id = New-Id; Top = New-Id; Bottom = New-Id; Bounds = $Bounds; TopAt = $TopAt; BottomAt = $BottomAt
        Elements = [System.Collections.Generic.List[object]]::new()
    }
    if ($Region) { $Region.Elements.Add($r) } else { $d.Elements.Add($r) }
    $r
}

function Render-Element($e) {
    if ($e -is [string]) { return $e }
    $inner = @($e.Elements | ForEach-Object { Render-Element $_ }) -join "`r`n"
    @(
        "          <Node Bounds=`"$($e.Bounds)`" Id=`"$($e.Id)`">",
        '            <p:NodeReference LastCategoryFullName="Primitive" LastDependency="CoreLibBasics.vl">',
        '              <Choice Kind="StatefulRegion" Name="Region (Stateful)" Fixed="true" />',
        '              <Choice Kind="ApplicationStatefulRegion" Name="ForEach" />',
        '              <CategoryReference Kind="Category" Name="Primitive" />',
        '            </p:NodeReference>',
        "            <Pin Id=`"$(New-Id)`" Name=`"Break`" Kind=`"OutputPin`" />",
        "            <Patch Id=`"$(New-Id)`" ManuallySortedPins=`"true`">",
        "              <Patch Id=`"$(New-Id)`" Name=`"Create`" ManuallySortedPins=`"true`" />",
        "              <Patch Id=`"$(New-Id)`" Name=`"Update`" ManuallySortedPins=`"true`" />",
        "              <Patch Id=`"$(New-Id)`" Name=`"Dispose`" ManuallySortedPins=`"true`" />",
        $inner,
        '            </Patch>',
        "            <ControlPoint Id=`"$($e.Top)`" Bounds=`"$($e.TopAt)`" Alignment=`"Top`" />",
        "            <ControlPoint Id=`"$($e.Bottom)`" Bounds=`"$($e.BottomAt)`" Alignment=`"Bottom`" />",
        '          </Node>'
    ) -join "`r`n"
}

function Link($d, [string]$From, [string]$To) {
    if (-not $From -or -not $To) { throw "Link needs two pin ids" }
    $d.Links.Add("        <Link Id=`"$(New-Id)`" Ids=`"$From,$To`" />")
}

# The three nodes every map patch ends in, wired: Map (Layers in, Initial Center/Zoom as pin
# defaults) -> ToSkiaLayer -> Renderer (900x560, bound to the document, so closing the patch closes
# the window). Returns the Map node's pins plus .ToSkiaLayer and .Renderer, so a patch can wire a
# Navigate node onto .Result or feed the Renderer a Group instead. Laid out down one column from
# X,Y in 60 px steps, the shape all the shipped patches here share.
function MapWindow($d, [int]$X, [int]$Y, [string]$Layers,
                   [double]$Longitude = 139.7, [double]$Latitude = 35.68, [int]$Zoom = 12,
                   [switch]$Diagnostics,
                   # -Detached leaves Map.Result -> ToSkiaLayer.Map unwired, for a patch that puts
                   # Navigate nodes between them; -Gap is the vertical room it gets (Map at Y,
                   # ToSkiaLayer at Y+Gap, Renderer 60 below that).
                   [switch]$Detached, [int]$Gap = 60,
                   # -WithConsole adds the mouse idiom vvvv's own help uses (Explanation Mouse and
                   # Keyboard: "the Mouse node needs to be connected to the Renderer it interacts
                   # with"): a Console whose Output is grouped with the map layer and whose Mouse
                   # pin feeds a MouseState in the patch. Returned as .Console and .Group.
                   [switch]$WithConsole) {
    $map = Node $d 'Map' 'Mapsui' "$X,$Y,110,19" -Kind ProcessAppFlag `
        -In 'Layers','Initial Center Longitude','Initial Center Latitude','Initial Zoom Level' -Out 'Result' `
        -Defaults @{ 'Initial Center Longitude' = "$Longitude|Float64"; 'Initial Center Latitude' = "$Latitude|Float64"; 'Initial Zoom Level' = "$Zoom|Integer32" }
    if ($Layers) { Link $d $Layers $map.Layers }
    $skiaDefaults = @{}
    if ($Diagnostics) { $skiaDefaults['Diagnostics'] = 'True|Boolean' }
    $skia = Node $d 'ToSkiaLayer' 'Mapsui.Skia' "$X,$($Y + $Gap),130,19" -Kind ProcessAppFlag -In 'Map','Diagnostics' -Out 'Result' -Defaults $skiaDefaults
    if (-not $Detached) { Link $d $map.Result $skia.Map }
    $rendererY = $Y + $Gap + 60
    $console = $null; $group = $null
    if ($WithConsole) {
        $console = Node $d 'Console' 'Graphics.Skia' "$($X + 250),$($Y + $Gap),70,19" -Kind ProcessAppFlag -Dependency 'VL.Skia.vl' `
            -Out 'Output','Mouse','Keyboard','Notifications'
        $group = Node $d 'Group' 'Graphics.Skia' "$X,$($Y + $Gap + 50),60,19" -Kind ProcessAppFlag -Dependency 'VL.Skia.vl' `
            -In 'Input','Input 2' -Out 'Output' -CategoryRef '<CategoryReference Kind="Category" Name="Skia" NeedsToBeDirectParent="true" />'
        Link $d $skia.Result $group.Input
        Link $d $console.Output $group.Input2
        $rendererY = $Y + $Gap + 100
    }
    $renderer = Node $d 'Renderer' 'Graphics.Skia' "$X,$rendererY,185,19" -Kind ProcessAppFlag -Dependency 'VL.Skia.vl' `
        -In 'Bounds','Bound to Document','Input' `
        -Defaults @{ 'Bounds' = '926, 114, 900, 560|Rectangle|System.Drawing|System.Drawing.dll'; 'Bound to Document' = 'True|Boolean' }
    if ($WithConsole) { Link $d $group.Output $renderer.Input } else { Link $d $skia.Result $renderer.Input }
    $map | Add-Member -NotePropertyName ToSkiaLayer -NotePropertyValue $skia -PassThru |
           Add-Member -NotePropertyName Renderer -NotePropertyValue $renderer -PassThru |
           Add-Member -NotePropertyName Console -NotePropertyValue $console -PassThru |
           Add-Member -NotePropertyName Group -NotePropertyValue $group -PassThru
}

# The mouse read off the map window: MouseState fed from MapWindow -WithConsole's Console, and
# its Position split into the X and Y every Navigate/Pick node takes. Returns an object with
# .X, .Y, .LeftPressed, .WheelState pin ids. Laid out down one column from X,Y.
function MouseXY($d, [int]$X, [int]$Y, $Window) {
    if (-not $Window.Console) { throw 'MouseXY needs a MapWindow made with -WithConsole' }
    $state = Node $d 'MouseState' 'IO.Mouse' "$X,$Y,80,19" -Kind ProcessAppFlag -Dependency 'CoreLibBasics.vl' `
        -In 'Mouse Device' -Out 'Position','Left Pressed','Wheel State'
    Link $d $Window.Console.Mouse $state.MouseDevice
    $split = Node $d 'Vector (Split)' '2D.Vector2' "$X,$($Y + 50),46,19" -Dependency 'VL.CoreLib.vl' `
        -StateIn 'Input' -Out 'X','Y' -CategoryRef '<CategoryReference Kind="Vector2Type" Name="Vector2" NeedsToBeDirectParent="true" />'
    Link $d $state.Position $split.Input
    [pscustomobject]@{ X = $split.X; Y = $split.Y; LeftPressed = $state.LeftPressed; WheelState = $state.WheelState }
}

# Every map patch declares VL.Skia (it ships inside vvvv; the Renderer needs it) and VL.Mapsui at
# the 0.0.0 sentinel Test-VLPackage.ps1 insists on. -Nts adds VL.NetTopologySuite, also 0.0.0, which
# is allowed because this package's nuspec depends on it. -Dependencies is for anything else that
# ships inside vvvv - never a package this one does not declare.
function Save-Doc($d, [string]$Path, [string[]]$Dependencies = @(), [switch]$Nts) {
    $docId = New-Id; $coreDep = New-Id; $patch = New-Id; $canvas = New-Id; $app = New-Id
    $appPatch = New-Id; $group = New-Id; $create = New-Id; $update = New-Id; $procDef = New-Id
    $frag1 = New-Id; $frag2 = New-Id; $ntsDep = New-Id
    $extraDeps = @(@('VL.Skia') + $Dependencies | Select-Object -Unique | ForEach-Object { "  <NugetDependency Id=`"$(New-Id)`" Location=`"$_`" Version=`"2025.7.4`" />" })
    if ($Nts) { $extraDeps += "  <NugetDependency Id=`"$(New-Id)`" Location=`"VL.NetTopologySuite`" Version=`"0.0.0`" />" }
    $rendered = @($d.Elements | ForEach-Object { Render-Element $_ })
    $xml = @(
        '<?xml version="1.0" encoding="utf-8"?>',
        "<Document xmlns:p=`"property`" xmlns:r=`"reflection`" Id=`"$docId`" LanguageVersion=`"2025.7.4`" Version=`"0.128`">",
        "  <NugetDependency Id=`"$coreDep`" Location=`"VL.CoreLib`" Version=`"2025.7.4`" />") + $extraDeps + @(
        "  <Patch Id=`"$patch`">",
        "    <Canvas Id=`"$canvas`" DefaultCategory=`"Main`" BordersChecked=`"false`" CanvasType=`"FullCategory`" />",
        '    <!--',
        '',
        '    ************************ Application ************************',
        '',
        '-->',
        "    <Node Name=`"Application`" Bounds=`"100,100`" Id=`"$app`">",
        '      <p:NodeReference>',
        '        <Choice Kind="ContainerDefinition" Name="Process" />',
        '        <CategoryReference Kind="Category" Name="Primitive" />',
        '      </p:NodeReference>',
        "      <Patch Id=`"$appPatch`">",
        "        <Canvas Id=`"$group`" CanvasType=`"Group`">"
    ) + $rendered + @(
        '        </Canvas>',
        "        <Patch Id=`"$create`" Name=`"Create`" />",
        "        <Patch Id=`"$update`" Name=`"Update`" />",
        "        <ProcessDefinition Id=`"$procDef`">",
        "          <Fragment Id=`"$frag1`" Patch=`"$create`" Enabled=`"true`" />",
        "          <Fragment Id=`"$frag2`" Patch=`"$update`" Enabled=`"true`" />",
        '        </ProcessDefinition>'
    ) + @($d.Links) + @(
        '      </Patch>',
        '    </Node>',
        '  </Patch>',
        "  <NugetDependency Id=`"$ntsDep`" Location=`"VL.Mapsui`" Version=`"0.0.0`" />",
        '</Document>'
    )
    $text = (($xml -join "`r`n") -replace "(?<!`r)`n", "`r`n") + "`r`n"
    [IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding $true))
    "wrote $Path ($($d.Elements.Count) elements, $($d.Links.Count) links)"
}
