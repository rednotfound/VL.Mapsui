<#
.SYNOPSIS
    Photographs the running vvvv renderer, optionally clicking in it first. Rung 4's camera.

.DESCRIPTION
    Rung 4 is "open it in vvvv and look", and the looking is a person's job. What this script does
    is the part a person should not have to relay by voice: it finds the renderer window of the
    running vvvv (the one titled "Skia" -- every chapter's Renderer), brings it forward, optionally
    LEFT-clicks in its centre N times (in a chapter where the picture is the control, a click is
    the GROW bang), and saves the window as a PNG that the session can read back.

    Written 2026-08-28, the evening Prompt Grow a town shipped, after an hour of "what does the
    readout say" over chat. The recipe it packages -- and the two things that do NOT work:

      - PrintWindow returns a STALE image of vvvv's windows (Skia-drawn UI). Three identical
        captures proved nothing that evening. CopyFromScreen of the window's rectangle, after
        SetForegroundWindow, is live -- so the renderer must actually be on screen.
      - GetWindowRect lies to a DPI-unaware process on a scaled display, and SetCursorPos then
        lands the click somewhere else. SetProcessDPIAware() first, always.
      - SetForegroundWindow is a request Windows may refuse; on the first test run a browser sat
        over the renderer and eleven "GROW" clicks went into it. The script now checks which
        top-level window is under the click point and refuses to click through anything else.

    A click sequence with -EachClick saves one PNG per click, named <Out>-00.png, -01.png ...; that
    is how "FOUND flips on the 11th press" was established without reading eleven screenshots:
    compare the readout region between frames and read only the ones that changed.

    It never launches vvvv (use Open-HelpPatch.ps1) and never closes it. Close vvvv yourself, then
    run Normalize-HelpPatches.ps1 -- the rules in CLAUDE.md are unchanged.

.PARAMETER Out
    Path of the PNG to write. Default: renderer.png in the current directory.
    With -EachClick this is a prefix: <Out minus .png>-NN.png.

.PARAMETER Clicks
    Number of LEFT clicks at the renderer's centre before the capture. Default 0.

.PARAMETER RightClicks
    Number of RIGHT clicks at the renderer's centre, performed BEFORE the left clicks (a chapter
    that uses right-click as RESET wants the reset first). Default 0.

.PARAMETER EachClick
    Save a capture after every left click (and one before the first), not only at the end.

.PARAMETER Title
    Window title to look for. Default "Skia".

.PARAMETER SettleMs
    Milliseconds to wait after each click and before each capture. Default 400.

.PARAMETER TimeoutSec
    How long to wait for the renderer window to appear (vvvv may still be loading). Default 60.

.EXAMPLE
    .\tools\Open-HelpPatch.ps1 "Grow a town"
    .\tools\Capture-Renderer.ps1 -Clicks 11 -Out grow11.png
.EXAMPLE
    .\tools\Capture-Renderer.ps1 -RightClicks 1 -Clicks 20 -EachClick -Out seq\p.png
#>
param(
    [string]$Out = 'renderer.png',
    [int]$Clicks = 0,
    [int]$RightClicks = 0,
    [switch]$EachClick,
    [string]$Title = 'Skia',
    [int]$SettleMs = 400,
    [int]$TimeoutSec = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class CaptureRendererNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    public static IntPtr FindTitled(uint[] pids, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (Array.IndexOf(pids, pid) < 0 || !IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    public const uint SWP_NOMOVE_NOSIZE_NOACTIVATE = 0x0001 | 0x0002 | 0x0010;
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    public struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    public static IntPtr TopLevelWindowAt(int x, int y) { return GetAncestor(WindowFromPoint(new Point(x, y)), 2); }
    public struct Rect { public int Left, Top, Right, Bottom; }
    public static void Click(int x, int y, bool right) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(right ? 0x08u : 0x02u, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(right ? 0x10u : 0x04u, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

[void][CaptureRendererNative]::SetProcessDPIAware()

# --- find the renderer -------------------------------------------------------------------------
$handle = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ($handle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
    # not MainWindowTitle: that is whichever window Windows calls main (often the editor, title empty).
    # Enumerate every visible top-level window of every vvvv process and match the title.
    $pids = [uint32[]]@(Get-Process vvvv -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })
    if ($pids.Count -gt 0) { $handle = [CaptureRendererNative]::FindTitled($pids, $Title) }
    if ($handle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 500 }
}
if ($handle -eq [IntPtr]::Zero) {
    $running = @(Get-Process vvvv -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { throw "vvvv is not running. Launch a chapter with tools\Open-HelpPatch.ps1 first." }
    throw "vvvv is running but no visible window titled '$Title' appeared within $TimeoutSec s."
}

$rect = New-Object CaptureRendererNative+Rect
[void][CaptureRendererNative]::GetWindowRect($handle, [ref]$rect)
$width  = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "renderer window has no size ($width x $height) - is it minimised?" }
$cx = [int](($rect.Left + $rect.Right) / 2)
$cy = [int](($rect.Top + $rect.Bottom) / 2)

[void][CaptureRendererNative]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds $SettleMs

# Windows may refuse to bring a window forward for a process the user is not interacting with, and
# a click then lands in whatever covers the renderer - a browser, on the first test run. Refuse.
$onTop = [CaptureRendererNative]::TopLevelWindowAt($cx, $cy)
if ($onTop -ne $handle) {
    # SetForegroundWindow is refused when this process is not the foreground one, and vvvv's own
    # Help Browser opens over the renderer. SetWindowPos TOPMOST needs no foreground permission.
    [void][CaptureRendererNative]::SetWindowPos($handle, [CaptureRendererNative]::HWND_TOPMOST, 0, 0, 0, 0, [CaptureRendererNative]::SWP_NOMOVE_NOSIZE_NOACTIVATE)
    Start-Sleep -Milliseconds 300
    [void][CaptureRendererNative]::SetWindowPos($handle, [CaptureRendererNative]::HWND_NOTOPMOST, 0, 0, 0, 0, [CaptureRendererNative]::SWP_NOMOVE_NOSIZE_NOACTIVATE)
    $onTop = [CaptureRendererNative]::TopLevelWindowAt($cx, $cy)
}
if ($onTop -ne $handle) {
    throw "another window covers the renderer's centre ($cx,$cy); nothing was clicked or captured. Move it aside (or minimise it) and run again."
}

# --- capture helper ----------------------------------------------------------------------------
$stem = [System.IO.Path]::ChangeExtension($Out, $null).TrimEnd('.')
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$written = New-Object System.Collections.Generic.List[string]

function Save-Frame([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height)) }
    finally { $g.Dispose() }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $written.Add($path)
}

# --- act ---------------------------------------------------------------------------------------
for ($i = 0; $i -lt $RightClicks; $i++) {
    [CaptureRendererNative]::Click($cx, $cy, $true)
    Start-Sleep -Milliseconds $SettleMs
}

if ($EachClick) { Save-Frame ("{0}-{1:00}.png" -f $stem, 0) }

for ($i = 1; $i -le $Clicks; $i++) {
    [CaptureRendererNative]::Click($cx, $cy, $false)
    Start-Sleep -Milliseconds $SettleMs
    if ($EachClick) { Save-Frame ("{0}-{1:00}.png" -f $stem, $i) }
}

if (-not $EachClick) { Save-Frame $Out }

Write-Host ("renderer '{0}' {1}x{2} at {3},{4}; {5} right + {6} left click(s); wrote:" -f $Title, $width, $height, $rect.Left, $rect.Top, $RightClicks, $Clicks)
$written | ForEach-Object { Write-Host "  $_" }
