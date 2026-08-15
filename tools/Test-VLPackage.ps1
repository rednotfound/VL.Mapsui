<#
.SYNOPSIS
    Static validation of every package in this repository. Needs no vvvv install.

.DESCRIPTION
    Every check here corresponds to a defect that shipped at least once in the 0.0.x line
    and produced no error message at all -- the package installed, and simply had no
    nodes. CI runs this before publishing, because a tag push goes straight to nuget.org
    and a version can never be replaced once it is there.

      1. UTF-8 BOM on the .vl            vvvv's deserializer expects it
      2. document IDs                    exactly 22 chars, first [A-V], all unique
      3. <Patch> + Application node      present in every working package
      4. Canvas DefaultCategory          set, or nodes have no category
      5. forwarded assemblies exist      relative paths in the .vl actually resolve
      6. VL.Core import attribute        without it the assemblies contribute no nodes
      7. nuspec ships the .vl at root    a subfolder means vvvv never finds it
      8. nuspec ships every assembly     forwarding a .dll that is not in the package
      9. nuspec declares every nuget     third-party deps the .vl references
     10. help patches                    BOM, no ProjectDependency, local packages pinned to 0.0.0
     11. no stray map tiles              a cache once wrote {z}\{x}\{y}.png into the repository

    Run .\build.ps1 first, or pass -FromBuildOutput to read assemblies straight out of
    src\*\bin\<Configuration>\net8.0 (what CI does).

.EXAMPLE
    .\tools\Test-VLPackage.ps1
.EXAMPLE
    .\tools\Test-VLPackage.ps1 -FromBuildOutput
#>
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    # Resolve assemblies from src\*\bin\... instead of dist\<Package>\lib\...
    [switch]$FromBuildOutput,
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# A package is a .vl at the repo root with a .nuspec of the same name. Discovered the same
# way build.ps1 discovers them, so the two cannot drift apart.
$Packages = @(
    Get-ChildItem $RepoRoot -Filter '*.vl' -File |
        Where-Object { Test-Path (Join-Path $RepoRoot "$($_.BaseName).nuspec") } |
        Sort-Object BaseName
)
if ($Packages.Count -eq 0) { throw "No package found: expected a .vl with a matching .nuspec at $RepoRoot" }

$errors = [System.Collections.Generic.List[string]]::new()

function Fail([string]$message) { $script:errors.Add($message) }
function Ok([string]$message)   { Write-Host "  ok    $message" -ForegroundColor DarkGray }

# Dot-notation on XmlElement throws under StrictMode when the element is absent, which is
# exactly the case a validator must survive. These are also namespace-agnostic, so the
# same helpers work on the .vl (no namespace) and the .nuspec (default namespace).
function Get-Child($node, [string]$name) {
    if ($null -eq $node) { return @() }
    @($node.ChildNodes | Where-Object { $_.LocalName -eq $name })
}
function Get-Attr($node, [string]$name) {
    if ($null -eq $node -or $null -eq $node.Attributes) { return $null }
    $a = $node.Attributes[$name]
    if ($a) { $a.Value } else { $null }
}

