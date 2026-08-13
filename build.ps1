<#
.SYNOPSIS
    Builds VL.Mapsui and stages it under dist\.

.DESCRIPTION
    A package is a .vl at the repo root with a .nuspec of the same name beside it. They are
    discovered rather than listed, so adding a second one needs no edit here. Ported from
    vvvv-gis, which is built the same way.

        dist\VL.Mapsui\
          VL.Mapsui.vl           <- entry point (nodes appear in the NodeBrowser)
          VL.Mapsui.nuspec       <- required for vvvv to recognise a source package
          lib\net8.0\*.dll|.xml
          help\**

    That is the same shape a published package has once installed under
    %LOCALAPPDATA%\vvvv\gamma\nugets\<id>.<version>\, so "works locally but not once
    published" cannot happen.

    dist\ is the package *repository*; each folder inside it is a package. Point vvvv at the
    repository, not at a package:

        vvvv.exe --package-repositories D:\2026_Projects\vl-mapsui\dist

    Which DLLs get staged is driven by the .vl's <PlatformDependency> entries, so dist\ always
    contains exactly what the document declares.

    The entry point being a real package rather than a ProjectDependency is also what lets VL
    build nodes whose signatures mention Mapsui types: VL learns a foreign library's types from
    the <NugetDependency> lines in the .vl, and without them such a node is silently never
    created. That is why this script exists before the interesting nodes do.
#>
param(
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$Dist     = Join-Path $RepoRoot 'dist'
$Deps     = Join-Path $RepoRoot 'deps'   # upstream packages, kept apart from ours - see step 3

$Packages = @(
    Get-ChildItem $RepoRoot -Filter '*.vl' -File |
        Where-Object { Test-Path (Join-Path $RepoRoot "$($_.BaseName).nuspec") } |
        Sort-Object BaseName |
        ForEach-Object {
            [pscustomobject]@{
                Name   = $_.BaseName
                VlFile = $_.FullName
                Nuspec = Join-Path $RepoRoot "$($_.BaseName).nuspec"
                PkgDir = Join-Path $Dist $_.BaseName
            }
        }
)
if ($Packages.Count -eq 0) { throw "No package found: expected a .vl with a matching .nuspec at $RepoRoot" }

# A running vvvv holds the staged assemblies open, so restaging fails with a confusing "used by
# another process". Say so plainly. It also would not pick up the change: vvvv keeps whatever it
# loaded at startup.
$running = @(Get-Process 'vvvv' -ErrorAction SilentlyContinue)
if ($running) {
    throw @"
vvvv is running (PID $($running.Id -join ', ')) and is holding the staged assemblies open.

Close vvvv, then run .\build.ps1 again. Remember that in vvvv, having it open means having
the patch running - there is no idle state.
"@
}

Write-Host "== 1/5 build ==" -ForegroundColor Cyan
dotnet build (Join-Path $RepoRoot 'src\VL.Mapsui\VL.Mapsui.csproj') -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host "`n== 2/5 stage dist\ ==" -ForegroundColor Cyan
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }

