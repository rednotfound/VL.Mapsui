<#
.SYNOPSIS
    Compiles every help patch headlessly with vvvvc, against the packages this repo just built.

.DESCRIPTION
    A hand-written .vl claims things about the C#: that a node exists, that its pins are named what
    the XML says. Only a compile checks the claim, and this repository has paid for every one of
    those claims it took on trust -- `Url Template` for `URL Template`, `Output` for `Result`,
    a node silently dropped because its type was never imported.

    TWO THINGS HAVE TO LINE UP, and discovering that cost three rounds on 2026-08-14:

      1. `--package-repositories` -- how vvvv finds a *package folder*, i.e. dist\VL.Mapsui\.
         Point it at dist\, not dist\feed\, or vvvvc says "Missing package: VL.Mapsui".

      2. a NuGet source carrying the *nupkg* -- because vvvvc then generates a .csproj with a
         PackageReference and runs a normal restore. Point that at dist\feed\, or restore says
         "NU1101: package VL.Mapsui not found" while listing nuget.org and friends.

    It used to appear to work with only (1), and that is the part worth remembering: restore was
    quietly satisfied by a STALE VL.Mapsui sitting in %USERPROFILE%\.nuget\packages. build.ps1 now
    evicts that on purpose -- it once made vvvvc insist a node did not exist hours after it was
    written -- so the feed has to be supplied honestly. A NuGet.config is dropped at the output
    root, where restore finds it by walking up from the generated project.

    Requires .\pack.ps1 to have run: dist\feed\*.nupkg is what (2) reads.

.PARAMETER OutputDirectory
    Where the generated C# goes. Defaults to a temp folder. Keep it if you want to read the C# --
    an exit code of 0 says the document parsed, NOT that the nodes in it resolved, so reading the
    generated source is the actual check.

.PARAMETER Patch
    Compile only patches whose name matches this wildcard. Default: all of them.

.EXAMPLE
    .\pack.ps1 ; .\tools\Compile-HelpPatches.ps1
.EXAMPLE
    .\tools\Compile-HelpPatches.ps1 -Patch "*Label*" -OutputDirectory D:\tmp\compile
#>
param(
    [string]$OutputDirectory,
    [string]$Patch = '*'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Vvvvc    = 'C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvvc.exe'

# The sibling package that MAKES geometry. Help patches here consume it; this one draws.
$NtsRepo  = 'D:\2026_Projects\vl-nettopologysuite'


if (-not (Test-Path $Vvvvc)) {
    Write-Host "vvvvc not found at $Vvvvc" -ForegroundColor Red
    exit 1
}

$feed = Join-Path $RepoRoot 'dist\feed'
if (-not (Test-Path $feed) -or -not (Get-ChildItem $feed -Filter *.nupkg -ErrorAction SilentlyContinue)) {
    Write-Host "no nupkg in dist\feed - run .\pack.ps1 first" -ForegroundColor Red
    exit 1
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) "vl-mapsui-compile-$PID"
}
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null

# (2): restore walks up from the generated .csproj and finds this.
$sources = @("    <add key=""mapsui-dist"" value=""$feed"" />")
$ntsFeed = Join-Path $NtsRepo 'dist\feed'
if (Test-Path $ntsFeed) { $sources += "    <add key=""nts-dist"" value=""$ntsFeed"" />" }


@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
$($sources -join "`n")
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $OutputDirectory 'NuGet.config') -Encoding utf8

# (1): package *folders*, not the feed.
$repositories = @((Join-Path $RepoRoot 'dist'), (Join-Path $RepoRoot 'deps'))
foreach ($sibling in @($NtsRepo)) {
    foreach ($sub in @('dist', 'deps')) {
        $candidate = Join-Path $sibling $sub
        if (Test-Path $candidate) { $repositories += $candidate }
    }
}
$repositories = ($repositories | Where-Object { Test-Path $_ }) -join ';'

# help\ and nothing else, because help\ IS the packaged surface and that is what a release has to
# be true of. A patch that needs a sibling package is not allowed here at all - Test-VLPackage.ps1
# refuses it - so a green run here means "the package is fine" rather than "fine on my machine,
# where three sibling checkouts happen to exist". VL.Cartography compiles the cross-package ones.
$roots = @(Join-Path $RepoRoot 'help')

$patches = @($roots | ForEach-Object { Get-ChildItem $_ -Recurse -File -Filter *.vl } |
             Where-Object { $_.BaseName -like $Patch })
if ($patches.Count -eq 0) {
    Write-Host "no help patch matches '$Patch'" -ForegroundColor Red
    exit 1
}

Write-Host "compiling $($patches.Count) patch(es) -> $OutputDirectory`n"

$failed = @()

foreach ($p in $patches) {
    $dir = Join-Path $OutputDirectory ($p.BaseName -replace '[^\w]', '_')
    $log = & $Vvvvc $p.FullName --output-directory $dir --package-repositories $repositories 2>&1
    $code = $LASTEXITCODE

    if ($code -eq 0) {
        Write-Host ("  ok    {0}" -f $p.Name)
        continue
    }

    $failed += $p.Name
    Write-Host ("  FAIL  {0}  (exit {1})" -f $p.Name, $code) -ForegroundColor Red
    $log | Select-String -Pattern 'error|Not found|ambiguous|Missing' -CaseSensitive:$false |
        Select-Object -First 4 |
        ForEach-Object { Write-Host "          $(($_ -replace '\s+', ' ').Trim())" -ForegroundColor Red }
}

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host "FAIL - $($failed.Count) patch(es) did not compile: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "PASS - every help patch compiles." -ForegroundColor Green
Write-Host "  exit 0 means the document parsed. Read the generated C# in $OutputDirectory to see"
Write-Host "  that the nodes RESOLVED - an unimported type is dropped in silence."
exit 0
