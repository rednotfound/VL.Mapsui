<#
.SYNOPSIS
    Checks the packages installed in vvvv's user package folder: outdated, built for an older .NET,
    installed twice. Reports by default; updates or removes only when asked.

.DESCRIPTION
    Written 2026-11-09 after VL.Fuse 1.0.0-alpha04 (net6.0, built for vvvv gamma 5) refused to open
    in vvvv gamma 7.4 (.NET 8): its help patch crashed vvvvc while 1.0.3-beta08 (net8.0) compiled.
    The same folder also held five stale copies of this family's own packages. Nothing in vvvv says
    "this package is too old" - it just fails to open - so this script says it.

    For every package folder in %LOCALAPPDATA%\vvvv\gamma\nugets it reads the .nuspec and lib\
    folders and reports:
      - OLDER COPY     another version of the same package is installed (only the highest is wanted)
      - OLD .NET       a VL.* package whose lib\ has no net8.0-compatible target (net5/6/7, net4x)
      - UPDATE n.n.n   nuget.org has a newer version (prereleases included: the family is alpha)

    -Update     installs the latest version of every VL.* package flagged UPDATE or OLD .NET, with
                vvvv's own NuGet.exe, into the same folder (dependencies come along).
    -RemoveOld  deletes the OLDER COPY folders - but never a version that another installed package
                still names in its dependencies. vvvv must be closed.
    Neither touches vvvv's package-cache under Program Files: stale entries there are only listed
    (they are compiled caches; delete them by hand with vvvv closed if you want the space).

    During development this family never installs its own packages here: launchers point vvvv at
    each repository's dist\ folder. An installed copy of our own package is therefore either a
    deliberate user-side install or a stale leftover - this script shows which.

.PARAMETER Offline
    Skip the nuget.org lookup (no UPDATE column).

.PARAMETER All
    Also report (and with -RemoveOld also clean) the dependency libraries - SkiaSharp, System.*,
    Mapsui... Off by default, and their UPDATE notes are not advice: they arrive at the versions the
    VL packages ask for, and vvvv itself pins several (SkiaSharp 2.88 in vvvv 7.4). Upgrade a VL
    package, never its dependencies by hand.

.EXAMPLE
    .\tools\Check-VvvvPackages.ps1                 # report only
    .\tools\Check-VvvvPackages.ps1 -Update         # then install newer VL.* versions
    .\tools\Check-VvvvPackages.ps1 -RemoveOld      # then delete older side-by-side copies
#>
param(
    [switch]$Update,
    [switch]$RemoveOld,
    [switch]$Offline,
    [switch]$All,
    [string]$Folder = (Join-Path $env:LOCALAPPDATA 'vvvv\gamma\nugets')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Folder)) { throw "No vvvv package folder at $Folder" }
if (($Update -or $RemoveOld) -and (Get-Process vvvv -ErrorAction SilentlyContinue)) {
    throw 'vvvv is running and holds the packages it loaded. Close it, then run this again.'
}

# Version ordering: PowerShell's [semver] understands 1.0.3-beta08; four-part versions fall back to
# [version]; anything else compares as text.
function ConvertTo-Comparable([string]$v) {
    $s = $null
    if ([System.Management.Automation.SemanticVersion]::TryParse($v, [ref]$s)) { return $s }
    $w = $null
    if ([version]::TryParse(($v -split '-')[0], [ref]$w)) {
        return [System.Management.Automation.SemanticVersion]::new([Math]::Max(0, $w.Major), [Math]::Max(0, $w.Minor), [Math]::Max(0, $w.Build))
    }
    return $null
}
function Compare-Versions([string]$a, [string]$b) {
    $x = ConvertTo-Comparable $a; $y = ConvertTo-Comparable $b
    if ($x -and $y) { return $x.CompareTo($y) }
    return [string]::CompareOrdinal($a, $b)
}

