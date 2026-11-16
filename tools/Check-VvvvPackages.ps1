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
      - FOR BETA       a VL.* package built only for .NET Framework (net4x, dx11): vvvv beta, not gamma
      - OLDER .NET     only net5/6/7 targets. Usually still loads in gamma 7 (.NET 8 runs net6 code);
                       it broke for VL.Fuse 1.0.0-alpha04 because that build expected an older VL.Stride,
                       not because of net6 itself. A hint to check, not a verdict
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

# What a package's lib\ targets, judged for vvvv gamma 7 (.NET 8): 'ok', 'older' (net5/6/7 only) or
# 'beta' (.NET Framework / dx11 only - vvvv beta's world).
function Get-TargetClass($tfms) {
    if (-not $tfms -or $tfms.Count -eq 0) { return 'ok' }      # no lib\: a VL-only package
    if (@($tfms | Where-Object { $_ -match '^net(8|9|10)\.\d' -or $_ -match '^netstandard' }).Count -gt 0) { return 'ok' }
    if (@($tfms | Where-Object { $_ -match '^net[5-7]\.\d' -or $_ -match '^netcoreapp' }).Count -gt 0) { return 'older' }
    return 'beta'
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
        if ($p.Id -like 'VL.*') {
            switch (Get-TargetClass $p.Tfms) {
                'beta'  { $notes += "FOR BETA ($($p.Tfms -join ','))" }
                'older' { $notes += "older .NET ($($p.Tfms -join ',')) - usually fine" }
            }
        }
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
    $toUpdate = @($rows | Where-Object { $_.Package -like 'VL.*' -and $_.Notes -match 'UPDATE' -and $_.Latest -notmatch '^\(|^-$' } | Select-Object -ExpandProperty Package -Unique)
    foreach ($id in $toUpdate) {
        Write-Host "`n== installing $id $($latest[$id])" -ForegroundColor Cyan
        & $nuget install $id -Version $latest[$id] -OutputDirectory $Folder -Source https://api.nuget.org/v3/index.json -NonInteractive | Select-Object -Last 2
        if ($LASTEXITCODE -ne 0) { Write-Host "   failed ($LASTEXITCODE)" -ForegroundColor Red }
    }
    if ($toUpdate.Count -gt 0) { Write-Host "`nRun again with -RemoveOld to delete the versions these replace." -ForegroundColor Yellow }
}

# ---------------------------------------------------------------- -RemoveOld
if ($RemoveOld) {
    # A nuspec dependency is a RANGE: "1.4.0" means >= 1.4.0, "[1.4.0]" means exactly, "[1.0,2.0)"
    # an interval. An older copy may go when the highest installed version satisfies every range that
    # names the package - counting only packages that will themselves remain. Iterate to a fixpoint,
    # because removing one old package can release the old dependencies it pinned.
    function Test-InRange([string]$version, [string]$range) {
        $r = $range.Trim()
        if (-not $r) { return $true }
        if ($r -notmatch '^[\[\(]') { return (Compare-Versions $version $r) -ge 0 }        # plain = minimum
        $incLo = $r.StartsWith('['); $incHi = $r.EndsWith(']')
        $parts = $r.Trim('[', ']', '(', ')').Split(',')
        if ($parts.Count -eq 1) { return (Compare-Versions $version $parts[0].Trim()) -eq 0 }  # [x] exact
        $lo = $parts[0].Trim(); $hi = $parts[1].Trim()
        if ($lo) { $c = Compare-Versions $version $lo; if ($c -lt 0 -or ($c -eq 0 -and -not $incLo)) { return $false } }
        if ($hi) { $c = Compare-Versions $version $hi; if ($c -gt 0 -or ($c -eq 0 -and -not $incHi)) { return $false } }
        return $true
    }
    $now = foreach ($dir in Get-ChildItem $Folder -Directory) {
        try { $x = Read-Nuspec $dir.FullName } catch { continue }
        if (-not $x) { continue }
        [pscustomobject]@{ Id = [string]$x.package.metadata.id; Version = [string]$x.package.metadata.version; Path = $dir.FullName
            Deps = @($x.SelectNodes("//*[local-name()='dependency']") | ForEach-Object { [pscustomobject]@{ Id = [string]$_.id; Range = [string]$_.version } }) }
    }
    $highest = @{}
    foreach ($g in ($now | Group-Object Id)) { $highest[$g.Name] = ($g.Group | Sort-Object { ConvertTo-Comparable $_.Version })[-1] }
    $candidates = @($now | Where-Object { ($All -or $_.Id -like 'VL.*') -and $_ -ne $highest[$_.Id] })
    $remove = [System.Collections.Generic.HashSet[string]]::new()
    $why = @{}
    do {
        $changed = $false
        foreach ($old in $candidates) {
            if ($remove.Contains($old.Path)) { continue }
            $blockers = @($now | Where-Object { -not $remove.Contains($_.Path) -and $_.Path -ne $old.Path } | Where-Object {
                $d = @($_.Deps | Where-Object Id -eq $old.Id)
                $d.Count -gt 0 -and @($d | Where-Object { -not (Test-InRange $highest[$old.Id].Version $_.Range) }).Count -gt 0 })
            if ($blockers.Count -eq 0) { [void]$remove.Add($old.Path); $changed = $true }
            else { $why[$old.Path] = ($blockers | ForEach-Object { "$($_.Id) $($_.Version)" }) -join ', ' }
        }
    } while ($changed)
    foreach ($old in $candidates) {
        if ($remove.Contains($old.Path)) {
            Remove-Item -LiteralPath $old.Path -Recurse -Force
            Write-Host "removed  $($old.Id) $($old.Version)" -ForegroundColor Green
        } else {
            Write-Host "kept     $($old.Id) $($old.Version) - $($why[$old.Path]) needs exactly this version" -ForegroundColor Yellow
        }
    }
}
