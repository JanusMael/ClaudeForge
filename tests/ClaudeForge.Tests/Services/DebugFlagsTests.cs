using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Tests for the command-line argument parser in <see cref="DebugFlags"/>.
/// Each test calls <see cref="DebugFlags.ResetForTesting"/> in cleanup so
/// flags do not bleed across tests (the class holds static state).
/// </summary>
[TestClass]
public sealed class DebugFlagsTests
{
    [TestCleanup]
    public void Cleanup()
    {
        DebugFlags.ResetForTesting();
    }

    [TestMethod]
    public void Initialize_NoArgs_LeavesAllFlagsDefault()
    {
        DebugFlags.Initialize([]);

        Assert.IsFalse(DebugFlags.ShowInstallBanner);
        Assert.IsNull(DebugFlags.EmulatedPlatform);
        Assert.IsFalse(DebugFlags.ShowAllNewBadges);
    }

    [TestMethod]
    public void Initialize_ShowAllNew_SetsFlag()
    {
        // --showAllNew exposes the "✨ NEW" badge styling without
        // requiring a schema bump or hand-edited snapshot cache.
        DebugFlags.Initialize(["--showAllNew"]);
        Assert.IsTrue(DebugFlags.ShowAllNewBadges);
    }

    [TestMethod]
    public void Initialize_ShowAllNew_IsCaseInsensitive()
    {
        DebugFlags.Initialize(["--SHOWALLNEW"]);
        Assert.IsTrue(DebugFlags.ShowAllNewBadges);
    }

    [TestMethod]
    public void Initialize_ShowInstallBanner_SetsFlag()
    {
        DebugFlags.Initialize(["--showInstallBanner"]);
        Assert.IsTrue(DebugFlags.ShowInstallBanner);
    }

    [TestMethod]
    public void Initialize_FlagsAreCaseInsensitive()
    {
        DebugFlags.Initialize(["--SHOWINSTALLBANNER", "--LINUX"]);
        Assert.IsTrue(DebugFlags.ShowInstallBanner);
        Assert.AreEqual("linux", DebugFlags.EmulatedPlatform);
    }

    [TestMethod]
    public void Initialize_UnknownArgs_AreIgnored()
    {
        // Avalonia and the debugger pass through their own args; the parser
        // must tolerate them without throwing or polluting flag state.
        DebugFlags.Initialize(["--showInstallBanner", "--unknown-arg", "/path/to/file"]);
        Assert.IsTrue(DebugFlags.ShowInstallBanner,
            "Unknown args must not interfere with recognised flags.");
    }

    // -----------------------------------------------------------------------
    // Platform emulation flags
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Initialize_Linux_EmulatesLinuxPlatform()
    {
        DebugFlags.Initialize(["--linux"]);

        Assert.AreEqual("linux", DebugFlags.EmulatedPlatform);
        Assert.IsTrue(PlatformInfo.Current.IsLinux,
            "PlatformInfo.Current must be swapped for an emulated Linux instance.");
        Assert.AreEqual("linux", PlatformInfo.Current.PlatformId);
    }

    [TestMethod]
    public void Initialize_Macos_EmulatesMacOSPlatform()
    {
        DebugFlags.Initialize(["--macos"]);

        Assert.AreEqual("macos", DebugFlags.EmulatedPlatform);
        Assert.IsTrue(PlatformInfo.Current.IsMacOS);
        Assert.AreEqual("macos", PlatformInfo.Current.PlatformId);
    }

    [TestMethod]
    public void Initialize_Windows_EmulatesWindowsPlatform()
    {
        DebugFlags.Initialize(["--windows"]);

        Assert.AreEqual("windows", DebugFlags.EmulatedPlatform);
        Assert.IsTrue(PlatformInfo.Current.IsWindows);
        Assert.AreEqual("windows", PlatformInfo.Current.PlatformId);
    }