# ---------------------------------------------------------------- read what is installed
# vvvv's installs keep the manifest only inside the .nupkg (no loose .nuspec at the root), so read
# it from there; a loose one is used when present.
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-Nuspec([string]$dir) {
    $loose = Get-ChildItem $dir -Filter *.nuspec -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($loose) { return [xml](Get-Content $loose.FullName -Raw) }
    $pkg = Get-ChildItem $dir -Filter *.nupkg -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $pkg) { return $null }
    $zip = [IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.nuspec' } | Select-Object -First 1
        if (-not $entry) { return $null }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

$installed = foreach ($dir in Get-ChildItem $Folder -Directory) {
    try { $x = Read-Nuspec $dir.FullName } catch { continue }
    if (-not $x) { continue }
    $meta = $x.package.metadata
    $deps = @()
    $depNodes = $x.SelectNodes("//*[local-name()='dependency']")
    foreach ($d in $depNodes) { $deps += [pscustomobject]@{ Id = $d.id; Version = ($d.version -replace '[\[\]\(\)\s]', '' -split ',')[0] } }
    $tfms = @()
    $lib = Join-Path $dir.FullName 'lib'
    if (Test-Path $lib) { $tfms = @(Get-ChildItem $lib -Directory | ForEach-Object { $_.Name.ToLowerInvariant() }) }
    [pscustomobject]@{
        Id      = [string]$meta.id
        Version = [string]$meta.version
        Path    = $dir.FullName
        Tfms    = $tfms
        Deps    = $deps
    }
}
if (-not $installed) { Write-Host "No packages in $Folder"; return }

# a target vvvv 7.x (.NET 8) can load: net8.0, netstandard, net8.0-windows...; not net5/6/7 or net4x
function Test-Net8Compatible($tfms) {
    if (-not $tfms -or $tfms.Count -eq 0) { return $true }     # no lib\: a VL-only package
    foreach ($t in $tfms) {
        if ($t -match '^net(8|9|10)\.\d' -or $t -match '^netstandard' -or $t -match '^netcoreapp') { return $true }
    }
    return $false
}

# ---------------------------------------------------------------- nuget.org
$latest = @{}
if (-not $Offline) {
    foreach ($id in ($installed.Id | Where-Object { $All -or $_ -like 'VL.*' } | Sort-Object -Unique)) {
        try {
            $r = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/$($id.ToLowerInvariant())/index.json" -TimeoutSec 20
            # Compare like with like: a stable install against stable releases; a prerelease install
            # (this family is alpha) against everything.
            $mine = @($installed | Where-Object Id -eq $id)
            $wantPre = @($mine | Where-Object { $_.Version -match '-' }).Count -gt 0
            $candidates = @($r.versions | Where-Object { $wantPre -or $_ -notmatch '-' })
            if ($candidates.Count -eq 0) { $candidates = @($r.versions) }
            $latest[$id] = $candidates | Sort-Object { ConvertTo-Comparable $_ } | Select-Object -Last 1
        } catch { $latest[$id] = $null }   # not on nuget.org (a private or local package)
    }
}

# ---------------------------------------------------------------- report
$rows = foreach ($g in ($installed | Where-Object { $All -or $_.Id -like 'VL.*' } | Group-Object Id)) {
    $sorted = @($g.Group | Sort-Object { ConvertTo-Comparable $_.Version })
    $highest = $sorted[-1]
    foreach ($p in $sorted) {
        $notes = @()
        if ($p -ne $highest) { $notes += 'OLDER COPY' }
        if ($p.Id -like 'VL.*' -and -not (Test-Net8Compatible $p.Tfms)) { $notes += "OLD .NET ($($p.Tfms -join ','))" }
        $l = $latest[$p.Id]
        if ($p -eq $highest -and $l -and (Compare-Versions $l $p.Version) -gt 0) { $notes += "UPDATE $l" }
        [pscustomobject]@{ Package = $p.Id; Installed = $p.Version; Latest = $(if ($Offline) { '-' } elseif ($l) { $l } else { '(not on nuget.org)' }); Notes = ($notes -join '; '); _p = $p }
    }
}

Write-Host "`nvvvv packages in $Folder$(if (-not $All) { '  (VL.* only - -All adds the dependency libraries)' })`n" -ForegroundColor Cyan
$rows | Sort-Object Package, { ConvertTo-Comparable $_.Installed } | Format-Table Package, Installed, Latest, Notes -AutoSize | Out-String -Width 220 | Write-Host

$flagged = @($rows | Where-Object Notes)
if ($flagged.Count -eq 0) { Write-Host 'Nothing to do.' -ForegroundColor Green }

# stale compiled caches: entries in vvvv's package-cache whose package is no longer installed
$vvvvDir = Get-ChildItem 'C:\Program Files\vvvv' -Directory -Filter 'vvvv_gamma_*' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
if ($vvvvDir) {
    $cache = Join-Path $vvvvDir.FullName 'package-cache'
    $have = @($installed | ForEach-Object { "$($_.Id).$($_.Version)".ToLowerInvariant() })
    $stale = @(Get-ChildItem $cache -Directory -ErrorAction SilentlyContinue | Where-Object {
        $key = ($_.Name -replace '_HDE_Plugin', '').ToLowerInvariant()
        $id = ($_.Name -replace '_HDE_Plugin', '') -replace '\.\d.*$', ''
        ($key -match '^vl\.') -and ($have -notcontains $key) -and (Test-Path (Join-Path $Folder "$id.*")) })
    if ($stale.Count -gt 0) {
        Write-Host "Compiled caches in $cache for package versions no longer installed (safe to delete by hand, vvvv closed):" -ForegroundColor Yellow
        $stale | ForEach-Object { Write-Host "  $($_.Name)" }
    }
}

# ---------------------------------------------------------------- -Update
if ($Update) {
    $nuget = Join-Path $vvvvDir.FullName 'tools\NuGet.exe'
    $toUpdate = @($rows | Where-Object { $_.Package -like 'VL.*' -and $_.Notes -match 'UPDATE|OLD \.NET' -and $_.Latest -notmatch '^\(|^-$' } | Select-Object -ExpandProperty Package -Unique)
    foreach ($id in $toUpdate) {
        Write-Host "`n== installing $id $($latest[$id])" -ForegroundColor Cyan
        & $nuget install $id -Version $latest[$id] -OutputDirectory $Folder -Source https://api.nuget.org/v3/index.json -NonInteractive | Select-Object -Last 2
        if ($LASTEXITCODE -ne 0) { Write-Host "   failed ($LASTEXITCODE)" -ForegroundColor Red }
    }
    if ($toUpdate.Count -gt 0) { Write-Host "`nRun again with -RemoveOld to delete the versions these replace." -ForegroundColor Yellow }
}

# ---------------------------------------------------------------- -RemoveOld
if ($RemoveOld) {
    # re-read: -Update may just have added versions
    $now = foreach ($dir in Get-ChildItem $Folder -Directory) {
        try { $x = Read-Nuspec $dir.FullName } catch { continue }
        if (-not $x) { continue }
        [pscustomobject]@{ Id = [string]$x.package.metadata.id; Version = [string]$x.package.metadata.version; Path = $dir.FullName
            Deps = @($x.SelectNodes("//*[local-name()='dependency']") | ForEach-Object { "$($_.id)|$(($_.version -replace '[\[\]\(\)\s]', '' -split ',')[0])".ToLowerInvariant() }) }
    }
    foreach ($g in ($now | Where-Object { $All -or $_.Id -like 'VL.*' } | Group-Object Id | Where-Object Count -gt 1)) {
        $sorted = @($g.Group | Sort-Object { ConvertTo-Comparable $_.Version })
        foreach ($old in $sorted[0..($sorted.Count - 2)]) {
            $key = "$($old.Id)|$($old.Version)".ToLowerInvariant()
            $users = @($now | Where-Object { $_.Path -ne $old.Path -and $_.Deps -contains $key })
            if ($users.Count -gt 0) {
                Write-Host "kept     $($old.Id) $($old.Version) - still named by $(($users | ForEach-Object { "$($_.Id) $($_.Version)" }) -join ', ')" -ForegroundColor Yellow
                continue
            }
            Remove-Item -LiteralPath $old.Path -Recurse -Force
            Write-Host "removed  $($old.Id) $($old.Version)" -ForegroundColor Green
        }
    }
}
