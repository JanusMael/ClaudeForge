using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

public enum AboutProduct
{
    ClaudeCode,
    ClaudeDesktop
}

public partial class AboutEditorViewModel : ObservableObject
{
    private readonly IShellLauncher _shellLauncher;
    private readonly IDialogService? _dialogService;
    private readonly IEnvironmentProvider _environmentProvider;
    private readonly IShareService? _shareService;
    private readonly Func<string?> _logPathProvider;
    private readonly PlatformPaths.ClaudeCodeLocation? _claudeCodeLocation;

    // Default log-path factory used in production.  Extracted as a static so
    // the lambda allocation happens once, not on every constructor call.
    private static readonly Func<string?> _defaultLogPathProvider =
        () => AvaloniaDiagnostics.CurrentLogFilePath;

    [ObservableProperty] private string? _claudeCodeVersion;
    [ObservableProperty] private string? _claudeDesktopVersion;
    [ObservableProperty] private string _platformInfo = string.Empty;

    /// <summary>
    /// Feedback string rendered next to the "Add to PATH" button after the
    /// user clicks it. Bound inline in the About view; <see langword="null"/>
    /// before the command runs, and one of the localized
    /// <c>TextAddToPath*</c> strings afterwards.
    /// </summary>
    [ObservableProperty] private string? _addToPathResultText;

    /// <summary>
    /// Flipped to <see langword="true"/> once a successful User PATH edit has
    /// been applied in this session (or the directory was already present).
    /// Makes <see cref="ShowClaudeCodePathWarning"/> return <see langword="false"/>
    /// so the caution banner + Add-to-PATH button disappear once the action is
    /// done — otherwise the UI keeps offering an operation that is no longer
    /// needed and users get no visual confirmation beyond the inline result text.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowClaudeCodePathWarning))]
    [NotifyCanExecuteChangedFor(nameof(AddClaudeCodeToPathCommand))]
    private bool _pathWasAddedOrAlreadyPresent;

    public AboutProduct Product { get; }

    /// <summary>
    /// Resolved absolute path to the Claude Code binary if it was found
    /// (either on PATH or at a canonical install location). Surfaced in the
    /// PATH-warning row so the user can see WHERE we detected the install
    /// before pressing "Add to PATH".
    /// </summary>
    public string? ClaudeCodeBinaryPath => _claudeCodeLocation?.BinaryPath;

    /// <summary>
    /// <see langword="true"/> when the binary is reachable via bare-name
    /// <c>claude</c> on PATH. Together with <see cref="ClaudeCodeBinaryPath"/>
    /// this distinguishes the "PATH is fine" case from the "installed but
    /// PATH not updated" case that motivates the Add-to-PATH UI.
    /// </summary>
    public bool IsClaudeCodeOnPath => _claudeCodeLocation?.IsOnPath == true;

    /// <summary>
    /// Gate for the PATH-warning row in the About view: we only show it when
    /// Claude Code was found on disk somewhere but <em>not</em> on the
    /// current PATH. An already-on-PATH install gets no warning; a truly
    /// missing install keeps the existing "(not detected)" + install panel UX.
    /// </summary>
    public bool ShowClaudeCodePathWarning =>
        _claudeCodeLocation is not null
        && !_claudeCodeLocation.IsOnPath
        && !PathWasAddedOrAlreadyPresent;

    /// <summary>
    /// Localized PATH-warning sentence with the detected binary path filled
    /// in. Computed in the VM rather than via <c>StringFormat</c> in XAML
    /// because Avalonia's compiled bindings don't accept <c>x:Static</c>
    /// in the <c>StringFormat</c> slot, and hard-coding the English
    /// format string in XAML would break localization.
    /// </summary>
    public string ClaudeCodeNotOnPathMessage =>
        string.Format(Strings.TextClaudeCodeNotOnPath, ClaudeCodeBinaryPath ?? string.Empty);

