<#
.SYNOPSIS
    A double-clickable picker for the help patches - select one, vvvv opens it.

.DESCRIPTION
    Wraps Open-HelpPatch.ps1, which stays the single source of truth for HOW a patch is launched:
    the THREE package repositories (dist\, deps\, and vl-nettopologysuite\dist\), the
    vvvv-already-running check, the missing-folder check. This file only supplies a window; every
    launch goes through the same gate and every refusal is printed in the log box with the same words.

    Start it by double-clicking Open-HelpPatch.cmd in the repository root, or:

        pwsh -File tools\Open-HelpPatch-GUI.ps1

    Buttons:
      Open in vvvv        the selected patch (double-click does the same)
      Close my vvvv       closes ONLY the vvvv this launcher started (the pid it wrote to
                          %TEMP%\vl-mapsui-vvvv.pid), never another session's window
      Normalize           tools\Normalize-HelpPatches.ps1 - after a hand edit was saved and vvvv closed;
                          vvvv repins the package version and saves Enabled=True, this undoes both
      Check               tools\Test-VLPatch.ps1 - IDs, links, label collisions, help flags, Help.xml

    Carried from vl-nettopologysuite\tools\Open-HelpPatch-GUI.ps1 (itself from vl-overworld) on
    2026-09-24; the Close button is new here, after a Stop-Process on every vvvv killed the sibling
    session's window.

    THE LIST IS IN Help.xml ORDER, NOT ALPHABETICAL - fixed 2026-09-25. The Gray Book states vvvv's
    own Help Browser "displays items in alphabetical order by default unless Help.xml specifies
    otherwise", and every shipped pack's Help.xml is hand-curated rather than incidentally
    alphabetical (VL.Audio lists Reference AudioPlayer before Reference ADSR). The first version of
    this picker sorted by filename and so showed a different order than vvvv's own browser does -
    found when the user opened the folder and asked whether other libraries order their files
    either (they do not; the order lives in Help.xml, universally). Topic headings appear as
    "-- Title --" and are not launchable; any .vl on disk that Help.xml does not list still appears,
    under "-- Not in Help.xml --", because the picker must never hide a patch that exists.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$RepoRoot  = Split-Path $PSScriptRoot -Parent
$Launcher  = Join-Path $PSScriptRoot 'Open-HelpPatch.ps1'
$Normalize = Join-Path $PSScriptRoot 'Normalize-HelpPatches.ps1'
$Check     = Join-Path $PSScriptRoot 'Test-VLPatch.ps1'
$HelpDir     = Join-Path $RepoRoot 'help\VL.Mapsui'
$HelpXmlPath = Join-Path $HelpDir 'Help.xml'
$PidFile     = Join-Path ([IO.Path]::GetTempPath()) 'vl-mapsui-vvvv.pid'

