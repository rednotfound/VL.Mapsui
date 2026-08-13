<#
.SYNOPSIS
    Pins the VL.GIS dependency of every help patch to 0.0.0.

.DESCRIPTION
    A help patch ships *inside* the package it demonstrates, and still has to declare a
    dependency on it. vvvv writes whichever version it resolved while the patch was being
    authored -- 0.1.0-alpha, read from dist\VL.GIS\VL.GIS.nuspec -- and the patch would
    then keep asking for that exact version long after the package has moved on.

    Every pack that ships with vvvv avoids this by pinning the lowest possible version:

        VL.IO.ArtNet  ->  <NugetDependency Location="VL.IO.ArtNet" Version="0.0.0.0" />
        VL.IO.TUIO    ->  <NugetDependency Location="VL.IO.TUIO"   Version="0.0.0.0" />
        VL.IO.Pipes   ->  <NugetDependency Location="VL.IO.Pipes"  Version="0.0.0" />

    VL.GIS's own 0.0.x help patches did the opposite: they pinned 0.0.10 while the package
    was 0.0.11.

    This has to run against the repo copy. Each nuspec packs help\<PackageName>\**\*.vl directly, so
    dist\ plays no part at release time and normalising during staging would never reach
    a published package.

    Idempotent, and it only ever touches the Version attribute of the VL.GIS dependency --
    VL.CoreLib and everything else are left alone. Writes UTF-8 *with* BOM, without which
    vvvv will not load a .vl at all (see docs\VL-PACKAGING.md).

    Run automatically by build.ps1. Test-VLPackage.ps1 asserts the result, so forgetting
    to run it fails the build rather than shipping quietly.

.PARAMETER Check
    Report what would change and exit non-zero if anything would, without writing.

.EXAMPLE
    .\tools\Normalize-HelpPatches.ps1
.EXAMPLE
    .\tools\Normalize-HelpPatches.ps1 -Check
#>
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$HelpDir  = Join-Path $RepoRoot 'help'

# The sentinel every shipped pack uses. Lowest possible version, so it is satisfied by
# whichever build of the package the patch happens to be sitting inside.
$Pinned  = '0.0.0'
# Every package in this repository, not just VL.GIS -- a help patch may reference several,
# and vvvv rewrites the version of each on save. Discovered the same way build.ps1 does.
$LocalPackages = @(
    Get-ChildItem $RepoRoot -Filter '*.vl' -File |
        Where-Object { Test-Path (Join-Path $RepoRoot "$($_.BaseName).nuspec") } |
        ForEach-Object { $_.BaseName }
)
# Three groups so the current version can be reported, not just replaced.
$Pattern = '(<NugetDependency\b[^>]*\bLocation="(?:' +
           (($LocalPackages | ForEach-Object { [regex]::Escape($_) }) -join '|') +
           ')"[^>]*\bVersion=")([^"]*)(")'

if (-not (Test-Path $HelpDir)) {
    Write-Host "no help\ directory - nothing to normalise"
    exit 0
}

$patches = @(Get-ChildItem $HelpDir -File -Recurse -Filter *.vl)
if ($patches.Count -eq 0) {
    Write-Host "no help patches - nothing to normalise"
    exit 0
}

$stale = @()

foreach ($patch in $patches) {
    $relative = $patch.FullName.Substring($RepoRoot.Length + 1)
    $text     = [IO.File]::ReadAllText($patch.FullName)

    if ($text -notmatch $Pattern) {
        # Not every help patch has to use nodes from this repository, but one that does not
        # is much more likely to have lost its dependency than to be deliberate.
        Write-Warning "$relative declares no dependency on any package in this repository"
        continue
    }

    $rewritten = $text -replace $Pattern, "`${1}$Pinned`${3}"
    if ($rewritten -eq $text) { continue }

    # A patch may reference several of our packages; report every version being replaced.
    $was = @(([regex]$Pattern).Matches($text) |
        Where-Object { $_.Groups[2].Value -ne $Pinned } |
        ForEach-Object { $_.Groups[2].Value }) -join ', '
    $stale += $relative

    if ($Check) {
        Write-Host "  would pin  $relative  ($was -> $Pinned)"
    }
    else {
        [IO.File]::WriteAllText($patch.FullName, $rewritten, (New-Object System.Text.UTF8Encoding($true)))
        Write-Host "   help\$($patch.Directory.Name)\$($patch.Name): pinned $was -> $Pinned"
    }
}

if ($Check -and $stale.Count -gt 0) {
    Write-Host "FAIL - $($stale.Count) help patch(es) pin a concrete VL.GIS version. Run tools\Normalize-HelpPatches.ps1" -ForegroundColor Red
    exit 1
}

exit 0