foreach ($pkg in $Packages) {
    Write-Host "`n   $($pkg.Name)" -ForegroundColor White
    New-Item -ItemType Directory -Force -Path $pkg.PkgDir | Out-Null

    Copy-Item $pkg.VlFile -Destination $pkg.PkgDir
    Copy-Item $pkg.Nuspec -Destination $pkg.PkgDir

    [xml]$vl = Get-Content $pkg.VlFile -Raw
    $forwards = @($vl.Document.PlatformDependency | Where-Object { $_.Location -like './lib/*' })
    if ($forwards.Count -eq 0) { throw "$($pkg.Name).vl declares no ./lib/... PlatformDependency" }

    foreach ($fwd in $forwards) {
        $rel       = $fwd.Location -replace '^\./', ''
        $asmName   = [IO.Path]::GetFileNameWithoutExtension($rel)
        $targetDir = Join-Path $pkg.PkgDir (Split-Path $rel -Parent)
        $sourceDir = Join-Path $RepoRoot "src\$asmName\bin\$Configuration\net8.0"

        if (-not (Test-Path $sourceDir)) { throw "Build output not found: $sourceDir" }
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

        # Only our own assembly. Mapsui, BruTile and the rest are declared as NugetDependency
        # in the .vl and resolved by vvvv, exactly as VL.GIS declares BruTile rather than
        # shipping it. CopyLocalLockFileAssemblies puts them in bin\ for the test host's
        # benefit, which is why this copies by name instead of copying the folder.
        foreach ($ext in 'dll', 'xml') {
            $src = Join-Path $sourceDir "$asmName.$ext"
            if (Test-Path $src) { Copy-Item $src -Destination $targetDir }
            elseif ($ext -eq 'dll') { throw "Missing $src" }
        }
        Write-Host "      forwards $asmName"
    }

    # Help patches live in help\<PackageName>\ and are staged as help\, which is where vvvv
    # looks. The nuspec decides whether a package ships any; this only has to agree with it.
    [xml]$nuspec = Get-Content $pkg.Nuspec -Raw
    $shipsHelp = @($nuspec.package.files.file | Where-Object { $_.src -like 'help\*' }).Count -gt 0
    $helpSrc = Join-Path $RepoRoot "help\$($pkg.Name)"
    if ($shipsHelp -and (Test-Path $helpSrc) -and (Get-ChildItem $helpSrc -File -Recurse -ErrorAction SilentlyContinue)) {
        Copy-Item $helpSrc -Destination (Join-Path $pkg.PkgDir 'help') -Recurse
        Write-Host "      help\ (from help\$($pkg.Name))"
    }
}

Write-Host "`n== 3/5 upstream packages ==" -ForegroundColor Cyan
#
# The upstream libraries have to sit in the package repository as packages, not merely be
# restorable as assemblies. Without that, VL cannot resolve their types, and the failure is the
# quiet kind: a node whose signature mentions Mapsui.Map is built but none of its links attach,
# so it vanishes from the compiled program and the renderer silently receives a default.
#
# A real install gets this for free - NuGet pulls VL.Mapsui's dependencies into
# %LOCALAPPDATA%\vvvv\gamma\nugets\ alongside it, which is why Rhino3dm, AssimpNet, OpenCvSharp
# and BruTile are all sitting there. This reproduces that locally so dist\ matches what a user
# would actually have.
#
# Discovered from each .vl rather than listed: anything not starting with VL. is an upstream
# library, since the VL.* ones ship inside vvvv.
$NuGetExe = & (Join-Path $RepoRoot 'tools\Find-Vvvv.ps1') -NuGet
foreach ($pkg in $Packages) {
    [xml]$vl = Get-Content $pkg.VlFile -Raw
    foreach ($dep in @($vl.Document.NugetDependency | Where-Object { $_.Location -notlike 'VL.*' })) {
        $folder = Join-Path $Deps "$($dep.Location).$($dep.Version)"
        if (Test-Path $folder) { Write-Host "      $($dep.Location) $($dep.Version) (already there)"; continue }

        # Transitive dependencies come too, because a real install gets them: Mapsui.Rendering
        # .Skia needs Mapsui.Nts, and leaving it out produced a FileNotFoundException the moment
        # a MapRenderer was constructed - at runtime, from inside a frame, long after everything
        # static had passed.
        #
        # This also pulls copies of things vvvv already ships, such as SkiaSharp. That is what a
        # real install does as well, and vvvv says so in its log: "wasn't picked up because it's
        # provided by vvvv itself".
        & $NuGetExe install $dep.Location -Version $dep.Version -OutputDirectory $Deps `
            -Source 'https://api.nuget.org/v3/index.json' -NonInteractive | Out-Null
        if (-not (Test-Path $folder)) { throw "Could not install $($dep.Location) $($dep.Version) into $Deps" }
        Write-Host "      $($dep.Location) $($dep.Version)"
    }
}

Write-Host "`n== 4/5 staged ==" -ForegroundColor Cyan
foreach ($pkg in $Packages) {
    Get-ChildItem $pkg.PkgDir -Recurse -File |
        ForEach-Object { "   " + $_.FullName.Replace("$Dist\", '') + "  [$($_.Length) B]" }
}

Write-Host "`n== 5/5 done ==" -ForegroundColor Green

Write-Host @"

Next:
  .\tools\Test-VLPackage.ps1            static checks, no vvvv needed
  vvvv.exe <patch> --package-repositories $Dist
"@ -ForegroundColor Yellow
