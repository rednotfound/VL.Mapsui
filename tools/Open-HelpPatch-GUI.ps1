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
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$RepoRoot  = Split-Path $PSScriptRoot -Parent
$Launcher  = Join-Path $PSScriptRoot 'Open-HelpPatch.ps1'
$Normalize = Join-Path $PSScriptRoot 'Normalize-HelpPatches.ps1'
$Check     = Join-Path $PSScriptRoot 'Test-VLPatch.ps1'
$HelpDir   = Join-Path $RepoRoot 'help\VL.Mapsui'
$PidFile   = Join-Path ([IO.Path]::GetTempPath()) 'vl-mapsui-vvvv.pid'

# The same enumeration Open-HelpPatch.ps1 uses, so the window never shows a patch the launcher
# would not find. Explanation first, then the HowTos, as the help browser orders them.
$patches = @(Get-ChildItem $HelpDir -File -Filter *.vl | Sort-Object { if ($_.BaseName -like 'Explanation*') { 0 } else { 1 } }, Name)

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
foreach ($p in $patches) { [void]$list.Items.Add($p.BaseName) }
if ($list.Items.Count -gt 0) { $list.SelectedIndex = 0 }

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
$log.Text             = "pick a patch and press Open - or double-click it.`r`n"

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
    $file = $patches[$list.SelectedIndex].FullName
    Invoke-Tool $Launcher @('-Path', $file) "opening $($list.SelectedItem)"
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
