<#
.SYNOPSIS
    Publishes ClaudeForge as a self-contained single-file binary for
    macOS x64 (Intel).

.DESCRIPTION
    Thin wrapper that delegates to Publish-Rid.ps1 with the `osx-x64` RID
    and `-Clean`. Other RIDs' zips in dist/ are preserved. See publish.ps1
    for the multi-RID orchestrator with prompting and -All support.
#>
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Publish-Rid.ps1') -Rid 'osx-x64' -Clean