foreach ($package in $Packages) {
    $pkgName = $package.BaseName
    $VlFile  = $package.FullName
    $Nuspec  = Join-Path $RepoRoot "$pkgName.nuspec"

    Write-Host "validating $pkgName`n" -ForegroundColor White

    # 1. BOM -----------------------------------------------------------------
    $firstBytes = Get-Content $VlFile -AsByteStream -TotalCount 3
    if ($firstBytes.Count -lt 3 -or $firstBytes[0] -ne 0xEF -or $firstBytes[1] -ne 0xBB -or $firstBytes[2] -ne 0xBF) {
        Fail "$pkgName.vl has no UTF-8 BOM. vvvv writes one on every .vl; without it the document fails to load silently."
    } else { Ok "UTF-8 BOM" }

    # 2. IDs -----------------------------------------------------------------
    $raw = Get-Content $VlFile -Raw

    # Parse explicitly rather than letting the [xml] cast throw. A hand-edited .vl is quite
    # capable of being malformed, and a validator that answers with a PowerShell stack trace
    # instead of "this file is not valid XML" is failing at the one job it has.
    $vl = New-Object System.Xml.XmlDocument
    try { $vl.LoadXml($raw) }
    catch {
        Fail "$pkgName.vl is not well-formed XML: $($_.Exception.Message)"
        Write-Host ''
        continue
    }

    $ids = [regex]::Matches($raw, 'Id="([^"]*)"') | ForEach-Object { $_.Groups[1].Value }
    $malformed = $ids | Where-Object { $_ -notmatch '^[A-V][0-9A-Za-z]{21}$' }
    if ($malformed) {
        Fail "$pkgName.vl has malformed document IDs (must be 22 chars, first in A-V): $($malformed -join ', ')"
    } else { Ok "$($ids.Count) document IDs well formed" }

    $dupes = $ids | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name
    if ($dupes) { Fail "$pkgName.vl has duplicate document IDs: $($dupes -join ', ')" } else { Ok "IDs unique" }

    # 3 + 4. patch structure -------------------------------------------------
    $doc      = $vl.DocumentElement
    $patch    = @(Get-Child $doc 'Patch') | Select-Object -First 1
    $canvas   = @(Get-Child $patch 'Canvas') | Select-Object -First 1
    $category = Get-Attr $canvas 'DefaultCategory'

    if (-not $patch) {
        Fail "$pkgName.vl has no <Patch>. Every shipped VL package has one containing an Application node."
    } elseif (-not $category) {
        Fail "$pkgName.vl: <Canvas> is missing DefaultCategory."
    } else { Ok "<Patch> with Canvas DefaultCategory=$category" }

    # 5 + 6. forwarded assemblies --------------------------------------------
    $forwards = @(Get-Child $doc 'PlatformDependency' | Where-Object { (Get-Attr $_ 'Location') -like './lib/*' })
    if ($forwards.Count -eq 0) { Fail "$pkgName.vl forwards no assemblies." }

    $importCheck = Join-Path $PSScriptRoot 'Test-VLImportAttribute.ps1'
    $forwardedNames = @()

    foreach ($fwd in $forwards) {
        $rel  = (Get-Attr $fwd 'Location') -replace '^\./', ''
        $name = Split-Path $rel -Leaf
        $forwardedNames += $name

        if ((Get-Attr $fwd 'IsForward') -ne 'true') {
            Fail "$name is a PlatformDependency but not IsForward=`"true`", so its nodes stay invisible to consumers."
        }

        $dll = if ($FromBuildOutput) {
            Join-Path $RepoRoot "src\$([IO.Path]::GetFileNameWithoutExtension($rel))\bin\$Configuration\net8.0\$name"
        } else {
            Join-Path $RepoRoot "dist\$pkgName\$rel"
        }

        if (-not (Test-Path $dll)) {
            Fail "$name not found at $dll (run .\build.ps1, or pass -FromBuildOutput)"
            continue
        }

        $attrs = @(& $importCheck -Path $dll)
        if (-not $attrs) {
            Fail "$name has no VL.Core.Import attribute. It will forward without error and contribute no nodes. Add [assembly: ImportAsIs(Namespace = `"VL`")]."
        } else { Ok "$name forwards, $($attrs -join ', ')" }
    }

    # 7 + 8 + 9. nuspec ------------------------------------------------------
    [xml]$nu   = Get-Content $Nuspec -Raw
    $pkg       = $nu.DocumentElement
    $files     = @(Get-Child (@(Get-Child $pkg 'files') | Select-Object -First 1) 'file')
    $metadata  = @(Get-Child $pkg 'metadata') | Select-Object -First 1
    $groups    = @(Get-Child (@(Get-Child $metadata 'dependencies') | Select-Object -First 1) 'group')
    $declared  = @($groups | ForEach-Object { Get-Child $_ 'dependency' } | ForEach-Object { Get-Attr $_ 'id' })

    if (-not ($files | Where-Object { (Get-Attr $_ 'src') -eq "$pkgName.vl" -and [string]::IsNullOrEmpty((Get-Attr $_ 'target')) })) {
        Fail "$pkgName.nuspec does not ship $pkgName.vl at the package root (target must be empty)."
    } else { Ok "nuspec ships $pkgName.vl at the package root" }

    foreach ($name in $forwardedNames) {
        if (-not ($files | Where-Object { (Get-Attr $_ 'src') -like "*$name" })) {
            Fail "$pkgName.vl forwards $name but the nuspec does not ship it."
        }
    }

    # VL.CoreLib, VL.Core and VL.Skia ship with vvvv and are deliberately not nuspec
    # dependencies.
    $needed = @(Get-Child $doc 'NugetDependency' |
        ForEach-Object { Get-Attr $_ 'Location' } |
        Where-Object { $_ -notin @('VL.CoreLib', 'VL.Core', 'VL.Skia') })

    foreach ($dep in $needed) {
        if ($dep -notin $declared) {
            Fail "$pkgName.vl references nuget '$dep' but the nuspec does not declare it, so it will be missing on install."
        }
    }
    if (-not ($needed | Where-Object { $_ -notin $declared })) { Ok "nuspec declares all $($needed.Count) referenced nugets" }

    Write-Host ''
}

