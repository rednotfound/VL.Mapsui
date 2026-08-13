<#
.SYNOPSIS
    Generates a VL package entry-point document (.vl) in the format vvvv gamma expects.

.DESCRIPTION
    A VL package's entry-point .vl file is what makes the package's nodes appear in the
    vvvv NodeBrowser. It is normally produced by vvvv itself; this script reproduces the
    exact structure so it can be generated reproducibly from CI.

    The structure below was verified against shipped packages
    (VL.Stride.vl, VL.Serialization.MessagePack.vl, VL.Nuget.Template.vl) and
    round-tripped through vvvvc.exe.

    Three things must be right or vvvv silently fails to load the package:

      1. Every Id is exactly 22 chars, first char [A-V]  -> see New-VLId.ps1
      2. The file is UTF-8 *with* BOM                    -> plain UTF-8 fails silently
      3. The <Patch> block with the Application node is present

    The real payload is one line per forwarded DLL:

        <PlatformDependency Id="..." Location="./lib/net8.0/X.dll" IsForward="true" />

    IsForward="true" is what exposes every public static method in that assembly as a
    node to documents that reference this package.

.PARAMETER ForwardDll
    Paths to forward, relative to the .vl file, e.g. './lib/net8.0/VL.GIS.Core.dll'.
    Emitted as <PlatformDependency ... IsForward="true" />.

.PARAMETER ProjectReference
    Paths to .csproj files, relative to the .vl file. Emitted as <ProjectDependency />.
    Referencing a project instead of its built .dll gives you hot-reload: saving a .cs
    file recompiles and hotswaps the running code without restarting vvvv.

    Use this only in scratch/dev documents. A shipped package must never reference a
    .csproj -- it would force the package and everything depending on it to stay
    editable, losing the read-only startup/memory benefit.

.PARAMETER NugetDependency
    Ordered dictionary / hashtable of packageId -> version.

.EXAMPLE
    .\tools\New-VLDocument.ps1 -OutFile .\VL.GIS.vl -DefaultCategory GIS `
        -NugetDependency ([ordered]@{ 'VL.CoreLib' = '2025.7.0' }) `
        -ForwardDll './lib/net8.0/VL.GIS.Core.dll'
#>
param(
    [Parameter(Mandatory)][string]$OutFile,
    [Parameter(Mandatory)][string]$DefaultCategory,
    [System.Collections.IDictionary]$NugetDependency = @{},
    [string[]]$ForwardDll = @(),
    [string[]]$ProjectReference = @(),

    # "Last saved with" marker. vvvv silently upgrades older documents, so an older
    # real build string is safer than inventing a current one.
    [string]$LanguageVersion = '2024.6.7-0009-ga0a8422da0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$newId = Join-Path $PSScriptRoot 'New-VLId.ps1'
$needed = 11 + $NugetDependency.Count + $ForwardDll.Count + $ProjectReference.Count
$ids = @(& $newId -Count $needed)

if (($ids | Sort-Object -Unique).Count -ne $needed) {
    throw "ID generator produced a collision; re-run."
}

$cursor = 0
function Next-Id { $script:cursor++; return $script:ids[$script:cursor - 1] }

$docId     = Next-Id
$patchId   = Next-Id
$canvasId  = Next-Id
$appNodeId = Next-Id
$appPatch  = Next-Id
$groupCv   = Next-Id
$createId  = Next-Id
$updateId  = Next-Id
$procDefId = Next-Id
$frag1Id   = Next-Id
$frag2Id   = Next-Id

$nugetLines = foreach ($key in $NugetDependency.Keys) {
    "  <NugetDependency Id=`"$(Next-Id)`" Location=`"$key`" Version=`"$($NugetDependency[$key])`" />"
}

$forwardLines = @(
    foreach ($dll in $ForwardDll) {
        "  <PlatformDependency Id=`"$(Next-Id)`" Location=`"$dll`" IsForward=`"true`" />"
    }
    foreach ($proj in $ProjectReference) {
        "  <ProjectDependency Id=`"$(Next-Id)`" Location=`"$proj`" />"
    }
)

$lines = @(
    '<?xml version="1.0" encoding="utf-8"?>'
    "<Document xmlns:p=`"property`" xmlns:r=`"reflection`" Id=`"$docId`" LanguageVersion=`"$LanguageVersion`" Version=`"0.128`">"
) + $nugetLines + @(
    "  <Patch Id=`"$patchId`">"
    "    <Canvas Id=`"$canvasId`" DefaultCategory=`"$DefaultCategory`" CanvasType=`"FullCategory`" />"
    '    <!--'
    ''
    '    ************************ Application ************************'
    ''
    '-->'
    "    <Node Name=`"Application`" Bounds=`"100,100`" Id=`"$appNodeId`">"
    '      <p:NodeReference>'
    '        <Choice Kind="ContainerDefinition" Name="Process" />'
    '        <FullNameCategoryReference ID="Primitive" />'
    '      </p:NodeReference>'
    "      <Patch Id=`"$appPatch`">"
    "        <Canvas Id=`"$groupCv`" CanvasType=`"Group`" />"
    "        <Patch Id=`"$createId`" Name=`"Create`" />"
    "        <Patch Id=`"$updateId`" Name=`"Update`" />"
    "        <ProcessDefinition Id=`"$procDefId`">"
    "          <Fragment Id=`"$frag1Id`" Patch=`"$createId`" Enabled=`"true`" />"
    "          <Fragment Id=`"$frag2Id`" Patch=`"$updateId`" Enabled=`"true`" />"
    '        </ProcessDefinition>'
    '      </Patch>'
    '    </Node>'
    '  </Patch>'
) + $forwardLines + @(
    '</Document>'
)

$dir = Split-Path $OutFile -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

# UTF-8 WITH BOM + CRLF, matching what vvvv writes.
[System.IO.File]::WriteAllText($OutFile, (($lines -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($true)))

Write-Host "Wrote $OutFile  ($((Get-Item $OutFile).Length) bytes, UTF-8 BOM, $needed ids)"
