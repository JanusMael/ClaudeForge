using System.Globalization;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Command-line debug flags that force specific UI states for manual testing.
/// <para>
/// Parsed once at startup from <c>Program.Main</c> args via
/// <see cref="Initialize"/>; read at runtime by view-models that gate
/// conditional UI on environment detection (e.g. install-banner visibility,
/// no-editor-selected welcome view). Each flag is a simple <c>bool</c>
/// property so call sites can OR the flag with their existing condition:
/// </para>
/// <code>
/// ShowInstallBanner = DebugFlags.ShowInstallBanner
///                  || (!PlatformPaths.IsClaudeCodeInstalled
///                      &amp;&amp; !PlatformPaths.IsDesktopInstalled);
/// </code>
/// <para>
/// <strong>Why static:</strong> Avalonia construction order makes injecting
/// a flags object into every consuming view-model awkward; the flags are
/// process-global and never change after Initialize, so a static class with
/// private setters is the lightest fit. Tests can call <c>Reset()</c> in
/// TestCleanup to restore default values between runs.
/// </para>
/// <para>
/// <strong>Adding a new flag:</strong>
/// </para>
/// <list type="number">
///   <item>Add a <c>public static bool</c> property with private setter.</item>
///   <item>Add a <c>case "--myFlag":</c> branch to <see cref="Initialize"/>.</item>
///   <item>Add the flag name to <see cref="ListActive"/> for log visibility.</item>
///   <item>Read it at the relevant call site, ORed with the production condition.</item>
/// </list>
/// </summary>
public static class DebugFlags
{
    /// <summary>
    /// Force the install-guidance banner to appear at the top of MainWindow,
    /// even when Claude Code or Claude Desktop is detected on this machine.
    /// </summary>
    public static bool ShowInstallBanner { get; private set; }

    /// <summary>
    /// Active platform-emulation override (lowercase: <c>"windows"</c>,
    /// <c>"macos"</c>, <c>"linux"</c>), or <c>null</c> when running with the
    /// real host platform. Set by the <c>--windows</c> / <c>--macos</c> /
    /// <c>--linux</c> command-line flags; also installs an
    /// <see cref="EmulatedPlatformInfo"/> in <see cref="PlatformInfo.Current"/>.
    /// <para>
    /// <strong>What emulation covers:</strong> UI surfaces and path strings
    /// that branch on platform identity for display purposes (Desktop config
    /// path, backup-manifest platform tag, About-page platform name).
    /// </para>
    /// <para>
    /// <strong>What emulation does NOT cover:</strong> platform-intrinsic
    /// APIs (Windows registry access, MSIX virtualization, env-var Machine /
    /// User scope) continue to use real <see cref="System.OperatingSystem"/>
    /// checks because the underlying calls genuinely cannot run on the wrong
    /// host OS. See <see cref="IPlatformInfo"/> docs for the full contract.
    /// </para>
    /// </summary>
    public static string? EmulatedPlatform { get; private set; }

    /// <summary>
    /// Force every property in every editor's nav tree to render with the
    /// "✨ NEW" badge as if it were freshly added by a schema bump.  Useful
    /// for QA / screenshots / smoke-testing the badge styling without having
    /// to actually mutate the bundled schema files or hand-edit the
    /// snapshot cache.
    /// <para>
    /// Set via <c>--showAllNew</c> on the command line.  When active, the
    /// app ALSO suppresses the at-exit <c>SaveSnapshot</c> call so the
    /// production snapshot at <c>~/.claude/cache/schema-snapshot-*.json</c>
    /// is not polluted; the next launch without the flag resumes correctly.
    /// </para>
    /// <para>
    /// Wired via <see cref="Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaTreeBuilder.BuildTopLevel(JsonSchemaNode, ISet{string}?, bool)"/>
    /// — passing <c>flagAllAsNew: true</c> bypasses the normal "diff against
    /// snapshot" logic and stamps every node with <c>IsNew = true</c>.
    /// </para>
    /// </summary>
    public static bool ShowAllNewBadges { get; private set; }