# 10. help patches -----------------------------------------------------------
# These are shipped documents too -- each nuspec packs help\<Package>\**\*.vl straight out of the
# repo -- so they carry the same load-silently-fail risks as an entry point itself.
$helpDir = Join-Path $RepoRoot 'help'
# The @() must wrap the whole if-expression: PowerShell unwraps a single-element array on
# the way out of one, and StrictMode then throws on .Count. Same trap as Get-Child above.
$helpDocs = @(if (Test-Path $helpDir) { Get-ChildItem $helpDir -File -Recurse -Filter *.vl })

foreach ($helpDoc in $helpDocs) {
    $name = $helpDoc.Name
    $head = Get-Content $helpDoc.FullName -AsByteStream -TotalCount 3
    if ($head.Count -lt 3 -or $head[0] -ne 0xEF -or $head[1] -ne 0xBB -or $head[2] -ne 0xBF) {
        Fail "help\$name has no UTF-8 BOM."
    }

    $helpRaw = Get-Content $helpDoc.FullName -Raw

    # A ProjectDependency points at a .csproj that is not in the package, and forces
    # everything downstream to stay editable. It belongs only in test\DevLoop.vl.
    if ($helpRaw -match '<ProjectDependency\b') {
        Fail "help\$name contains a <ProjectDependency>. Shipped documents must not reference a .csproj."
    }

    # Every dependency on a package from this repository must be the 0.0.0 sentinel, not
    # whichever version vvvv resolved while authoring -- see tools\Normalize-HelpPatches.ps1.
    # A patch may reference more than one of them, so check them all rather than the first.
    $localNames = @($Packages | ForEach-Object { $_.BaseName })
    $pins = @([regex]::Matches($helpRaw, '<NugetDependency\b[^>]*\bLocation="([^"]*)"[^>]*\bVersion="([^"]*)"') |
        Where-Object { $_.Groups[1].Value -in $localNames })

    if ($pins.Count -eq 0) {
        Fail "help\$name declares no dependency on any package in this repository, so none of its nodes will resolve."
    }
    foreach ($pin in $pins) {
        if ($pin.Groups[2].Value -ne '0.0.0') {
            Fail "help\$name pins $($pin.Groups[1].Value) $($pin.Groups[2].Value); it must be 0.0.0 or it will ask for that exact version forever. Run tools\Normalize-HelpPatches.ps1."
        }
    }

    # ------------------------------------------------------------------------------------
    # Every internal reference resolves.
    #
    # A .vl is a graph held together by 22-character strings, and every one of these has
    # broken at least once while editing by hand. None of them is an XML error: the document
    # parses, vvvv loads it, and the patch is quietly wrong.
    #
    #   Link@Ids                    a wire with no endpoint
    #   Pad@SlotId                  a pad glyph belonging to no slot
    #   Fragment@Patch              an operation registered against a patch that is not there
    #   Patch@ParticipatingElements what runs in Create - and this is the one that bit us:
    #                               DELETING nodes made the Create fragment's seed link dangle,
    #                               the dangling-link sweep removed it, and Create was left
    #                               naming an element that no longer existed. A cleanup pass
    #                               needs the same validation as an edit pass.
    # ------------------------------------------------------------------------------------
    try { $helpXml = [xml]$helpRaw } catch { Fail "help\$name is not well-formed XML: $($_.Exception.Message)"; continue }

    $known = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@($helpXml.SelectNodes('//@Id') | ForEach-Object { $_.Value }))

    $unresolved = [System.Collections.Generic.List[string]]::new()
    foreach ($link in $helpXml.SelectNodes('//Link')) {
        foreach ($end in ($link.Ids -split ',')) {
            if (-not $known.Contains($end)) { $unresolved.Add("Link $($link.Id) -> $end") }
        }
    }
    foreach ($pad in $helpXml.SelectNodes('//Pad[@SlotId]')) {
        if (-not $known.Contains($pad.SlotId)) { $unresolved.Add("Pad $($pad.Id) -> Slot $($pad.SlotId)") }
    }
    foreach ($fragment in $helpXml.SelectNodes('//Fragment[@Patch]')) {
        if (-not $known.Contains($fragment.Patch)) { $unresolved.Add("Fragment $($fragment.Id) -> Patch $($fragment.Patch)") }
    }
    foreach ($operation in $helpXml.SelectNodes('//Patch[@ParticipatingElements]')) {
        foreach ($element in ($operation.ParticipatingElements -split ',')) {
            if (-not $known.Contains($element)) {
                $unresolved.Add("Patch '$($operation.Name)' ParticipatingElements -> $element")
            }
        }
    }

    foreach ($reference in $unresolved) {
        Fail "help\$name has an unresolved reference: $reference"
    }

    # Ids are the same 22-character shape everywhere, and a hand-typed one has slipped through
    # three times because the format looked right - see docs\RULES.md on tools\New-VLId.ps1.
    $malformed = @($helpXml.SelectNodes('//@Id') | Where-Object { $_.Value -notmatch '^[A-V][0-9A-Za-z]{21}$' })
    foreach ($id in $malformed) {
        Fail "help\$name has a malformed Id '$($id.Value)'. Generate them with tools\New-VLId.ps1."
    }

    $duplicates = @($helpXml.SelectNodes('//@Id') | Group-Object { $_.Value } | Where-Object Count -gt 1)
    foreach ($group in $duplicates) {
        Fail "help\$name uses Id $($group.Name) $($group.Count) times; ids must be unique within a document."
    }

    # A readout left behind. When a node is deleted its links go with it, but the IOBox that
    # displayed its output stays on the canvas showing nothing forever - which is what a reader
    # then asks about, quite reasonably. A WARNING rather than a failure: an IOBox carrying a
    # value is a constant or an annotation and is unconnected on purpose, so only a *labelled,
    # empty* one with no link is debris.
    $linked = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($link in $helpXml.SelectNodes('//Link')) {
        foreach ($end in ($link.Ids -split ',')) { [void]$linked.Add($end) }
    }
    foreach ($box in $helpXml.SelectNodes('//Pad[@isIOBox="true"][@Comment]')) {
        if (-not $linked.Contains($box.Id) -and [string]::IsNullOrEmpty($box.Value)) {
            Write-Host "  warn  help\$name has an empty IOBox '$($box.Comment)' connected to nothing" -ForegroundColor DarkYellow
        }
    }
}

