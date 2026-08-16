<#
.SYNOPSIS
    Installs the packed package the way a user would, and compiles the help patches that came
    out of it. Verifies the one thing dev-loop testing cannot.

.DESCRIPTION
    Everything else in this repository tests the package as it sits in the working tree:
    `--package-repositories dist;deps;<sibling>\dist` hands vvvv every folder we happen to
    have. A user has none of that. They install one package and expect its dependencies to
    arrive with it, and its help patches to open without red nodes.

    Between those two situations sits an assumption that was reasoned about for weeks and
    never measured: **a `<dependency>` in the nuspec means the dependency is installed too.**
    Specifically `VL.NetTopologySuite`, which the help patches need because they create
    geometry, and which nothing in the working-tree setup proves is reachable.

    So this script:

      1. resolves VL.Mapsui from dist\feed (plus the sibling feed and nuget.org) into a
         throwaway folder, with NuGet doing the dependency graph -- the same resolution a
         user's install performs;
      2. reports whether each expected dependency actually landed;
      3. compiles every help patch **from inside the installed package**, with that folder as
         the only package repository.

    A red node in vvvv is an unresolved node, and an unresolved node fails the compile. So a
    clean run here is the strongest evidence available short of publishing -- and publishing
    is irreversible, which is why this exists.

    Nothing is installed into vvvv's own `%LOCALAPPDATA%\vvvv\gamma\nugets\`: that folder is
    shared by everything vvvv loads and outlived an uninstall by five months once already.

.PARAMETER OutputDirectory
    Where to install. Defaults to a temp folder. Keep it to inspect what landed.

.PARAMETER Version
    Which version to install. Defaults to the version in the nuspec.

.EXAMPLE
    .\pack.ps1 ; .\tools\Test-Install.ps1
#>
param(
    [string]$OutputDirectory,
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Vvvv     = 'C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64'
$NuGet    = Join-Path $Vvvv 'tools\NuGet.exe'
$Vvvvc    = Join-Path $Vvvv 'vvvvc.exe'
$SiblingFeed = 'D:\2026_Projects\vl-nettopologysuite\dist\feed'

foreach ($tool in @($NuGet, $Vvvvc)) {
    if (-not (Test-Path $tool)) { Write-Host "not found: $tool" -ForegroundColor Red; exit 1 }
}

$feed = Join-Path $RepoRoot 'dist\feed'
if (-not (Get-ChildItem $feed -Filter *.nupkg -ErrorAction SilentlyContinue)) {
    Write-Host "no nupkg in dist\feed - run .\pack.ps1 first" -ForegroundColor Red
    exit 1
}

$nuspec = Get-ChildItem $RepoRoot -Filter *.nuspec -File | Select-Object -First 1
[xml]$nuspecXml = Get-Content $nuspec.FullName -Raw
$packageId = $nuspecXml.package.metadata.id
if (-not $Version) { $Version = $nuspecXml.package.metadata.version }

# What the nuspec promises will come along. Checked by name, because that is the promise.
# XPath rather than property access: dependencies may be wrapped in <group> elements, and
# property access silently walks only one level.
$ns = New-Object Xml.XmlNamespaceManager($nuspecXml.NameTable)
$ns.AddNamespace('n', $nuspecXml.DocumentElement.NamespaceURI)
$expected = @($nuspecXml.SelectNodes('//n:dependency', $ns) | ForEach-Object { $_.id })
if ($expected.Count -eq 0) {
    Write-Host "the nuspec declares no dependencies - nothing to verify" -ForegroundColor DarkYellow
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) "vl-mapsui-install-$PID"
}
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null

Write-Host "installing $packageId $Version -> $OutputDirectory`n"

$sources = @($feed)
if (Test-Path $SiblingFeed) { $sources += $SiblingFeed }
$sources += 'https://api.nuget.org/v3/index.json'

$log = & $NuGet install $packageId -Version $Version -PreRelease `
    -Source ($sources -join ';') -OutputDirectory $OutputDirectory -NonInteractive 2>&1

if ($LASTEXITCODE -ne 0) {
    $log | Select-Object -Last 15 | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    Write-Host "`nFAIL - the package does not even install." -ForegroundColor Red
    exit 1
}

# ---- 1. did the promised dependencies arrive? ------------------------------------------------
$landed = @(Get-ChildItem $OutputDirectory -Directory | ForEach-Object { $_.Name })
$missing = @()

foreach ($dependency in $expected) {
    if ($landed | Where-Object { $_ -like "$dependency.*" }) {
        Write-Host ("  ok    {0} came along" -f $dependency) -ForegroundColor DarkGray
    }
    else {
        $missing += $dependency
        Write-Host ("  FAIL  {0} did NOT install - a patch needing it opens with red nodes" -f $dependency) -ForegroundColor Red
    }
}
Write-Host ("        ({0} packages in total)`n" -f $landed.Count)

# ---- 2. compile the help patches that shipped inside the package -----------------------------
$installed = Get-ChildItem $OutputDirectory -Directory | Where-Object { $_.Name -like "$packageId.*" } | Select-Object -First 1
$helpPatches = @(Get-ChildItem (Join-Path $installed.FullName 'help') -Filter *.vl -ErrorAction SilentlyContinue)

if ($helpPatches.Count -eq 0) {
    Write-Host "FAIL - the installed package contains no help patches." -ForegroundColor Red
    exit 1
}

# vvvvc needs the package FOLDER repository and, separately, a NuGet source for restore -
# see tools\Compile-HelpPatches.ps1 for why both are required.
$compileRoot = Join-Path $OutputDirectory '_compile'
New-Item -ItemType Directory $compileRoot -Force | Out-Null
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="feed" value="$feed" />
$(if (Test-Path $SiblingFeed) { "    <add key=`"sibling`" value=`"$SiblingFeed`" />" })
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $compileRoot 'NuGet.config') -Encoding utf8

Write-Host "compiling $($helpPatches.Count) help patch(es) from the INSTALLED package`n"

$failed = @()
foreach ($patch in $helpPatches) {
    $dir = Join-Path $compileRoot ($patch.BaseName -replace '[^\w]', '_')
    $out = & $Vvvvc $patch.FullName --output-directory $dir --package-repositories $OutputDirectory 2>&1

    if ($LASTEXITCODE -eq 0) { Write-Host ("  ok    {0}" -f $patch.Name) -ForegroundColor DarkGray; continue }

    $failed += $patch.Name
    Write-Host ("  FAIL  {0}" -f $patch.Name) -ForegroundColor Red
    $out | Select-String -Pattern 'error|Not found|Missing|ambiguous' -CaseSensitive:$false |
        Select-Object -First 3 | ForEach-Object { Write-Host "          $(($_ -replace '\s+', ' ').Trim())" -ForegroundColor Red }
}

Write-Host ''
if ($missing.Count -gt 0 -or $failed.Count -gt 0) {
    if ($missing.Count) { Write-Host "FAIL - missing dependencies: $($missing -join ', ')" -ForegroundColor Red }
    if ($failed.Count)  { Write-Host "FAIL - $($failed.Count) installed help patch(es) do not compile: $($failed -join ', ')" -ForegroundColor Red }
    exit 1
}

Write-Host "PASS - the package installs with its dependencies and every shipped help patch compiles from it." -ForegroundColor Green
Write-Host "  This is resolution and node existence. It does not prove the patches RUN - that still needs a"
Write-Host "  GUI round - and it does not prove nuget.org behaves like a local feed."
exit 0
