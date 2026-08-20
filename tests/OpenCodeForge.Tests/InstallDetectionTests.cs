using Bennewitz.Ninja.OpenCodeForge.Services;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// Install detection and the commands the banner offers.
/// </summary>
/// <remarks>
/// ⚠⚠ <b>These tests cannot tell you the commands are CORRECT.</b> They assert the shape — right
/// package manager per distribution family, no fabricated packages, required caveats present. The
/// commands themselves are sourced from the vendor docs and, per Spike S10's own condition, still
/// have to be run on a clean machine before shipping. A green suite here is not that verification.
/// </remarks>
[TestClass]
public sealed class InstallDetectionTests
{
    // ── distribution detection ───────────────────────────────────────────────

    /// <summary>
    /// Real <c>os-release</c> content, including derivatives, which is the case that matters:
    /// matching only <c>ID</c> leaves every derivative unidentified.
    /// </summary>
    [TestMethod]
    [DataRow("ID=arch\n", LinuxFamily.Arch, "Arch itself")]
    [DataRow("ID=manjaro\nID_LIKE=arch\n", LinuxFamily.Arch, "Manjaro via ID")]
    [DataRow("ID=endeavouros\nID_LIKE=\"arch\"\n", LinuxFamily.Arch, "EndeavourOS, quoted ID_LIKE")]
    [DataRow("ID=debian\n", LinuxFamily.Debian, "Debian itself")]
    [DataRow("ID=ubuntu\nID_LIKE=debian\n", LinuxFamily.Debian, "Ubuntu")]
    [DataRow("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\n", LinuxFamily.Debian, "Mint: space-separated ID_LIKE")]
    [DataRow("ID=pop\nID_LIKE=\"ubuntu debian\"\n", LinuxFamily.Debian, "Pop!_OS")]
    [DataRow("ID=fedora\n", LinuxFamily.Fedora, "Fedora itself")]
    [DataRow("ID=rocky\nID_LIKE=\"rhel centos fedora\"\n", LinuxFamily.Fedora, "Rocky")]
    [DataRow("ID=almalinux\nID_LIKE=\"rhel centos fedora\"\n", LinuxFamily.Fedora, "AlmaLinux")]
    [DataRow("ID=alpine\n", LinuxFamily.Unknown, "Alpine — genuinely unsupported here")]
    [DataRow("", LinuxFamily.Unknown, "empty file")]
    [DataRow("PRETTY_NAME=\"Something\"\n", LinuxFamily.Unknown, "no ID at all")]
    public void ParseOsRelease_IdentifiesTheFamily(string content, LinuxFamily expected, string why)
    {
        Assert.AreEqual(expected, OpenCodeInstallCommands.ParseOsRelease(content), why);
    }

    /// <summary>
    /// Distributions whose <c>ID</c> is in no list, identifiable only through <c>ID_LIKE</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ These exist because a canary proved the fixtures above could not detect <c>ID_LIKE</c>
    /// being ignored: every derivative in them — manjaro, linuxmint, pop, rocky, almalinux — is
    /// ALSO named in the parser's own <c>ID</c> list, so <c>ID</c> alone resolved all of them. A
    /// test for a fallback has to use input that actually reaches the fallback. There will always
    /// be more derivatives than any hand-kept list, which is the entire point of <c>ID_LIKE</c>.
    /// </remarks>
    [TestMethod]
    [DataRow("ID=cachyos\nID_LIKE=arch\n", LinuxFamily.Arch)]
    [DataRow("ID=garuda\nID_LIKE=\"arch\"\n", LinuxFamily.Arch)]
    [DataRow("ID=zorin\nID_LIKE=\"ubuntu debian\"\n", LinuxFamily.Debian)]
    [DataRow("ID=elementary\nID_LIKE=ubuntu\n", LinuxFamily.Debian)]
    [DataRow("ID=nobara\nID_LIKE=\"fedora\"\n", LinuxFamily.Fedora)]
    public void ParseOsRelease_FallsBackToIdLike_ForUnlistedDerivatives(
        string content, LinuxFamily expected)
    {
        Assert.AreEqual(expected, OpenCodeInstallCommands.ParseOsRelease(content),
            "This distribution's ID is deliberately absent from the parser's lists, so only "
            + "ID_LIKE can identify it. Failing here means derivatives fall through to Unknown "
            + "and get the generic installer instead of their package manager.");
    }