    /// <summary>
    /// Specific-culture name (e.g. <c>"en-US"</c>, <c>"zh-CN"</c>,
    /// <c>"fr-FR"</c>) supplied via <c>--culture &lt;code&gt;</c> on the
    /// command line.  <see langword="null"/> means "no override; inherit
    /// the OS default".
    /// <para>
    /// Validation: only <see cref="CultureTypes.SpecificCultures"/> values
    /// recognised by <see cref="CultureInfo.GetCultureInfo(string, bool)"/>
    /// in <c>predefinedOnly: true</c> mode are accepted.  Neutral cultures
    /// (e.g. <c>"en"</c>) and gibberish (e.g. <c>"xx-XX"</c>) are rejected
    /// — the flag is then ignored and the OS default is used, with a warning
    /// emitted to the Serilog rolling file.
    /// </para>
    /// <para>
    /// Designed for QA / translator / screenshot workflows: users can
    /// confirm that a specific .resx satellite is loading correctly,
    /// preview placeholder strings while a translation is in flight, or
    /// switch to a culture that has NO satellite at all (the
    /// <see cref="System.Resources.ResourceManager"/> falls back to the
    /// neutral resources automatically — verifying the fallback path is
    /// itself a useful test).
    /// </para>
    /// </summary>
    public static string? CultureOverride { get; private set; }

    /// <summary>
    /// Parse-time validation messages accumulated during <see cref="Initialize"/>
    /// — emitted by <see cref="LogActiveFlags"/> after the Serilog pipeline
    /// is configured.  We can't log directly during <see cref="Initialize"/>
    /// because culture parsing has to run BEFORE
    /// <see cref="LocalizationService.ApplyCulture"/> (Step 1 of
    /// <c>Program.Main</c>), which is BEFORE
    /// <see cref="LayeredEditors.Avalonia.Diagnostics.AvaloniaDiagnostics.ConfigureLogging"/>
    /// (Step 3).  Logging at parse time would silently drop into the
    /// no-op default Serilog sink.
    /// </summary>
    private static readonly List<string> _deferredWarnings = new();

