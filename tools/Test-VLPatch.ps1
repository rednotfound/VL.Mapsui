<#
.SYNOPSIS
    Validates .vl patch documents structurally, without needing vvvv.

.DESCRIPTION
    Test-VLPackage.ps1 checks the package: that the entry point exists, forwards an assembly with
    [ImportAsIs], and declares its nugets. It does not look inside a help patch. This does.

    Every check here corresponds to a failure that is SILENT in vvvv - the patch opens, nothing is
    red, and something simply does not work:

      BOM              a .vl without a UTF-8 BOM is not loaded at all
      ID format        22 chars, first [A-V] - vvvv's deserializer rejects anything else quietly
      ID uniqueness    duplicates make one node shadow another
      link endpoints   a Link naming an ID that is not a Pin or Pad is dropped without warning
      dangling IOBox   a Pad connected to nothing is decoration the user cannot use - EXCEPT an
                       annotation box, which carries its own Value and no Comment. That is the
                       shipped convention for explanatory text and is allowed on purpose
      empty Path IOBox Value="" on a Path means the DOCUMENT'S OWN FOLDER, not "empty" - this is
                       the one that wrote 444 files next to two repositories with every guard
                       reporting success

    It cannot tell you whether a node resolves: that depends on what the assembly actually exports,
    and only the GUI proves a node appears under the expected category. A PASS here means the
    document is well formed, not that it works.

.EXAMPLE
    .\tools\Test-VLPatch.ps1
    .\tools\Test-VLPatch.ps1 -Path 'help\VL.Mapsui\HowTo Show a map.vl'
