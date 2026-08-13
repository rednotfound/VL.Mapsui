<#
.SYNOPSIS
    Locates the newest installed vvvv gamma and returns the path to vvvv.exe or vvvvc.exe.

.DESCRIPTION
    vvvv gamma installs side-by-side: every version gets its own folder under
    C:\Program Files\vvvv\ (vvvv_gamma_7.0-win-x64, vvvv_gamma_7.4-win-x64, ...).
    Picking "the newest" by sorting folder names as strings breaks as soon as a 7.10
    exists ("7.4" sorts above "7.10"), so parse the version out and compare numerically.

    Folder names seen in the wild:
        vvvv_gamma_7.4-win-x64
        vvvv_gamma_7.0-win-x64
        vvvv_gamma_6.5
        vvvv_gamma_5.3-0222-gc9b9f1b9c9
        vvvv_gamma_2021.4.12

    2021.x is the old .NET Framework line and is deliberately excluded -- VL.GIS targets
    net8.0, which needs gamma 6.0 or newer.

.EXAMPLE
    $vvvv = & .\tools\Find-Vvvv.ps1
.EXAMPLE
    $vvvvc = & .\tools\Find-Vvvv.ps1 -Compiler
.EXAMPLE
    $nuget = & .\tools\Find-Vvvv.ps1 -NuGet
#>
[CmdletBinding(DefaultParameterSetName = 'Editor')]
param(
    # Return vvvvc.exe (the commandline compiler) instead of vvvv.exe.
    [Parameter(ParameterSetName = 'Compiler')]
    [switch]$Compiler,

    # Return the NuGet.exe that ships with vvvv, so packing needs no separate install.
    [Parameter(ParameterSetName = 'NuGet')]
    [switch]$NuGet,

    # Lowest acceptable gamma version. 7.2 is when [Name] / [SkipCategory] arrived, which
    # VL.GIS.Core uses to place its nodes; 6.0 was the first net8.0 release, so anything
    # between the two builds but produces wrong node categories.
    [version]$MinimumVersion = '7.2',

    [string]$SearchRoot = 'C:\Program Files\vvvv'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$relativeExe =
    if     ($Compiler) { 'vvvvc.exe' }
    elseif ($NuGet)    { 'tools\NuGet.exe' }
    else               { 'vvvv.exe' }

if (-not (Test-Path $SearchRoot)) {
    throw "vvvv install root not found: $SearchRoot"
}

$candidates = Get-ChildItem $SearchRoot -Directory -ErrorAction SilentlyContinue |
    ForEach-Object {
        # vvvv_gamma_7.4-win-x64 -> 7.4 ; vvvv_gamma_5.3-0222-g... -> 5.3
        if ($_.Name -match '^vvvv_gamma_(\d+)\.(\d+)') {
            $exe = Join-Path $_.FullName $relativeExe
            if (Test-Path $exe) {
                [pscustomobject]@{
                    Version = [version]"$($Matches[1]).$($Matches[2])"
                    Path    = $exe
                    Folder  = $_.Name
                }
            }
        }
    } |
    Where-Object { $_.Version -ge $MinimumVersion } |
    Sort-Object Version -Descending

if (-not $candidates) {
    throw "No vvvv gamma >= $MinimumVersion with $relativeExe found under $SearchRoot"
}

$candidates[0].Path