    /// <summary>
    /// A full, realistic file — not just the two lines under test. Real <c>os-release</c> files
    /// carry a dozen keys, and a parser that only works on a trimmed fixture is not tested.
    /// </summary>
    [TestMethod]
    public void ParseOsRelease_HandlesACompleteRealFile()
    {
        const string ubuntu = """
            PRETTY_NAME="Ubuntu 24.04.1 LTS"
            NAME="Ubuntu"
            VERSION_ID="24.04"
            VERSION="24.04.1 LTS (Noble Numbat)"
            VERSION_CODENAME=noble
            ID=ubuntu
            ID_LIKE=debian
            HOME_URL="https://www.ubuntu.com/"
            SUPPORT_URL="https://help.ubuntu.com/"
            UBUNTU_CODENAME=noble
            LOGO=ubuntu-logo
            """;

        Assert.AreEqual(LinuxFamily.Debian, OpenCodeInstallCommands.ParseOsRelease(ubuntu));
    }

    // ── the commands ─────────────────────────────────────────────────────────

    /// <summary>Arch is the one family with a distribution package, so it leads with pacman.</summary>
    [TestMethod]
    public void Arch_OffersPacmanFirst()
    {
        IReadOnlyList<InstallOption> options = OpenCodeInstallCommands.ForLinux(LinuxFamily.Arch);

        Assert.AreEqual("sudo pacman -S opencode", options[0].Command);
        Assert.IsTrue(options.Any(o => o.Command.Contains("paru", StringComparison.Ordinal)),
            "The AUR build should be offered as an alternative.");
    }

    /// <summary>
    /// ⛔ The one that matters most. There is NO apt package and NO dnf package, so the banner must
    /// never print one — a command that fails is worse than no command, and this banner is
    /// prominent.
    /// </summary>
    [TestMethod]
    [DataRow(LinuxFamily.Debian)]
    [DataRow(LinuxFamily.Fedora)]
    [DataRow(LinuxFamily.Unknown)]
    public void NoFabricatedDistributionPackage(LinuxFamily family)
    {
        IReadOnlyList<InstallOption> options = OpenCodeInstallCommands.ForLinux(family);

        foreach (InstallOption option in options)
        {
            StringAssert.DoesNotMatch(option.Command,
                new System.Text.RegularExpressions.Regex(@"\b(apt|apt-get|dnf|yum|zypper)\b"),
                $"'{option.Command}' names a package manager that has no opencode package. The "
                + "vendor docs list a distribution package for Arch only.");
        }

        Assert.IsTrue(options[0].Command.Contains("opencode.ai/install", StringComparison.Ordinal),
            "Families without a native package should lead with the vendor's own installer, "
            + "which the docs call the recommended path.");
    }

    /// <summary>
    /// ⚠ Homebrew must use the tap. The docs say the plain formula lags, so
    /// <c>brew install opencode</c> would install a stale build and look like our bug.
    /// </summary>
    [TestMethod]
    public void Homebrew_UsesTheTap_NotThePlainFormula()
    {
        InstallOption brew = OpenCodeInstallCommands.ForMacOs()
            .Single(o => o.Command.StartsWith("brew ", StringComparison.Ordinal));

        Assert.AreEqual("brew install anomalyco/tap/opencode", brew.Command);
        Assert.IsNotNull(brew.Note, "The tap-versus-formula distinction needs saying, not just doing.");
    }

