<#
.SYNOPSIS
    Compiles every help patch headlessly with vvvvc and checks that this package's nodes RESOLVED.

.DESCRIPTION
    This is the only automated check that says anything about whether a node actually works.
    Test-VLPatch.ps1 proves the XML is well formed; Test-VLPackage.ps1 proves the package could
    contribute nodes. Neither notices that a node failed to resolve.

    AND THE EXIT CODE OF vvvvc DOES NOT NOTICE EITHER. An unresolved node is built with no working
    pins, every link to it is dropped, and vvvvc exits 0 with nothing red. So this script does not
    trust the exit code: it READS the generated C# and asserts that every node the patch takes from
    VL.Mapsui appears in it - and that every [ProcessNode] is constructed in __Create__, never in
    Update, because built per frame the map node once opened 17,000 connections and took a network
    down. A patch in which nothing resolved looks exactly like a patch in which everything did,
    until the C# is read.

    Carried from vl-nettopologysuite\tools\Compile-HelpPatches.ps1 on 2026-09-24; the previous
    script here (tools\legacy\Compile-HelpPatches.no-csharp-read.ps1) trusted the exit code.

    Carried from vl-geojson\tools\Compile-HelpPatches.ps1, where two things were learned that both
    have to line up:

      1. `--package-repositories` - how vvvv finds a *package folder*, i.e. dist\VL.Mapsui\.
         Point it at dist\, not dist\feed\, or vvvvc says "Missing package".

      2. a NuGet source carrying the *nupkg* - because vvvvc then generates a .csproj with a
         PackageReference and runs a normal restore. Point that at dist\feed\, or restore says
         "NU1101: package VL.Mapsui not found". The sibling's CLAUDE.md used to call that NU1101
         "expected, because it is the export stage after codegen". The C# was indeed already
         written, but the export half never ran, so half of what a compile proves was never proved.
         A NuGet.config dropped at the output root fixes it; restore finds it by walking up.

    Requires .\pack.ps1 to have run: dist\feed\*.nupkg is what (2) reads, and pack.ps1 evicts the
    global NuGet cache entry, which is what makes the NU1101 check able to fail at all. Running this
    twice without repacking proves less the second time, because restore is then satisfied from
    %USERPROFILE%\.nuget\packages and never consults a source.

    Pass ABSOLUTE paths for both the document and the output directory: with a relative document
    path vvvvc dies inside PathUtils.GetRelativePath with "UriFormatException: Invalid URI".

    What it still cannot tell you: which CATEGORY a node lands in. LastCategoryFullName in a .vl
    is a hint - negative-tested here by setting it to NTS.Wrong (in the sibling), after which every node still
    resolved. Only the NodeBrowser proves that, and only running the patch proves it computes the
    right value.

.PARAMETER Patch
    Compile only patches whose name matches this wildcard. Default: all of them, and that is the
    gate - "compile every help patch, not the one you edited". Deleting a node once produced
    "Not found" in a file nobody had touched, in a sibling repository.

.PARAMETER KeepOutput
    Leave the generated projects in place so the *.vl.1.cs can be read by hand. The path is
    printed at the end.

.EXAMPLE
    .\pack.ps1 ; .\tools\Compile-HelpPatches.ps1
.EXAMPLE
    .\tools\Compile-HelpPatches.ps1 -Patch "*Buffer*" -KeepOutput
