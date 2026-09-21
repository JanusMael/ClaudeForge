#requires -Version 7.0
<#
.SYNOPSIS
    Launch a published Forge app straight onto one page and photograph its window.

.DESCRIPTION
    The harness --deep-link exists for, and the only way a page in these apps gets LOOKED at.

    The headless test app is deliberately stripped of the App's resource dictionaries and cannot
    instantiate views, so no test can render a page. Before this, verifying a page meant clicking
    to it by hand and remembering to — which is how OpenCodeForge's Backup page reached a green
    4,245-test suite, a clean trimmed publish, and a documented "never seen on screen" gap all at
    once.

    Read-only: it navigates and photographs, and presses nothing.

    ⚠ WINDOWS ONLY. System.Drawing's screen capture and the three user32 entry points have no
    cross-platform equivalent, and the apps' other scripts that must run everywhere are kept in
    byte parity with a .sh twin. This one has no twin on purpose rather than by omission.

.PARAMETER ExePath
    The PUBLISHED executable, not `dotnet run` — a page can be correct in Debug and absent from a
    trimmed publish, which is the difference this harness is most useful for catching.

.PARAMETER NodeId
    A navigation node id. OpenCodeForge: essentials, artifacts, backup-restore, footprint.

.EXAMPLE
    pwsh -NoProfile -File scripts/capture-page.ps1 `
        -ExePath src/OpenCodeForge/bin/Release/net10.0/win-x64/publish/OpenCodeForge.exe `
        -NodeId footprint -OutFile footprint.png
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $NodeId,
    [Parameter(Mandatory)][string] $OutFile,

    # How long to wait for a main window to appear. The page content gets its own settle below.
    [int] $SettleSeconds = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Win32Window
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@

# ⛔ --schema-source bundled, always. Without it an OFFLINE launch pays a fetch timeout per schema
# before the window appears — and OpenCodeForge builds three registries, so it pays two per schema
# — while an ONLINE one photographs whichever schema upstream served that day. Neither makes a
# reviewable screenshot. This flag is fatal if a fetch is attempted and fails, so it also cannot
# silently degrade into the thing it is avoiding.
$proc = Start-Process -FilePath $ExePath `
                      -ArgumentList '--deep-link', $NodeId, '--schema-source', 'bundled' `
                      -PassThru

try {
    $deadline = (Get-Date).AddSeconds($SettleSeconds)
    while ((Get-Date) -lt $deadline -and $proc.MainWindowHandle -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }

    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "No main window appeared within $SettleSeconds seconds."
    }

    # ⚠ The window exists well before its pages do: InitializeAsync opens both clients, builds
    # every settings page and only then applies the deep link, and a page like the footprint one
    # starts an async filesystem walk after that. A screenshot taken on MainWindowHandle alone
    # photographs an empty shell that looks exactly like a broken page.
    Start-Sleep -Seconds 4

    [void][Win32Window]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Milliseconds 1000

    $rect = New-Object Win32Window+RECT
    if (-not [Win32Window]::GetWindowRect($proc.MainWindowHandle, [ref] $rect)) {
        throw 'GetWindowRect failed.'
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    # ⛔ PRINTED ON PURPOSE, and it is not decoration. The capture below is a SCREEN grab, so any
    # window sitting on top of this one lands in the PNG and reads exactly like clipped layout —
    # this harness's first output appeared to show truncated buttons that were in fact behind
    # another application. The rectangle is what lets a reviewer tell the two apart.
    #
    # ⚠ PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT) was tried here and is NOT a fix: an Avalonia
    # window composites on the GPU and does not re-render into a supplied DC, so it returns a
    # title bar over a blank client area. Do not "improve" this back to PrintWindow.
    Write-Output ('window {0}x{1} at {2},{3}' -f $width, $height, $rect.Left, $rect.Top)

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        }
        finally {
            $graphics.Dispose()
        }

        $bitmap.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }

    Write-Output ('saved ' + $OutFile)
}
finally {
    # Closed rather than killed where possible: these apps persist window state on shutdown, and a
    # killed one leaves the state file describing a session that never ended.
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { $proc.Kill() }
    }
}