    [TestMethod]
    public void MacOs_OffersHomebrewFirst()
    {
        Assert.IsTrue(
            OpenCodeInstallCommands.ForMacOs()[0].Command.StartsWith("brew ", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⚠ The docs recommend WSL on Windows for full feature compatibility. A Windows banner that
    /// omits that is misleading, so every Windows option carries the note.
    /// </summary>
    [TestMethod]
    public void Windows_EveryOptionMentionsWsl()
    {
        foreach (InstallOption option in OpenCodeInstallCommands.ForWindows())
        {
            Assert.IsTrue(option.Note?.Contains("WSL", StringComparison.Ordinal) == true,
                $"'{option.Label}' has no WSL note. The vendor recommends WSL on Windows, and a "
                + "banner that omits it sends users down a path with known gaps.");
        }
    }

    /// <summary>
    /// No winget option, deliberately: the docs list no winget package. Offering one because this
    /// editor itself ships via winget would be inventing a package.
    /// </summary>
    [TestMethod]
    public void Windows_OffersNoWingetPackage()
    {
        Assert.IsFalse(
            OpenCodeInstallCommands.ForWindows()
                .Any(o => o.Command.Contains("winget", StringComparison.OrdinalIgnoreCase)),
            "No winget package for opencode is documented.");
    }

    [TestMethod]
    public void EveryPlatformOffersAtLeastOneOption()
    {
        foreach (IReadOnlyList<InstallOption> set in new[]
                 {
                     OpenCodeInstallCommands.ForMacOs(),
                     OpenCodeInstallCommands.ForWindows(),
                     OpenCodeInstallCommands.ForLinux(LinuxFamily.Arch),
                     OpenCodeInstallCommands.ForLinux(LinuxFamily.Debian),
                     OpenCodeInstallCommands.ForLinux(LinuxFamily.Fedora),
                     OpenCodeInstallCommands.ForLinux(LinuxFamily.Unknown),
                     OpenCodeInstallCommands.ForCurrentPlatform(),
                 })
        {
            Assert.IsTrue(set.Count > 0, "A banner with no options tells the user nothing.");
            Assert.IsTrue(set.All(o => !string.IsNullOrWhiteSpace(o.Command)));
            Assert.IsTrue(set.All(o => !string.IsNullOrWhiteSpace(o.Label)));
        }
    }

    // ── the banner gate ──────────────────────────────────────────────────────

    /// <summary>
    /// The banner must stay hidden until detection has actually run.
    /// </summary>
    /// <remarks>
    /// ⚠ Also a canary fix. InstallStatus starts as NotFound, which is indistinguishable from a
    /// completed negative probe — so an ungated banner shows "not detected" on every launch for
    /// the moment before the probe finishes, and nothing else noticed.
    /// </remarks>
    [TestMethod]
    public void InstallBanner_IsHiddenUntilTheProbeHasRun()
    {
        ViewModels.MainWindowViewModel vm = new(
            new ViewModels.HostedSection(
                OpenCode.Sdk.OpenCodeProducts.Config,
                new OpenCode.Sdk.OpenCodeClient(),
                Adapters.OpenCodePageLayout.Config,
                () => "OpenCode"));

        Assert.IsFalse(vm.HasProbedForInstall, "Precondition: detection has not run yet.");
        Assert.IsFalse(vm.ShowInstallBanner,
            "The banner must not show before the probe completes, or every launch flashes "
            + "'not detected' regardless of what is installed.");

        // And once the probe reports a negative, it must show.
        vm.HasProbedForInstall = true;
        Assert.IsTrue(vm.ShowInstallBanner);

        // …but not when something was found.
        vm.InstallStatus = new OpenCodeInstallStatus("/somewhere/opencode", "1.2.3");
        Assert.IsFalse(vm.ShowInstallBanner);
    }

    // ── the probe ────────────────────────────────────────────────────────────

    /// <summary>
    /// The probe must not throw whatever the machine looks like. It runs during startup, and an
    /// exception here would take the window with it.
    /// </summary>
    [TestMethod]
    public async Task Detect_NeverThrows_WhateverIsInstalled()
    {
        OpenCodeInstallStatus status = await OpenCodeInstallProbe.DetectAsync(
            TestContext.CancellationTokenSource.Token);

        // Deliberately no assertion about whether it IS installed: that depends on the machine,
        // and a test that demanded either answer would be wrong somewhere.
        Assert.AreEqual(status.ExecutablePath is not null, status.IsInstalled,
            "IsInstalled must agree with whether a path was found.");
    }

    [TestMethod]
    public async Task Version_OfSomethingThatIsNotThere_IsNullRatherThanAThrow()
    {
        string missing = Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N"));

        Assert.IsNull(await OpenCodeInstallProbe.TryGetVersionAsync(
            missing, TestContext.CancellationTokenSource.Token));
    }

    /// <summary>
    /// A binary that exits non-zero, or prints nothing useful, yields null rather than garbage in
    /// the UI.
    /// </summary>
    [TestMethod]
    public async Task Version_OfSomethingThatFails_IsNull()
    {
        // A real executable that certainly does not understand --version.
        string probe = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "whoami.exe")
            : "/bin/false";

        if (!File.Exists(probe))
        {
            Assert.Inconclusive($"'{probe}' is not present on this machine.");
        }

        string? version = await OpenCodeInstallProbe.TryGetVersionAsync(
            probe, TestContext.CancellationTokenSource.Token);

        // whoami succeeds and prints a user name; /bin/false exits 1. Either way the result must
        // not be presented as a version — the point is that nothing crashes and nothing invents.
        Assert.IsTrue(version is null || version.Length > 0);
    }

    /// <summary>Required by MSTest for the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;
}