#>
param(
    [string]$OutputDirectory = '',
    [string]$Patch = '*',
    [switch]$KeepOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Dist     = Join-Path $RepoRoot 'dist'
$Deps     = Join-Path $RepoRoot 'deps'
$Feed     = Join-Path $RepoRoot 'dist\feed'

if (-not (Test-Path $Dist)) { throw "dist\ not found. Run .\build.ps1 first." }
if (-not (Get-ChildItem $Feed -Filter '*.nupkg' -ErrorAction SilentlyContinue)) {
    throw "no .nupkg in dist\feed - run .\pack.ps1 first, or restore will fail with NU1101."
}

$vvvvc = & (Join-Path $PSScriptRoot 'Find-Vvvv.ps1') -Compiler
if (-not (Test-Path $vvvvc)) { throw "vvvvc.exe not found at '$vvvvc'" }

# Detect and refuse - never kill. A running vvvv holds the staged assemblies open, and killing it
# corrupts its session state. Same guard as build.ps1.
$running = @(Get-Process 'vvvv' -ErrorAction SilentlyContinue)
if ($running) { throw "vvvv is running (PID $($running.Id -join ', ')). Close it by hand, then run this again." }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) "vl-mapsui-compile-$PID"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null

# The sibling package that MAKES geometry. Help patches here may consume it (this package's nuspec
# depends on it); its dist\feed carries the nupkg restore needs and its dist\ the folder vvvv needs.
$NtsRepo = 'D:\2026_Projects\vl-nettopologysuite'