    /// <summary>
    /// Indicates whether the product this page represents is installed on
    /// the current machine.  Bound by the view to flip between the "detected,
    /// here is the version" layout and the "(not detected) + install panel"
    /// layout.  Computed at construction; there is no refresh, matching the
    /// one-shot nature of <see cref="LoadVersionsAsync"/>.
    /// </summary>
    public bool IsProductInstalled => Product switch
    {
        // Binary presence, not PlatformPaths.IsClaudeCodeInstalled — that also
        // returns true when ~/.claude/settings.json exists from a prior install,
        // which would keep config buttons enabled when the CLI is not reachable.
        AboutProduct.ClaudeCode => _claudeCodeLocation is not null,
        AboutProduct.ClaudeDesktop => PlatformPaths.IsDesktopInstalled,
        var _ => false,
    };

    /// <summary>
    /// Install-command panel shown beside the Claude Code "(not detected)"
    /// label.  <see langword="null"/> when Claude Code is installed — the view
    /// hides the whole row then.  Exposed independently of
    /// <see cref="ClaudeDesktopInstallPanel"/> because the About page lists
    /// both products regardless of which one this page represents, and the
    /// user should get install guidance for either one that is missing.
    /// </summary>
    public InstallCommandViewModel? ClaudeCodeInstallPanel { get; }

    /// <summary>
    /// Install-command panel shown beside the Claude Desktop "(not detected)"
    /// label.  <see langword="null"/> when Claude Desktop is installed.
    /// Uses the URL-based factory (no shell installer for Desktop).
    /// </summary>
    public InstallCommandViewModel? ClaudeDesktopInstallPanel { get; }

    // Documentation / download links — distinct per product.
    public string DocsUrl => Product == AboutProduct.ClaudeCode
        ? "https://docs.anthropic.com/claude-code"
        : "https://support.anthropic.com";

    public string ReleaseNotesUrl => Product == AboutProduct.ClaudeCode
        ? "https://github.com/anthropics/claude-code/releases"
        : "https://claude.ai/download";

    public string DocsLabel => Product == AboutProduct.ClaudeCode ? Strings.LabelDocumentation : Strings.LabelSupport;

    public string ReleaseNotesLabel =>
        Product == AboutProduct.ClaudeCode ? Strings.LabelReleaseNotes : Strings.LabelDownloadUpdates;

    /// <summary>
    /// Path to the primary config file for this product.
    /// Claude Code: ~/.claude/settings.json; Claude Desktop: claude_desktop_config.json.
    /// </summary>
    private string PrimaryConfigPath => Product == AboutProduct.ClaudeCode
        ? PlatformPaths.UserSettingsPath
        : PlatformPaths.DesktopConfigPath;

    public AboutEditorViewModel(
        AboutProduct product,
        IShellLauncher? shellLauncher = null,
        IDialogService? dialogService = null,
        IEnvironmentProvider? environmentProvider = null,
        IShareService? shareService = null,
        Func<string?>? logPathProvider = null)
    {
        Product = product;
        _shellLauncher = shellLauncher ?? ShellLauncher.Instance;
        _dialogService = dialogService;
        _environmentProvider = environmentProvider ?? new DefaultEnvironmentProvider();
        _shareService = shareService;
        _logPathProvider = logPathProvider ?? _defaultLogPathProvider;
        PlatformInfo = ProductVersionProbe.GetPlatformInfo();

        // Resolve the Claude Code binary once at construction. The resulting
        // ClaudeCodeLocation (or null) drives ClaudeCodeBinaryPath /
        // IsClaudeCodeOnPath / ShowClaudeCodePathWarning and is forwarded to
        // LoadVersionsAsync so the version probe uses the exact binary we
        // located rather than depending on a PATH lookup the user may not have.
        _claudeCodeLocation = PlatformPaths.TryFindClaudeCodeBinary();

        // Seed install panels only for the products that are not currently
        // detected — when a product is installed we hide its entire row, so
        // building the VM would be wasted allocation. Both slots are populated
        // independently since the About page lists Claude Code and Claude
        // Desktop together regardless of which product this page represents.
        //
        // Claude Code: gate on binary detection (_claudeCodeLocation != null).
        // Using IsClaudeCodeInstalled here would cause the install panel to be
        // null (hidden) even when the binary is absent, because that property
        // also returns true when ~/.claude/settings.json exists from a prior
        // install. The binary-presence check is the user-visible meaning of
        // "installed" and keeps the version row and install panel in lockstep.
        if (_claudeCodeLocation is null)
        {
            ClaudeCodeInstallPanel = InstallCommandViewModel.ForClaudeCode(_shellLauncher);
        }

        if (!PlatformPaths.IsDesktopInstalled)
        {
            ClaudeDesktopInstallPanel = InstallCommandViewModel.ForClaudeDesktop();
        }

        _ = LoadVersionsAsync();
    }