#>
param(
    [string]$Path,
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# [array] on the assignment, not just @() inside the branches: PowerShell unwraps a
# single-element array back to a scalar on its way out of an `if` expression, and under
# Set-StrictMode the next line then fails with "The property 'Count' cannot be found".
# Caught by running this with -Path on one file, which is the case @() alone does not cover.
[array]$targets = if ($Path) {
    Get-Item (Join-Path $RepoRoot $Path) -ErrorAction Stop
} else {
    Get-ChildItem $RepoRoot -Filter '*.vl' -File -Recurse |
        Where-Object { $_.FullName -notmatch '\\dist\\' } | Sort-Object FullName
}
if ($targets.Count -eq 0) { throw "No .vl documents found under $RepoRoot" }

$totalProblems = 0

foreach ($file in $targets) {
    $rel = $file.FullName.Replace("$RepoRoot\", '')
    Write-Host "`nvalidating $rel" -ForegroundColor Cyan

    $problems = [System.Collections.Generic.List[string]]::new()
    $raw   = Get-Content $file.FullName -Raw
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)

    if ($bytes.Length -lt 3 -or -not (($bytes[0] -eq 0xEF) -and ($bytes[1] -eq 0xBB) -and ($bytes[2] -eq 0xBF))) {
        $problems.Add('missing UTF-8 BOM - vvvv will not load this document')
    }
    # PowerShell's cast error embeds the ENTIRE document before saying what is wrong, so the one
    # useful sentence - "An XML comment cannot contain '--' ... Line 18, position 31" - arrives
    # after three thousand characters of the file you already have. Keep the tail.
    try { $null = [xml]$raw }
    catch {
        $message = $_.Exception.Message -replace '(?s)^.*?Error:\s*', ''
        $problems.Add("XML does not parse: " + ($message -replace '\s+', ' ').Trim())
    }

    # (?<![A-Za-z]) so that SlotId="…" is not counted as an Id: a Pad inside a Record definition
    # references its Slot that way, and the bare pattern reported the four slots of HowTo Draw many
    # features as duplicate IDs when they were one definition and one reference each. Latent in the
    # sibling's copy too, which has no records.
    $allIds = @(([regex]'(?<![A-Za-z])Id="([^"]+)"').Matches($raw) | ForEach-Object { $_.Groups[1].Value })
    $illegal = @($allIds | Where-Object { $_ -notmatch '^[A-V][0-9A-Za-z]{21}$' })
    if ($illegal.Count) { $problems.Add("illegal IDs: $($illegal -join ', ')") }
    $dupes = @($allIds | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($dupes.Count) { $problems.Add("duplicate IDs: $($dupes -join ', ')") }

    # A LINK'S Ids IS A PATH: source, any number of waypoint ControlPoints, sink. A link given a bend
    # in the GUI is written Ids="src,waypoint,sink" (134 shipped help files do it; docs\VL-PATCH-XML.md
    # says so). Every check below used to read exactly two ids, so the user bending the Console link in
    # HowTo Draw many features (2026-09-25) produced a false "endpoint is neither a Pin, a Pad nor a
    # ControlPoint" - "waypoint,sink" read as one id. Parse once; Source is the first id, Sink the last.
    $links = @([regex]::Matches($raw, '<Link Id="([^"]+)" Ids="([^"]+)"') | ForEach-Object {
        # $ids, not $path: this script has a [string]$Path parameter, and PowerShell names are
        # case-insensitive - assigning the split result to $path turned it back into one string.
        $ids = $_.Groups[2].Value -split ','
        [pscustomobject]@{ Id = $_.Groups[1].Value; Path = $ids; Source = $ids[0]; Sink = $ids[$ids.Count - 1] }
    })

    # No Path IOBox may feed a pin named Folder (TileCache's). Rule 8 in CLAUDE.md, and it happened
    # twice: 444 tiles next to two repositories on 2026-08-14, and 25 tiles INSIDE help\VL.Mapsui on
    # 2026-09-25 - a Path box wired to Folder was created empty in the GUI, VL read "" as the
    # document's own folder, OSM was switched on, and the cache wrote there. The box was null by the
    # time the patch was saved, so the file looked safe; the danger is the box itself, which anyone
    # can click and clear. Leave Folder unconnected in a shipped patch.
    $pathPads = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($raw, '(?s)<Pad Id="([^"]+)"[^>]*>\s*<p:TypeAnnotation[^>]*>\s*<Choice Kind="\w+" Name="Path" />')) { [void]$pathPads.Add($m.Groups[1].Value) }
    foreach ($l in $links) {
        if ($pathPads.Contains($l.Source) -and $raw -match "<Pin Id=`"$([regex]::Escape($l.Sink))`" Name=`"Folder`"") {
            $problems.Add("a Path IOBox feeds a Folder pin - an empty one means THIS document's folder, and the tile cache writes there (rule 8). Leave Folder unconnected.")
        }
    }

    # A value holding a user-profile path ships the author's user name and machine layout inside
    # the package. vvvv stores the last value an output IOBox showed; on 2026-09-25 a Status box
    # came back as "C:\Users\<name>\AppData\Local\VL.Mapsui\tiles - 4077 tiles". Normalize strips
    # output values; this catches anything else - a constant typed in, a pasted path.
    $userPaths = @([regex]::Matches($raw, 'Value="([^"]*[A-Za-z]:\\Users\\[^"]*)"') | ForEach-Object { $_.Groups[1].Value })
    if ($userPaths.Count) { $problems.Add("a stored value contains a user-profile path (run tools\Normalize-HelpPatches.ps1 for output boxes): $($userPaths[0])") }

    # Pins, Pads and ControlPoints are the only things a Link's path may name.
    $endpoints = @(
        ([regex]'<Pin Id="([^"]+)"').Matches($raw)  | ForEach-Object { $_.Groups[1].Value }
        ([regex]'<Pad Id="([^"]+)"').Matches($raw)  | ForEach-Object { $_.Groups[1].Value }
        # region border control points and link waypoints are both ControlPoint elements
        ([regex]'<ControlPoint Id="([^"]+)"').Matches($raw) | ForEach-Object { $_.Groups[1].Value }
    )
    $linkMatches = $links   # kept by name: the summary line below counts it
    foreach ($l in $links) {
        foreach ($e in $l.Path) {
            if ($endpoints -notcontains $e) { $problems.Add("link endpoint $e is neither a Pin, a Pad nor a ControlPoint") }
        }
    }

    # Three more references that must resolve, from the commit gate in docs\VL-PATCH-XML.md. Each
    # corresponds to a defect that shipped in a sibling repository, and none of them produces an XML
    # error - the document parses, vvvv loads it, and the patch is quietly not what you meant.
    #
    # ParticipatingElements is the expensive one: "deleting nodes made a Create seed link dangle,
    # the sweep removed it, and Create named nothing" - so everything that was supposed to run once
    # silently moved into Update, which is the per-frame mistake this whole ecosystem is about.
    #
    # All seven documents in this repository pass these today because they are simple: no slots, no
    # ParticipatingElements. That is luck, not a check, which is exactly why they are here.
    $declaredIds = @(([regex]'\bId="([^"]+)"').Matches($raw) | ForEach-Object { $_.Groups[1].Value })
    $references = @(
        @{ What = 'Pad@SlotId';                  Pattern = '<Pad\b[^>]*\bSlotId="([^"]+)"' }
        @{ What = 'Fragment@Patch';              Pattern = '<Fragment\b[^>]*\bPatch="([^"]+)"' }
        @{ What = 'Patch@ParticipatingElements'; Pattern = '\bParticipatingElements="([^"]+)"'; Split = $true }
    )
    foreach ($reference in $references) {
        foreach ($m in ([regex]$reference.Pattern).Matches($raw)) {
            # ContainsKey, not $reference.Split - StrictMode throws on a missing hashtable key,
            # and only the ParticipatingElements entry carries one.
            #
            # NOT named $targets: that is the outer list of documents being validated, and shadowing
            # it made the final summary report "1 document" instead of 7. The loop still ran - a
            # foreach captures its collection up front - so only the count lied, which is the kind
            # of wrong number that gets read past.
            $referenced = if ($reference.ContainsKey('Split')) { $m.Groups[1].Value -split ',' } else { @($m.Groups[1].Value) }
            foreach ($target in $referenced) {
                $target = $target.Trim()
                if ($target -and $declaredIds -notcontains $target) {
                    $problems.Add("$($reference.What) names $target, which is not declared in this document")
                }
            }
        }
    }

    # An XML comment cannot contain '--'. The document then does not parse at all, which the check
    # above catches - but the message points at a byte offset rather than at the comment, and this
    # has cost three rounds across these repositories and one more here, in a .csproj.
    foreach ($m in ([regex]'(?s)<!--(.*?)-->').Matches($raw)) {
        if ($m.Groups[1].Value -match '--') {
            $problems.Add("an XML comment contains '--', which is not legal: " +
                ($m.Groups[1].Value.Trim() -replace '\s+', ' ').Substring(0, [Math]::Min(60, $m.Groups[1].Value.Trim().Length)))
        }
    }

    # A Pad wired to nothing is usually dead - a label the user cannot read a value out of.
    #
    # But NOT always, and the exception is a shipped convention rather than a loophole: an
    # ANNOTATION IOBox holds explanatory text as its own Value and is deliberately connected to
    # nothing. vvvv's own packs are full of them, and VL.Mapsui's "Explanation Overview of
    # available nodes" is made of nothing else. An Explanation patch is prose, not dataflow.
    #
    # The two are told apart by what the Pad carries, which is exactly how they differ in intent:
    #
    #   Comment, no Value   a LABEL for a value arriving over a link. Unwired, it shows nothing
    #                       and is the thing this check exists to catch.
    #   Value, no Comment   an ANNOTATION. The text IS the content; a link would overwrite it.
    #
    # A Pad carrying both is a constant feeding something, and an unwired one still gets flagged -
    # deliberately, since that is a link somebody forgot.
    $linked = @($links | ForEach-Object { $_.Source; $_.Sink })
    foreach ($m in ([regex]'<Pad Id="([^"]+)"([^>]*)>').Matches($raw)) {
        $pad = $m.Groups[1].Value
        if ($linked -contains $pad) { continue }

        $attributes = $m.Groups[2].Value
        $isAnnotation = ($attributes -match '\sValue="') -and ($attributes -notmatch '\sComment="')
        if ($isAnnotation) { continue }

        $problems.Add("dangling Pad $pad - an IOBox wired to nothing")
    }

    # An annotation Pad needs a String TypeAnnotation, or vvvv cannot type it: the box renders
    # EMPTY, its text is reported as a warning in the Debug panel, and the patch looks broken to
    # whoever opened it to learn from. Costs nothing to check and was missed on all six patches
    # at once, because nothing static complains - only the running GUI does.
    #
    # The `stringtype = Comment` setting is what makes it a multi-line prose box rather than a
    # one-line editable value; every annotation in vvvv's own packs and in VL.Mapsui carries both.
    $padRegex = [regex]::new('<Pad Id="([^"]+)"([^>]*)>(?<body>.*?)</Pad>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($m in $padRegex.Matches($raw)) {
        $attributes = $m.Groups[2].Value
        if (($attributes -notmatch '\sValue="') -or ($attributes -match '\sComment="')) { continue }

        $body = $m.Groups['body'].Value
        if ($body -notmatch '<Choice Kind="TypeFlag" Name="String" />') {
            $problems.Add("annotation Pad $($m.Groups[1].Value) has no String TypeAnnotation - it will render as an empty box")
        }
        elseif ($body -notmatch 'StringType">Comment<') {
            $problems.Add("annotation Pad $($m.Groups[1].Value) is not set to stringtype Comment - it will render as a one-line value box")
        }
    }
    # A self-closing annotation Pad has no body at all, so the loop above cannot see it.
    foreach ($m in ([regex]'<Pad Id="([^"]+)"([^>]*?)/>').Matches($raw)) {
        $attributes = $m.Groups[2].Value
        if (($attributes -match '\sValue="') -and ($attributes -notmatch '\sComment="')) {
            $problems.Add("annotation Pad $($m.Groups[1].Value) is self-closing - it carries no TypeAnnotation and will render as an empty box")
        }
    }

    # A Pad's `Comment` renders as a LABEL TO THE RIGHT of the box, so an IOBox occupies far more
    # width than its Bounds says. CLAUDE.md here states this and allows "~200px of clear space
    # to its right"; the arithmetic was first implemented in vl-geojson, and every label collision in the
    # first help patches here came from ignoring it.
    #
    # It happened here too. One collision was visible in a screenshot; running this found TEN,
    # across four patches, all of them a label reaching into an annotation box - which is precisely
    # what "check overlaps arithmetically, counting the label strip" means and why doing it by eye
    # does not work.
    #
    # 6.5px per character is calibrated from that ~200px-for-30-characters allowance. A warning,
    # like the density check and for the same reason: it is arithmetic about a font this script
    # cannot measure.
    $glyph = 6.5
    $boxes = @()
    # Attribute order is NOT fixed and must not be assumed: a Pad is written
    # `<Pad Id="…" Comment="…" Bounds="…">` but a Node is `<Node Bounds="…" Id="…">`. The first
    # version of this matched `<(Pad|Node) Id="` and therefore never saw a single Node - so it
    # reported ten Pad-on-Pad collisions and was structurally blind to every Pad-on-Node one.
    # Caught by the negative test, which planted exactly that and stayed green.
    foreach ($m in ([regex]'<(Pad|Node)\s([^>]*)').Matches($raw)) {
        $attributes = $m.Groups[2].Value
        $b = [regex]::Match($attributes, '\bBounds="(\d+),(\d+),(\d+),(\d+)"')
        if (-not $b.Success) { continue }   # a two-number Bounds has no width to collide with
        $id = [regex]::Match($attributes, '\bId="([^"]+)"').Groups[1].Value
        if (-not $id) { continue }
        $boxes += [pscustomobject]@{
            Id      = $id
            Kind    = $m.Groups[1].Value
            X       = [int]$b.Groups[1].Value
            Y       = [int]$b.Groups[2].Value
            W       = [int]$b.Groups[3].Value
            H       = [int]$b.Groups[4].Value
            Comment = [regex]::Match($attributes, '\bComment="([^"]*)"').Groups[1].Value
        }
    }
    foreach ($box in $boxes) {
        if (-not $box.Comment) { continue }
        $labelEnd = $box.X + $box.W + [int]($box.Comment.Length * $glyph)
        foreach ($other in $boxes) {
            if ($other.Id -eq $box.Id) { continue }
            $sameRows = ($box.Y -lt ($other.Y + $other.H)) -and (($box.Y + $box.H) -gt $other.Y)
            if ($sameRows -and $other.X -ge ($box.X + $box.W) -and $other.X -lt $labelEnd) {
                Write-Host ("  warn  Pad {0} label '{1}' reaches x={2} and runs into a {3} at x={4} - a Comment renders to the RIGHT of the box" -f
                    $box.Id, $box.Comment, $labelEnd, $other.Kind, $other.X) -ForegroundColor DarkYellow
            }
        }
    }

    # An annotation Pad whose text does not FIT its box overflows onto the canvas and lands on top
    # of the nodes and labels underneath. `Bounds` is not a clip rectangle. Found by opening a patch
    # and seeing a note spill across three other labels; nothing static complained, because to every
    # other check the document was perfectly well formed.
    #
    # A WARNING, deliberately, not a failure. This is arithmetic about a font the script cannot
    # measure, and the first version of it - a single 0.0045 budget - would have red-flagged ALL
    # TWELVE annotation pads in vl-mapsui, every one of which has been through a GUI review. A gate
    # that disagrees with shipped sibling code gets switched off, and a check nobody runs is worse
    # than no check.
    #
    # The budgets come from vl-mapsui's reviewed set (its four Explanation blocks and eight HowTo
    # notes span 0.0067-0.0413 chars/px2) intersected with what was measured here: at fontsize 12 a
    # 420px-wide box wraps at ~48 characters with ~25px between lines. Capacity scales with the
    # inverse square of the font size, which is why the two budgets are not the same number.
    $densityBudget = @{ 11 = 0.0105; 12 = 0.0070 }
    $defaultBudget = 0.0105   # vvvv's own default when no fontsize is given
    foreach ($m in $padRegex.Matches($raw)) {
        $attributes = $m.Groups[2].Value
        if (($attributes -notmatch '\sValue="') -or ($attributes -match '\sComment="')) { continue }

        $bounds = [regex]::Match($attributes, '\sBounds="(\d+),(\d+),(\d+),(\d+)"')
        if (-not $bounds.Success) { continue }     # a two-number Bounds is not an IOBox
        $area = [int]$bounds.Groups[3].Value * [int]$bounds.Groups[4].Value
        if ($area -le 0) { continue }

        $value = [regex]::Match($attributes, '\sValue="([^"]*)"').Groups[1].Value
        $size = [regex]::Match($m.Groups['body'].Value, '<p:fontsize[^>]*>(\d+)</p:fontsize>')
        $fontsize = if ($size.Success) { [int]$size.Groups[1].Value } else { 0 }
        $budget = if ($densityBudget.ContainsKey($fontsize)) { $densityBudget[$fontsize] } else { $defaultBudget }

        $density = $value.Length / $area
        if ($density -gt $budget) {
            Write-Host ("  warn  annotation Pad {0} may overflow: {1} chars in {2}x{3} at fontsize {4} is {5:F4} chars/px2, over {6} - open it and look" -f
                $m.Groups[1].Value, $value.Length, $bounds.Groups[3].Value, $bounds.Groups[4].Value,
                $(if ($fontsize) { $fontsize } else { 'default' }), $density, $budget) -ForegroundColor DarkYellow
        }
    }

    # An empty Path IOBox is NOT empty: Value="" means the path relative to this document, which is
    # the document's own folder, absolute. Only an unconnected pin can mean "the default".
    $pathPadRegex = [regex]::new(
        '<Pad Id="([^"]+)"[^>]*Value=""[^>]*>(?<body>.*?)</Pad>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($m in $pathPadRegex.Matches($raw)) {
        if ($m.Groups['body'].Value -match 'Name="Path"') {
            $problems.Add("Pad $($m.Groups[1].Value) is an empty Path IOBox - this resolves to the document's own folder, not to nothing")
        }
    }

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "  FAIL  $_" -ForegroundColor Red }
        $totalProblems += $problems.Count
    } else {
        Write-Host "  ok    UTF-8 BOM" -ForegroundColor DarkGray
        Write-Host "  ok    $($allIds.Count) IDs, well formed and unique" -ForegroundColor DarkGray
        Write-Host "  ok    $($linkMatches.Count) links, all endpoints resolve" -ForegroundColor DarkGray
        Write-Host "  ok    no dangling IOBoxes" -ForegroundColor DarkGray
    }
}