# Builds the picker list in Help.xml's own order - the order vvvv's Help Browser actually shows,
# per the Gray Book ("alphabetical by default unless Help.xml specifies otherwise"). Each entry is
# @{ Kind = 'Header' | 'Patch' | 'Missing'; Text; File }. A 'Header' cannot be opened; a 'Missing'
# entry is something Help.xml names that is not on disk, shown so a broken listing is visible here
# too rather than only in Test-VLPatch. Falls back to a flat alphabetical list, with a header saying
# so, if Help.xml is absent or fails to parse - the picker must degrade, never go blank.
function Get-PatchEntries {
    $onDisk = @(Get-ChildItem $HelpDir -File -Filter *.vl)
    $byName = @{}
    foreach ($f in $onDisk) { $byName[$f.Name] = $f }

    $entries = [System.Collections.Generic.List[object]]::new()
    $ordered = [System.Collections.Generic.HashSet[string]]::new()
    $parsed  = $false

    if (Test-Path $HelpXmlPath) {
        try {
            [xml]$hx = Get-Content $HelpXmlPath -Raw
            foreach ($topic in @($hx.SelectNodes('//Topic'))) {
                $docs = @($topic.SelectNodes('VLDocument'))
                if ($docs.Count -eq 0) { continue }
                $entries.Add([pscustomobject]@{ Kind = 'Header'; Text = $topic.title; File = $null })
                foreach ($doc in $docs) {
                    $link = $doc.link
                    if ($byName.ContainsKey($link)) {
                        $entries.Add([pscustomobject]@{ Kind = 'Patch'; Text = [IO.Path]::GetFileNameWithoutExtension($link); File = $byName[$link] })
                        [void]$ordered.Add($link)
                    }
                    else {
                        $entries.Add([pscustomobject]@{ Kind = 'Missing'; Text = "$link  (listed in Help.xml, not on disk)"; File = $null })
                    }
                }
            }
            $parsed = $true
        }
        catch {
            $entries.Clear()
            $entries.Add([pscustomobject]@{ Kind = 'Header'; Text = "Help.xml did not parse - alphabetical order: $($_.Exception.Message)"; File = $null })
        }
    }
    else {
        $entries.Add([pscustomobject]@{ Kind = 'Header'; Text = 'no Help.xml found - alphabetical order'; File = $null })
    }

    # Every .vl on disk appears somewhere, even one Help.xml forgot - Test-VLPatch already fails on
    # that, but the picker should not silently hide a patch either.
    $strayLabel = if ($parsed) { 'Not in Help.xml' } else { 'help\VL.Mapsui (alphabetical)' }
    $stray = @($onDisk | Where-Object { -not $ordered.Contains($_.Name) } | Sort-Object Name)
    if ($stray.Count -gt 0) {
        $entries.Add([pscustomobject]@{ Kind = 'Header'; Text = $strayLabel; File = $null })
        foreach ($f in $stray) { $entries.Add([pscustomobject]@{ Kind = 'Patch'; Text = $f.BaseName; File = $f }) }
    }
    , $entries
}

$entries = Get-PatchEntries

$form                 = [System.Windows.Forms.Form]::new()
$form.Text            = 'VL.Mapsui - open a help patch'
$form.ClientSize      = [System.Drawing.Size]::new(700, 640)
$form.StartPosition   = 'CenterScreen'
$form.Font            = [System.Drawing.Font]::new('Segoe UI', 10)
$form.MinimumSize     = [System.Drawing.Size]::new(560, 520)

$list                 = [System.Windows.Forms.ListBox]::new()
$list.Location        = [System.Drawing.Point]::new(12, 12)
$list.Size            = [System.Drawing.Size]::new(676, 330)
$list.Anchor          = 'Top,Left,Right,Bottom'
$list.Font            = [System.Drawing.Font]::new('Segoe UI', 11)
$list.IntegralHeight  = $false
foreach ($e in $entries) {
    # Plain ASCII decoration on purpose - the file is ASCII throughout, unlike the .vl documents,
    # and an em dash or warning glyph risks mangling depending on how this gets saved or edited.
    $line = switch ($e.Kind) {
        'Header'  { "-- $($e.Text) --" }
        'Missing' { "    !! $($e.Text)" }
        default   { "    $($e.Text)" }
    }
    [void]$list.Items.Add($line)
}
$firstPatch = 0
for ($i = 0; $i -lt $entries.Count; $i++) { if ($entries[$i].Kind -eq 'Patch') { $firstPatch = $i; break } }
if ($list.Items.Count -gt 0) { $list.SelectedIndex = $firstPatch }

$hint                 = [System.Windows.Forms.Label]::new()
$hint.Text            = 'Opening a document in vvvv is RUNNING it. Read, adjust, save, close vvvv - then Normalize and Check. Tile layers start switched off.'
$hint.Location        = [System.Drawing.Point]::new(12, 350)
$hint.Size            = [System.Drawing.Size]::new(676, 20)
$hint.Anchor          = 'Left,Right,Bottom'

function New-Button([string]$text, [int]$x, [int]$w) {
    $b          = [System.Windows.Forms.Button]::new()
    $b.Text     = $text
    $b.Location = [System.Drawing.Point]::new($x, 376)
    $b.Size     = [System.Drawing.Size]::new($w, 34)
    $b.Anchor   = 'Left,Bottom'
    $b
}
$openBtn  = New-Button 'Open in vvvv'  12  140
$closeBtn = New-Button 'Close my vvvv' 160 140
$normBtn  = New-Button 'Normalize'     308 120
$checkBtn = New-Button 'Check'         436 120
$buttons  = @($openBtn, $closeBtn, $normBtn, $checkBtn)

