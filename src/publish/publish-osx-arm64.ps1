<#
.SYNOPSIS
    Publishes ClaudeForge as a self-contained single-file binary for
    macOS on Apple Silicon (M1/M2/M3/M4).

.DESCRIPTION
    Thin wrapper that delegates to Publish-Rid.ps1 with the `osx-arm64` RID
    and `-Clean`. Other RIDs' zips in dist/ are preserved. See publish.ps1
    for the multi-RID orchestrator with prompting and -All support.
#>
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Publish-Rid.ps1') -Rid 'osx-arm64' -Clean