    /// <summary>
    /// <see langword="true"/> when the primary config FILE exists, so "Open config"
    /// has a real target. Gated on the file — NOT product-install detection — because
    /// ClaudeForge is a config editor: an existing config should be openable even when
    /// the product binary isn't detected (installed-but-not-on-PATH, or a config
    /// authored before the product is installed). Previously both actions gated on
    /// <see cref="IsProductInstalled"/>, which (a) disabled them whenever the product
    /// wasn't detected even though a config existed, and (b) enabled them against a
    /// MISSING file when only the install directory was found.
    /// </summary>
    private bool CanOpenConfig()
    {
        return File.Exists(PrimaryConfigPath);
    }

    /// <summary>
    /// <see langword="true"/> when the config file OR its parent directory exists.
    /// "Reveal" shows the file when present, otherwise opens the containing folder —
    /// useful when the product is installed but has not written its config yet.
    /// </summary>
    private bool CanRevealConfig()
    {
        return File.Exists(PrimaryConfigPath)
               || Directory.Exists(Path.GetDirectoryName(PrimaryConfigPath));
    }

    [RelayCommand(CanExecute = nameof(CanOpenConfig))]
    private void OpenConfig()
    {
        _shellLauncher.OpenInDefaultEditor(PrimaryConfigPath);
    }

    [RelayCommand(CanExecute = nameof(CanRevealConfig))]
    private void RevealConfig()
    {
        // Reveal the file when it exists; otherwise open the containing folder so the
        // user can still get to where the config lives (or will live).
        if (File.Exists(PrimaryConfigPath))
        {
            _shellLauncher.RevealInFileManager(PrimaryConfigPath);
        }
        else if (Path.GetDirectoryName(PrimaryConfigPath) is { } dir)
        {
            _shellLauncher.RevealInFileManager(dir);
        }
    }

    /// <summary>True when a log file path is known and a share service is wired up.</summary>
    private bool CanShareLog()
    {
        return _shareService is not null && _logPathProvider() is not null;
    }