# ── Help.xml must account for every patch, and name only patches that exist ──
# Both directions fail silently: a patch missing from Help.xml still ships, but unordered and
# untagged, and a link naming a file that is not there just lists nothing. Numbering the files
# instead of ordering them here is what produced "01 03 04 06", where every gap read as a broken
# install - so this is now the only place ordering lives, and it has to be checked.
if (-not $Path) {
    foreach ($helpXml in Get-ChildItem $RepoRoot -Filter 'Help.xml' -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\dist\\' }) {
        $dir = Split-Path $helpXml.FullName -Parent
        Write-Host "`nvalidating $($helpXml.FullName.Replace("$RepoRoot\", ''))" -ForegroundColor Cyan
        $problems = [System.Collections.Generic.List[string]]::new()

        try { [xml]$hx = Get-Content $helpXml.FullName -Raw } catch { $hx = $null; $problems.Add("does not parse: $($_.Exception.Message)") }

        # XPath rather than property access: $hx.Pack.Topic.VLDocument throws under Set-StrictMode
        # as soon as any Topic has no children, which is exactly the shape a half-edited Help.xml
        # has. Found by negative-testing this check with one entry deleted.
        [array]$entries = if ($hx) { @($hx.SelectNodes('//VLDocument')) } else { @() }
        if ($hx) {
            [array]$listed = $entries | ForEach-Object { $_.link }
            foreach ($link in $listed) {
                if (-not (Test-Path (Join-Path $dir $link))) { $problems.Add("lists `"$link`", which does not exist") }
            }
            foreach ($vl in Get-ChildItem $dir -Filter '*.vl' -File) {
                if ($listed -notcontains $vl.Name) { $problems.Add("`"$($vl.Name)`" is not listed - it would ship unordered and untagged") }
            }
            # Measured across every shipped .vl: 251 multi-term tag lists use commas and none use
            # spaces, which contradicts the written guidelines. Follow the shipped code.
            foreach ($e in $entries) {
                if ($e.tags -match ' ') { $problems.Add("tag list contains a space: `"$($e.tags)`"") }
                if (-not $e.tags) { $problems.Add("`"$($e.link)`" has no tags - it will not be findable by search") }
            }
            # An empty Topic renders as a heading with nothing under it.
            foreach ($t in @($hx.SelectNodes('//Topic'))) {
                if (@($t.SelectNodes('VLDocument')).Count -eq 0) { $problems.Add("Topic `"$($t.title)`" is empty") }
            }
        }

        if ($problems.Count -gt 0) {
            $problems | ForEach-Object { Write-Host "  FAIL  $_" -ForegroundColor Red }
            $totalProblems += $problems.Count
        } else {
            Write-Host "  ok    $($entries.Count) patch(es) listed, all present, none missing" -ForegroundColor DarkGray
            Write-Host "  ok    every entry tagged, no spaces in any tag list" -ForegroundColor DarkGray
            Write-Host "  ok    no empty Topic" -ForegroundColor DarkGray
        }
    }
}

# ── Help flags: F1 works only through them ──
# Pressing F1 on a node opens the help patch in which that node carries a HIGH help flag
# (<p:HelpFocus ...>High</p:HelpFocus> right after </p:NodeReference>; Ctrl+H in the editor). A
# node with no High flag anywhere has no F1 help, and nothing says so - the first fifteen patches
# here shipped without a single flag, and F1 found none of them. Low flags list the patch under
# the node's Node Info. Measured 2026-09-24: 511 of vvvv 7.4's 689 shipped help patches carry flags.
if (-not $Path) {
    Write-Host "`nvalidating help flags (F1)" -ForegroundColor Cyan
    # The 33 public nodes of VL.Mapsui (src\VL.Mapsui; 36 until ToFeatures, ZoomAt and Refresh were removed 2026-09-25): every [ProcessNode] and every
    # public static method of a [Name]d static class. A node added to the package and not here is
    # invisible to this audit, so keep the two in step - docs\MAPSUI-SURFACE.md lists the same set.
    $ourNodes = @(
        'Mapsui|Map', 'Mapsui|ViewportInfo', 'Mapsui|LayerInfo', 'Mapsui|Pick',
        'Mapsui.Project|ScreenToWorld', 'Mapsui.Project|WorldToScreen', 'Mapsui.Debug|DiagnosticsLayer',
        'Mapsui.Layers|OpenStreetMap', 'Mapsui.Layers|XYZ', 'Mapsui.Layers|FeatureLayer', 'Mapsui.Layers|Geometry',
        'Mapsui.Layers|TileCache', 'Mapsui.Layers|Graticule', 'Mapsui.Layers|VisibleRange',
        'Mapsui.Styles|VectorStyle', 'Mapsui.Styles|SymbolStyle', 'Mapsui.Styles|LabelStyle',
        'Mapsui.Styles|StyleByGeometry', 'Mapsui.Styles|StyleByValue',
        'Mapsui.Navigate|CenterOn', 'Mapsui.Navigate|ZoomToLevel', 'Mapsui.Navigate|ZoomByWheel',
        'Mapsui.Navigate|DragBetween', 'Mapsui.Navigate|ZoomToLayer', 'Mapsui.Navigate|ZoomToLayers',
        'Mapsui.Navigate|Drag', 'Mapsui.Navigate|ZoomIn', 'Mapsui.Navigate|ZoomOut',
        'Mapsui.Widgets|ScaleBar', 'Mapsui.Widgets|Attribution', 'Mapsui.Widgets|ZoomButtons', 'Mapsui.Widgets|Click',
        'Mapsui.Skia|ToSkiaLayer')
    $high = @{}; $low = @{}; $used = @{}
    foreach ($vl in Get-ChildItem (Join-Path $RepoRoot 'help') -Filter *.vl -Recurse -File) {
        $raw = [IO.File]::ReadAllText($vl.FullName)
        # (?:\s*<PinReference[^>]*/>)* : a node whose pins carry defaults (Map's Initial pins, the
        # Renderer's Bounds) lists them as PinReference elements AFTER the Choice and before
        # </p:NodeReference>. Without this the audit could not see Map or ToSkiaLayer in any patch
        # and reported both as never flagged High while both were.
        $pattern = '<p:NodeReference LastCategoryFullName="([^"]+)" LastDependency="VL\.Mapsui\.vl">(?:(?!</p:NodeReference>).)*?<Choice Kind="(?:OperationCallFlag|ProcessAppFlag)" Name="([^"]+)" />(?:\s*<PinReference[^>]*/>)*\s*</p:NodeReference>\s*(?:<p:HelpFocus[^>]*>(High|Low)</p:HelpFocus>)?'
        foreach ($m in [regex]::Matches($raw, $pattern, 'Singleline')) {
            $key = "$($m.Groups[1].Value)|$($m.Groups[2].Value)"
            $used[$key] = $true
            if ($m.Groups[3].Success) {
                $table = if ($m.Groups[3].Value -eq 'High') { $high } else { $low }
                if (-not $table.ContainsKey($key)) { $table[$key] = @() }
                $table[$key] += $vl.BaseName
            }
        }
    }
    $flagProblems = 0
    foreach ($n in $ourNodes) {
        if (-not $high.ContainsKey($n)) {
            $where = if ($used.ContainsKey($n)) { 'used in a patch but never flagged High' } else { 'in no help patch at all' }
            Write-Host "  warn  $n has no F1 help - $where" -ForegroundColor Yellow
        }
        elseif ($high[$n].Count -gt 1) {
            Write-Host "  warn  $n carries a High flag in $($high[$n].Count) patches - F1 can open only one: $($high[$n] -join ', ')" -ForegroundColor Yellow
        }
    }
    $covered = @($ourNodes | Where-Object { $high.ContainsKey($_) }).Count
    # Not `($low.Values | Measure-Object -Sum Count).Sum`: with no Low flag anywhere the pipeline is
    # empty, Measure-Object returns nothing, and StrictMode fails on `.Sum` - which is exactly the
    # state this repository was in on 2026-09-24, so the "0 of 36" line never printed. The sibling's
    # copy has the same latent bug and never hit it because it always had Low flags.
    $lowCount = 0; foreach ($v in $low.Values) { $lowCount += @($v).Count }
    Write-Host "  ok    $covered of $($ourNodes.Count) nodes open a help patch on F1; $lowCount Low flags list patches under Node Info" -ForegroundColor DarkGray
}
Write-Host ''
if ($totalProblems -gt 0) {
    Write-Host "FAIL - $totalProblems problem(s) across $($targets.Count) document(s)." -ForegroundColor Red
    exit 1
}
Write-Host "PASS - $($targets.Count) document(s) structurally valid." -ForegroundColor Green
Write-Host @"
Note: this proves the documents are well formed, and nothing more. Three separate claims follow it:
  a node RESOLVED       -> vvvvc, then READ the generated *.vl.1.cs (exit code 0 proves nothing;
                           an unresolved node has its links dropped silently)
  its CATEGORY is right -> only the NodeBrowser. LastCategoryFullName in a .vl is a hint, and a
                           patch compiles fine with it set to nonsense
  it COMPUTES the right value -> only running it
"@ -ForegroundColor Yellow
