using Bennewitz.Ninja.ClaudeForge.Core.Platform;
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
}