    /// <summary>
    /// Shares the current log file via the OS share panel.
    /// Useful for attaching logs to bug reports without manual file navigation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShareLog))]
    private async Task ShareLogAsync()
    {
        string? logPath = _logPathProvider();
        if (logPath is null || _shareService is null)
        {
            return;
        }

        try
        {
            Log.Information("[About] Share log requested: {LogPath}", logPath);
            await _shareService.ShareFileAsync("ClaudeForge Log", logPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[About] Share log failed for {LogPath}", logPath);
        }
    }

    /// <summary>
    /// <see langword="true"/> when the "Add to PATH" button should be clickable:
    /// the PATH warning is visible AND the current platform supports editing
    /// the persistent per-user PATH without elevation.
    /// <para>
    /// Windows: writes <c>HKCU\Environment\Path</c> via <see cref="IEnvironmentProvider"/>
    /// — affects every new process the user launches.
    /// </para>
    /// <para>
    /// macOS / Linux (added 2026-05-07): appends an idempotent
    /// <c>export PATH=…</c> line to the user's shell rc file
    /// (<c>~/.bashrc</c> / <c>~/.zshrc</c> / <c>~/.config/fish/config.fish</c>,
    /// auto-detected from <c>$SHELL</c>).  Affects every new shell session;
    /// the running shell needs to source the file or restart for the change
    /// to take effect.
    /// </para>
    /// </summary>
    private bool CanAddClaudeCodeToPath()
    {
        return ShowClaudeCodePathWarning &&
               (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                || RuntimeInformation.IsOSPlatform(OSPlatform.Linux));
    }

    /// <summary>
    /// Prompts the user for confirmation, then appends the directory that
    /// contains the detected <c>claude</c> binary to their per-user <c>Path</c>
    /// environment variable via <see cref="IEnvironmentProvider"/>. The
    /// underlying write goes to <c>HKCU\Environment\Path</c> on Windows and
    /// does not require administrator rights — the same mechanism the
    /// Environment editor uses elsewhere in the app.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddClaudeCodeToPath))]
    private async Task AddClaudeCodeToPathAsync()
    {
        string? binary = ClaudeCodeBinaryPath;
        if (string.IsNullOrEmpty(binary))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(binary);
        if (string.IsNullOrEmpty(directory))
        {
            AddToPathResultText = Strings.TextAddToPathFailed;
            return;
        }

        Log.Information("[About.Command] action=AddClaudeCodeToPath directory=\"{Dir}\"", directory);

        // Confirm with the user before mutating a persistent environment
        // variable. We surface the exact directory so they can verify it
        // before accepting. If no dialog service is wired (unit test mode)
        // we proceed without prompting.
        if (_dialogService is not null)
        {
            // Slice the localized format around the {0} placeholder so we can
            // render `directory` as a click-to-copy path span.
            string? fmt = Strings.DialogMessageAddClaudeCodeToPath;
            const string placeholder = "{0}";
            int splitIdx = fmt.IndexOf(placeholder, StringComparison.Ordinal);
            DialogMessage pathMsg = splitIdx >= 0
                ? DialogMessage.Builder()
                               .Text(fmt[..splitIdx])
                               .Path(directory)
                               .Text(fmt[(splitIdx + placeholder.Length)..])
                               .Build()
                : DialogMessage.Plain(string.Format(fmt, directory));
            bool? confirmed = await _dialogService.ShowConfirmAsync(
                title: Strings.DialogTitleAddClaudeCodeToPath,
                message: pathMsg,
                category: DialogCategory.Confirmation,
                confirmLabel: Strings.ButtonAddClaudeCodeToPath,
                cancelLabel: Strings.ButtonCancel);

            // Binary yes/no — both Cancel (false) and X (null) abort.
            if (confirmed != true)
            {
                return;
            }
        }

        string result = AppendDirectoryToUserPath(directory);
        AddToPathResultText = result;

        // Flip the "done" flag only when the User PATH now contains the
        // directory — either because we just added it or because it was
        // already there. Failures (e.g. registry write blocked) leave the
        // banner visible so the user can retry.
        if (result == Strings.TextAddToPathAdded || result == Strings.TextAddToPathAlreadyPresent)
        {
            PathWasAddedOrAlreadyPresent = true;
            // Drop PlatformPaths' process-lifetime PATH cache so any next
            // call to TryFindClaudeCodeBinary / IsClaudeCodeOnPath /
            // FindFirstOnPath re-probes. Today's in-process PATH won't
            // actually have changed (Windows propagates HKCU\Environment
            // edits only to new processes), but invalidating is still the
            // honest thing to do — and the "already present" branch can
            // genuinely benefit if the user externally tweaked PATH and we
            // had a stale negative cache entry.
            PlatformPaths.InvalidatePathCache();
        }
    }

    /// <summary>
    /// Appends <paramref name="directory"/> to the user's persistent PATH if
    /// not already present.  Windows writes <c>HKCU\Environment\Path</c> via
    /// the environment provider; macOS / Linux append an
    /// <c>export PATH=…</c> line to the user's shell rc file (auto-detected
    /// from <c>$SHELL</c>).  Returns a localized feedback string for the view.
    /// </summary>
    private string AppendDirectoryToUserPath(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return AppendToWindowsUserPath(directory);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return AppendToUnixShellRcFile(directory);
        }

        return Strings.TextAddToPathUnsupported;
    }

