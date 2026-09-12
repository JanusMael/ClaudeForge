using System.Xml.Linq;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The Backup / Restore page's place in this window, and the three things about it that fail
/// silently.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The engine is the one that matters.</b> A backup takes its products from the request, so
/// <see cref="BackupEngine.Default"/> writes an OpenCode archive perfectly happily; a restore is
/// driven by the archive and resolves its folder names through the engine's own restorable-product
/// list, which on the default engine is Claude Code and Claude Desktop. Wiring the wrong engine
/// here produces a one-way backup that reports success at both ends — the archive exists, the
/// restore says it finished, and not one file comes back.
/// <c>OpenCodeBackupRoundTripTests</c> proves the engines differ; nothing before this proved the
/// PAGE picked the right one.
/// </para>
/// <para>
/// ⚠ <b>The two deliberate deviations from ClaudeForge's view are asserted as markup</b>, because
/// both are omissions and an omission cannot be observed from a running view-model. The MSIX tab
/// and the third scope radio are absent on purpose; a future copy-paste from the sibling view
/// would restore them and nothing else would notice.
/// </para>
/// <para>
/// ⚠ Every test that drives <see cref="MainWindowViewModel.InitializeAsync"/> does so against a
/// redirected sandbox, so none of them can read or write the developer's own OpenCode
/// configuration — and none of them writes an archive.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeBackupWiringTests
{
    private string _sandbox = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ocbackup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", "1");
        File.WriteAllText(Path.Combine(_sandbox, "opencode.json"), """{ "autoupdate": "notify" }""");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", null);
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private static MainWindowViewModel BuildViewModel() => new(
        new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
            OpenCodePageLayout.Config, () => Strings.SectionOpenCode),
        new HostedSection(OpenCodeProducts.Tui, new OpenCodeTuiClient(),
            OpenCodePageLayout.Tui, () => Strings.SectionOpenCodeTui));

    private async Task<MainWindowViewModel> InitializedAsync()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);
        return vm;
    }

    private static BackupRestoreViewModel BackupPageOf(MainWindowViewModel vm)
    {
        NavigationNodeViewModel node =
            vm.Navigation.FirstOrDefault(
                n => string.Equals(n.NodeId, MainWindowViewModel.BackupNodeId, StringComparison.Ordinal))
            ?? throw new AssertFailedException("No Backup / Restore node in the navigation tree.");

        return node.Editor as BackupRestoreViewModel
            ?? throw new AssertFailedException(
                $"The Backup node's editor is '{node.Editor?.GetType().Name ?? "null"}'.");
    }

    // ── The page exists and is reachable ────────────────────────────────────

    [TestMethod]
    public async Task TheBackupPageIsATopLevelNodeOwningTheSharedViewModel()
    {
        MainWindowViewModel vm = await InitializedAsync();

        NavigationNodeViewModel node =
            vm.Navigation.FirstOrDefault(
                n => string.Equals(n.NodeId, MainWindowViewModel.BackupNodeId, StringComparison.Ordinal))
            ?? throw new AssertFailedException("No Backup / Restore node in the navigation tree.");

        Assert.IsTrue(node.IsTopLevel, "The Backup page is a tool page, not a child of a section.");
        Assert.IsInstanceOfType<BackupRestoreViewModel>(node.Editor);
        Assert.AreEqual(Strings.HeadingBackupRestore, node.Title);
    }

    /// <summary>
    /// The page survives being rebuilt, because disposing it cancels an in-flight backup.
    /// </summary>
    [TestMethod]
    public async Task TheBackupPageViewModelIsCachedRatherThanRebuilt()
    {
        MainWindowViewModel vm = await InitializedAsync();
        BackupRestoreViewModel first = BackupPageOf(vm);

        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        Assert.AreSame(first, BackupPageOf(vm),
            "A second initialise built a new Backup page; a backup running at the time would have "
            + "been cancelled by the old one's Dispose.");
    }

    // ── ⛔ The engine ───────────────────────────────────────────────────────

    [TestMethod]
    public void ThePageBacksUpThroughOpenCodesOwnEngine()
    {
        BackupPageOptions options = OpenCodeBackupPage.Options(OpenCodeProducts.All);

        Assert.AreSame(OpenCodeBackup.Engine, options.Engine,
            "The Backup page must restore through the engine whose restorable products are "
            + "OpenCode's. Any other engine writes archives it cannot read back.");
        Assert.AreNotSame(BackupEngine.Default, options.Engine,
            "BackupEngine.Default restores Claude Code and Claude Desktop only.");
    }

    /// <summary>
    /// No source file in the app reaches for the default engine.
    /// </summary>
    /// <remarks>
    /// A source scan, like <c>ProductionSchemaRegistryTests</c>, and for the same reason: a
    /// <see cref="BackupEngine"/> does not expose which products it can restore, so no assertion
    /// over a constructed instance can tell the right one from the wrong one. What CAN be checked
    /// is that the wrong one is never named.
    /// <para>
    /// ⚠ <b>Comments are blanked before the scan</b>, the same way the repo's dead-string guard
    /// blanks them in <c>Directory.Build.targets</c>, and for the same reason: the file that gets
    /// this most right is the one whose doc comment explains why the default engine is wrong, and
    /// a scan that counted prose would redden on the warning rather than on the mistake.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void NoSourceFileInTheAppNamesTheDefaultEngine()
    {
        string appDir = Path.Combine(RepoRoot(), "src", "OpenCodeForge");
        Assert.IsTrue(Directory.Exists(appDir), $"Premise: {appDir} must exist.");

        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (WithoutComments(File.ReadAllText(file))
                .Contains("BackupEngine.Default", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(appDir, file));
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "BackupEngine.Default restores Claude's products only, so naming it anywhere in this "
            + "app is a one-way backup waiting to happen: " + string.Join(", ", offenders));
    }

    // ── What the page offers ────────────────────────────────────────────────

    [TestMethod]
    public async Task TheProductCheckboxesComeFromTheWindowsOwnSections()
    {
        MainWindowViewModel vm = await InitializedAsync();
        BackupRestoreViewModel page = BackupPageOf(vm);

        CollectionAssert.AreEquivalent(
            vm.Sections.Select(s => s.Product.Id).ToList(),
            page.SelectableProducts.Select(p => p.Product.Id).ToList(),
            "A product this window hosts must be a product the user can back up.");
    }

    /// <summary>The checkbox labels are the nav headings, so the two cannot disagree on a name.</summary>
    [TestMethod]
    public void TheProductCheckboxLabelsAreTheNavigationSectionHeadings()
    {
        BackupPageOptions options = OpenCodeBackupPage.Options(OpenCodeProducts.All);

        Assert.AreEqual(Strings.SectionOpenCode, options.ProductCheckboxLabel(OpenCodeProducts.Config));
        Assert.AreEqual(Strings.SectionOpenCodeTui, options.ProductCheckboxLabel(OpenCodeProducts.Tui));
    }

    /// <summary>
    /// A product the lookup has never heard of falls back to its own display name.
    /// </summary>
    [TestMethod]
    public void AnUnknownProductFallsBackToItsDisplayName()
    {
        BackupPageOptions options = OpenCodeBackupPage.Options(OpenCodeProducts.All);
        ProductDescriptor stranger = new(
            "stranger", "Some Other Agent", "bundled://x", "x.json", ArchiveFolder: "Stranger");

        Assert.AreEqual("Some Other Agent", options.ProductCheckboxLabel(stranger));
    }

    /// <summary>
    /// ⛔ The include-credentials prompt names OpenCode's OWN credential store.
    /// </summary>
    /// <remarks>
    /// The shared page hardcoded <c>~/.claude/.credentials.json</c> until this app arrived. A
    /// prompt that names the wrong file is worse than a vague one: the user's answer is a decision
    /// about something that was never going into the archive. What is actually at stake here is
    /// <c>opencode.db</c> and its two SQLite sidecars — and never <c>auth.json</c>, which is
    /// excluded outright.
    /// </remarks>
    [TestMethod]
    public void TheCredentialsPromptNamesOpenCodesOwnStore()
    {
        BackupPageOptions options = OpenCodeBackupPage.Options(OpenCodeProducts.All);

        StringAssert.Contains(options.CredentialsPathDisplay, "opencode.db", StringComparison.Ordinal);
        Assert.IsFalse(
            options.CredentialsPathDisplay.Contains("claude", StringComparison.OrdinalIgnoreCase),
            "The prompt must not name the other product's credential file.");
        Assert.IsFalse(
            options.CredentialsPathDisplay.Contains("auth.json", StringComparison.OrdinalIgnoreCase),
            "auth.json is never archived, so asking about it would be asking about nothing.");
    }

    [TestMethod]
    public void TheProcessAdvisoryNamesOpenCode()
    {
        BackupPageOptions options = OpenCodeBackupPage.Options(OpenCodeProducts.All);

        CollectionAssert.AreEqual(new[] { "opencode" }, options.AgentProcessNames.ToList());
        StringAssert.Contains(options.Text.StatusAgentRunningFmt, "OpenCode", StringComparison.Ordinal);
    }

    // ── The two deliberate omissions in the markup ──────────────────────────

    /// <summary>
    /// ⛔ No MSIX tab.
    /// </summary>
    /// <remarks>
    /// <c>MsixPathProbe</c> scans <c>%LOCALAPPDATA%\Packages</c> for a <c>Claude_*</c> package, so
    /// the shared view-model's <c>ShowMsixTab</c> goes true on any Windows machine that also has
    /// Claude Desktop installed. Binding it from this app's view would offer, from inside
    /// OpenCodeForge, to repair another vendor's application.
    /// </remarks>
    [TestMethod]
    public void TheViewBindsNothingFromTheMsixSurface()
    {
        string markup = File.ReadAllText(ViewPath());

        foreach (string member in new[] { "ShowMsixTab", "MsixStatus", "FixMsixCommand" })
        {
            Assert.IsFalse(markup.Contains("Binding " + member, StringComparison.Ordinal),
                $"The OpenCodeForge Backup view binds {member}, which is Claude Desktop's.");
        }
    }

    /// <summary>
    /// ⛔ Two scope radios, not the sibling app's three.
    /// </summary>
    /// <remarks>
    /// <c>BackupMode.Full</c> differs from <c>SettingsOnly</c> only by which of a product's
    /// <c>SkippedSubdirs</c> it lets through, and <c>OpenCodeProducts.Config</c> declares none —
    /// the config root's own <c>.gitignore</c> does that work. Offering both would put two radios
    /// on the page that produce byte-identical archives, distinguishable only by the label the
    /// manifest records and the Restore tab's Mode column then displays as if it meant something.
    /// </remarks>
    [TestMethod]
    public void TheScopeRadiosOfferBackupAndSanitizedOnly()
    {
        Assert.IsTrue(
            OpenCodeProducts.Config.Backup.SkippedSubdirs.Count == 0,
            "Premise: this test's whole reason is that OpenCode declares no skipped subdirs. If "
            + "that has changed, Full and SettingsOnly now differ and the third radio belongs back.");

        XDocument doc = XDocument.Load(ViewPath());
        XNamespace ns = "https://github.com/avaloniaui";

        List<string> radios =
        [
            .. doc.Descendants(ns + "RadioButton")
                  .Where(r => string.Equals((string?)r.Attribute("GroupName"), "BackupMode", StringComparison.Ordinal))
                  .Select(r => (string?)r.Attribute("IsChecked") ?? string.Empty)
        ];

        Assert.AreEqual(2, radios.Count, "Expected exactly two backup-scope radios.");
        Assert.IsTrue(radios.Any(r => r.Contains("IsSettingsOnly", StringComparison.Ordinal)));
        Assert.IsTrue(radios.Any(r => r.Contains("IsSanitized", StringComparison.Ordinal)));
        Assert.IsFalse(radios.Any(r => r.Contains("IsFull", StringComparison.Ordinal)),
            "BackupMode.Full captures nothing SettingsOnly does not, for either OpenCode product.");
    }

    /// <summary>
    /// The view carries no literal colours, unlike the sibling app's copy of this page.
    /// </summary>
    /// <remarks>
    /// ClaudeForge's <c>BackupRestoreView.axaml</c> paints its advisory banners with six light-mode
    /// hex literals (<c>#FFF3CD</c> on <c>#664D03</c> and friends). Carried over verbatim they are
    /// unreadable in Semi Dark, and the repo's no-hex guard is scoped to view-models and so would
    /// not have said a word.
    /// </remarks>
    [TestMethod]
    public void TheViewUsesThemeTokensRatherThanLiteralColours()
    {
        string markup = File.ReadAllText(ViewPath());

        List<string> literals =
        [
            .. System.Text.RegularExpressions.Regex
                .Matches(markup, "\"#[0-9A-Fa-f]{3,8}\"")
                .Select(m => m.Value)
        ];

        Assert.AreEqual(0, literals.Count,
            "Literal colours in the Backup view: " + string.Join(", ", literals));
    }

    /// <summary>
    /// Drop whole-line comments so prose about a token is not mistaken for a use of it.
    /// </summary>
    /// <remarks>
    /// Line-based and deliberately simple: every comment in the scanned app is either an XML doc
    /// comment or a <c>//</c> line, and a block comment's continuation lines start with <c>*</c>.
    /// A trailing comment after real code keeps its code, which is the safe direction — this
    /// filter may only ever let a genuine use through, never invent one.
    /// </remarks>
    private static string WithoutComments(string source) =>
        string.Join(
            '\n',
            source.Split('\n')
                  .Where(line =>
                  {
                      string t = line.TrimStart();
                      return !t.StartsWith("///", StringComparison.Ordinal)
                             && !t.StartsWith("//", StringComparison.Ordinal)
                             && !t.StartsWith("/*", StringComparison.Ordinal)
                             && !t.StartsWith('*');
                  }));

    private static string ViewPath() =>
        Path.Combine(RepoRoot(), "src", "OpenCodeForge", "Views", "BackupRestoreView.axaml");

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new AssertFailedException(
            "Could not locate the repository root by walking up from " + AppContext.BaseDirectory);
    }
}