if ($helpDocs.Count -eq 0) {
    Write-Host "  warn  no help patches" -ForegroundColor DarkYellow
} elseif (-not ($errors | Where-Object { $_ -like 'help\*' })) {
    Ok "$($helpDocs.Count) help patch(es) valid"
}

# 11. stray map tiles -------------------------------------------------------
#
# On 2026-08-14 a cache wrote 444 tiles next to two repositories, 38 of them reached a
# commit, and build.ps1 had already staged them into dist\ -- one pack.ps1 away from
# shipping inside the package. Every check in this file passed throughout: they all ask
# whether what should be there is there, and none asked whether something else had turned
# up. This asks that.
#
# The shape is BruTile's FileCache layout, {zoom}\{x}\{y}.png, which is what a tile cache
# writes and what nothing else in a package looks like.
Write-Host "`nchecking for stray map tiles" -ForegroundColor White

$tileRoots = @('help', 'dist') |
    ForEach-Object { Join-Path $RepoRoot $_ } |
    Where-Object { Test-Path $_ }

$strays = @(
    foreach ($root in $tileRoots) {
        Get-ChildItem $root -Recurse -File -Filter '*.png' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\\d+\\\d+\\\d+\.png$' }
    }
)

if ($strays.Count -gt 0) {
    $shown = ($strays | Select-Object -First 5 | ForEach-Object { $_.FullName.Replace("$RepoRoot\", '') }) -join ', '
    $more  = if ($strays.Count -gt 5) { " (and $($strays.Count - 5) more)" } else { '' }
    Fail "found $($strays.Count) file(s) shaped like cached map tiles under help\ or dist\: $shown$more. A tile cache has written into the repository; see NOTES.md 2026-08-14. Delete them and find out which folder the cache was given."
} else {
    Ok "no {zoom}\{x}\{y}.png under help\ or dist\"
}

# ---------------------------------------------------------------------------
Write-Host ''
if ($errors.Count -gt 0) {
    Write-Host "FAIL - $($errors.Count) problem(s):" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "PASS - package structure is valid." -ForegroundColor Green
exit 0
