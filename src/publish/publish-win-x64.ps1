<#
.SYNOPSIS
    Publishes ClaudeForge as a self-contained single-file exe for
    Windows x64.

.DESCRIPTION
    Thin wrapper that delegates to Publish-Rid.ps1 with the `win-x64` RID
    and `-Clean` so standalone invocations always start from a fresh bin/obj
    state. Other RIDs' zips in dist/ are preserved. To build every RID in one
    shot, use publish.ps1 with the -All switch instead.
#>
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Publish-Rid.ps1') -Rid 'win-x64' -Clean
