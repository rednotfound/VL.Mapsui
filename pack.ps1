<#
.SYNOPSIS
    Builds every package in this repository and packs each into a .nupkg under dist\feed.

.DESCRIPTION
    dist\feed is a local NuGet feed. Nothing is published; it exists so the package can
    be consumed exactly the way a real installed package would be:

        vvvvc SomeDoc.vl --export-package-sources <repo>\dist\feed

    That is what tools\Test-Install.ps1 does, and it is the closest automated
    equivalent to a user installing the package from nuget.org.

    Uses the NuGet.exe that ships with vvvv, so nothing extra needs installing.

    Note: `nuget pack` reads <version> from the nuspec. The publish workflow overrides it
    with -Version from the git tag, so the tag is the source of truth at release time.

.EXAMPLE
    .\pack.ps1
#>
param(
    [string]$Configuration = 'Release',
    [string]$NuGetPath = '',
    # Skip the build step when dist\ is already current.
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$FeedDir  = Join-Path $RepoRoot 'dist\feed'

# Same discovery rule as build.ps1 and Test-VLPackage.ps1: a .vl at the root with a .nuspec
# of the same name beside it.
$Packages = @(
    Get-ChildItem $RepoRoot -Filter '*.vl' -File |
        Where-Object { Test-Path (Join-Path $RepoRoot "$($_.BaseName).nuspec") } |
        Sort-Object BaseName
)
if ($Packages.Count -eq 0) { throw "No package found at $RepoRoot" }

if (-not $NoBuild) {
    & (Join-Path $RepoRoot 'build.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed" }
}

if (-not $NuGetPath) {
    $NuGetPath = & (Join-Path $RepoRoot 'tools\Find-Vvvv.ps1') -NuGet
}
if (-not (Test-Path $NuGetPath)) {
    throw "NuGet.exe not found at '$NuGetPath'. Pass -NuGetPath explicitly."
}

New-Item -ItemType Directory -Force -Path $FeedDir | Out-Null
Get-ChildItem $FeedDir -Filter '*.nupkg' -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "`n== pack ==" -ForegroundColor Cyan
Write-Host "nuget : $NuGetPath"

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($package in $Packages) {
    $pkgName = $package.BaseName
    $Nuspec  = Join-Path $RepoRoot "$pkgName.nuspec"

    & $NuGetPath pack $Nuspec -OutputDirectory $FeedDir -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "nuget pack failed for $pkgName ($LASTEXITCODE)" }

    $nupkg = Get-ChildItem $FeedDir -Filter "$pkgName.*.nupkg" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $nupkg) { throw "nuget pack produced no .nupkg for $pkgName" }

    # A package missing its .vl at the root, or missing a forwarded assembly, installs fine
    # and then silently contributes no nodes -- so assert the layout here.
    $zip = [IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
    try   { $entries = $zip.Entries | ForEach-Object { $_.FullName } }
    finally { $zip.Dispose() }

    [xml]$vl = Get-Content $package.FullName -Raw
    $required = @("$pkgName.vl") + @(
        $vl.Document.PlatformDependency |
            Where-Object { $_.Location -like './lib/*' } |
            ForEach-Object { $_.Location -replace '^\./', '' }
    )

    $missing = $required | Where-Object { $_ -notin $entries }
    if ($missing) {
        throw "$pkgName is missing required entries: $($missing -join ', ')"
    }

    # NuGet treats a version as immutable and will happily reuse an already-extracted copy
    # from the global cache, so repacking 0.2.0 with different contents is invisible to any
    # consumer that resolved it earlier. Evict our own entry; without this, Test-Install.ps1's
    # consumer test silently validates a stale package.
    $meta    = ([xml](Get-Content $Nuspec -Raw)).package.metadata
    $cached  = Join-Path $env:USERPROFILE ".nuget\packages\$($meta.id.ToLowerInvariant())\$($meta.version)"
    if (Test-Path $cached) {
        Remove-Item $cached -Recurse -Force
        Write-Host "   evicted stale $($meta.id) $($meta.version) from the global NuGet cache"
    }
}

Write-Host "`n== done ==" -ForegroundColor Green
foreach ($nupkg in Get-ChildItem $FeedDir -Filter '*.nupkg' | Sort-Object Name) {
    Write-Host "   $($nupkg.Name)  [$($nupkg.Length) B]"
    $zip = [IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
    try {
        $zip.Entries |
            ForEach-Object { $_.FullName } |
            Where-Object { $_ -notmatch '^_rels|^package/|Content_Types' } |
            ForEach-Object { "      $_" }
    }
    finally { $zip.Dispose() }
}

Write-Host @"

Next:
  .\tools\Test-Install.ps1               install it for real and compile its help patches
"@ -ForegroundColor Yellow