    /// <summary>
    /// Parse args once at startup. Unknown args are ignored — Avalonia's own
    /// args (<c>StartWithClassicDesktopLifetime(args)</c> reuses them) and
    /// debugger-injected args should pass through untouched. Comparison is
    /// case-insensitive so <c>--showInstallBanner</c> and
    /// <c>--showinstallbanner</c> both match.
    /// <para>
    /// Two-token flags (currently <c>--culture &lt;code&gt;</c>) consume the
    /// NEXT positional arg as the value.  Index advances past the consumed
    /// value to avoid mis-parsing it as a separate flag.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Does NOT emit Serilog logs during the parse — call
    /// <see cref="LogActiveFlags"/> after the Serilog pipeline is configured
    /// to flush deferred warnings (e.g. invalid <c>--culture</c> values) and
    /// the active-flags summary line.  The split is required because the
    /// culture flag must be parsed BEFORE
    /// <see cref="LocalizationService.ApplyCulture"/> runs in
    /// <c>Program.Main</c>, which is BEFORE Serilog is configured.
    /// </remarks>
    public static void Initialize(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i];
            string lower = raw.ToLowerInvariant();
            switch (lower)
            {
                case "--showinstallbanner":
                    ShowInstallBanner = true;
                    break;

                // Platform-emulation flags: install an EmulatedPlatformInfo so
                // PlatformInfo.Current reports the requested OS for the rest of
                // the process. Only the LAST platform flag wins if multiple are
                // passed (the switch falls through to the assignment each time).
                case "--windows":
                    EmulatedPlatform = "windows";
                    PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));
                    break;

                case "--macos":
                    EmulatedPlatform = "macos";
                    PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));
                    break;

                case "--linux":
                    EmulatedPlatform = "linux";
                    PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));
                    break;

                case "--showallnew":
                    ShowAllNewBadges = true;
                    break;

                // Two-token flag — consume the next arg as the value.
                case "--culture":
                    if (i + 1 >= args.Length)
                    {
                        _deferredWarnings.Add(
                            "[DebugFlags] --culture flag requires a value (e.g. --culture en-US); ignoring.");
                        break;
                    }

                    string requested = args[++i]; // advance past the value so the outer loop doesn't re-scan it.
                    if (TryValidateCulture(requested, out string valid))
                    {
                        CultureOverride = valid;
                    }
                    else
                    {
                        _deferredWarnings.Add(
                            $"[DebugFlags] --culture '{requested}' rejected: not a recognised specific culture " +
                            "(must be a real BCP-47 specific-culture code like 'en-US' or 'zh-CN'). Using OS default.");
                    }

                    break;

                // Help / discovery: defer the help message so it surfaces in the log
                // after Serilog is configured (Initialize runs before logging).
                case "--debug-help":
                case "--help-debug":
                    _deferredWarnings.Add(
                        "[DebugFlags] available flags: --showInstallBanner, " +
                        "--windows, --macos, --linux, --showAllNew, --culture <code>, " +
                        "--cleanup-restore-sidecars, --debug-help");
                    break;
            }
        }
    }

    /// <summary>
    /// Emit Serilog log lines for any deferred warnings recorded during
    /// <see cref="Initialize"/> + the standard "active flags" summary.
    /// Call this AFTER
    /// <see cref="LayeredEditors.Avalonia.Diagnostics.AvaloniaDiagnostics.ConfigureLogging"/>
    /// has run; calling it before is safe but the log lines route to the
    /// no-op default sink and are dropped.
    /// </summary>
    public static void LogActiveFlags()
    {
        foreach (string warning in _deferredWarnings)
        {
            Log.Information("{Warning}", warning);
        }

        string[] active = ListActive().ToArray();
        if (active.Length > 0)
        {
            Log.Information("[DebugFlags] active: {Flags}", string.Join(", ", active));
        }
    }

    /// <summary>
    /// Validate <paramref name="raw"/> against the OS / .NET predefined
    /// culture list.  Returns <see langword="true"/> + the canonical name
    /// in <paramref name="canonical"/> when the input names a real
    /// SPECIFIC culture (a culture with a region — e.g. <c>en-US</c>,
    /// <c>zh-CN</c>); rejects neutral cultures (<c>en</c> alone) and
    /// arbitrary gibberish (<c>xx-XX</c>, <c>not-a-real-code</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="CultureInfo.GetCultureInfo(string, bool)"/> with
    /// <c>predefinedOnly: true</c> is the gatekeeper — without that flag,
    /// .NET's "any culture is valid" BCP-47 fallback would happily
    /// accept anything that LOOKS like a culture code, defeating the
    /// validation.  The neutral-culture check eliminates the few
    /// remaining false-accepts (the user explicitly asked for the
    /// <c>2-dash-2</c> "specific" form).
    /// </remarks>
    internal static bool TryValidateCulture(string raw, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            CultureInfo ci = CultureInfo.GetCultureInfo(raw, predefinedOnly: true);
            if (ci.IsNeutralCulture)
            {
                return false;
            }

            canonical = ci.Name; // canonicalises case + separators
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Test-only reset hook so tests can isolate their flag setup.</summary>
    internal static void ResetForTesting()
    {
        ShowInstallBanner = false;
        EmulatedPlatform = null;
        ShowAllNewBadges = false;
        CultureOverride = null;
        _deferredWarnings.Clear();
        PlatformInfo.ResetForTesting();
    }

    private static IEnumerable<string> ListActive()
    {
        if (ShowInstallBanner)
        {
            yield return "--showInstallBanner";
        }

        if (EmulatedPlatform != null)
        {
            yield return "--" + EmulatedPlatform;
        }

        if (ShowAllNewBadges)
        {
            yield return "--showAllNew";
        }

        if (CultureOverride != null)
        {
            yield return "--culture " + CultureOverride;
        }
    }
}