    /// <summary>
    /// Windows-only: write <c>HKCU\Environment\Path</c> via the environment
    /// abstraction.  Persists for every new process the user launches; the
    /// running app process keeps its old PATH (we invalidate the cache so
    /// the next probe picks up the change after the user restarts the app).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private string AppendToWindowsUserPath(string directory)
    {
        try
        {
            // Read the *persistent* User PATH via the existing environment
            // abstraction — NOT Process PATH, which is a merged snapshot and
            // would cause us to re-add entries the user already has at the
            // Machine scope.
            IDictionary userVars = _environmentProvider.GetVariables(EnvironmentVariableTarget.User);
            // Defensive: the non-generic IDictionary contract says nothing
            // about the value type, and on Windows an exotic registry value
            // (e.g. REG_DWORD masquerading as PATH on a misconfigured system)
            // would return a non-string. `.ToString()` coerces any such
            // value rather than silently dropping it the way `as string` would.
            object? pathObj = userVars.Contains("Path") ? userVars["Path"] : null;
            string existing = pathObj?.ToString() ?? string.Empty;

            if (ContainsDirectory(existing, directory))
            {
                return Strings.TextAddToPathAlreadyPresent;
            }

            char separator = Path.PathSeparator;
            bool needsSep = existing.Length > 0 && !existing.EndsWith(separator);
            string updated = existing + (needsSep ? separator.ToString() : string.Empty) + directory;

            _environmentProvider.SetVariable("Path", updated, EnvironmentVariableTarget.User);
            return Strings.TextAddToPathAdded;
        }
        catch (Exception ex) when (ex is SecurityException
                                       or UnauthorizedAccessException
                                       or IOException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            Log.Warning(ex, "[AboutEditor] AppendToWindowsUserPath failed for {Directory}", directory);
            return Strings.TextAddToPathFailed;
        }
    }

    /// <summary>
    /// macOS / Linux: detect the user's login shell from
    /// <c>$SHELL</c>, pick the matching rc file, and append a single
    /// <c>export PATH=…</c> (or <c>set -x PATH …</c> for fish) line if not
    /// already present.  Idempotent: the contains-check uses the literal
    /// directory string so re-running returns
    /// <see cref="Strings.TextAddToPathAlreadyPresent"/>.
    /// <para>
    /// Conservative writes: we do NOT mutate the existing file in place;
    /// we only append at the end with a comment header attributing the line
    /// to ClaudeForge.  If the file doesn't exist we create it (and any
    /// missing parent directory, e.g. <c>~/.config/fish/</c>).
    /// </para>
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private string AppendToUnixShellRcFile(string directory)
    {
        (string? rcPath, string exportLine, string _) = ResolveUnixShellRcTarget(directory);
        if (rcPath is null)
        {
            return Strings.TextAddToPathUnsupported;
        }

        try
        {
            // Idempotency check: if the file already mentions the directory
            // anywhere (even via $HOME / ~ expansion the user wrote manually),
            // we skip appending.  Worst case is a duplicate export line — not
            // dangerous (last one wins on shell startup) but noisy.
            if (File.Exists(rcPath))
            {
                string existing = File.ReadAllText(rcPath);
                if (existing.Contains(directory, StringComparison.Ordinal))
                {
                    return Strings.TextAddToPathAlreadyPresent;
                }
            }
            else
            {
                // Create parent dir for ~/.config/fish/ etc.  No-op if it
                // already exists.
                string? parent = Path.GetDirectoryName(rcPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }
            }

            // Two-line append: comment header + the export line itself.  The
            // header makes the addition discoverable to the user reviewing
            // their rc file later (and tells them which app added it, so a
            // future ClaudeForge uninstall can clean up if we add that flow).
            string addition = $"\n# Added by ClaudeForge — Claude Code install path\n{exportLine}\n";
            File.AppendAllText(rcPath, addition);
            Log.Information("[AboutEditor] Appended PATH export to {RcPath}", rcPath);
            return Strings.TextAddToPathAdded;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or ArgumentException)
        {
            Log.Warning(ex, "[AboutEditor] AppendToUnixShellRcFile failed for {RcPath}", rcPath);
            return Strings.TextAddToPathFailed;
        }
    }

