#requires -Version 7
<#
    Open-DiagnosticsWindows.ps1 — press F12 and Shift+F12 at the app, for `B2`.

    ⚠ THIS IS THE ONE STEP THAT NEEDS REAL FOCUS. Everything else in this folder
      drives the app through UIA patterns precisely because the app is not the
      foreground window while an agent works — but a global key binding is
      delivered by the window manager to whatever IS focused, so the window must
      be activated first. Skipping the activation sends the keys somewhere else
      entirely, and the script "succeeds".

    Verifies by counting top-level windows before and after, so a keypress that
    went nowhere is reported rather than assumed.
#>

[CmdletBinding()]
param([int] $WaitSeconds = 3)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiaCommon.ps1"

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Fg {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>
    /// ⛔ SetForegroundWindow ALONE FAILS from a background process — Windows
    /// refuses foreground changes from a process that did not receive the last
    /// input, and it fails by returning false rather than throwing. Attaching our
    /// input queue to the target window's thread lifts that restriction for the
    /// duration, which is the documented remedy.
    /// </summary>
    public static bool ForceActivate(IntPtr hWnd) {
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        uint ours     = GetCurrentThreadId();
        bool attached = false;
        try {
            if (fgThread != ours) { attached = AttachThreadInput(ours, fgThread, true); }
            ShowWindow(hWnd, 9);            // SW_RESTORE
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        } finally {
            if (attached) { AttachThreadInput(ours, fgThread, false); }
        }
        return GetForegroundWindow() == hWnd;
    }
}
'@

function Get-TopLevelCount {
    return @(Get-ForgeWindows).Count
}

[void] (Wait-ForgeSettled)
$before = Get-TopLevelCount
Write-Host ('top-level ClaudeForge windows before: ' + $before)

$proc = @(Get-Process ClaudeForge -ErrorAction SilentlyContinue)[0]
if (-not $proc) { throw 'ClaudeForge is not running.' }

$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw 'No main window handle.' }

$isFg = [Fg]::ForceActivate($h)
Start-Sleep -Milliseconds 500
Write-Host ('app is foreground: ' + $isFg)
if (-not $isFg) {
    Write-Host '  ⚠ Activation did not take. The keys below would go to another window.' -ForegroundColor Yellow
}

$wsh = New-Object -ComObject WScript.Shell

Write-Host 'sending F12…'
$wsh.SendKeys('{F12}')
Start-Sleep -Seconds $WaitSeconds
$afterF12 = Get-TopLevelCount
Write-Host ('  windows now: ' + $afterF12)

# ⛔ Re-activate AND VERIFY. Shift+F12 is handled on the MAIN window
# (MainWindow.axaml.cs), but F12 just opened a window that took focus — so an
# unverified SetForegroundWindow sends the chord to the log window, where nothing
# handles it, and the script reports "no second window" as though the binding were
# broken. Retry until the main window really is foreground.
$reactivated = $false
for ($attempt = 1; $attempt -le 5; $attempt++) {
    if ([Fg]::ForceActivate($h)) { $reactivated = $true; break }
    Start-Sleep -Milliseconds 600
    Write-Host ('  (re-activation attempt ' + $attempt + ' did not take)') -ForegroundColor DarkYellow
}
Write-Host ('main window re-activated before Shift+F12: ' + $reactivated)
if (-not $reactivated) {
    Write-Host '  ⛔ Not foreground — Shift+F12 would go elsewhere. Reporting rather than pretending.' -ForegroundColor Red
}

Write-Host 'sending Shift+F12…'
$wsh.SendKeys('+{F12}')
Start-Sleep -Seconds $WaitSeconds
$afterShift = Get-TopLevelCount
Write-Host ('  windows now: ' + $afterShift)

Write-Host ''
Write-Host '======== RESULT ========'
Write-Host ('windows: ' + $before + ' -> ' + $afterF12 + ' -> ' + $afterShift)
foreach ($w in Get-ForgeWindows) {
    try { Write-Host ('  window: ' + $w.Current.Name) } catch { }
}
if ($afterShift -lt ($before + 2)) {
    Write-Host ''
    Write-Host '⚠ Fewer than two new windows. Do NOT run the audit and call it a pass — the' -ForegroundColor Yellow
    Write-Host '  script walks whatever exists, so a missing window reads as a clean result.' -ForegroundColor Yellow
}