# (2): restore walks up from the generated .csproj and finds this. nuget.org is listed as well
# because the generated project also references Mapsui and NetTopologySuite themselves, which our
# feed does not carry; on a warm cache it is never consulted, on a cold one it is needed.
$sources = @("    <add key=`"mapsui-dist`" value=`"$Feed`" />")
$ntsFeed = Join-Path $NtsRepo 'dist\feed'
if (Test-Path $ntsFeed) { $sources += "    <add key=`"nts-dist`" value=`"$ntsFeed`" />" }
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
$($sources -join "`n")
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $OutputDirectory 'NuGet.config') -Encoding utf8

# (1): package *folders*, not the feed. THREE of them plus the sibling's deps - omitting one gives
# an error naming something else entirely ("The referenced symbol source 'Mapsui.dll' couldn't be
# found", or 25 ambiguous Point candidates). Same list as tools\Open-HelpPatch.ps1.
$repositories = @($Dist, $Deps)
foreach ($sub in @('dist', 'deps')) { $repositories += Join-Path $NtsRepo $sub }
$repositories = ($repositories | Where-Object { Test-Path $_ }) -join ';'

$patches = @(Get-ChildItem (Join-Path $RepoRoot 'help') -Filter '*.vl' -Recurse -File |
             Where-Object { $_.BaseName -like $Patch } | Sort-Object Name)
if ($patches.Count -eq 0) { throw "No help patch matches -Patch '$Patch'" }

# Every node this package contributes, by the name the patch uses, and the C# a resolved call
# must contain. A static method compiles to `Class.Method(`; a process node to `new ClassNode(`.
# 35 entries (ToFeatures removed 2026-09-25), the same set as tools\Test-VLPatch.ps1's $ourNodes and docs\MAPSUI-SURFACE.md.
#
# A node the patch names that is NOT in this table is itself a failure, so that adding a node
# to the package and forgetting it here shows up the first time a patch uses it.
$OurNodes = @{
    # Mapsui
    'Map'             = 'new\s+[\w\.]*MapNode\('
    'ViewportInfo'    = 'MapInfoNodes\.ViewportInfo\('
    'LayerInfo'       = 'MapInfoNodes\.LayerInfo\('
    'Pick'            = 'new\s+[\w\.]*PickNode\('
    'ScreenToWorld'   = 'ProjectNodes\.ScreenToWorld\('
    'WorldToScreen'   = 'ProjectNodes\.WorldToScreen\('
    'DiagnosticsLayer'= 'MapNodes\.DiagnosticsLayer\('
    # Mapsui.Layers
    'OpenStreetMap'   = 'new\s+[\w\.]*OpenStreetMapLayerNode\('
    'XYZ'             = 'new\s+[\w\.]*XyzTileLayerNode\('
    'FeatureLayer'    = 'new\s+[\w\.]*FeatureLayerNode\('
    'Geometry'        = 'new\s+[\w\.]*GeometryLayerNode\('
    'TileCache'       = 'new\s+[\w\.]*TileCacheNode\('
    'Graticule'       = 'new\s+[\w\.]*GraticuleNode\('
    'VisibleRange'    = 'VisibleRangeNodes\.VisibleRange\('
    # Mapsui.Styles
    'VectorStyle'     = 'new\s+[\w\.]*VectorStyleNode\('
    'SymbolStyle'     = 'new\s+[\w\.]*SymbolStyleNode\('
    'LabelStyle'      = 'new\s+[\w\.]*LabelStyleNode\('
    'StyleByGeometry' = 'new\s+[\w\.]*StyleByGeometryNode\('
    'StyleByValue'    = 'new\s+[\w\.]*StyleByValueNode\('
    # Mapsui.Navigate
    'CenterOn'        = 'NavigateNodes\.CenterOn\('
    'ZoomToLevel'     = 'NavigateNodes\.ZoomToLevel\('
    'ZoomAt'          = 'NavigateNodes\.ZoomAt\('
    'ZoomByWheel'     = 'NavigateNodes\.ZoomByWheel\('
    'DragBetween'     = 'NavigateNodes\.DragBetween\('
    'Refresh'         = 'NavigateNodes\.Refresh\('
    'ZoomToLayer'     = 'new\s+[\w\.]*ZoomToLayerNode\('
    'ZoomToLayers'    = 'new\s+[\w\.]*ZoomToLayersNode\('
    'Drag'            = 'new\s+[\w\.]*DragNode\('
    'ZoomIn'          = 'new\s+[\w\.]*ZoomInNode\('
    'ZoomOut'         = 'new\s+[\w\.]*ZoomOutNode\('
    # Mapsui.Widgets
    'ScaleBar'        = 'new\s+[\w\.]*ScaleBarWidgetNode\('
    'Attribution'     = 'new\s+[\w\.]*AttributionWidgetNode\('
    'ZoomButtons'     = 'new\s+[\w\.]*ZoomButtonsWidgetNode\('
    'Click'           = 'new\s+[\w\.]*WidgetClickNode\('
    # Mapsui.Skia
    'ToSkiaLayer'     = 'new\s+[\w\.]*ToSkiaLayerNode\('
}

# Process nodes must be constructed in __Create__ and only updated in Update. Built per frame, the
# map node opened 17,000 TCP connections in 13 minutes and took a home network down (NOTES.md,
# 2026-08-13) - so for this package the Create check is not hygiene, it is the incident's regression
# test at the patch level. Every [ProcessNode] here, node name -> C# class; derived from $OurNodes
# so the two cannot drift.
$OurProcessNodes = @{}
foreach ($entry in $OurNodes.GetEnumerator()) {
    if ($entry.Value -match '^new\\s\+\[\\w\\\.\]\*(\w+)\\\($') { $OurProcessNodes[$entry.Key] = $Matches[1] }
}
if ($OurProcessNodes.Count -ne 23) { throw "expected 23 process nodes in the node table, found $($OurProcessNodes.Count) - the table's shape changed" }

# Every node the patch takes from THIS package, by name. Anchored on LastDependency so that a
# "Split" from VL.NetTopologySuite or a "Drag" from elsewhere is not counted against us.
function Get-ClaimedNodes([string]$patchXml) {
    $pattern = 'LastDependency="VL\.Mapsui\.vl">\s*(?:<[^>]+>\s*)*?<Choice Kind="(?:OperationCallFlag|ProcessAppFlag|ProcessNodeFlag|OperationFlag)" Name="([^"]+)"'
    [regex]::Matches($patchXml, $pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
}

$failures = New-Object System.Collections.Generic.List[string]

foreach ($p in $patches) {
    Write-Host "`ncompiling $($p.Name)" -ForegroundColor Cyan

    $outDir = Join-Path $OutputDirectory ($p.BaseName -replace '[^\w\-]', '_')
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force $outDir | Out-Null
    Copy-Item (Join-Path $OutputDirectory 'NuGet.config') $outDir

    $log = & $vvvvc $p.FullName '--package-repositories' $repositories '--output-directory' $outDir 2>&1 | Out-String

    if ($log -match 'NU1101') {
        $failures.Add("$($p.Name): restore failed with NU1101 - dist\feed is not reaching restore")
        Write-Host "  FAIL  NU1101 - dist\feed is not reaching restore" -ForegroundColor Red
    }

    $generated = @(Get-ChildItem $outDir -Recurse -Filter '*.vl.1.cs' -ErrorAction SilentlyContinue)
    if ($generated.Count -eq 0) {
        $failures.Add("$($p.Name): no C# was generated")
        Write-Host "  FAIL  no C# generated" -ForegroundColor Red
        Write-Host (($log -split "`n" | Select-Object -Last 12) -join "`n") -ForegroundColor DarkGray
        continue
    }

    $csharp   = ($generated | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    $patchXml = Get-Content $p.FullName -Raw
    $claimed  = @(Get-ClaimedNodes $patchXml)

    if ($claimed.Count -eq 0) {
        Write-Host "  ok    compiled (uses no VL.Mapsui node)" -ForegroundColor DarkGray
        continue
    }

    foreach ($nodeName in $claimed) {
        if (-not $OurNodes.ContainsKey($nodeName)) {
            $failures.Add("$($p.Name): '$nodeName' is taken from VL.Mapsui but is not in this script's node table - add it")
            Write-Host "  FAIL  '$nodeName' is not in the node table" -ForegroundColor Red
            continue
        }
        if ($csharp -notmatch $OurNodes[$nodeName]) {
            $failures.Add("$($p.Name): '$nodeName' is in the patch but NOT in the generated C# - it did not resolve")
            Write-Host "  FAIL  '$nodeName' did not resolve" -ForegroundColor Red
        }
        else {
            Write-Host "  ok    '$nodeName' resolved" -ForegroundColor DarkGray
        }

        if ($OurProcessNodes.ContainsKey($nodeName)) {
            $class = $OurProcessNodes[$nodeName]
            # The FIRST "__Create__" in the file is a CALL, not the definition, and the parameter
            # list has its own parentheses - hence the lazy `.*?\)\{`. Carried from vl-geojson.
            $createBody = [regex]::Match(
                $csharp, 'public\s+[\w\.]+\s+__Create__\(.*?\)\{(?<body>.*?)\n        \}', 'Singleline')
            if ($createBody.Success -and $createBody.Groups['body'].Value -match "new\s+[\w\.]*$class\(") {
                Write-Host "  ok    '$nodeName' is constructed in Create, not Update" -ForegroundColor DarkGray
            }
            else {
                $failures.Add("$($p.Name): $class is not constructed in __Create__ - it would be rebuilt every frame")
                Write-Host "  FAIL  '$nodeName' is not constructed in Create" -ForegroundColor Red
            }
        }
    }
}

if ($KeepOutput) {
    Write-Host "`ngenerated projects kept at $OutputDirectory" -ForegroundColor DarkGray
}
elseif (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}

Write-Host ""
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "  $f" -ForegroundColor Red }
    Write-Host "`nFAIL - $($failures.Count) problem(s)." -ForegroundColor Red
    exit 1
}

Write-Host "PASS - $($patches.Count) patch(es) compiled, every VL.Mapsui node resolved, every process node built in Create, no NU1101." -ForegroundColor Green
Write-Host @"
Note: this proves the nodes RESOLVE. It does not prove which CATEGORY they appear under - only
the NodeBrowser does - nor that the patch computes the right value, which only running it does.
"@ -ForegroundColor Yellow

# Explicitly, because vvvvc's own exit code may still be sitting in $LASTEXITCODE.
exit 0