$log                  = [System.Windows.Forms.TextBox]::new()
$log.Multiline        = $true
$log.ReadOnly         = $true
$log.ScrollBars       = 'Vertical'
$log.Font             = [System.Drawing.Font]::new('Consolas', 9)
$log.Location         = [System.Drawing.Point]::new(12, 422)
$log.Size             = [System.Drawing.Size]::new(676, 206)
$log.Anchor           = 'Left,Right,Bottom'
$patchCount = @($entries | Where-Object Kind -eq 'Patch').Count
$topicCount = @($entries | Where-Object Kind -eq 'Header').Count
$missingCount = @($entries | Where-Object Kind -eq 'Missing').Count
$log.Text             = "$patchCount patch(es) in $topicCount topic(s), Help.xml order. Pick one and press Open - or double-click it.`r`n"
if ($missingCount -gt 0) { $log.Text += "$missingCount entr$(if ($missingCount -eq 1) {'y'} else {'ies'}) in Help.xml point at a file that is not on disk - marked with !! above.`r`n" }

function Write-Log([string]$text) {
    $log.AppendText($text)
    $log.SelectionStart = $log.TextLength; $log.ScrollToCaret()
}

# Run a tool script in a CHILD pwsh: the tools call `exit` on refusal, which would close this
# window if they ran in-process. The child's console output lands in the log box either way.
function Invoke-Tool([string]$scriptPath, [string[]]$toolArgs, [string]$doing) {
    foreach ($b in $buttons) { $b.Enabled = $false }
    $form.UseWaitCursor = $true
    Write-Log "`r`n== $doing`r`n"
    [System.Windows.Forms.Application]::DoEvents()
    try {
        $out = & pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath @toolArgs 2>&1 | Out-String
        Write-Log (($out -replace "`e\[[\d;]*m", ''))
        if ($LASTEXITCODE -ne 0) { Write-Log "`r`nREFUSED / FAILED (exit $LASTEXITCODE) - the reason is above.`r`n" }
    }
    finally {
        $form.UseWaitCursor = $false
        foreach ($b in $buttons) { $b.Enabled = $true }
    }
}

$openPatch = {
    if ($list.SelectedIndex -lt 0) { return }
    $entry = $entries[$list.SelectedIndex]
    if ($entry.Kind -ne 'Patch') {
        Write-Log "`r`n'$($entry.Text)' is a topic heading, not a patch - pick one listed under it.`r`n"
        return
    }
    Invoke-Tool $Launcher @('-Path', $entry.File.FullName) "opening $($entry.Text)"
}

# Only the vvvv this launcher started. Another session on this machine may have its own vvvv open;
# that one is not ours to close, and a plain `Stop-Process vvvv` would take it.
$closeMine = {
    Write-Log "`r`n== closing the vvvv this launcher started`r`n"
    if (-not (Test-Path $PidFile)) { Write-Log "no pid file at $PidFile - nothing was launched from here.`r`n"; return }
    $ourPid = [int](Get-Content $PidFile)
    $p = Get-Process -Id $ourPid -ErrorAction SilentlyContinue
    if (-not $p -or $p.ProcessName -ne 'vvvv') { Write-Log "pid $ourPid is not a running vvvv - already closed.`r`n"; return }
    [void]$p.CloseMainWindow()
    if (-not $p.WaitForExit(8000)) { Stop-Process -Id $ourPid -Force; Write-Log "did not close on request; stopped pid $ourPid.`r`n" }
    else { Write-Log "closed pid $ourPid.`r`n" }
    $others = @(Get-Process vvvv -ErrorAction SilentlyContinue)
    if ($others) { Write-Log "NOTE: $($others.Count) other vvvv still running (pid $($others.Id -join ', ')) - not ours, left alone.`r`n" }
}

$openBtn.Add_Click($openPatch)
$list.Add_DoubleClick($openPatch)
$closeBtn.Add_Click($closeMine)
$normBtn.Add_Click({ Invoke-Tool $Normalize @() 'normalizing help patches (vvvv must be closed)' })
$checkBtn.Add_Click({ Invoke-Tool $Check @() 'checking every .vl and Help.xml' })

$form.Controls.AddRange(@($list, $hint, $openBtn, $closeBtn, $normBtn, $checkBtn, $log))
[void]$form.ShowDialog()