    /// <summary>
    /// Resolves the rc file path + export-line syntax for the user's login
    /// shell.  Returns <see langword="null"/> rcPath when <c>$HOME</c> can't
    /// be resolved (test environments, sandboxed contexts) so the caller
    /// can fall through to the unsupported branch instead of throwing.
    /// </summary>
    /// <remarks>
    /// Internal so a unit test can pin the shell-detection table:
    /// <c>fish</c> → fish syntax (<c>set -x PATH …</c>); <c>zsh</c> → zsh
    /// rc; everything else falls back to <c>~/.bashrc</c> with bash/POSIX
    /// <c>export PATH=…</c> syntax.  bash syntax also works in sh / ash /
    /// dash so the fallback is broad.
    /// </remarks>
    internal static (string? RcPath, string ExportLine, string ShellKind)
        ResolveUnixShellRcTarget(string directory)
    {
        string shell = Environment.GetEnvironmentVariable("SHELL") ?? string.Empty;
        string home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        if (string.IsNullOrEmpty(home))
        {
            return (null, string.Empty, "unknown");
        }

        // fish has different syntax — `set -x PATH "<dir>" $PATH`.
        if (shell.EndsWith("/fish", StringComparison.Ordinal)
            || shell.EndsWith("\\fish", StringComparison.Ordinal))
        {
            string fishRc = Path.Combine(home, ".config", "fish", "config.fish");
            string fishLine = $"set -x PATH \"{directory}\" $PATH";
            return (fishRc, fishLine, "fish");
        }

        // zsh — used by macOS by default since Catalina.
        if (shell.EndsWith("/zsh", StringComparison.Ordinal)
            || shell.EndsWith("\\zsh", StringComparison.Ordinal))
        {
            string zshRc = Path.Combine(home, ".zshrc");
            string zshLine = $"export PATH=\"{directory}:$PATH\"";
            return (zshRc, zshLine, "zsh");
        }

        // Default to bash — also covers sh / ash / dash / busybox via the
        // same syntax.  ~/.bashrc is sourced by login + interactive bash on
        // most distros; .profile / .bash_profile are alternatives but
        // .bashrc is the safest single-file default for an "added line".
        string bashRc = Path.Combine(home, ".bashrc");
        string bashLine = $"export PATH=\"{directory}:$PATH\"";
        return (bashRc, bashLine, "bash");
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="directory"/> is
    /// already one of the <see cref="Path.PathSeparator"/>-separated entries
    /// in <paramref name="pathVar"/>, case-insensitively on Windows. Trailing
    /// directory separators on individual entries are ignored.
    /// </summary>
    internal static bool ContainsDirectory(string pathVar, string directory)
    {
        if (string.IsNullOrEmpty(pathVar) || string.IsNullOrEmpty(directory))
        {
            return false;
        }

        StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string needle = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (string raw in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string entry = raw.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(entry, needle, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            // Prefer the explicit binary we resolved at construction time — on
            // Windows ARM64 this is often %APPDATA%\npm\claude.cmd, which a
            // bare-name PATH lookup would miss. The probe cmd.exe-wraps .cmd
            // shims internally so this works identically to a bare "claude"
            // invocation on a correctly-configured machine.
            //
            // If the probe fails but we DID resolve a binary, fall back to a
            // generic "installed (version unknown)" string. Without this, the
            // About page would flip into the "(not detected)" layout even
            // though detection succeeded — and the inlined InstallCommandPanel
            // would bind against a null ClaudeCodeInstallPanel VM (which the
            // constructor leaves null whenever IsClaudeCodeInstalled is true),
            // producing a partially-rendered "Copy button + blank Run button"
            // with no code-block text.
            string? probed = await ProductVersionProbe.TryGetClaudeCodeVersionAsync(
                _claudeCodeLocation?.BinaryPath);
            // Fallback: binary was found but the version probe timed out or the
            // shim returned unexpected output. Use the same binary-presence check
            // as IsProductInstalled / CanActOnConfig so all three signals stay in
            // lockstep — if no binary, ClaudeCodeVersion stays null which the view
            // uses to show "(not detected)" + the install panel.
            ClaudeCodeVersion =
                probed
                ?? (_claudeCodeLocation is not null ? "installed (version unknown)" : null);

            // Try the composite probe (Registry → Claude.exe FileVersionInfo → Squirrel folder)
            // first; fall back to the legacy single-strategy probe to preserve macOS's plist
            // reader. If everything fails but the config directory exists, surface a generic
            // "installed" string so the About tab is useful rather than silent.
            ClaudeDesktopVersion =
                ClaudeDesktopVersionProbe.TryGetVersion()
                ?? ProductVersionProbe.TryGetClaudeDesktopVersion()
                ?? (PlatformPaths.IsDesktopInstalled ? "installed (version unknown)" : null);
        }
        catch (Exception ex)
        {
            // Surface the failure rather than silently leaving both fields null.
            Log.Error(ex, "[About] LoadVersionsAsync failed — version probe could not complete");
            ClaudeCodeVersion ??= $"(probe error: {ex.Message})";
            ClaudeDesktopVersion ??= null;
        }
    }
}