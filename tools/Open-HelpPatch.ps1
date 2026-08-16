<#
.SYNOPSIS
    Opens a help patch in vvvv with every package repository it needs.

.DESCRIPTION
    There are THREE of them and forgetting one produces an error that reads like a broken
    package. Both failures happened within ten minutes on 2026-08-16, from typing the command by
    hand:

      dist\ only
          "The referenced symbol source 'Mapsui.dll' couldn't be found."
          Thrown by VL's symbol loader before any window appears. deps\ was missing.

      dist\ + deps\
          "Missing package: VL.NetTopologySuite", followed by "The reference is ambiguous: Point"
          with twenty-five candidates out of NetTopologySuite.dll. The ambiguity is a CONSEQUENCE:
          with NTS.Geometry gone, VL matches the bare .NET members instead and finds many.

    So the list lives in a script rather than in a comment, a README or anyone's memory. It is
    the same list `Compile-HelpPatches.ps1` builds, for the same reason.

      dist\                             VL.Mapsui itself     (.\pack.ps1 stages it)
      deps\                             Mapsui and friends   (build.ps1 installs them)
      <vl-nettopologysuite>\dist\       the geometry package the help patches create shapes with

    OPENING A DOCUMENT IN VVVV IS RUNNING IT. Read what you came for and close the window. Never
    leave it running unattended - that is how 17,000 TCP connections happened.

.EXAMPLE
    .\tools\Open-HelpPatch.ps1 "Draw many features"

.EXAMPLE
    .\tools\Open-HelpPatch.ps1 -List
#>
param(
    [Parameter(Position = 0)]
    [string]$Patch,

    [switch]$List,

    # Any .vl, help patch or not - the scratchpad probes take this route.
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Vvvv     = 'C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvv.exe'
$NtsRepo  = 'D:\2026_Projects\vl-nettopologysuite'

if (-not (Test-Path $Vvvv)) {
    Write-Host "vvvv not found at $Vvvv" -ForegroundColor Red
    exit 1
}

$helpDir = Join-Path $RepoRoot 'help'
$patches = @(Get-ChildItem $helpDir -Recurse -File -Filter *.vl | Sort-Object Name)

if ($List -or (-not $Patch -and -not $Path)) {
    Write-Host "`nhelp patches in $helpDir`n"
    $patches | ForEach-Object { Write-Host "  $($_.BaseName)" }
    Write-Host "`nusage: .\tools\Open-HelpPatch.ps1 ""Draw many features""`n"
    exit 0
}

if ($Path) {
    if (-not (Test-Path $Path)) { Write-Host "no such file: $Path" -ForegroundColor Red; exit 1 }
    $target = (Resolve-Path $Path).Path
} else {
    # Exactly one match or nothing. A launch that opens the wrong patch wastes a whole round,
    # and vvvv reports nothing about which document it was handed.
    $hits = @($patches | Where-Object { $_.BaseName -like "*$Patch*" })
    if ($hits.Count -eq 0) {
        Write-Host "no help patch matches '$Patch'. -List shows them." -ForegroundColor Red
        exit 1
    }
    if ($hits.Count -gt 1) {
        Write-Host "'$Patch' matches $($hits.Count) patches:" -ForegroundColor Red
        $hits | ForEach-Object { Write-Host "  $($_.BaseName)" }
        exit 1
    }
    $target = $hits[0].FullName
}

# The three folders, each checked - a repository that does not exist is silently ignored by vvvv,
# which is how a missing one looks like a missing package.
$wanted = @(
    @{ Name = 'dist (VL.Mapsui)';        Path = (Join-Path $RepoRoot 'dist') }
    @{ Name = 'deps (Mapsui, BruTile…)'; Path = (Join-Path $RepoRoot 'deps') }
    @{ Name = 'VL.NetTopologySuite';     Path = (Join-Path $NtsRepo 'dist') }
)

$missing = @($wanted | Where-Object { -not (Test-Path $_.Path) })
if ($missing) {
    Write-Host "`nmissing package repositories - vvvv would report this as a missing PACKAGE:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $($_.Name)  ->  $($_.Path)" -ForegroundColor Red }
    Write-Host "`n  dist\ and deps\ come from .\pack.ps1" -ForegroundColor Yellow
    Write-Host "  the third comes from packing $NtsRepo`n" -ForegroundColor Yellow
    exit 1
}

$repositories = ($wanted | ForEach-Object { $_.Path }) -join ';'

if (Get-Process vvvv -ErrorAction SilentlyContinue) {
    Write-Host "`nvvvv is ALREADY RUNNING. Close it first - two instances share one tile cache" -ForegroundColor Red
    Write-Host "and one set of ephemeral ports.`n" -ForegroundColor Red
    exit 1
}

Write-Host "`nopening $(Split-Path $target -Leaf)"
$wanted | ForEach-Object { Write-Host "  repo  $($_.Path)" }
Write-Host ""

Start-Process -FilePath $Vvvv -ArgumentList @("`"$target`"", '--package-repositories', "`"$repositories`"")

Write-Host "READ IT AND CLOSE IT. Opening a document in vvvv is running it." -ForegroundColor Yellow
Write-Host "  the overlay's first line goes red on a rebuild across two frames - close immediately if it does`n" -ForegroundColor Yellow
