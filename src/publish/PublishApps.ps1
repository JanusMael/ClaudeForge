<#
.SYNOPSIS
    The table of publishable apps, shared by every script under src/publish/.

.DESCRIPTION
    This repository builds two shipping apps out of one source tree. Before this
    file existed, five of the ten publish scripts named ClaudeForge directly —
    project path, assembly name, Linux asset list, the obj/ probe path, and the
    smoke gate's startup-log assertion. Publishing the second app meant either
    editing those five in place (so only one app could be published at a time) or
    copying all ten (so they drift).

    Everything app-shaped now lives here and the scripts take `-App <name>`.

    ⚠ THE SMOKE GATE IS WHY THIS IS A TABLE AND NOT TWO SCRIPTS.
    Smoke-PublishedBinary.ps1 asserts the app logged a startup line. Both fields
    it needs to do that differ per app and neither is derivable from the assembly
    name:

      - ClaudeForge logs through AvaloniaDiagnostics' bucketed rolling sink, so
        its files are `app-<yyyyMMdd>-<HH>.txt`, and Program.cs writes the literal
        "Starting ClaudeForge".
      - OpenCodeForge configures Serilog directly with `opencodeforge-.log` +
        RollingInterval.Day, and its startup line is "Starting {App}" resolved
        from Strings.AppTitle.

    Copying the smoke script and hand-editing one string is exactly how a gate
    ends up asserting a line the app never writes — and the failure is silent in
    the direction that matters, because a gate that cannot match its token fails
    the publish it was supposed to bless.

.PARAMETER -
    Dot-source this file to gain Get-PublishApp / Get-PublishAppName:

        . (Join-Path $PSScriptRoot 'PublishApps.ps1')
        $app = Get-PublishApp -Name 'ClaudeForge'

.NOTES
    Paths are REPO-RELATIVE with forward slashes, deliberately. They are resolved
    against the repo root by Resolve-PublishAppPath, and the forward-slash
    `src/...` spelling is the form BuildFilePathIntegrityTests can see — so a
    project that moves breaks a test here instead of breaking a release at the
    hour a tag is pushed.
#>

# The apps this repository can publish. Add a row rather than a script.
#
# Name            Display name, and the value every script takes as -App.
# ProjectPath     Repo-relative .csproj to publish.
# AssemblyName    <AssemblyName> from that csproj. Drives the produced binary
#                 name, the archive name, and the Linux Exec= target.
# StartupLogToken The literal substring the smoke gate requires in the log.
# LogFilePattern  Glob matching the app's log files inside its logs/ directory.
# TagPrefix       The release-tag prefix, matching this app's ReleaseTagScheme in
#                 code. Empty means unprefixed — reserved for the app that
#                 published this repo's releases before it hosted two. See
#                 AgentForge.Core/Updates/ReleaseTagScheme.cs.
# IconSvg         Repo-relative SVG staged beside the Linux binary, or $null when
#                 the app has no icon yet.
# DesktopFile     Repo-relative .desktop template, or $null.
# LinuxSetup      Repo-relative desktop-integration installer, or $null.
$script:PublishAppTable = @(
    [pscustomobject]@{
        Name            = 'ClaudeForge'
        ProjectPath     = 'src/ClaudeForge/ClaudeForge.csproj'
        AssemblyName    = 'ClaudeForge'
        StartupLogToken = 'Starting ClaudeForge'
        LogFilePattern  = 'app-*.txt'
        TagPrefix       = ''
        IconSvg         = 'src/ClaudeForge/Resources/ClaudeForge.svg'
        DesktopFile     = 'assets/linux/claudeforge.desktop'
        LinuxSetup      = 'assets/linux/linux-setup.sh'
    }
    [pscustomobject]@{
        Name            = 'OpenCodeForge'
        ProjectPath     = 'src/OpenCodeForge/OpenCodeForge.csproj'
        AssemblyName    = 'OpenCodeForge'
        StartupLogToken = 'Starting OpenCodeForge'
        LogFilePattern  = 'opencodeforge-*.log'
        TagPrefix       = 'opencodeforge-'
        # ⛔ No icon, no .desktop, no setup script yet. Left NULL rather than
        # pointed at ClaudeForge's: a Linux archive carrying the other app's icon
        # and an Exec= line naming the other app's binary is worse than one that
        # ships without desktop integration and says so. Publish-Rid reports the
        # omission per RID instead of silently staging nothing.
        IconSvg         = $null
        DesktopFile     = $null
        LinuxSetup      = $null
    }
)

<#
.SYNOPSIS
    The repo root, found by walking up from this script's directory.
.DESCRIPTION
    src/publish/ -> src/ -> repo root. Computed rather than assumed so the
    scripts work when invoked from any working directory, which is how both CI
    and the per-RID wrappers call them.
#>
function Get-PublishRepoRoot
{
    [CmdletBinding()]
    param()

    return (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
}

<#
.SYNOPSIS
    Every app name in the table.
.NOTES
    Returned unwrapped. PowerShell's `return @($x)` unwraps a one-element
    collection back to a bare item at the call site, so the guarantee belongs at
    the assignment: `$names = @(Get-PublishAppName)`.
#>
function Get-PublishAppName
{
    [CmdletBinding()]
    param()

    return $script:PublishAppTable.Name
}

<#
.SYNOPSIS
    One app descriptor by name.
.PARAMETER Name
    The app to look up. Case-insensitive.
.NOTES
    Throws on an unknown name rather than returning $null: every caller
    immediately dereferences the result, so a null would surface later as a
    property-on-null and name the wrong culprit.
#>
function Get-PublishApp
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $match = $script:PublishAppTable |
        Where-Object { $_.Name -eq $Name } |
        Select-Object -First 1

    if (-not $match)
    {
        $known = (Get-PublishAppName) -join ', '
        throw "Unknown app '$Name'. Known apps: $known."
    }

    return $match
}

<#
.SYNOPSIS
    Resolve one of a descriptor's repo-relative paths to an absolute path.
.PARAMETER RelativePath
    A repo-relative, forward-slash path from the table, or $null.
.OUTPUTS
    The absolute path, or $null when RelativePath was $null/empty. The path is
    NOT required to exist — callers decide whether an absent asset is fatal.
#>
function Resolve-PublishAppPath
{
    [CmdletBinding()]
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string] $RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath))
    {
        return $null
    }

    return (Join-Path (Get-PublishRepoRoot) $RelativePath)
}