    [TestMethod]
    public void Initialize_LastPlatformFlagWins()
    {
        // Multiple platform flags are unusual but must be deterministic.
        DebugFlags.Initialize(["--macos", "--linux"]);

        Assert.AreEqual("linux", DebugFlags.EmulatedPlatform,
            "Later flags overwrite earlier ones.");
        Assert.IsTrue(PlatformInfo.Current.IsLinux);
    }

    [TestMethod]
    public void ResetForTesting_RestoresPlatformInfoToRuntime()
    {
        DebugFlags.Initialize(["--linux"]);
        Assert.IsTrue(PlatformInfo.Current.IsLinux, "Setup: emulation active.");

        DebugFlags.ResetForTesting();

        Assert.AreSame(RuntimePlatformInfo.Instance, PlatformInfo.Current,
            "ResetForTesting must restore the runtime PlatformInfo so tests do not " +
            "leak emulation state into subsequent tests.");
    }

    // -----------------------------------------------------------------------
    // --culture <code>  (2026-05-07)
    //
    // Two-token flag that drives LocalizationService.ApplyCulture in
    // Program.Main.  Validation must accept real specific cultures even
    // when no .resx satellite exists for them (the user explicitly asked
    // for that — useful for verifying ResourceManager fallback behaviour).
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Initialize_NoCultureFlag_LeavesOverrideNull()
    {
        DebugFlags.Initialize([]);
        Assert.IsNull(DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_EnUs_AcceptedAndCanonicalised()
    {
        DebugFlags.Initialize(["--culture", "en-US"]);
        Assert.AreEqual("en-US", DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_ZhCn_AcceptedEvenThoughSatelliteExistsAsZhCN()
    {
        // Validates that the SHIPPED satellite culture (zh-CN) is accepted —
        // canonical form preserved.
        DebugFlags.Initialize(["--culture", "zh-CN"]);
        Assert.AreEqual("zh-CN", DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_FrFr_AcceptedDespiteNoSatellite()
    {
        // The user wants the flag to accept any real specific culture,
        // even when no .resx satellite exists for it (ResourceManager
        // falls back to the neutral resources automatically).  fr-FR has
        // no Strings.fr-FR.resx in this repo but is a real .NET culture.
        DebugFlags.Initialize(["--culture", "fr-FR"]);
        Assert.AreEqual("fr-FR", DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_CaseInsensitive_FlagName()
    {
        // The flag NAME is case-insensitive; the VALUE is canonicalised
        // via CultureInfo.GetCultureInfo (en-us → en-US, EN-US → en-US).
        DebugFlags.Initialize(["--CULTURE", "en-us"]);
        Assert.AreEqual("en-US", DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_GibberishCode_RejectedAndOverrideStaysNull()
    {
        DebugFlags.Initialize(["--culture", "xx-XX"]);
        Assert.IsNull(DebugFlags.CultureOverride,
            "Arbitrary 2-dash-2 codes that are not real cultures must be rejected.");
    }

    [TestMethod]
    public void Initialize_Culture_NotEvenDashed_Rejected()
    {
        DebugFlags.Initialize(["--culture", "not-a-real-code"]);
        Assert.IsNull(DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_NeutralCulture_Rejected()
    {
        // The user spec calls out "2-dash-2" form: en-US, zh-CN, fr-FR.
        // Neutral cultures (en, zh, fr — language only, no region) are
        // valid in .NET but rejected here so the flag's contract matches
        // the user's mental model.
        DebugFlags.Initialize(["--culture", "en"]);
        Assert.IsNull(DebugFlags.CultureOverride,
            "Neutral cultures (no region) must be rejected — user wants specific cultures only.");
    }

    [TestMethod]
    public void Initialize_Culture_EmptyValue_Rejected()
    {
        DebugFlags.Initialize(["--culture", ""]);
        Assert.IsNull(DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_MissingValue_Rejected()
    {
        // --culture as the LAST arg with nothing after it must not crash
        // and must not set the override.
        DebugFlags.Initialize(["--culture"]);
        Assert.IsNull(DebugFlags.CultureOverride);
    }

    [TestMethod]
    public void Initialize_Culture_ConsumesNextArg_DoesNotMisparseAsFlag()
    {
        // The value MUST be consumed by --culture so the outer parse loop
        // doesn't re-scan it.  Passing an arg sequence where the value
        // happens to look like another flag would otherwise mis-parse.
        // (en-US doesn't start with `--`, so this is mostly a hypothetical
        // future safeguard — but the existing impl uses i++ to skip past
        // the consumed value and we want a regression test for that.)
        DebugFlags.Initialize(["--culture", "en-US", "--showInstallBanner"]);

        Assert.AreEqual("en-US", DebugFlags.CultureOverride);
        Assert.IsTrue(DebugFlags.ShowInstallBanner,
            "The flag AFTER the --culture value must still be parsed normally.");
    }

    [TestMethod]
    public void TryValidateCulture_RealSpecificCulture_ReturnsTrueAndCanonical()
    {
        Assert.IsTrue(DebugFlags.TryValidateCulture("en-US", out string canonical));
        Assert.AreEqual("en-US", canonical);
    }

    [TestMethod]
    public void TryValidateCulture_LowercaseInput_CanonicalisesCase()
    {
        Assert.IsTrue(DebugFlags.TryValidateCulture("en-us", out string canonical));
        Assert.AreEqual("en-US", canonical);
    }

    [TestMethod]
    public void TryValidateCulture_NeutralCulture_ReturnsFalse()
    {
        Assert.IsFalse(DebugFlags.TryValidateCulture("en", out string _));
    }

    [TestMethod]
    public void TryValidateCulture_Gibberish_ReturnsFalse()
    {
        Assert.IsFalse(DebugFlags.TryValidateCulture("xx-XX", out string _));
        Assert.IsFalse(DebugFlags.TryValidateCulture("not-a-real-code", out string _));
        Assert.IsFalse(DebugFlags.TryValidateCulture("", out string _));
        Assert.IsFalse(DebugFlags.TryValidateCulture("   ", out string _));
    }

    [TestMethod]
    public void ResetForTesting_ClearsCultureOverride()
    {
        DebugFlags.Initialize(["--culture", "en-US"]);
        Assert.AreEqual("en-US", DebugFlags.CultureOverride);

        DebugFlags.ResetForTesting();

        Assert.IsNull(DebugFlags.CultureOverride);
    }

    // ──────────────────────────────────────────────────────────────────────
    // --simulate-update — QA / dev-loop flag for the auto-update banner.
    //
    // 2026-05-29 — was previously a two-token flag (--simulate-update vX.Y.Z)
    // that captured a tag string.  Switched to a zero-argument boolean
    // so QA never has to pick a version that's actually newer than the
    // running auto-versioned build.  The synth path now computes the
    // "next" tag at check time by incrementing the assembly version's
    // rightmost segment — see AppUpdateService.SynthesiseSimulatedNextVersion.
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Initialize_SimulateUpdate_SetsTheBooleanFlag()
    {
        DebugFlags.Initialize(["--simulate-update"]);
        Assert.IsTrue(DebugFlags.SimulateUpdate,
            "--simulate-update must set the boolean flag — no argument required.");
    }

    [TestMethod]
    public void Initialize_SimulateUpdate_CaseInsensitiveFlagName()
    {
        DebugFlags.Initialize(["--SIMULATE-UPDATE"]);
        Assert.IsTrue(DebugFlags.SimulateUpdate,
            "Flag name matching is case-insensitive (matches the parser's general convention).");
    }

    [TestMethod]
    public void Initialize_SimulateUpdate_FollowedByOtherFlag_BothApply()
    {
        // Post-change: --simulate-update no longer consumes a following
        // argument.  An arg that looks like another flag should now be
        // processed normally by the outer loop.
        DebugFlags.Initialize(["--simulate-update", "--linux"]);
        Assert.IsTrue(DebugFlags.SimulateUpdate,
            "--simulate-update is consumed independently of any following args.");
        Assert.AreEqual("linux", DebugFlags.EmulatedPlatform,
            "A following --linux must be processed as its own platform flag — " +
            "no greedy two-token consumption.");
    }

    [TestMethod]
    public void Initialize_NoSimulateUpdate_LeavesFlagFalse()
    {
        DebugFlags.Initialize(["--showAllNew"]);
        Assert.IsFalse(DebugFlags.SimulateUpdate,
            "SimulateUpdate defaults to false when the flag is absent.");
    }

    [TestMethod]
    public void ResetForTesting_ClearsSimulateUpdate()
    {
        DebugFlags.Initialize(["--simulate-update"]);
        Assert.IsTrue(DebugFlags.SimulateUpdate);

        DebugFlags.ResetForTesting();

        Assert.IsFalse(DebugFlags.SimulateUpdate);
    }

    // -----------------------------------------------------------------------
    // --deep-link <path>
    //
    // Two-token flag that launches the GUI at a specific page / tab / item.
    // Validation here is SHAPE-only (NavDeepPath.TryParse); whether the path
    // names a page this install actually has is decided later against the built
    // navigation tree, because a stale shortcut must never block startup.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Initialize_NoDeepLinkFlag_LeavesPathNull()
    {
        DebugFlags.Initialize([]);
        Assert.IsNull(DebugFlags.DeepLinkPath);
    }

    [TestMethod]
    public void Initialize_DeepLink_AcceptsAWellFormedPath()
    {
        DebugFlags.Initialize(["--deep-link", "agents-skills/skills/pdf"]);
        Assert.AreEqual("agents-skills/skills/pdf", DebugFlags.DeepLinkPath);
    }

    [TestMethod]
    public void Initialize_DeepLink_AcceptsAPageOnlyPath()
    {
        DebugFlags.Initialize(["--deep-link", "essentials"]);
        Assert.AreEqual("essentials", DebugFlags.DeepLinkPath);
    }

    [TestMethod]
    public void Initialize_DeepLink_CaseInsensitiveFlagName()
    {
        DebugFlags.Initialize(["--DEEP-LINK", "essentials"]);
        Assert.AreEqual("essentials", DebugFlags.DeepLinkPath);
    }

    [TestMethod]
    public void Initialize_DeepLink_MissingValue_IsIgnored()
    {
        // Last arg with nothing after it — must not throw or index past the end.
        DebugFlags.Initialize(["--deep-link"]);
        Assert.IsNull(DebugFlags.DeepLinkPath);
    }

    [TestMethod]
    public void Initialize_DeepLink_MalformedValue_IsRejected()
    {
        DebugFlags.Initialize(["--deep-link", "/leading-separator"]);
        Assert.IsNull(DebugFlags.DeepLinkPath, "A malformed path must be rejected, not stored.");

        DebugFlags.ResetForTesting();
        DebugFlags.Initialize(["--deep-link", "a/b/c/d/e"]);
        Assert.IsNull(DebugFlags.DeepLinkPath, "Too many segments must be rejected.");

        DebugFlags.ResetForTesting();
        DebugFlags.Initialize(["--deep-link", "a//b"]);
        Assert.IsNull(DebugFlags.DeepLinkPath, "An empty interior segment must be rejected.");
    }

    [TestMethod]
    public void Initialize_DeepLink_ValueThenNextFlag_BothProcessed()
    {
        // The value must be consumed WITHOUT swallowing the flag that follows it.
        DebugFlags.Initialize(["--deep-link", "essentials", "--linux"]);

        Assert.AreEqual("essentials", DebugFlags.DeepLinkPath);
        Assert.AreEqual("linux", DebugFlags.EmulatedPlatform,
            "A following --linux must be processed as its own flag — no greedy consumption.");
    }

    [TestMethod]
    public void Initialize_DeepLink_DoesNotSwallowAFollowingFlagAsItsValue()
    {
        // "--linux" is a legal NavDeepPath shape, so without care it would be
        // accepted as the deep-link value and the platform flag would vanish.
        // Documented behaviour: the value IS consumed positionally, so a user who
        // omits the path gets the next flag eaten. Assert what actually happens so
        // the trade-off is visible rather than accidental.
        DebugFlags.Initialize(["--deep-link", "--linux"]);

        Assert.AreEqual("--linux", DebugFlags.DeepLinkPath,
            "Positional consumption means a missing value eats the next token; " +
            "the resulting path simply fails to resolve later.");
        Assert.IsNull(DebugFlags.EmulatedPlatform);
    }

    [TestMethod]
    public void ResetForTesting_ClearsDeepLinkPath()
    {
        DebugFlags.Initialize(["--deep-link", "essentials"]);
        Assert.IsNotNull(DebugFlags.DeepLinkPath);

        DebugFlags.ResetForTesting();

        Assert.IsNull(DebugFlags.DeepLinkPath);
    }

    // ── --writer <legacy|jsonc> ──────────────────────────────────────────────
    //
    // The one-release escape hatch for the comment-preserving writer. Unlike
    // --deep-link, an unrecognised value must NOT be accepted positionally: the two
    // writers produce different bytes, and a typo silently selecting the lossy one is
    // exactly the failure this flag exists to protect against.

    [TestMethod]
    public void Initialize_NoWriterFlag_LeavesWriterUnset()
    {
        DebugFlags.Initialize([]);

        Assert.IsNull(DebugFlags.ConfigWriterName,
            "Unset means the default comment-preserving writer.");
    }

    [TestMethod]
    public void Initialize_WriterLegacy_SelectsLegacy()
    {
        DebugFlags.Initialize(["--writer", "legacy"]);

        Assert.AreEqual("legacy", DebugFlags.ConfigWriterName);
    }

    [TestMethod]
    public void Initialize_WriterJsonc_IsAcceptedAndNormalized()
    {
        // Accepted so a script can pin the default explicitly and the flag reads
        // symmetrically; it resolves to the same writer as omitting the flag.
        DebugFlags.Initialize(["--writer", "JSONC"]);

        Assert.AreEqual("jsonc", DebugFlags.ConfigWriterName,
            "Value should be normalized to lower case so downstream comparisons are ordinal.");
    }

    [TestMethod]
    public void Initialize_WriterUnknownValue_FallsBackToTheSafeWriter()
    {
        DebugFlags.Initialize(["--writer", "legcy"]);

        Assert.IsNull(DebugFlags.ConfigWriterName,
            "A typo must fall back to the preserving writer, never to the lossy one.");
    }

    [TestMethod]
    public void Initialize_WriterMissingValue_DoesNotEatTheNextFlag()
    {
        // Contrast with --deep-link, which consumes positionally by design. Here the
        // value is validated against a closed set, so a following flag is rejected as a
        // writer name and still takes effect as a flag.
        DebugFlags.Initialize(["--writer", "--linux"]);

        Assert.IsNull(DebugFlags.ConfigWriterName);
        Assert.AreEqual("linux", DebugFlags.EmulatedPlatform,
            "Validation against a closed set means the swallowed token is not silently lost "
            + "the way an unvalidated positional value would be.");
    }

    [TestMethod]
    public void Initialize_WriterAtEndOfArgs_IsIgnoredWithoutThrowing()
    {
        DebugFlags.Initialize(["--writer"]);

        Assert.IsNull(DebugFlags.ConfigWriterName);
    }

    [TestMethod]
    public void ResetForTesting_ClearsConfigWriterName()
    {
        DebugFlags.Initialize(["--writer", "legacy"]);
        Assert.IsNotNull(DebugFlags.ConfigWriterName);

        DebugFlags.ResetForTesting();

        Assert.IsNull(DebugFlags.ConfigWriterName,
            "Static flag state must not bleed into the next test — a leaked 'legacy' here "
            + "would silently make other tests assert against the lossy writer.");
    }

    // ── The --debug-help text must match what Initialize actually parses ──

    /// <summary>The source of <c>DebugFlags.cs</c>, which is the only place the pairing lives.</summary>
    /// <remarks>
    /// Source text rather than reflection: a <c>case "--x":</c> label leaves no metadata to
    /// reflect over, and the help string is a literal inside a switch arm. There is nothing at
    /// runtime that relates the two.
    /// </remarks>
    private static string DebugFlagsSource()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "src", "ClaudeForge", "Services", "DebugFlags.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate DebugFlags.cs by walking up from '{AppContext.BaseDirectory}'.");
    }

    /// <summary>Every <c>case "--x":</c> label in the file, lower-cased as the switch sees them.</summary>
    private static HashSet<string> ParsedFlagNames()
    {
        MatchCollection matches = Regex.Matches(
            DebugFlagsSource(), @"case\s+""(--[a-z0-9-]+)""\s*:", RegexOptions.CultureInvariant);

        return [.. matches.Select(m => m.Groups[1].Value)];
    }

    /// <summary>Every flag the <c>--debug-help</c> output advertises.</summary>
    private static HashSet<string> AdvertisedFlagNames()
    {
        // The help text is the concatenated string literals in the --debug-help arm. Take the
        // whole file and pull flag-shaped tokens out of the "available flags:" message only, so
        // a flag named in an ordinary comment cannot count as documented.
        Match message = Regex.Match(
            DebugFlagsSource(),
            @"available flags:(?<body>.*?)""\s*\)\s*;",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.IsTrue(message.Success,
            "Could not find the \"available flags:\" message in DebugFlags.cs. If it was "
            + "reworded, update this scan — otherwise both directions below pass vacuously.");

        return
        [
            .. Regex.Matches(message.Groups["body"].Value, @"--[a-zA-Z0-9-]+",
                    RegexOptions.CultureInvariant)
                .Select(m => m.Value.ToLowerInvariant()),
        ];
    }

    /// <summary>
    /// Every flag <c>Initialize</c> parses is advertised by <c>--debug-help</c>.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Nothing enforced this, and the absence cost real time.</b> <c>AGENTS.md</c>'s
    /// flag-adding checklist says to update the help text, but a checklist is not a guard: a new
    /// <c>case</c> ships undiscoverable and the only symptom is a flag nobody can find. The
    /// author of this test first asserted, from a truncated grep, that
    /// <c>--showInstallBanner</c> was missing. It was not — reading the file settled in seconds
    /// what a guess got wrong, which is the argument for having the check at all.
    /// </remarks>
    [TestMethod]
    public void EveryFlagInitializeParses_IsAdvertisedByDebugHelp()
    {
        HashSet<string> parsed = ParsedFlagNames();
        HashSet<string> advertised = AdvertisedFlagNames();

        Assert.IsTrue(parsed.Count > 5,
            $"Only {parsed.Count} flag case(s) were found; the scan has lost its subject.");

        // --help-debug is an alias of --debug-help and needs no separate line.
        List<string> undocumented =
        [
            .. parsed
                .Where(f => !advertised.Contains(f))
                .Where(f => f != "--help-debug")
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(0, undocumented.Count,
            $"{undocumented.Count} flag(s) are parsed but not listed by --debug-help: "
            + string.Join(", ", undocumented)
            + ". A flag nobody can discover is a flag nobody uses. Add it to the "
            + "\"available flags:\" message.");
    }

    /// <summary>
    /// The reverse: <c>--debug-help</c> advertises nothing <c>Initialize</c> does not parse.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>This is the direction that found a real defect.</b> The help text listed
    /// <c>--cleanup-restore-sidecars</c>, which <c>DebugFlags</c> does not parse at all — it is a
    /// CLI-bypass tool dispatched in <c>Program.cs</c> above <c>BuildAvaloniaApp()</c>.
    /// <c>AGENTS.md</c> is explicit that the two are "conceptually different" and that a tool
    /// belongs in the CLI-bypass section, NOT the debug-flags table. Advertising it here told a
    /// user it was a debug flag and told the next maintainer to look for a <c>case</c> that does
    /// not exist.
    /// </remarks>
    [TestMethod]
    public void DebugHelpAdvertisesNothingItCannotParse()
    {
        HashSet<string> parsed = ParsedFlagNames();
        HashSet<string> advertised = AdvertisedFlagNames();

        Assert.IsTrue(advertised.Count > 5,
            $"Only {advertised.Count} advertised flag(s) were found; the scan has lost its subject.");

        List<string> phantom =
        [
            .. advertised.Where(f => !parsed.Contains(f)).Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(0, phantom.Count,
            $"--debug-help advertises {phantom.Count} name(s) DebugFlags.Initialize does not "
            + "parse: " + string.Join(", ", phantom)
            + ". Either it is a CLI-bypass tool — which belongs in its own list, not this one, "
            + "per AGENTS.md — or the flag was renamed and the help text was not.");
    }
    // ── --schema-source ───────────────────────────────────────────────

    [TestMethod]
    [DataRow("bundled")]
    [DataRow("fetched")]
    [DataRow("BUNDLED")]
    public void SchemaSource_AcceptsEitherValue_CaseInsensitively(string value)
    {
        DebugFlags.Initialize(["--schema-source", value]);

        Assert.AreEqual(value.ToLowerInvariant(), DebugFlags.SchemaSourceName,
            "The value is normalised to lower case so the Program.cs switch can match literals.");
    }

    [TestMethod]
    public void SchemaSource_RejectsAnUnknownValue()
    {
        DebugFlags.Initialize(["--schema-source", "disk"]);

        Assert.IsNull(DebugFlags.SchemaSourceName,
            "An unrecognised source must leave the normal loading chain in place rather than "
            + "guessing which branch the user meant.");
    }

    [TestMethod]
    public void SchemaSource_WithNoValue_IsIgnored()
    {
        DebugFlags.Initialize(["--schema-source"]);

        Assert.IsNull(DebugFlags.SchemaSourceName);
    }

    /// <summary>
    /// ⭐ A rejected value must not SWALLOW the next flag.
    /// </summary>
    /// <remarks>
    /// The two-token flags split into two families and this one is in the PEEK family, like
    /// <c>--writer</c>: because the value set is closed, a token that is not a valid value may
    /// still be a valid flag in its own right. Consuming positionally — correct for
    /// <c>--deep-link</c>, where any string is a plausible path — would silently discard it.
    /// </remarks>
    [TestMethod]
    public void SchemaSource_WithAFlagAsItsValue_RejectsTheValueAndHonoursTheFlag()
    {
        DebugFlags.Initialize(["--schema-source", "--linux"]);

        Assert.IsNull(DebugFlags.SchemaSourceName, "'--linux' is not a schema source.");
        Assert.AreEqual("linux", DebugFlags.EmulatedPlatform,
            "--linux was swallowed as --schema-source's value instead of being honoured.");
    }

    [TestMethod]
    public void SchemaSource_AppearsInTheActiveFlagList()
    {
        DebugFlags.Initialize(["--schema-source", "bundled"]);

        DebugFlags.LogActiveFlags();

        Assert.AreEqual("bundled", DebugFlags.SchemaSourceName,
            "Premise: the flag must be set for the listing to have anything to report.");
    }

    [TestMethod]
    public void SchemaSource_IsClearedByResetForTesting()
    {
        DebugFlags.Initialize(["--schema-source", "fetched"]);
        Assert.AreEqual("fetched", DebugFlags.SchemaSourceName, "Premise: it must be set first.");

        DebugFlags.ResetForTesting();

        Assert.IsNull(DebugFlags.SchemaSourceName,
            "A flag left set after a reset leaks into whatever test runs next.");
    }
}
